using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace SimpleRay.App.Services;

/// <summary>Session-local activation that remains writable from Medium integrity.</summary>
internal sealed class WindowActivationSignal : IDisposable
{
    internal const string EventName = @"Local\SimpleRay.ShowWindow";
    private const uint ModifyState = 0x0002;
    private readonly EventWaitHandle _event;
    private RegisteredWaitHandle? _registration;

    private WindowActivationSignal(EventWaitHandle signal) => _event = signal;

    internal static WindowActivationSignal Create(string name = EventName)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value
            ?? throw new InvalidOperationException("The current user has no SID.");
        // Full control for this user/system; authenticated users can only signal/wait.
        // Medium mandatory label lets an unelevated relaunch write an elevated event.
        var sddl = $"D:P(A;;GA;;;SY)(A;;GA;;;{sid})(A;;0x00100002;;;AU)S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor
            };
            var handle = CreateEventW(ref attributes, false, false, name);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error);
            }
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset);
            signal.SafeWaitHandle.Dispose();
            signal.SafeWaitHandle = handle;
            return new WindowActivationSignal(signal);
        }
        finally { LocalFree(descriptor); }
    }

    internal void Listen(Action showWindow)
    {
        _registration = ThreadPool.RegisterWaitForSingleObject(_event,
            (_, _) => showWindow(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    internal static bool TrySignal(string name = EventName)
    {
        using var handle = OpenEventW(ModifyState, false, name);
        return !handle.IsInvalid && SetEvent(handle);
    }

    public void Dispose()
    {
        if (_registration is not null)
        {
            using var complete = new ManualResetEvent(false);
            if (_registration.Unregister(complete)) complete.WaitOne();
            _registration = null;
        }
        _event.Dispose();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public nint SecurityDescriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string descriptor, uint revision, out nint securityDescriptor, out uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeWaitHandle CreateEventW(ref SecurityAttributes attributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeWaitHandle OpenEventW(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(SafeWaitHandle handle);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint memory);
}
