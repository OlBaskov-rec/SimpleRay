using System;
using System.Threading;
using SimpleRay.App.Services;

namespace SimpleRay.App.Tests;

public class WindowActivationSignalTests
{
    private static string UniqueName() => @"Local\SimpleRay.Tests.ShowWindow." + Guid.NewGuid().ToString("N");

    [Fact]
    public void MissingEvent_CannotBeSignaled() => Assert.False(WindowActivationSignal.TrySignal(UniqueName()));

    [Fact]
    public void SignalBeforeListener_IsDelivered_AndAutoReset()
    {
        var name = UniqueName();
        using var receiver = WindowActivationSignal.Create(name);
        Assert.True(WindowActivationSignal.TrySignal(name));
        using var delivered = new AutoResetEvent(false);
        int calls = 0;
        receiver.Listen(() => { Interlocked.Increment(ref calls); delivered.Set(); });
        Assert.True(delivered.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.False(delivered.WaitOne(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, Volatile.Read(ref calls));
        Assert.True(WindowActivationSignal.TrySignal(name));
        Assert.True(delivered.WaitOne(TimeSpan.FromSeconds(5)));
        Assert.Equal(2, Volatile.Read(ref calls));
    }

    [Fact]
    public void Dispose_ReleasesNamedEvent()
    {
        var name = UniqueName();
        var receiver = WindowActivationSignal.Create(name);
        receiver.Listen(() => { });
        Assert.True(WindowActivationSignal.TrySignal(name));
        receiver.Dispose();
        Assert.False(WindowActivationSignal.TrySignal(name));
    }
}
