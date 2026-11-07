using System.Text.Json;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>Result of reading Xray JSON configs: usable profiles and the count left out.</summary>
public sealed record XrayJsonResult(IReadOnlyList<ProfileConfig> Profiles, int Skipped);

/// <summary>
/// Reads full Xray client configs — a JSON array of them, or a single object — as served by
/// Happ-oriented subscription panels. Each config becomes one profile, taken from its proxy
/// outbound (tag "proxy" preferred, else the first vless/vmess/trojan/shadowsocks outbound) and
/// named by its "remarks". Routing/DNS/inbounds are ignored: SimpleRay builds its own. Configs
/// whose transport sing-box can't run (xhttp, kcp, …) are skipped and counted.
/// </summary>
public static class XrayJsonParser
{
    private static readonly string[] Supported = { "vless", "vmess", "trojan", "shadowsocks" };

    public static bool LooksLikeJson(string text)
    {
        var t = text.TrimStart();
        return t.StartsWith('[') || t.StartsWith('{');
    }

    public static XrayJsonResult Parse(string json)
    {
        var profiles = new List<ProfileConfig>();
        int skipped = 0;
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException)
        {
            return new XrayJsonResult(profiles, 0);
        }

        using (doc)
        {
            var root = doc.RootElement;
            IEnumerable<JsonElement> configs = root.ValueKind switch
            {
                JsonValueKind.Array => root.EnumerateArray(),
                JsonValueKind.Object => new[] { root },
                _ => Array.Empty<JsonElement>(),
            };
            foreach (var config in configs)
            {
                if (config.ValueKind != JsonValueKind.Object)
                    continue;
                var outbound = FindProxyOutbound(config);
                if (outbound is null)
                    continue; // e.g. a direct-only or informational entry
                try
                {
                    var p = ParseOutbound(outbound.Value);
                    if (p is null)
                    {
                        skipped++;
                        continue;
                    }
                    p.Tag = NullIfEmpty(Str(config, "remarks")) ?? p.Server;
                    // Stable identity for dedup / group membership across refreshes.
                    p.Raw = "xray-json:" + p.Tag + "\n" + outbound.Value.GetRawText();
                    profiles.Add(p);
                }
                catch (Exception ex) when (ex is FormatException or InvalidOperationException or KeyNotFoundException)
                {
                    skipped++;
                }
            }
        }
        return new XrayJsonResult(profiles, skipped);
    }

    private static JsonElement? FindProxyOutbound(JsonElement config)
    {
        if (!config.TryGetProperty("outbounds", out var outbounds) || outbounds.ValueKind != JsonValueKind.Array)
            return null;
        JsonElement? first = null;
        foreach (var o in outbounds.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object ||
                Str(o, "protocol")?.ToLowerInvariant() is not { } proto || !Supported.Contains(proto))
                continue;
            if (Str(o, "tag") == "proxy")
                return o;
            first ??= o;
        }
        return first;
    }

    /// <summary>Maps one outbound; null when its transport isn't supported.</summary>
    private static ProfileConfig? ParseOutbound(JsonElement o)
    {
        var settings = Obj(o, "settings");
        var p = new ProfileConfig();
        switch (Str(o, "protocol")!.ToLowerInvariant())
        {
            case "vless":
            {
                var (server, user) = ServerAndUser(settings, "vnext");
                p.Protocol = ProxyProtocol.VLESS;
                ApplyAddress(p, server);
                p.Uuid = Required(Str(user, "id"), "id");
                p.Flow = NullIfEmpty(Str(user, "flow"));
                break;
            }
            case "vmess":
            {
                var (server, user) = ServerAndUser(settings, "vnext");
                p.Protocol = ProxyProtocol.VMess;
                ApplyAddress(p, server);
                p.Uuid = Required(Str(user, "id"), "id");
                p.AlterId = Int(user, "alterId") ?? 0;
                p.Method = NullIfEmpty(Str(user, "security")) ?? "auto";
                break;
            }
            case "trojan":
            {
                var server = FirstServer(settings);
                p.Protocol = ProxyProtocol.Trojan;
                ApplyAddress(p, server);
                p.Password = Required(Str(server, "password"), "password");
                break;
            }
            default: // shadowsocks
            {
                var server = FirstServer(settings);
                p.Protocol = ProxyProtocol.Shadowsocks;
                ApplyAddress(p, server);
                p.Method = Required(Str(server, "method"), "method");
                p.Password = Required(Str(server, "password"), "password");
                break;
            }
        }
        return ApplyStream(p, Obj(o, "streamSettings")) ? p : null;
    }

    // vless/vmess: settings.vnext[0] + its users[0]; newer Xray also accepts the flat form
    // where address/port/id sit directly in settings.
    private static (JsonElement server, JsonElement user) ServerAndUser(JsonElement settings, string listName)
    {
        if (settings.TryGetProperty(listName, out var list) && list.ValueKind == JsonValueKind.Array &&
            list.GetArrayLength() > 0)
        {
            var server = list[0];
            var users = Obj(server, "users");
            if (users.ValueKind == JsonValueKind.Array && users.GetArrayLength() > 0)
                return (server, users[0]);
            throw new FormatException("Outbound has no users.");
        }
        return (settings, settings);
    }

    private static JsonElement FirstServer(JsonElement settings) =>
        settings.TryGetProperty("servers", out var list) && list.ValueKind == JsonValueKind.Array &&
        list.GetArrayLength() > 0
            ? list[0]
            : settings;

    private static void ApplyAddress(ProfileConfig p, JsonElement server)
    {
        p.Server = Required(Str(server, "address"), "address");
        p.Port = Int(server, "port") is int port and > 0 and <= 65535
            ? port
            : throw new FormatException("Invalid or missing port.");
    }

    /// <summary>Applies transport + TLS; false when the transport is one sing-box can't run.</summary>
    private static bool ApplyStream(ProfileConfig p, JsonElement stream)
    {
        var network = (NullIfEmpty(Str(stream, "network")) ?? "tcp").ToLowerInvariant();
        switch (network)
        {
            case "tcp":
            case "raw":
                p.Network = "tcp";
                break;
            case "ws":
            {
                var ws = Obj(stream, "wsSettings");
                p.Network = "ws";
                p.Path = NullIfEmpty(Str(ws, "path"));
                p.Host = NullIfEmpty(Str(ws, "host")) ?? NullIfEmpty(Str(Obj(ws, "headers"), "Host"));
                break;
            }
            case "grpc":
            case "gun":
                p.Network = "grpc";
                p.ServiceName = NullIfEmpty(Str(Obj(stream, "grpcSettings"), "serviceName"));
                break;
            case "http":
            case "h2":
            {
                var http = Obj(stream, "httpSettings");
                p.Network = "http";
                p.Path = NullIfEmpty(Str(http, "path"));
                var hosts = Obj(http, "host");
                p.Host = hosts.ValueKind == JsonValueKind.Array && hosts.GetArrayLength() > 0
                    ? NullIfEmpty(hosts[0].ValueKind == JsonValueKind.String ? hosts[0].GetString() : null)
                    : null;
                break;
            }
            default:
                return false;
        }

        var security = (Str(stream, "security") ?? "none").ToLowerInvariant();
        if (security == "tls")
        {
            var t = Obj(stream, "tlsSettings");
            p.Tls = new TlsSettings
            {
                Enabled = true,
                ServerName = NullIfEmpty(Str(t, "serverName")) ?? p.Host,
                AllowInsecure = Bool(t, "allowInsecure"),
                Alpn = StrArray(t, "alpn"),
                Fingerprint = NullIfEmpty(Str(t, "fingerprint")),
            };
        }
        else if (security == "reality")
        {
            var r = Obj(stream, "realitySettings");
            p.Tls = new TlsSettings
            {
                Enabled = true,
                IsReality = true,
                ServerName = NullIfEmpty(Str(r, "serverName")),
                Fingerprint = NullIfEmpty(Str(r, "fingerprint")),
                // Recent Xray renamed publicKey to password in client configs.
                RealityPublicKey = NullIfEmpty(Str(r, "publicKey")) ?? NullIfEmpty(Str(r, "password")),
                RealityShortId = NullIfEmpty(Str(r, "shortId")),
            };
        }
        else if (security is not ("none" or ""))
        {
            return false;
        }
        return true;
    }

    // ---- tolerant JSON accessors (absent / wrong kind reads as missing) ----------------

    private static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    private static string? Str(JsonElement e, string name)
    {
        var v = Obj(e, name);
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        };
    }

    private static int? Int(JsonElement e, string name)
    {
        var v = Obj(e, name);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            return n;
        return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out n) ? n : null;
    }

    private static bool Bool(JsonElement e, string name)
    {
        var v = Obj(e, name);
        return v.ValueKind == JsonValueKind.True ||
               (v.ValueKind == JsonValueKind.String && v.GetString() is "1" or "true");
    }

    private static string[]? StrArray(JsonElement e, string name)
    {
        var v = Obj(e, name);
        if (v.ValueKind != JsonValueKind.Array)
            return null;
        var items = v.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .Where(s => s.Length > 0)
            .ToArray();
        return items.Length > 0 ? items : null;
    }

    private static string Required(string? value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new FormatException($"Missing {what}.") : value;

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
