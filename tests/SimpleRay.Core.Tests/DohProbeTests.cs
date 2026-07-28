using System.Net;
using SimpleRay.Core.Net;
using Xunit;

namespace SimpleRay.Core.Tests;

public class DohProbeTests
{
    [Fact]
    public void Request_NegotiatesHttp2()
    {
        // Regression guard: setting the version on HttpClient.DefaultRequestVersion does
        // NOT carry to a manually built HttpRequestMessage, so the probe would fall back
        // to HTTP/1.1 and wrongly report HTTP/2-only resolvers (Quad9) as unreachable.
        using var request = DohProbe.BuildRequest("9.9.9.9");

        Assert.Equal(HttpVersion.Version20, request.Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrHigher, request.VersionPolicy);
    }

    [Fact]
    public void Request_IsRfc8484DnsQuery()
    {
        using var request = DohProbe.BuildRequest("1.1.1.1");

        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.StartsWith("https://1.1.1.1/dns-query?dns=", request.RequestUri!.ToString());
        Assert.Contains(request.Headers.Accept, h => h.MediaType == "application/dns-message");
    }
}
