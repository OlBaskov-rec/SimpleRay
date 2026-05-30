using SimpleRay.Core.Models;

namespace SimpleRay.Core.Profiles;

/// <summary>
/// Parses a standard WireGuard <c>[Interface]</c>/<c>[Peer]</c> .conf (the format
/// exported by wg-quick / AmneziaWG / most providers) into a <see cref="ProfileConfig"/>.
/// WireGuard has no universal share-link scheme, so import is config-text based.
/// </summary>
public static class WireGuardConfParser
{
    /// <summary>Quick check: does this text look like a WireGuard config?</summary>
    public static bool LooksLikeConf(string text) =>
        text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase);

    public static bool TryParse(string text, out ProfileConfig? profile)
    {
        profile = null;
        try
        {
            profile = Parse(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static ProfileConfig Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !LooksLikeConf(text))
            throw new FormatException("Not a WireGuard config.");

        string section = "";
        string? name = null;
        var iface = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var peer = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in text.Replace("\r", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("#") || line.StartsWith(";"))
            {
                // Use the first non-empty comment as a friendly name if we have none yet.
                var comment = line.TrimStart('#', ';', ' ').Trim();
                if (name is null && comment.Length > 0)
                    name = comment;
                continue;
            }

            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line.Trim('[', ']').Trim().ToLowerInvariant();
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            if (section == "interface") iface[key] = value;
            else if (section == "peer") peer[key] = value;
        }

        var privateKey = Get(iface, "PrivateKey");
        var publicKey = Get(peer, "PublicKey");
        var endpoint = Get(peer, "Endpoint");
        if (privateKey is null || publicKey is null || endpoint is null)
            throw new FormatException("WireGuard config missing PrivateKey, Peer PublicKey or Endpoint.");

        var (host, port) = SplitEndpoint(endpoint);
        if (host.Length == 0 || port == 0)
            throw new FormatException("WireGuard config has an invalid Endpoint.");

        var wg = new WireGuardSettings
        {
            PrivateKey = privateKey,
            PeerPublicKey = publicKey,
            PreSharedKey = Get(peer, "PresharedKey"),
            LocalAddresses = SplitList(Get(iface, "Address")),
            AllowedIps = SplitList(Get(peer, "AllowedIPs")) is { Count: > 0 } a
                ? a
                : new List<string> { "0.0.0.0/0", "::/0" },
            Mtu = ParseIntOrNull(Get(iface, "MTU")),
            PersistentKeepalive = ParseIntOrNull(Get(peer, "PersistentKeepalive")),
        };

        return new ProfileConfig
        {
            Protocol = ProxyProtocol.WireGuard,
            Server = host,
            Port = port,
            Tag = !string.IsNullOrWhiteSpace(name) ? name! : $"WireGuard {host}",
            Wg = wg,
            Raw = text.Trim(),
        };
    }

    private static string? Get(IReadOnlyDictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    private static List<string> SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static int? ParseIntOrNull(string? value) =>
        int.TryParse(value, out var n) ? n : null;

    private static (string host, int port) SplitEndpoint(string endpoint)
    {
        endpoint = endpoint.Trim();
        if (endpoint.StartsWith("[")) // [IPv6]:port
        {
            int close = endpoint.IndexOf(']');
            if (close > 0)
            {
                var host = endpoint.Substring(1, close - 1);
                int colon = endpoint.IndexOf(':', close);
                int.TryParse(colon >= 0 ? endpoint[(colon + 1)..] : "", out var port);
                return (host, port);
            }
        }
        int last = endpoint.LastIndexOf(':');
        if (last > 0)
        {
            int.TryParse(endpoint[(last + 1)..], out var port);
            return (endpoint[..last], port);
        }
        return (endpoint, 0);
    }
}
