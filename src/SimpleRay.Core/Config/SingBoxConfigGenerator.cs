using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SimpleRay.Core.Dns;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Config;

public sealed class GeneratorOptions
{
    /// <summary>Directory holding *.srs rule-set files (geoip-*.srs, geosite-*.srs).</summary>
    public string RuleSetDirectory { get; set; } = "geo";
    public string TunInterfaceName { get; set; } = "simpleray";
    public LanDns? LanDns { get; set; }
    public int Mtu { get; set; } = 9000;
    public string LogLevel { get; set; } = "info";

    /// <summary>Tag used for the proxy outbound everywhere in the config.</summary>
    public const string ProxyTag = "proxy";
    public const string DirectTag = "direct";
}

/// <summary>
/// Builds a sing-box configuration (schema targeted at sing-box 1.11+) from a
/// parsed <see cref="ProfileConfig"/> and <see cref="RoutingSettings"/>.
///
/// The output is validated against the real binary via `sing-box check` before
/// the engine starts; this class only encodes our routing intent.
/// </summary>
public static class SingBoxConfigGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string GenerateJson(ProfileConfig profile, RoutingSettings routing, GeneratorOptions? options = null)
        => Generate(profile, routing, options).ToJsonString(WriteOptions);

    public static JsonObject Generate(ProfileConfig profile, RoutingSettings routing, GeneratorOptions? options = null)
        => Generate(new[] { profile }, routing, options);

    /// <summary>
    /// Builds a config for one or more servers. With a single server the proxy
    /// outbound is tagged "proxy" directly; with several, each becomes proxy-N and a
    /// failover group (urltest/selector) takes the "proxy" tag that routing uses.
    /// </summary>
    public static string GenerateJson(IReadOnlyList<ProfileConfig> profiles, RoutingSettings routing, GeneratorOptions? options = null)
        => Generate(profiles, routing, options).ToJsonString(WriteOptions);

    public static JsonObject Generate(IReadOnlyList<ProfileConfig> profiles, RoutingSettings routing, GeneratorOptions? options = null)
    {
        if (profiles is null || profiles.Count == 0)
            throw new ArgumentException("At least one profile is required.", nameof(profiles));
        options ??= new GeneratorOptions();

        var topology = BuildProxyTopology(profiles, routing.Failover);

        // Direct routing (Rule mode) must also resolve DIRECTLY, or a direct connection uses
        // tunnel-resolved (foreign) IPs and gets reset. Collect what routes direct — the user's
        // domains and the direct geosite sets (e.g. geosite-category-ru: ya.ru, vk.com, ...) —
        // and give each a DNS rule pointing at the local resolver. (geoip is IP-based, so it
        // can't drive DNS; most Russian sites are covered by the geosite anyway.)
        var directDomains = routing.Mode == RoutingMode.Rule
            ? CleanDomains(routing.DirectDomains)
            : new List<string>();
        var directGeosites = routing.Mode == RoutingMode.Rule
            ? routing.DirectGeosite.Where(t => t != "geosite-private").ToList()
            : new List<string>();
        // Domains forced THROUGH the VPN, overriding the direct/geo lists (both routing and DNS).
        var proxyDomains = routing.Mode == RoutingMode.Rule
            ? CleanDomains(routing.ProxyDomains)
            : new List<string>();

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = options.LogLevel, ["timestamp"] = true },
            ["dns"] = BuildDns(routing.Dns, directDomains, directGeosites, proxyDomains, routing.EnableIpv6, options.LanDns),
            ["inbounds"] = new JsonArray { BuildTunInbound(options, routing.EnableIpv6) },
            ["outbounds"] = topology.Outbounds,
            ["route"] = BuildRoute(routing, options, directDomains, proxyDomains),
        };
        // WireGuard servers live in "endpoints" (sing-box 1.11+); only emit when present.
        if (topology.Endpoints.Count > 0)
            root["endpoints"] = topology.Endpoints;
        return root;
    }

    /// <summary>
    /// Config for "ping through the server": a throwaway sing-box with one local proxy inbound
    /// per server (127.0.0.1 only, username/password required), each routed to just that
    /// server. No TUN, no routing rules, nothing system-wide. auto_detect_interface makes it
    /// reach the servers over the physical interface even while the main tunnel is up.
    /// </summary>
    public static string GenerateProbeJson(IReadOnlyList<ProfileConfig> profiles, IReadOnlyList<int> ports,
        string username, string password)
        => GenerateProbe(profiles, ports, username, password).ToJsonString(WriteOptions);

    public static JsonObject GenerateProbe(IReadOnlyList<ProfileConfig> profiles, IReadOnlyList<int> ports,
        string username, string password)
    {
        if (profiles is null || profiles.Count == 0)
            throw new ArgumentException("At least one profile is required.", nameof(profiles));
        if (ports.Count != profiles.Count)
            throw new ArgumentException("One port per profile is required.", nameof(ports));
        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            throw new ArgumentException("The probe inbounds must require credentials.");

        var inbounds = new JsonArray();
        var outbounds = new JsonArray();
        var endpoints = new JsonArray();
        var rules = new JsonArray();
        for (int i = 0; i < profiles.Count; i++)
        {
            string inTag = $"probe-in-{i + 1}", outTag = $"probe-{i + 1}";
            inbounds.Add(new JsonObject
            {
                ["type"] = "mixed",
                ["tag"] = inTag,
                ["listen"] = "127.0.0.1",
                ["listen_port"] = ports[i],
                ["users"] = new JsonArray { new JsonObject { ["username"] = username, ["password"] = password } },
            });
            if (profiles[i].Protocol == ProxyProtocol.WireGuard)
                endpoints.Add(BuildWireGuardEndpoint(profiles[i], outTag));
            else
                outbounds.Add(BuildProxyOutbound(profiles[i], outTag));
            rules.Add(new JsonObject { ["inbound"] = new JsonArray { inTag }, ["outbound"] = outTag });
        }
        // Nothing should fall through, but if it did it must not leave the machine unproxied.
        rules.Add(new JsonObject { ["action"] = "reject" });

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = "warn" },
            ["dns"] = new JsonObject { ["servers"] = new JsonArray { new JsonObject { ["type"] = "local", ["tag"] = "local" } } },
            ["inbounds"] = inbounds,
            ["outbounds"] = outbounds,
            ["route"] = new JsonObject
            {
                ["rules"] = rules,
                ["default_domain_resolver"] = "local",
                ["auto_detect_interface"] = true,
            },
        };
        if (endpoints.Count > 0)
            root["endpoints"] = endpoints;
        return root;
    }

    // New DNS server format (sing-box 1.12+); the legacy "address" form is removed in 1.14.
    // Resolvers are addressed by IP: a hostname here would itself need resolving first.
    private static JsonObject BuildDns(DnsSettings dns, IReadOnlyList<string> directDomains, IReadOnlyList<string> directGeosites, IReadOnlyList<string> proxyDomains, bool enableIpv6, LanDns? lanDns)
    {
        dns ??= new DnsSettings();
        // ResolveRemote enforces DoH for the tunnelled server, so a UDP pick can never
        // send plaintext DNS through the proxy.
        var root = new JsonObject
        {
            ["servers"] = new JsonArray
            {
                BuildDnsServer("remote", DnsCatalog.ResolveRemote(dns.RemoteProviderId), GeneratorOptions.ProxyTag),
                // No detour: for a DNS server, "no detour" already means direct. sing-box
                // 1.12+ rejects detouring to the (empty) direct outbound at service start
                // ("detour to an empty direct outbound makes no sense") — which `check`
                // does NOT catch, so it surfaced only as "exited immediately (code 1)".
                BuildDnsServer("local", DnsCatalog.ResolveLocal(dns.LocalProviderId), detour: null),
            },
            ["final"] = "remote",
            // Default IPv4-only: most target networks have no native IPv6, so a DIRECT IPv6
            // connection has no physical path and is reset (proxied IPv6 works, being tunnelled).
            // Clients pick IPv6 first when given AAAA, so returning only A records avoids those
            // resets. enableIpv6 restores dual-stack (prefer IPv4) for IPv6-capable networks.
            ["strategy"] = enableIpv6 ? "prefer_ipv4" : "ipv4_only",
        };

        // Resolve everything that routes DIRECT via the local (direct) resolver so the direct
        // outbound connects to region-correct IPs — otherwise they'd be resolved through the
        // tunnel and a direct connection to a foreign-resolved IP can be reset.
        var dnsRules = new JsonArray();
        if (lanDns is not null)
        {
            root["servers"]!.AsArray().Add(new JsonObject
            {
                ["type"] = "udp",
                ["tag"] = "lan",
                ["server"] = lanDns.Server,
            });
            // LAN DNS must precede even explicit proxy domains. No detour means direct.
            var suffixes = lanDns.Suffixes.Concat(new[] { "10.in-addr.arpa" })
                .Concat(Enumerable.Range(16, 16).Select(n => $"{n}.172.in-addr.arpa"))
                .Concat(new[] { "168.192.in-addr.arpa", "254.169.in-addr.arpa" });
            dnsRules.Add(new JsonObject
            {
                ["domain_suffix"] = ToJsonArray(suffixes),
                ["server"] = "lan",
            });
        }
        // Proxy-domains first: force them onto the remote (tunnel) resolver, overriding the
        // direct/geosite local rules below for any domain that appears in both.
        if (proxyDomains.Count > 0)
            dnsRules.Add(new JsonObject
            {
                ["domain_suffix"] = new JsonArray(proxyDomains.Select(d => (JsonNode)d!).ToArray()),
                ["server"] = "remote",
            });
        if (directDomains.Count > 0)
            dnsRules.Add(new JsonObject
            {
                ["domain_suffix"] = new JsonArray(directDomains.Select(d => (JsonNode)d!).ToArray()),
                ["server"] = "local",
            });
        if (directGeosites.Count > 0)
            dnsRules.Add(new JsonObject
            {
                ["rule_set"] = new JsonArray(directGeosites.Select(t => (JsonNode)t!).ToArray()),
                ["server"] = "local",
            });
        if (dnsRules.Count > 0)
            root["rules"] = dnsRules;
        return root;
    }

    private static JsonObject BuildDnsServer(string tag, DnsProvider provider, string? detour)
    {
        var server = new JsonObject
        {
            ["type"] = provider.Transport == DnsTransport.Udp ? "udp" : "https",
            ["tag"] = tag,
            ["server"] = provider.Server,
        };
        if (!string.IsNullOrEmpty(detour))
            server["detour"] = detour;
        return server;
    }

    // LAN, link-local, multicast and broadcast ranges are kept OFF the tunnel. With auto_route +
    // strict_route, sing-box otherwise forces even local traffic through the TUN, where it can't
    // proxy ICMP and strict_route's WFP rules drop the OS-level bypass — so pinging a LAN host or
    // reaching a printer/NAS fails with "General failure". Excluding them lets local traffic stay
    // local (the ip_is_private route rule already expresses the same intent for packets that do
    // enter the tunnel; this makes the OS routing match it instead of black-holing the LAN).
    private static readonly string[] LocalV4Ranges =
    {
        "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16",
        "169.254.0.0/16", "224.0.0.0/4", "255.255.255.255/32",
    };

    private static readonly string[] LocalV6Ranges = { "fc00::/7", "fe80::/10", "ff00::/8" };

    private static JsonObject BuildTunInbound(GeneratorOptions o, bool enableIpv6)
    {
        var tun = new JsonObject
        {
            ["type"] = "tun",
            ["tag"] = "tun-in",
            ["interface_name"] = o.TunInterfaceName,
            // Default IPv4-only tun (see the DNS ipv4_only note): no IPv6 address means auto_route
            // adds no IPv6 default route, so stray IPv6 traffic can't be hijacked into the broken
            // direct-IPv6 path. enableIpv6 restores the dual-stack address for IPv6-capable networks.
            ["address"] = enableIpv6
                ? new JsonArray { "172.18.0.1/30", "fdfe:dcba:9876::1/126" }
                : new JsonArray { "172.18.0.1/30" },
            ["mtu"] = o.Mtu,
            ["auto_route"] = true,
            ["strict_route"] = true,
            // Unified route field (sing-box 1.10+; legacy inet4_/inet6_ variants were removed in
            // 1.12). v6 ranges are only added when the dual-stack tun is enabled.
            ["route_exclude_address"] = ToJsonArray(
                enableIpv6 ? LocalV4Ranges.Concat(LocalV6Ranges) : LocalV4Ranges),
            ["stack"] = "system",
        };
        return tun;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> items) =>
        new(items.Select(s => (JsonNode)s!).ToArray());

    /// <summary>Proxy nodes split into plain outbounds and WireGuard endpoints (plus the direct outbound).</summary>
    private readonly record struct ProxyTopology(JsonArray Outbounds, JsonArray Endpoints);

    private static ProxyTopology BuildProxyTopology(IReadOnlyList<ProfileConfig> profiles, GroupSettings group)
    {
        var outbounds = new JsonArray();
        var endpoints = new JsonArray();

        // WireGuard goes to "endpoints"; everything else to "outbounds". Both expose a tag.
        void AddNode(ProfileConfig p, string tag)
        {
            if (p.Protocol == ProxyProtocol.WireGuard)
                endpoints.Add(BuildWireGuardEndpoint(p, tag));
            else
                outbounds.Add(BuildProxyOutbound(p, tag));
        }

        if (profiles.Count == 1)
        {
            // Single server: it directly owns the "proxy" tag used by routing/DNS.
            AddNode(profiles[0], GeneratorOptions.ProxyTag);
        }
        else
        {
            // Several servers: proxy-1..N members + a group that takes the "proxy" tag.
            var memberTags = new JsonArray();
            for (int i = 0; i < profiles.Count; i++)
            {
                var tag = $"{GeneratorOptions.ProxyTag}-{i + 1}";
                AddNode(profiles[i], tag);
                memberTags.Add(tag);
            }
            outbounds.Add(BuildGroupOutbound(memberTags, group));
        }

        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = GeneratorOptions.DirectTag });
        return new ProxyTopology(outbounds, endpoints);
    }

    private static JsonObject BuildWireGuardEndpoint(ProfileConfig p, string tag)
    {
        var wg = p.Wg ?? new WireGuardSettings();

        var endpoint = new JsonObject
        {
            ["type"] = "wireguard",
            ["tag"] = tag,
            ["address"] = new JsonArray(wg.LocalAddresses.Select(a => (JsonNode)a!).ToArray()),
            ["private_key"] = wg.PrivateKey,
        };
        if (wg.Mtu is int mtu) endpoint["mtu"] = mtu;

        var peer = new JsonObject
        {
            ["address"] = p.Server,
            ["port"] = p.Port,
            ["public_key"] = wg.PeerPublicKey,
            ["allowed_ips"] = new JsonArray(
                (wg.AllowedIps.Count > 0 ? wg.AllowedIps : new List<string> { "0.0.0.0/0", "::/0" })
                .Select(a => (JsonNode)a!).ToArray()),
        };
        if (!string.IsNullOrEmpty(wg.PreSharedKey)) peer["pre_shared_key"] = wg.PreSharedKey;
        if (wg.PersistentKeepalive is int ka) peer["persistent_keepalive_interval"] = ka;

        endpoint["peers"] = new JsonArray { peer };
        return endpoint;
    }

    private static JsonObject BuildGroupOutbound(JsonArray memberTags, GroupSettings group)
    {
        if (group.Mode == FailoverMode.Selector)
        {
            return new JsonObject
            {
                ["type"] = "selector",
                ["tag"] = GeneratorOptions.ProxyTag,
                ["outbounds"] = memberTags.DeepClone(),
                ["default"] = memberTags[0]!.DeepClone(),
                ["interrupt_exist_connections"] = true,
            };
        }

        return new JsonObject
        {
            ["type"] = "urltest",
            ["tag"] = GeneratorOptions.ProxyTag,
            ["outbounds"] = memberTags.DeepClone(),
            ["url"] = group.TestUrl,
            ["interval"] = $"{Math.Max(10, group.IntervalSeconds)}s",
            ["tolerance"] = Math.Max(0, group.ToleranceMs),
            ["interrupt_exist_connections"] = true,
        };
    }

    private static JsonObject BuildProxyOutbound(ProfileConfig p, string tag)
    {
        var o = new JsonObject
        {
            ["tag"] = tag,
            ["server"] = p.Server,
            ["server_port"] = p.Port,
        };

        switch (p.Protocol)
        {
            case ProxyProtocol.VLESS:
                o["type"] = "vless";
                o["uuid"] = p.Uuid;
                if (!string.IsNullOrEmpty(p.Flow)) o["flow"] = p.Flow;
                break;
            case ProxyProtocol.VMess:
                o["type"] = "vmess";
                o["uuid"] = p.Uuid;
                o["alter_id"] = p.AlterId;
                o["security"] = string.IsNullOrEmpty(p.Method) ? "auto" : p.Method;
                break;
            case ProxyProtocol.Trojan:
                o["type"] = "trojan";
                o["password"] = p.Password;
                break;
            case ProxyProtocol.Shadowsocks:
                o["type"] = "shadowsocks";
                o["method"] = p.Method;
                o["password"] = p.Password;
                break;
            case ProxyProtocol.Hysteria2:
                o["type"] = "hysteria2";
                o["password"] = p.Password ?? "";
                if (p.PortRanges is { Count: > 0 } ranges)
                {
                    // Port hopping: server_ports replaces server_port (they conflict). sing-box
                    // accepts only "from:to" ranges there, so a lone port becomes "443:443".
                    o.Remove("server_port");
                    o["server_ports"] = new JsonArray(ranges
                        .Select(r => (JsonNode)(r.Contains('-') ? r.Replace('-', ':') : $"{r}:{r}")!)
                        .ToArray());
                    if (!string.IsNullOrEmpty(p.HopInterval)) o["hop_interval"] = p.HopInterval;
                }
                if (p.UpMbps is int up) o["up_mbps"] = up;
                if (p.DownMbps is int down) o["down_mbps"] = down;
                if (!string.IsNullOrEmpty(p.ObfsType))
                    o["obfs"] = new JsonObject { ["type"] = p.ObfsType, ["password"] = p.ObfsPassword ?? "" };
                break;
            case ProxyProtocol.Socks:
                o["type"] = "socks";
                o["version"] = "5";
                if (!string.IsNullOrEmpty(p.Username)) o["username"] = p.Username;
                if (!string.IsNullOrEmpty(p.Password)) o["password"] = p.Password;
                break;
        }

        // Reorder so "type" is first for readability.
        var ordered = new JsonObject { ["type"] = o["type"]!.DeepClone(), ["tag"] = tag };
        foreach (var kv in o)
        {
            if (kv.Key is "type" or "tag") continue;
            ordered[kv.Key] = kv.Value?.DeepClone();
        }

        if (p.Protocol == ProxyProtocol.Socks)
            return ordered; // plain SOCKS5: no TLS, no transport

        var tls = BuildTls(p);
        if (p.Protocol == ProxyProtocol.Hysteria2)
        {
            // QUIC: TLS is mandatory, and uTLS / REALITY don't apply to it (sing-box rejects them).
            tls ??= new JsonObject { ["enabled"] = true, ["server_name"] = p.Server };
            tls.Remove("utls");
            tls.Remove("reality");
            ordered["tls"] = tls;
            return ordered; // no V2Ray transport either
        }
        if (tls is not null) ordered["tls"] = tls;

        var transport = BuildTransport(p);
        if (transport is not null) ordered["transport"] = transport;

        return ordered;
    }

    private static JsonObject? BuildTls(ProfileConfig p)
    {
        if (p.Tls is not { Enabled: true } t)
            return null;

        var tls = new JsonObject { ["enabled"] = true };
        if (!string.IsNullOrEmpty(t.ServerName)) tls["server_name"] = t.ServerName;
        if (t.AllowInsecure) tls["insecure"] = true;
        if (t.Alpn is { Length: > 0 })
            tls["alpn"] = new JsonArray(t.Alpn.Select(a => (JsonNode)a!).ToArray());

        if (!string.IsNullOrEmpty(t.Fingerprint))
            tls["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = t.Fingerprint };

        if (t.IsReality)
        {
            tls["reality"] = new JsonObject
            {
                ["enabled"] = true,
                ["public_key"] = t.RealityPublicKey,
                ["short_id"] = t.RealityShortId ?? "",
            };
        }
        return tls;
    }

    private static JsonObject? BuildTransport(ProfileConfig p)
    {
        switch (p.Network)
        {
            case "ws":
                var ws = new JsonObject { ["type"] = "ws" };
                if (!string.IsNullOrEmpty(p.Path)) ws["path"] = p.Path;
                if (!string.IsNullOrEmpty(p.Host))
                    ws["headers"] = new JsonObject { ["Host"] = p.Host };
                return ws;
            case "grpc":
                return new JsonObject { ["type"] = "grpc", ["service_name"] = p.ServiceName ?? p.Path ?? "" };
            case "http":
                var http = new JsonObject { ["type"] = "http" };
                if (!string.IsNullOrEmpty(p.Path)) http["path"] = p.Path;
                if (!string.IsNullOrEmpty(p.Host))
                    http["host"] = new JsonArray { p.Host };
                return http;
            default:
                return null; // tcp / raw: no transport block
        }
    }

    private static JsonObject BuildRoute(RoutingSettings r, GeneratorOptions o, IReadOnlyList<string> directDomains, IReadOnlyList<string> proxyDomains)
    {
        var rules = new JsonArray
        {
            new JsonObject { ["action"] = "sniff" },
            new JsonObject { ["protocol"] = "dns", ["action"] = "hijack-dns" },
            new JsonObject { ["ip_is_private"] = true, ["outbound"] = GeneratorOptions.DirectTag },
        };

        var referenced = new SortedSet<string>(StringComparer.Ordinal);

        if (r.BlockAds)
        {
            const string ads = "geosite-category-ads-all";
            referenced.Add(ads);
            rules.Add(new JsonObject { ["rule_set"] = new JsonArray { ads }, ["action"] = "reject" });
        }

        if (r.DirectProcesses.Count > 0)
            rules.Add(new JsonObject
            {
                ["process_name"] = ToProcessArray(r.DirectProcesses),
                ["outbound"] = GeneratorOptions.DirectTag,
            });

        if (r.ProxyProcesses.Count > 0)
            rules.Add(new JsonObject
            {
                ["process_name"] = ToProcessArray(r.ProxyProcesses),
                ["outbound"] = GeneratorOptions.ProxyTag,
            });

        if (r.Mode == RoutingMode.Rule)
        {
            // Force-through-VPN domains first, so they override the direct/geo rules below.
            if (proxyDomains.Count > 0)
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = new JsonArray(proxyDomains.Select(d => (JsonNode)d!).ToArray()),
                    ["outbound"] = GeneratorOptions.ProxyTag,
                });

            // User's own direct-by-domain overrides (matches the domain and its subdomains),
            // e.g. "yandex.ru" also covers mail.yandex.ru / disk.yandex.ru. Already cleaned
            // and paired with a DNS rule (see BuildDns) so resolution is direct too.
            if (directDomains.Count > 0)
                rules.Add(new JsonObject
                {
                    ["domain_suffix"] = new JsonArray(directDomains.Select(d => (JsonNode)d!).ToArray()),
                    ["outbound"] = GeneratorOptions.DirectTag,
                });

            var directSets = new JsonArray();
            foreach (var tag in r.DirectGeosite.Concat(r.DirectGeoip))
            {
                referenced.Add(tag);
                directSets.Add(tag);
            }
            if (directSets.Count > 0)
                rules.Add(new JsonObject { ["rule_set"] = directSets, ["outbound"] = GeneratorOptions.DirectTag });
        }

        string final = r.Mode == RoutingMode.Direct
            ? GeneratorOptions.DirectTag
            : GeneratorOptions.ProxyTag;

        return new JsonObject
        {
            ["rules"] = rules,
            ["rule_set"] = BuildRuleSets(referenced, o),
            ["final"] = final,
            ["default_domain_resolver"] = "local",
            ["auto_detect_interface"] = true,
        };
    }

    private static JsonArray BuildRuleSets(IEnumerable<string> tags, GeneratorOptions o)
    {
        var arr = new JsonArray();
        foreach (var tag in tags)
        {
            arr.Add(new JsonObject
            {
                ["tag"] = tag,
                ["type"] = "local",
                ["format"] = "binary",
                ["path"] = Path.Combine(o.RuleSetDirectory, tag + ".srs"),
            });
        }
        return arr;
    }

    private static JsonArray ToProcessArray(IEnumerable<string> names)
    {
        var arr = new JsonArray();
        foreach (var n in names)
        {
            var name = n.Trim();
            if (name.Length == 0) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name += ".exe";
            arr.Add(name);
        }
        return arr;
    }

    /// <summary>Normalises user-typed direct domains: strips scheme/path/leading dots, lowercases, dedupes.</summary>
    private static List<string> CleanDomains(IEnumerable<string> domains)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in domains)
        {
            var d = raw.Trim().ToLowerInvariant();
            if (d.Length == 0) continue;
            int scheme = d.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) d = d[(scheme + 3)..];
            int slash = d.IndexOf('/');
            if (slash >= 0) d = d[..slash];
            d = d.Trim('.', ' ');
            if (d.Length == 0 || !seen.Add(d)) continue;
            result.Add(d);
        }
        return result;
    }
}
