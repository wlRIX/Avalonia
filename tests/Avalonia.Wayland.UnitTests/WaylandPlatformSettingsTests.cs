using Tmds.DBus.Protocol;
using Xunit;

namespace Avalonia.Wayland.UnitTests;

public class WaylandPlatformSettingsTests
{
    [Fact]
    public void DoubleClick_Is_Read_As_Milliseconds()
    {
        Assert.Equal(250, WaylandPlatformSettings.ToDoubleClickTime(VariantValue.Int32(250)));
        Assert.Equal(250, WaylandPlatformSettings.ToDoubleClickTime(VariantValue.UInt32(250)));
    }

    [Fact]
    public void DoubleClick_Unwraps_The_Settings_V1_Variant() =>
        Assert.Equal(400,
            WaylandPlatformSettings.ToDoubleClickTime(VariantValue.Variant(VariantValue.Int32(400))));

    [Fact]
    public void DoubleClick_Ignores_Unusable_Values()
    {
        Assert.Null(WaylandPlatformSettings.ToDoubleClickTime(VariantValue.String("400")));
        Assert.Null(WaylandPlatformSettings.ToDoubleClickTime(VariantValue.Int32(0)));
        Assert.Null(WaylandPlatformSettings.ToDoubleClickTime(VariantValue.Int32(-1)));
        Assert.Null(WaylandPlatformSettings.ToDoubleClickTime(VariantValue.Int32(60_000)));
    }
}
