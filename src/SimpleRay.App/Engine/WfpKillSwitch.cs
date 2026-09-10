using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SimpleRay.App.Engine;

/// <summary>
/// Kill-switch backed by the Windows Filtering Platform (WFP). While engaged, a block
/// filter drops all outbound traffic except: the TUN interface (tunnel traffic),
/// sing-box.exe (so it can reach the server over the physical link), and loopback.
///
/// Fail-closed: filters live in a NON-dynamic session, so they survive an app crash
/// (no leak). They are NOT boot-persistent, so a reboot always clears them (a final
/// backstop against a permanently blocked machine), and <see cref="CleanupLeftovers"/>
/// removes them on the next launch. Requires administrator rights (the app already
/// elevates for TUN); if WFP can't be opened, Engage throws and the caller degrades.
///
/// UNVERIFIED at runtime in this repo's CI/dev environment (needs a real machine with
/// admin + a live tunnel to confirm blocking and recovery). Off by default.
/// </summary>
public sealed class WfpKillSwitch : IKillSwitch
{
    // Stable identity for everything we add, so CleanupLeftovers can find and remove it.
    private static readonly Guid ProviderKey = new("6f1e2a54-9b7c-4c3a-8d21-5a2f9c0b1e77");
    private static readonly Guid SubLayerKey = new("6f1e2a55-9b7c-4c3a-8d21-5a2f9c0b1e77");
    private const string Name = "SimpleRay kill-switch";

    // The filter tags we may add (per layer). Cleanup deletes filters by deterministic key
    // built from (layer, tag) — deleting the sublayer does NOT cascade to its filters, so
    // without per-key deletion the filters accumulate across runs.
    private static readonly string[] FilterTags =
    {
        "block-all", "permit-loopback", "permit-tun", "permit-singbox",
        // LAN permits (see LanV4/LanV6). Cleanup tries every tag on both layers; a v4 tag on the
        // v6 layer (or vice-versa) simply isn't found and is skipped.
        "permit-lan-10", "permit-lan-172", "permit-lan-192", "permit-lan-169", "permit-lan-mcast", "permit-lan-bcast",
        "permit-lan-v6ll", "permit-lan-v6ula", "permit-lan-v6mcast",
    };

    // Private / link-local / multicast / broadcast ranges kept reachable while engaged, so the
    // kill-switch blocks the internet but not the LAN. These are non-routable, so permitting them
    // never opens an internet leak. IPv4 addr/mask are host byte order.
    private static readonly (string tag, uint addr, uint mask)[] LanV4 =
    {
        ("permit-lan-10",    0x0A000000, 0xFF000000), // 10.0.0.0/8
        ("permit-lan-172",   0xAC100000, 0xFFF00000), // 172.16.0.0/12
        ("permit-lan-192",   0xC0A80000, 0xFFFF0000), // 192.168.0.0/16
        ("permit-lan-169",   0xA9FE0000, 0xFFFF0000), // 169.254.0.0/16 link-local
        ("permit-lan-mcast", 0xE0000000, 0xF0000000), // 224.0.0.0/4 multicast (mDNS/SSDP)
        ("permit-lan-bcast", 0xFFFFFFFF, 0xFFFFFFFF), // 255.255.255.255 broadcast
    };

    private static readonly (string tag, byte[] addr, byte prefix)[] LanV6 =
    {
        ("permit-lan-v6ll",    Ip6(0xFE, 0x80), 10), // fe80::/10 link-local
        ("permit-lan-v6ula",   Ip6(0xFC),        7), // fc00::/7 unique-local
        ("permit-lan-v6mcast", Ip6(0xFF),        8), // ff00::/8 multicast
    };

    private static byte[] Ip6(params byte[] lead)
    {
        var a = new byte[16];
        Array.Copy(lead, a, lead.Length);
        return a;
    }

    private readonly object _gate = new();
    private nint _engine;

    public bool IsEngaged { get; private set; }

    public void Engage(string tunInterfaceName, string allowedExePath)
        => Engage(tunInterfaceName, allowedExePath, requireTunPermit: true);

