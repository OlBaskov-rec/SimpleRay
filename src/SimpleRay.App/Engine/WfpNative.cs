using System.Runtime.InteropServices;

namespace SimpleRay.App.Engine;

/// <summary>
/// P/Invoke surface for the Windows Filtering Platform used by <see cref="WfpKillSwitch"/>.
/// Struct layouts follow fwpmtypes.h / fwptypes.h. UNVERIFIED at runtime here — validate on
/// real hardware (see the kill-switch verification checklist in docs).
/// </summary>
internal static class Native
{
    public const uint RPC_C_AUTHN_WINNT = 10;
    public const uint FWP_E_ALREADY_EXISTS = 0x80320009;

    public const uint FWP_ACTION_FLAG_TERMINATING = 0x00001000;
    public const uint FWP_ACTION_BLOCK = 0x00000001 | FWP_ACTION_FLAG_TERMINATING;
    public const uint FWP_ACTION_PERMIT = 0x00000002 | FWP_ACTION_FLAG_TERMINATING;

    public const uint FWP_CONDITION_FLAG_IS_LOOPBACK = 0x00000001;

    // Layers (fwpmu.h GUIDs).
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V4 = new("c38d57d1-05a7-4c33-904f-7fbceee60e82");
    public static readonly Guid FWPM_LAYER_ALE_AUTH_CONNECT_V6 = new("4a72393b-319f-44bc-84c3-ba54dcb3b6b4");

    // Conditions.
    public static readonly Guid FWPM_CONDITION_FLAGS = new("632ce23b-5167-435c-86d7-e903684aa80c");
    public static readonly Guid FWPM_CONDITION_IP_LOCAL_INTERFACE = new("4cd62a49-59c3-4969-b7f3-bda5d32890a4");
    public static readonly Guid FWPM_CONDITION_ALE_APP_ID = new("d78e1e87-8644-4ea5-9437-d809ecefc971");

    // FWP_DATA_TYPE values we use.
    public const uint FWP_UINT8 = 1;
    public const uint FWP_UINT32 = 4;
    public const uint FWP_UINT64 = 5;
    public const uint FWP_BYTE_BLOB_TYPE = 10;

