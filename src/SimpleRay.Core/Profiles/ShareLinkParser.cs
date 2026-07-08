using System.Text;
using System.Text.Json;
using System.Web;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>
/// Parses proxy share links (vless://, vmess://, trojan://, ss://) into
/// <see cref="ProfileConfig"/>. Throws <see cref="FormatException"/> on links
/// that are syntactically recognizable but malformed; returns false from
/// <see cref="TryParse"/> for anything unrecognized.
/// </summary>
public static class ShareLinkParser
{
    public static bool TryParse(string link, out ProfileConfig? profile)
    {
        profile = null;
        if (string.IsNullOrWhiteSpace(link))
            return false;
        try
        {
            profile = Parse(link.Trim());
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static ProfileConfig Parse(string link)
    {
        link = link.Trim();
        int scheme = link.IndexOf("://", StringComparison.Ordinal);
        if (scheme < 0)
            throw new FormatException("Not a share link: missing scheme.");

        string proto = link[..scheme].ToLowerInvariant();
        return proto switch
        {
            "vless" => ParseVless(link),
            "vmess" => ParseVmess(link),
            "trojan" => ParseTrojan(link),
            "ss" => ParseShadowsocks(link),
            _ => throw new FormatException($"Unsupported scheme '{proto}'.")
        };
    }

    /// <summary>Parses a block of text (e.g. clipboard) into every link it contains.</summary>
    public static IReadOnlyList<ProfileConfig> ParseMany(string text)
    {
        var result = new List<ProfileConfig>();
        if (string.IsNullOrWhiteSpace(text))
            return result;

        // A WireGuard .conf is a multi-line block, not a per-line share link.
        if (WireGuardConfParser.LooksLikeConf(text) &&
            WireGuardConfParser.TryParse(text, out var wg) && wg is not null)
        {
            result.Add(wg);
            return result;
        }

        foreach (var rawLine in text.Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (TryParse(line, out var p) && p is not null)
                result.Add(p);
        }
        return result;
    }

    // vless://uuid@host:port?params#remark
    private static ProfileConfig ParseVless(string link)
    {
        var uri = ToUri(link);
        var q = HttpUtility.ParseQueryString(uri.Query);

        var p = new ProfileConfig
        {
            Protocol = ProxyProtocol.VLESS,
            Server = Host(uri),
            Port = uri.Port,
            Uuid = UserInfo(uri),
            Flow = NullIfEmpty(q["flow"]),
            Raw = link,
            Tag = Remark(link, Host(uri)),
        };
        ApplyTransport(p, q);
        ApplyTls(p, q, q["security"]);
        return p;
    }

    // vmess://base64(json)
    private static ProfileConfig ParseVmess(string link)
    {
        string b64 = link["vmess://".Length..];
        string json = Encoding.UTF8.GetString(DecodeBase64(b64));

        using var doc = ParseVmessJson(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Invalid vmess payload: not a JSON object.");

        // Tolerate any JSON value kind: strings and numbers are used, the rest
        // (null/bool/object/array) read as absent instead of throwing.
        string Get(string name) =>
            root.TryGetProperty(name, out var v)
                ? v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString() ?? "",
                    JsonValueKind.Number => v.GetRawText(),
                    _ => "",
                }
                : "";

        var p = new ProfileConfig
        {
            Protocol = ProxyProtocol.VMess,
            Server = Get("add"),
            Port = ParsePort(Get("port")),
            Uuid = Get("id"),
            AlterId = int.TryParse(Get("aid"), out var aid) ? aid : 0,
            Method = NullIfEmpty(Get("scy")) ?? "auto",
            Network = NullIfEmpty(Get("net")) ?? "tcp",
            Path = NullIfEmpty(Get("path")),
            Host = NullIfEmpty(Get("host")),
            Raw = link,
        };
        p.ServiceName = p.Network == "grpc" ? NullIfEmpty(Get("path")) : null;
        p.Tag = NullIfEmpty(Get("ps")) ?? p.Server;

        string tls = Get("tls");
        if (!string.IsNullOrEmpty(tls) && tls != "none")
        {
            p.Tls = new TlsSettings
            {
                Enabled = true,
                ServerName = NullIfEmpty(Get("sni")) ?? NullIfEmpty(Get("host")),
                Alpn = SplitCsv(Get("alpn")),
                Fingerprint = NullIfEmpty(Get("fp")),
            };
        }
        return p;
    }

    // trojan://password@host:port?params#remark
    private static ProfileConfig ParseTrojan(string link)
    {
        var uri = ToUri(link);
        var q = HttpUtility.ParseQueryString(uri.Query);

        var p = new ProfileConfig
        {
            Protocol = ProxyProtocol.Trojan,
            Server = Host(uri),
            Port = uri.Port,
            Password = UserInfo(uri),
            Raw = link,
            Tag = Remark(link, Host(uri)),
        };
        ApplyTransport(p, q);
        // Trojan is TLS by default unless explicitly disabled.
        ApplyTls(p, q, q["security"] ?? "tls");
        return p;
    }

    // ss://base64(method:password)@host:port#remark  (SIP002)
    // ss://base64(method:password@host:port)#remark   (legacy)
    private static ProfileConfig ParseShadowsocks(string link)
    {
        string body = link["ss://".Length..];

        string? remark = null;
        int hash = body.IndexOf('#');
        if (hash >= 0)
        {
            remark = Uri.UnescapeDataString(body[(hash + 1)..]);
            body = body[..hash];
        }

        // Strip plugin/query if present (SIP002); plugins are not supported in v1.
        int qIdx = body.IndexOf('?');
        if (qIdx >= 0)
            body = body[..qIdx];

        string method, password, host;
        int port;

        int at = body.IndexOf('@');
        if (at >= 0)
        {
            // SIP002: userinfo is base64(method:password), host:port in clear.
            string userInfo = body[..at];
            string hostPort = body[(at + 1)..];
            (method, password) = SplitMethodPassword(Encoding.UTF8.GetString(DecodeBase64(userInfo)));
            (host, port) = SplitHostPort(hostPort);
        }
        else
        {
            // Legacy: whole thing is base64(method:password@host:port).
            string decoded = Encoding.UTF8.GetString(DecodeBase64(body));
            int da = decoded.LastIndexOf('@');
            if (da < 0)
                throw new FormatException("Invalid legacy ss link.");
            (method, password) = SplitMethodPassword(decoded[..da]);
            (host, port) = SplitHostPort(decoded[(da + 1)..]);
        }

        return new ProfileConfig
        {
            Protocol = ProxyProtocol.Shadowsocks,
            Server = host,
            Port = port,
            Method = method,
            Password = password,
            Raw = link,
            Tag = remark ?? host,
        };
    }

    // ---- shared helpers -------------------------------------------------

    private static void ApplyTransport(ProfileConfig p, System.Collections.Specialized.NameValueCollection q)
    {
        p.Network = NullIfEmpty(q["type"]) ?? "tcp";
        p.Host = NullIfEmpty(q["host"]);
        p.Path = NullIfEmpty(q["path"]);
        p.ServiceName = NullIfEmpty(q["serviceName"]);
    }

    private static void ApplyTls(ProfileConfig p, System.Collections.Specialized.NameValueCollection q, string? security)
    {
        security = security?.ToLowerInvariant();
        if (security is null or "" or "none")
            return;

        var tls = new TlsSettings
        {
            Enabled = true,
            ServerName = NullIfEmpty(q["sni"]) ?? p.Host,
            Alpn = SplitCsv(q["alpn"]),
            Fingerprint = NullIfEmpty(q["fp"]),
            AllowInsecure = q["allowInsecure"] is "1" or "true",
        };

        if (security == "reality")
        {
            tls.IsReality = true;
            tls.RealityPublicKey = NullIfEmpty(q["pbk"]);
            tls.RealityShortId = NullIfEmpty(q["sid"]);
        }
        p.Tls = tls;
    }

    // Strips the #remark fragment before parsing, so an already percent-encoded
    // fragment is not escaped a second time. The fragment is decoded by Remark.
    private static Uri ToUri(string link)
    {
        int hash = link.IndexOf('#');
        string head = hash >= 0 ? link[..hash] : link;
        if (!Uri.TryCreate(head, UriKind.Absolute, out var uri))
            throw new FormatException("Malformed URI.");
        return uri;
    }

    private static string Host(Uri uri) =>
        string.IsNullOrEmpty(uri.Host) ? throw new FormatException("Missing host.") : uri.Host;

    private static string UserInfo(Uri uri) =>
        string.IsNullOrEmpty(uri.UserInfo)
            ? throw new FormatException("Missing credentials.")
            : Uri.UnescapeDataString(uri.UserInfo);

    private static string Remark(string link, string fallback)
    {
        int hash = link.IndexOf('#');
        if (hash < 0 || hash == link.Length - 1)
            return fallback;
        return Uri.UnescapeDataString(link[(hash + 1)..]);
    }

    private static (string method, string password) SplitMethodPassword(string s)
    {
        int colon = s.IndexOf(':');
        if (colon < 0)
            throw new FormatException("Invalid method:password.");
        return (s[..colon], s[(colon + 1)..]);
    }

    private static (string host, int port) SplitHostPort(string s)
    {
        int colon = s.LastIndexOf(':');
        if (colon < 0)
            throw new FormatException("Invalid host:port.");
        return (s[..colon], ParsePort(s[(colon + 1)..]));
    }

    private static int ParsePort(string s) =>
        int.TryParse(s, out var port) && port is > 0 and <= 65535
            ? port
            : throw new FormatException($"Invalid port '{s}'.");

    // The class contract is "malformed link => FormatException" (TryParse relies on
    // it), so JSON errors are converted rather than left to escape as JsonException.
    private static JsonDocument ParseVmessJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException("Invalid vmess JSON payload.", ex);
        }
    }

    private static byte[] DecodeBase64(string s)
    {
        s = s.Trim().Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        try
        {
            return Convert.FromBase64String(s);
        }
        catch (FormatException)
        {
            throw new FormatException("Invalid base64 payload.");
        }
    }

    private static string[]? SplitCsv(string? s) =>
        string.IsNullOrWhiteSpace(s)
            ? null
            : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NullIfEmpty(string? s) => string.IsNullOrEmpty(s) ? null : s;
}