    public void Engage(string tunInterfaceName, string allowedExePath, bool requireTunPermit)
        => EngageCore(tunInterfaceName, allowedExePath, requireTunPermit);

    private void EngageCore(string tun, string exe, bool requireTunPermit)
    {
        lock (_gate)
        {
            if (IsEngaged) return;

            // Wait for the TUN adapter so permit-tun is actually installed. sing-box brings the
            // adapter up a second or two AFTER the process starts, and the app engages right after
            // start — without permit-tun, block-all drops ALL tunnelled traffic (the browser's
            // connections egress via the TUN) and the machine goes fully offline. If it never
            // appears, fail-open unless block-only mode was explicitly requested by the harness.
            ulong tunLuid = WaitForLuid(tun, TimeSpan.FromSeconds(8));
            // Fail-open: better to stay connected without the kill-switch than to black-hole the
            // whole machine. The caller surfaces this as "kill-switch could not be enabled".
            if (requireTunPermit && tunLuid == 0)
                throw new InvalidOperationException(
                    "Kill-switch not engaged: the TUN adapter did not appear, so blocking would cut all traffic. The connection stays up without the kill-switch.");

            Open();
            try
            {
                Check(Native.FwpmTransactionBegin0(_engine, 0), "transaction begin");
                EnsureProviderAndSubLayer();
                AddBlockAndPermitFilters(tunLuid, exe);
                Check(Native.FwpmTransactionCommit0(_engine), "commit");
                IsEngaged = true;
            }
            catch
            {
                try { Native.FwpmTransactionAbort0(_engine); } catch { }
                CloseNoLock();
                throw;
            }
        }
    }

    public void Disengage()
    {
        lock (_gate)
        {
            if (_engine == 0) { IsEngaged = false; return; }
            try { DeleteOurObjects(_engine); } catch { /* best-effort */ }
            CloseNoLock();
            IsEngaged = false;
        }
    }

    public void Dispose() => Disengage();

    /// <summary>
    /// Remove any filters left over from a previous run (e.g. an app crash while engaged).
    /// Safe to call on every startup; no-op if nothing is present or WFP can't be opened.
    /// </summary>
    public static void CleanupLeftovers()
    {
        nint engine = 0;
        try
        {
            if (Native.FwpmEngineOpen0(null, Native.RPC_C_AUTHN_WINNT, 0, 0, out engine) != 0)
                return;
            DeleteOurObjects(engine);
        }
        catch { /* best-effort recovery */ }
        finally
        {
            if (engine != 0) { try { Native.FwpmEngineClose0(engine); } catch { } }
        }
    }

    // --- internals --------------------------------------------------------

    private void Open()
    {
        if (_engine != 0) return;
        // Non-dynamic session: objects persist past this handle (fail-closed on crash).
        Check(Native.FwpmEngineOpen0(null, Native.RPC_C_AUTHN_WINNT, 0, 0, out _engine), "open");
    }

    private void CloseNoLock()
    {
        if (_engine == 0) return;
        try { Native.FwpmEngineClose0(_engine); } catch { }
        _engine = 0;
    }

    private void EnsureProviderAndSubLayer()
    {
        var allocs = new List<nint>();
        try
        {
            var provider = new Native.FWPM_PROVIDER0
            {
                providerKey = ProviderKey,
                displayData = Display(Name, Name, allocs),
            };
            // AlreadyExists is fine — we reuse a stable identity.
            var r = Native.FwpmProviderAdd0(_engine, ref provider, 0);
            if (r != 0 && r != Native.FWP_E_ALREADY_EXISTS) Check(r, "provider add");

            var sub = new Native.FWPM_SUBLAYER0
            {
                subLayerKey = SubLayerKey,
                displayData = Display(Name, Name, allocs),
                providerKey = nint.Zero, // optional GUID* — leave null
                weight = 0xFFFF,
            };
            var rs = Native.FwpmSubLayerAdd0(_engine, ref sub, 0);
            if (rs != 0 && rs != Native.FWP_E_ALREADY_EXISTS) Check(rs, "sublayer add");
        }
        finally { FreeAll(allocs); }
    }

