using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SimpleRay.Core.Net;
using Xunit;

namespace SimpleRay.Core.Tests;

public class LatencyProbeTests
{
    [Fact]
    public async Task OpenPort_ReturnsNonNegativeLatency()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var ms = await LatencyProbe.MeasureAsync("127.0.0.1", port);
            Assert.NotNull(ms);
            Assert.True(ms >= 0);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task ClosedPort_ReturnsNull()
    {
        // Reserve then release a port so it's (almost certainly) closed.
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();

        var ms = await LatencyProbe.MeasureAsync("127.0.0.1", port, timeoutMs: 500);
        Assert.Null(ms);
    }

    [Theory]
    [InlineData("", 443)]
    [InlineData("host", 0)]
    [InlineData("host", 70000)]
    public async Task InvalidInput_ReturnsNull(string host, int port)
    {
        Assert.Null(await LatencyProbe.MeasureAsync(host, port));
    }
}
