using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Config;

public sealed class GeneratorOptions
{
    /// <summary>Directory holding *.srs rule-set files (geoip-*.srs, geosite-*.srs).</summary>
    public string RuleSetDirectory { get; set; } = "geo";
    public string TunInterfaceName { get; set; } = "simpleray";
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

        var root = new JsonObject
        {
            ["log"] = new JsonObject { ["level"] = options.LogLevel, ["timestamp"] = true },
            ["dns"] = BuildDns(),
            ["inbounds"] = new JsonArray { BuildTunInbound(options) },
            ["outbounds"] = BuildOutbounds(profiles, routing.Failover),
            ["route"] = BuildRoute(routing, options),
        };
        return root;
    }

    // New DNS server format (sing-box 1.12+); the legacy "address" form is removed in 1.14.
    private static JsonObject BuildDns() => new()
    {
        ["servers"] = new JsonArray
        {
            new JsonObject { ["type"] = "https", ["tag"] = "remote", ["server"] = "1.1.1.1", ["detour"] = GeneratorOptions.ProxyTag },
            new JsonObject { ["type"] = "https", ["tag"] = "local", ["server"] = "223.5.5.5", ["detour"] = GeneratorOptions.DirectTag },
        },
        ["final"] = "remote",
        ["strategy"] = "prefer_ipv4",
    };

    private static JsonObject BuildTunInbound(GeneratorOptions o) => new()
    {
        ["type"] = "tun",
        ["tag"] = "tun-in",
        ["interface_name"] = o.TunInterfaceName,
        ["address"] = new JsonArray { "172.18.0.1/30", "fdfe:dcba:9876::1/126" },
        ["mtu"] = o.Mtu,
        ["auto_route"] = true,
        ["strict_route"] = true,
        ["stack"] = "system",
    };

    private static JsonArray BuildOutbounds(IReadOnlyList<ProfileConfig> profiles, GroupSettings group)
    {
        var outbounds = new JsonArray();

        if (profiles.Count == 1)
        {
            // Single server: it directly owns the "proxy" tag used by routing/DNS.
            outbounds.Add(BuildProxyOutbound(profiles[0], GeneratorOptions.ProxyTag));
        }
        else
        {
            // Several servers: proxy-1..N members + a group that takes the "proxy" tag.
            var memberTags = new JsonArray();
            for (int i = 0; i < profiles.Count; i++)
            {
                var tag = $"{GeneratorOptions.ProxyTag}-{i + 1}";
                outbounds.Add(BuildProxyOutbound(profiles[i], tag));
                memberTags.Add(tag);
            }
            outbounds.Add(BuildGroupOutbound(memberTags, group));
        }

        outbounds.Add(new JsonObject { ["type"] = "direct", ["tag"] = GeneratorOptions.DirectTag });
        return outbounds;
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
        }

        // Reorder so "type" is first for readability.
        var ordered = new JsonObject { ["type"] = o["type"]!.DeepClone(), ["tag"] = tag };
        foreach (var kv in o)
        {
            if (kv.Key is "type" or "tag") continue;
            ordered[kv.Key] = kv.Value?.DeepClone();
        }

        var tls = BuildTls(p);
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

    private static JsonObject BuildRoute(RoutingSettings r, GeneratorOptions o)
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
}