    /// <summary>Builds a display-data with LPWSTR pointers, tracking them for later free.</summary>
    private static Native.FWPM_DISPLAY_DATA0 Display(string name, string description, List<nint> allocs)
    {
        var n = Marshal.StringToHGlobalUni(name);
        var d = Marshal.StringToHGlobalUni(description);
        allocs.Add(n); allocs.Add(d);
        return new Native.FWPM_DISPLAY_DATA0 { name = n, description = d };
    }

    private static void FreeAll(List<nint> allocs)
    {
        foreach (var p in allocs) Marshal.FreeHGlobal(p);
    }

    private void AddBlockAndPermitFilters(ulong tunLuid, string allowedExePath)
    {
        foreach (var layer in new[] { Native.FWPM_LAYER_ALE_AUTH_CONNECT_V4, Native.FWPM_LAYER_ALE_AUTH_CONNECT_V6 })
        {
            // 1) Block everything (lowest weight).
            AddFilter(layer, "block-all", weight: 0, block: true, conditions: Array.Empty<Native.FWPM_FILTER_CONDITION0>());

            // 2) Permit loopback (highest weight).
            using (var loopback = Conditions.Loopback())
                AddFilter(layer, "permit-loopback", weight: 15, block: false, loopback.Items);

            // 3) Permit the TUN interface (tunnel traffic). Skipped only if the adapter never
            //    came up (luid 0) — e.g. the harness without a running sing-box.
            if (tunLuid != 0)
                using (var iface = Conditions.LocalInterface(tunLuid))
                    AddFilter(layer, "permit-tun", weight: 12, block: false, iface.Items);

            // 4) Permit sing-box.exe so it can reach the server over the physical NIC.
            using (var app = Conditions.AppId(allowedExePath))
                if (app.Ok)
                    AddFilter(layer, "permit-singbox", weight: 10, block: false, app.Items);

            // 5) Permit the local network so LAN stays reachable while the internet is blocked.
            //    LAN ranges are non-routable, so this never opens an internet leak.
            if (layer == Native.FWPM_LAYER_ALE_AUTH_CONNECT_V4)
                foreach (var (tag, addr, mask) in LanV4)
                    using (var lan = Conditions.RemoteV4Subnet(addr, mask))
                        AddFilter(layer, tag, weight: 8, block: false, lan.Items);
            else
                foreach (var (tag, addr, prefix) in LanV6)
                    using (var lan = Conditions.RemoteV6Subnet(addr, prefix))
                        AddFilter(layer, tag, weight: 8, block: false, lan.Items);
        }
    }

    // FWPM_FILTER0 field offsets (x64, from fwpmtypes.h — verified in WfpLayoutTests).
    private const int OffFilterKey = 0, OffDisplayName = 16, OffDisplayDesc = 24, OffLayerKey = 64, OffSubLayerKey = 80,
        OffWeightType = 96, OffWeightValue = 104, OffNumConditions = 112, OffFilterCondition = 120,
        OffActionType = 128, FilterSize = 200;

