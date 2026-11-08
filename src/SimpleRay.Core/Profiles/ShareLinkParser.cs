using System.Text;
using System.Text.Json;
using System.Web;
using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>
/// Parses proxy share links (vless://, vmess://, trojan://, ss://, hysteria2:// / hy2://,
/// socks:// / socks5://) into
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
            "hysteria2" or "hy2" => ParseHysteria2(link),
            "socks" or "socks5" => ParseSocks(link),
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
            Port = Port(uri),
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
            Port = Port(uri),
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

    // hysteria2://auth@host:port/?sni=…&insecure=1&obfs=salamander&obfs-password=…#remark
    // (hy2:// is an alias). Always TLS (QUIC); the port defaults to 443. Port hopping comes
    // either in the authority ("host:443,20000-30000") or as "mport=20000-30000", with an
    // optional "hop_interval=30s".
    private static ProfileConfig ParseHysteria2(string link)
    {
        var (uriLink, ports) = SplitHysteriaPorts(link);
        var uri = ToUri(uriLink);
        var q = HttpUtility.ParseQueryString(uri.Query);
        var host = Host(uri);
        int port = uri.Port is > 0 and <= 65535 ? uri.Port : 443;

        if (NullIfEmpty(q["mport"]) is { } mport)
        {
            if (ports.Count == 0)
                ports.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            foreach (var range in ParsePortSpec(mport))
                if (!ports.Contains(range))
                    ports.Add(range);
        }

        var p = new ProfileConfig
        {
            Protocol = ProxyProtocol.Hysteria2,
            Server = host,
            Port = port,
            // A lone port isn't hopping; keep the list only when there's more to hop over.
            PortRanges = ports.Count > 1 || ports.Any(r => r.Contains('-')) ? ports : null,
            HopInterval = NormalizeHopInterval(q["hop_interval"] ?? q["hopInterval"]),
            Password = Uri.UnescapeDataString(uri.UserInfo),
            ObfsType = NullIfEmpty(q["obfs"]),
            ObfsPassword = NullIfEmpty(q["obfs-password"]),
            UpMbps = int.TryParse(q["upmbps"], out var up) && up > 0 ? up : null,
            DownMbps = int.TryParse(q["downmbps"], out var down) && down > 0 ? down : null,
            Raw = link,
            Tag = Remark(link, host),
            Tls = new TlsSettings
            {
                Enabled = true,
                ServerName = NullIfEmpty(q["sni"]) ?? host,
                AllowInsecure = q["insecure"] is "1" or "true",
                Alpn = SplitCsv(q["alpn"]),
            },
        };
        return p;
    }

    /// <summary>
    /// Pulls a port-hopping spec out of the authority ("host:443,20000-30000"), which
    /// <see cref="Uri"/> can't parse, and returns the link with just the first port plus the
    /// normalized ranges ("443", "20000-30000"); an ordinary link comes back unchanged.
    /// </summary>
    private static (string Link, List<string> Ports) SplitHysteriaPorts(string link)
    {
        int start = link.IndexOf("://", StringComparison.Ordinal) + 3;
        int end = link.IndexOfAny(new[] { '/', '?', '#' }, start);
        if (end < 0) end = link.Length;
        var authority = link[start..end];
        int at = authority.LastIndexOf('@');
        var hostPort = authority[(at + 1)..];
        int portSep = hostPort.StartsWith('[')
            ? hostPort.IndexOf("]:", StringComparison.Ordinal) + 1 // IPv6: the colon after ']'
            : hostPort.LastIndexOf(':');
        if (portSep <= 0)
            return (link, new List<string>());
        var spec = hostPort[(portSep + 1)..];
        if (!spec.Contains(',') && !spec.Contains('-'))
            return (link, new List<string>());

        var ports = ParsePortSpec(spec);
        var firstPort = ports[0].Split('-')[0];
        var rebuilt = authority[..(at + 1)] + hostPort[..portSep] + ":" + firstPort;
        return (link[..start] + rebuilt + link[end..], ports);
    }

    /// <summary>"443,20000-30000" → ["443", "20000-30000"]; throws on anything out of 1..65535.</summary>
    private static List<string> ParsePortSpec(string spec)
    {
        var result = new List<string>();
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-', StringSplitOptions.TrimEntries);
            if (bounds.Length is < 1 or > 2 ||
                !bounds.All(b => int.TryParse(b, out var n) && n is > 0 and <= 65535))
                throw new FormatException($"Invalid port range '{part}'.");
            int lo = int.Parse(bounds[0]), hi = int.Parse(bounds[^1]);
            if (lo > hi)
                throw new FormatException($"Invalid port range '{part}'.");
            var normalized = lo == hi ? lo.ToString() : $"{lo}-{hi}";
            if (!result.Contains(normalized))
                result.Add(normalized);
        }
        if (result.Count == 0)
            throw new FormatException("Empty port list.");
        return result;
    }

    // "30s" / "30" / "1m" → "30s" / "30s" / "1m"; anything else is ignored (sing-box default).
    private static string? NormalizeHopInterval(string? value)
    {
        value = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
            return null;
        if (value.All(char.IsAsciiDigit))
            return value == "0" ? null : value + "s";
        return System.Text.RegularExpressions.Regex.IsMatch(value, @"^[1-9]\d*(s|m)$") ? value : null;
    }

    // socks://base64(user:pass)@host:port#remark (v2rayN) or socks5://user:pass@host:port.
    private static ProfileConfig ParseSocks(string link)
    {
        var uri = ToUri(link);
        var host = Host(uri);
        string? user = null, pass = null;
        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            var info = Uri.UnescapeDataString(uri.UserInfo);
            if (!info.Contains(':'))
            {
                // v2rayN packs "user:pass" as base64; anything that doesn't decode to that
                // shape is a bare username.
                try
                {
                    var decoded = Encoding.UTF8.GetString(DecodeBase64(info));
                    if (decoded.Contains(':') && !decoded.Any(char.IsControl))
                        info = decoded;
                }
                catch (FormatException) { }
            }
            int colon = info.IndexOf(':');
            (user, pass) = colon >= 0 ? (info[..colon], info[(colon + 1)..]) : (info, null);
        }
        return new ProfileConfig
        {
            Protocol = ProxyProtocol.Socks,
            Server = host,
            Port = Port(uri),
            Username = NullIfEmpty(user),
            Password = NullIfEmpty(pass),
            Raw = link,
            Tag = Remark(link, host),
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

    // Uri.Port is -1 for a schemeless-default like vless://host (no ":port"); reject it
    // rather than emit server_port:-1, which sing-box would refuse at startup.
    private static int Port(Uri uri) =>
        uri.Port is > 0 and <= 65535
            ? uri.Port
            : throw new FormatException($"Invalid or missing port '{uri.Port}'.");

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
