using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using SimpleRay.Core.Dns;

namespace SimpleRay.App.Services;

internal sealed record LanDnsAdapter(string Name, bool IsUp, bool HasDefaultGateway,
    string DnsSuffix, IReadOnlyList<string> DnsServers, bool IsLoopback = false);

internal static class LanDnsDetector
{
    /// <summary>Pure selection in adapter order; DHCP suffixes cannot redirect public domains.</summary>
    internal static LanDns? Select(IEnumerable<LanDnsAdapter> adapters, string tunInterfaceName = "simpleray")
    {
        foreach (var adapter in adapters)
        {
            if (!adapter.IsUp || adapter.IsLoopback || !adapter.HasDefaultGateway ||
                string.Equals(adapter.Name, tunInterfaceName, StringComparison.OrdinalIgnoreCase))
                continue;

            var suffix = adapter.DnsSuffix.Trim('.').ToLowerInvariant();
            if (!IsInternalSuffix(suffix)) continue;
            var server = adapter.DnsServers.FirstOrDefault(IsPrivateIpv4);
            if (server is not null) return new LanDns(new[] { suffix }, server);
        }
        return null;
    }

    internal static LanDns? Detect(string tunInterfaceName = "simpleray") =>
        Select(NetworkInterface.GetAllNetworkInterfaces().Select(adapter =>
        {
            // Avoid requesting IP properties of adapters we cannot use.
            if (adapter.OperationalStatus != OperationalStatus.Up ||
                adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                string.Equals(adapter.Name, tunInterfaceName, StringComparison.OrdinalIgnoreCase))
                return new LanDnsAdapter(adapter.Name, false, false, "", Array.Empty<string>());
            var ip = adapter.GetIPProperties();
            return new LanDnsAdapter(adapter.Name, true,
                ip.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any) &&
                    !g.Address.Equals(IPAddress.IPv6Any)),
                ip.DnsSuffix, ip.DnsAddresses.Select(a => a.ToString()).ToArray());
        }), tunInterfaceName);

    private static bool IsPrivateIpv4(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
            (b[0] == 192 && b[1] == 168);
    }

    private static bool IsInternalSuffix(string suffix)
    {
        if (suffix.Length == 0 || suffix.Any(c =>
                !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-')))
            return false;
        var labels = suffix.Split('.');
        if (labels.Any(l => l.Length == 0 || l.StartsWith('-') || l.EndsWith('-')))
            return false;
        // A single label must itself be internal: a DHCP suffix of "com" or "ru" would
        // otherwise send every .com/.ru lookup to the network's own DNS server.
        return labels[^1] is
            "local" or "lan" or "home" or "corp" or "internal" or "intranet" or "localdomain" or "private" ||
            suffix == "home.arpa" || suffix.EndsWith(".home.arpa", StringComparison.Ordinal);
    }
}
