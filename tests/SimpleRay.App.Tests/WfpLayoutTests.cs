using System.Runtime.InteropServices;
using SimpleRay.App.Engine;

namespace SimpleRay.App.Tests;

/// <summary>
/// Locks the marshalled layout of the WFP structs to the native x64 layout (fwpmtypes.h).
/// A mismatch here is what made FwpmFilterAdd0 read a pointer from the wrong offset and
/// crash with AccessViolation — this catches it without needing admin/WFP at runtime.
/// </summary>
public class WfpLayoutTests
{
    private static int Off(string field) =>
        (int)Marshal.OffsetOf<Native.FWPM_FILTER0>(field);

    [Fact]
    public void FwpmFilter0_MatchesNativeX64Layout()
    {
        // Offsets computed from fwpmtypes.h with natural x64 alignment.
        Assert.Equal(0, Off(nameof(Native.FWPM_FILTER0.filterKey)));
        Assert.Equal(16, Off(nameof(Native.FWPM_FILTER0.displayData)));
        Assert.Equal(32, Off(nameof(Native.FWPM_FILTER0.flags)));
        Assert.Equal(40, Off(nameof(Native.FWPM_FILTER0.providerKey)));
        Assert.Equal(48, Off(nameof(Native.FWPM_FILTER0.providerData)));
        Assert.Equal(64, Off(nameof(Native.FWPM_FILTER0.layerKey)));
        Assert.Equal(80, Off(nameof(Native.FWPM_FILTER0.subLayerKey)));
        Assert.Equal(96, Off(nameof(Native.FWPM_FILTER0.weight)));
        Assert.Equal(112, Off(nameof(Native.FWPM_FILTER0.numFilterConditions)));
        Assert.Equal(120, Off(nameof(Native.FWPM_FILTER0.filterCondition)));
        Assert.Equal(128, Off(nameof(Native.FWPM_FILTER0.action)));
        // The union must be 8-aligned (the earlier GUID field sat at 148 and shifted
        // everything after it, corrupting the struct).
        Assert.Equal(152, Off(nameof(Native.FWPM_FILTER0.providerContext0)));
        Assert.Equal(168, Off(nameof(Native.FWPM_FILTER0.reserved)));
        Assert.Equal(176, Off(nameof(Native.FWPM_FILTER0.filterId)));
        Assert.Equal(184, Off(nameof(Native.FWPM_FILTER0.effectiveWeight)));
        Assert.Equal(200, Marshal.SizeOf<Native.FWPM_FILTER0>());
    }

    [Fact]
    public void HelperStructs_AreExpectedSizes()
    {
        Assert.Equal(16, Marshal.SizeOf<Native.FWP_VALUE0>());        // UINT32 type + 8-byte union (x64)
        Assert.Equal(16, Marshal.SizeOf<Native.FWP_CONDITION_VALUE0>());
        Assert.Equal(16, Marshal.SizeOf<Native.FWP_BYTE_BLOB>());     // UINT32 + pad + pointer
        Assert.Equal(16, Marshal.SizeOf<Native.FWPM_DISPLAY_DATA0>()); // two pointers
        Assert.Equal(20, Marshal.SizeOf<Native.FWPM_ACTION0>());      // UINT32 + GUID
        Assert.Equal(40, Marshal.SizeOf<Native.FWPM_FILTER_CONDITION0>()); // GUID + UINT32 + pad + 16
    }
}
