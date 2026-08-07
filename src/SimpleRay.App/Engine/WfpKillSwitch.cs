using System.Net.NetworkInformation;
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

    private readonly object _gate = new();
    private nint _engine;

    public bool IsEngaged { get; private set; }

    public void Engage(string tunInterfaceName, string allowedExePath)
    {
        lock (_gate)
        {
            if (IsEngaged) return;

            Open();
            try
            {
                Native.FwpmTransactionBegin0(_engine, 0);
                EnsureProviderAndSubLayer();
                AddBlockAndPermitFilters(tunInterfaceName, allowedExePath);
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

    private void AddBlockAndPermitFilters(string tunInterfaceName, string allowedExePath)
    {
        foreach (var layer in new[] { Native.FWPM_LAYER_ALE_AUTH_CONNECT_V4, Native.FWPM_LAYER_ALE_AUTH_CONNECT_V6 })
        {
            // 1) Block everything (lowest weight).
            AddFilter(layer, "block-all", weight: 0, block: true, conditions: Array.Empty<Native.FWPM_FILTER_CONDITION0>());

            // 2) Permit loopback (highest weight).
            using (var loopback = Conditions.Loopback())
                AddFilter(layer, "permit-loopback", weight: 15, block: false, loopback.Items);

            // 3) Permit the TUN interface (tunnel traffic).
            if (TryGetLuid(tunInterfaceName, out ulong luid))
                using (var iface = Conditions.LocalInterface(luid))
                    AddFilter(layer, "permit-tun", weight: 12, block: false, iface.Items);

            // 4) Permit sing-box.exe so it can reach the server over the physical NIC.
            using (var app = Conditions.AppId(allowedExePath))
                if (app.Ok)
                    AddFilter(layer, "permit-singbox", weight: 10, block: false, app.Items);
        }
    }

    private void AddFilter(Guid layer, string tag, byte weight, bool block, Native.FWPM_FILTER_CONDITION0[] conditions)
    {
        var allocs = new List<nint>();
        var handle = GCHandle.Alloc(conditions, GCHandleType.Pinned);
        try
        {
            var filter = new Native.FWPM_FILTER0
            {
                displayData = Display(Name, tag, allocs),
                layerKey = layer,
                subLayerKey = SubLayerKey,
                weight = Native.FwpValueByte(weight),
                numFilterConditions = (uint)conditions.Length,
                filterCondition = conditions.Length == 0 ? nint.Zero : handle.AddrOfPinnedObject(),
                action = new Native.FWPM_ACTION0
                {
                    type = block ? Native.FWP_ACTION_BLOCK : Native.FWP_ACTION_PERMIT,
                },
                providerKey = nint.Zero,
            };
            Check(Native.FwpmFilterAdd0(_engine, ref filter, 0, out _), "filter add " + tag);
        }
        finally
        {
            if (handle.IsAllocated) handle.Free();
            FreeAll(allocs);
        }
    }

    private static void DeleteOurObjects(nint engine)
    {
        // Deleting the sublayer removes the filters that reference it; then the provider.
        var sub = SubLayerKey;
        var prov = ProviderKey;
        try { Native.FwpmSubLayerDeleteByKey0(engine, ref sub); } catch { }
        try { Native.FwpmProviderDeleteByKey0(engine, ref prov); } catch { }
    }

    private static bool TryGetLuid(string interfaceName, out ulong luid)
    {
        luid = 0;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!string.Equals(ni.Name, interfaceName, StringComparison.OrdinalIgnoreCase))
                    continue;
                // Resolve the interface LUID from its alias.
                if (Native.ConvertInterfaceAliasToLuid(interfaceName, out luid) == 0)
                    return true;
            }
            // Fall back to a direct alias lookup even if enumeration didn't match yet.
            return Native.ConvertInterfaceAliasToLuid(interfaceName, out luid) == 0;
        }
        catch { return false; }
    }

    private static void Check(uint error, string what)
    {
        if (error != 0)
            throw new InvalidOperationException($"WFP {what} failed (0x{error:X8}).");
    }
}