    // FWP_MATCH_TYPE values.
    public const uint FWP_MATCH_EQUAL = 0;
    public const uint FWP_MATCH_FLAGS_ANY_SET = 6;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct FWPM_DISPLAY_DATA0
    {
        public string name;
        public string description;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_PROVIDER0
    {
        public Guid providerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public FWP_BYTE_BLOB providerData;
        public nint serviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_SUBLAYER0
    {
        public Guid subLayerKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public nint providerKey;       // GUID*
        public FWP_BYTE_BLOB providerData;
        public ushort weight;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWP_BYTE_BLOB
    {
        public uint size;
        public nint data;
    }

    // FWP_VALUE0: 4-byte type, padded, 8-byte value union (x64).
    [StructLayout(LayoutKind.Explicit)]
    public struct FWP_VALUE0
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public byte uint8;
        [FieldOffset(8)] public uint uint32;
        [FieldOffset(8)] public nint value; // pointer for uint64 / byteBlob
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct FWP_CONDITION_VALUE0
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public uint uint32;
        [FieldOffset(8)] public nint value; // pointer for uint64 / byteBlob
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER_CONDITION0
    {
        public Guid fieldKey;
        public uint matchType;
        public FWP_CONDITION_VALUE0 conditionValue;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_ACTION0
    {
        public uint type;
        public Guid filterType; // union member (GUID) — unused, left zero
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FWPM_FILTER0
    {
        public Guid filterKey;
        public FWPM_DISPLAY_DATA0 displayData;
        public uint flags;
        public nint providerKey;        // GUID*
        public FWP_BYTE_BLOB providerData;
        public Guid layerKey;
        public Guid subLayerKey;
        public FWP_VALUE0 weight;
        public uint numFilterConditions;
        public nint filterCondition;    // FWPM_FILTER_CONDITION0*
        public FWPM_ACTION0 action;
        public Guid providerContextKey; // union { UINT64 rawContext; GUID providerContextKey } — GUID is larger
        public nint reserved;           // GUID*
        public ulong filterId;
        public FWP_VALUE0 effectiveWeight;
    }

    public static FWP_VALUE0 FwpValueByte(byte v) => new() { type = FWP_UINT8, uint8 = v };

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineOpen0(
        [MarshalAs(UnmanagedType.LPWStr)] string? serverName, uint authnService, nint authIdentity, nint session, out nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmEngineClose0(nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionBegin0(nint engineHandle, uint flags);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionCommit0(nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmTransactionAbort0(nint engineHandle);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmProviderAdd0(nint engineHandle, ref FWPM_PROVIDER0 provider, nint sd);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmProviderDeleteByKey0(nint engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmSubLayerAdd0(nint engineHandle, ref FWPM_SUBLAYER0 subLayer, nint sd);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmSubLayerDeleteByKey0(nint engineHandle, ref Guid key);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFilterAdd0(nint engineHandle, ref FWPM_FILTER0 filter, nint sd, out ulong id);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmGetAppIdFromFileName0([MarshalAs(UnmanagedType.LPWStr)] string fileName, out nint appId);

    [DllImport("fwpuclnt.dll")]
    public static extern uint FwpmFreeMemory0(ref nint p);

    [DllImport("iphlpapi.dll")]
    public static extern uint ConvertInterfaceAliasToLuid([MarshalAs(UnmanagedType.LPWStr)] string alias, out ulong luid);
}

/// <summary>
/// Builds unmanaged filter-condition arrays and owns the native memory they point at until
/// the filter has been added (disposed by the caller right after FwpmFilterAdd0).
/// </summary>
internal sealed class Conditions : IDisposable
{
    public Native.FWPM_FILTER_CONDITION0[] Items { get; private set; } = Array.Empty<Native.FWPM_FILTER_CONDITION0>();
    public bool Ok { get; private set; } = true;

    private readonly List<nint> _allocs = new();
    private nint _appId; // freed with FwpmFreeMemory0

    public static Conditions Loopback()
    {
        var c = new Conditions();
        c.Items = new[]
        {
            new Native.FWPM_FILTER_CONDITION0
            {
                fieldKey = Native.FWPM_CONDITION_FLAGS,
                matchType = Native.FWP_MATCH_FLAGS_ANY_SET,
                conditionValue = new Native.FWP_CONDITION_VALUE0
                {
                    type = Native.FWP_UINT32,
                    uint32 = Native.FWP_CONDITION_FLAG_IS_LOOPBACK,
                },
            },
        };
        return c;
    }

    public static Conditions LocalInterface(ulong luid)
    {
        var c = new Conditions();
        var p = Marshal.AllocHGlobal(sizeof(ulong));
        Marshal.WriteInt64(p, (long)luid);
        c._allocs.Add(p);
        c.Items = new[]
        {
            new Native.FWPM_FILTER_CONDITION0
            {
                fieldKey = Native.FWPM_CONDITION_IP_LOCAL_INTERFACE,
                matchType = Native.FWP_MATCH_EQUAL,
                conditionValue = new Native.FWP_CONDITION_VALUE0 { type = Native.FWP_UINT64, value = p },
            },
        };
        return c;
    }

    public static Conditions AppId(string exePath)
    {
        var c = new Conditions();
        if (Native.FwpmGetAppIdFromFileName0(exePath, out c._appId) != 0 || c._appId == 0)
        {
            c.Ok = false;
            return c;
        }
        c.Items = new[]
        {
            new Native.FWPM_FILTER_CONDITION0
            {
                fieldKey = Native.FWPM_CONDITION_ALE_APP_ID,
                matchType = Native.FWP_MATCH_EQUAL,
                conditionValue = new Native.FWP_CONDITION_VALUE0 { type = Native.FWP_BYTE_BLOB_TYPE, value = c._appId },
            },
        };
        return c;
    }

    public void Dispose()
    {
        foreach (var p in _allocs)
            Marshal.FreeHGlobal(p);
        _allocs.Clear();
        if (_appId != 0)
        {
            var p = _appId;
            try { Native.FwpmFreeMemory0(ref p); } catch { }
            _appId = 0;
        }
    }
}