    /// <summary>Deterministic filter key from (layer, tag) so cleanup can delete it by key across runs.</summary>
    private static Guid FilterKey(Guid layer, string tag)
    {
        var bytes = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"SimpleRay-killswitch|{layer}|{tag}"));
        return new Guid(bytes);
    }

    private void AddFilter(Guid layer, string tag, byte weight, bool block, Native.FWPM_FILTER_CONDITION0[] conditions)
    {
        // Build the FWPM_FILTER0 as raw bytes and pass a pointer — passing the struct by
        // `ref` crashes FwpmFilterAdd0 (AccessViolation) despite a byte-correct layout.
        var name = Marshal.StringToHGlobalUni(Name);
        var desc = Marshal.StringToHGlobalUni(tag);
        var condHandle = conditions.Length > 0
            ? GCHandle.Alloc(conditions, GCHandleType.Pinned)
            : default;
        try
        {
            var buf = new byte[FilterSize]; // zero-initialised
            WriteGuid(buf, OffFilterKey, FilterKey(layer, tag)); // deterministic, for cleanup-by-key
            WritePtr(buf, OffDisplayName, name);
            WritePtr(buf, OffDisplayDesc, desc);
            WriteGuid(buf, OffLayerKey, layer);
            WriteGuid(buf, OffSubLayerKey, SubLayerKey);
            WriteU32(buf, OffWeightType, Native.FWP_UINT8);
            WriteI64(buf, OffWeightValue, weight);
            WriteU32(buf, OffNumConditions, (uint)conditions.Length);
            WritePtr(buf, OffFilterCondition, conditions.Length > 0 ? condHandle.AddrOfPinnedObject() : nint.Zero);
            WriteU32(buf, OffActionType, block ? Native.FWP_ACTION_BLOCK : Native.FWP_ACTION_PERMIT);
            // providerKey / providerData / union / reserved / filterId / effectiveWeight stay zero.

            var bufHandle = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try
            {
                var r = Native.FwpmFilterAdd0(_engine, bufHandle.AddrOfPinnedObject(), nint.Zero, out _);
                // AlreadyExists means a prior cleanup missed it; the filter is present, so it's fine.
                if (r != 0 && r != Native.FWP_E_ALREADY_EXISTS) Check(r, "filter add " + tag);
            }
            finally { bufHandle.Free(); }
        }
        finally
        {
            if (condHandle.IsAllocated) condHandle.Free();
            Marshal.FreeHGlobal(name);
            Marshal.FreeHGlobal(desc);
        }
    }

    private static void WriteU32(byte[] b, int off, uint v) => BitConverter.GetBytes(v).CopyTo(b, off);
    private static void WriteI64(byte[] b, int off, long v) => BitConverter.GetBytes(v).CopyTo(b, off);
    private static void WritePtr(byte[] b, int off, nint p) => BitConverter.GetBytes((long)p).CopyTo(b, off);
    private static void WriteGuid(byte[] b, int off, Guid g) => g.ToByteArray().CopyTo(b, off);

    private static void DeleteOurObjects(nint engine)
    {
        // Delete filters by their deterministic keys FIRST: deleting the sublayer does not
        // cascade to filters (and would fail while they reference it), so they'd otherwise
        // pile up across runs. Then remove the now-unused sublayer and provider.
        foreach (var layer in new[] { Native.FWPM_LAYER_ALE_AUTH_CONNECT_V4, Native.FWPM_LAYER_ALE_AUTH_CONNECT_V6 })
            foreach (var tag in FilterTags)
            {
                var key = FilterKey(layer, tag);
                try { Native.FwpmFilterDeleteByKey0(engine, ref key); } catch { }
            }
        var sub = SubLayerKey;
        var prov = ProviderKey;
        try { Native.FwpmSubLayerDeleteByKey0(engine, ref sub); } catch { }
        try { Native.FwpmProviderDeleteByKey0(engine, ref prov); } catch { }
    }

    /// <summary>
    /// Resolves the interface LUID, polling until the adapter appears or <paramref name="timeout"/>
    /// elapses (sing-box brings the TUN up a beat after the process starts). Returns 0 if it never
    /// appears — the caller then engages without permit-tun (block-all only), which is what the
    /// harness (no sing-box) wants but must never happen in the app once connected.
    /// </summary>
    private static ulong WaitForLuid(string interfaceName, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                if (Native.ConvertInterfaceAliasToLuid(interfaceName, out ulong luid) == 0 && luid != 0)
                    return luid;
            }
            catch { /* keep polling */ }

            if (sw.Elapsed >= timeout)
                return 0;
            Thread.Sleep(200);
        }
    }

    private static void Check(uint error, string what)
    {
        if (error != 0)
            throw new InvalidOperationException($"WFP {what} failed (0x{error:X8}).");
    }
}
