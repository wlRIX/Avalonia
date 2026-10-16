using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.FreeDesktop;
using Avalonia.FreeDesktop.DBus;
using Avalonia.Input;
using Tmds.DBus.Protocol;

namespace Avalonia.Wayland;

/// <summary>
/// <see cref="DBusPlatformSettings"/>, plus the desktop's double-click time.
/// </summary>
/// <remarks>
/// Wayland has no protocol for the double-click time, so a toolkit takes it from the settings
/// portal: GTK reads <c>org.gnome.desktop.peripherals.mouse</c> <c>double-click</c>, in
/// milliseconds, and follows <c>SettingChanged</c> for it. Without this Avalonia uses a fixed
/// 500 ms, so a double click that works in every other application on the desktop could fail
/// here, or the other way round.
///
/// It lives here rather than in <see cref="DBusPlatformSettings"/> itself only so the patched
/// <c>Avalonia.Wayland</c> package is all an application has to take.
/// </remarks>
internal sealed class WaylandPlatformSettings : DBusPlatformSettings
{
    private const string MouseNamespace = "org.gnome.desktop.peripherals.mouse";
    private const string DoubleClickKey = "double-click";

    // Written from the bus, read from the UI thread on every press; -1 is "not known".
    private long _doubleClickMilliseconds = -1;

    public WaylandPlatformSettings()
    {
        if (DBusHelper.DefaultConnection is not { } connection)
            return;

        var settings = new Settings(connection, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop");
        _ = settings.WatchSettingChangedAsync(OnSettingChanged);
        _ = ReadDoubleClickAsync(settings);
    }

    public override TimeSpan GetDoubleTapTime(PointerType type)
    {
        var milliseconds = Interlocked.Read(ref _doubleClickMilliseconds);
        return type == PointerType.Mouse && milliseconds > 0
            ? TimeSpan.FromMilliseconds(milliseconds)
            : base.GetDoubleTapTime(type);
    }

    /// <summary>
    /// The double-click time a portal value stands for, or null for one not to use: a value of
    /// the wrong type, or a time no one could click twice within (or would wait out).
    /// </summary>
    internal static int? ToDoubleClickTime(VariantValue value)
    {
        // Settings v1's Read wraps the value in a second variant; v2's ReadOne and the signal do
        // not. Unwrapping whatever is there serves all three.
        while (value.Type == VariantValueType.Variant)
            value = value.GetVariantValue();

        int milliseconds;
        switch (value.Type)
        {
            case VariantValueType.Int32:
                milliseconds = value.GetInt32();
                break;
            case VariantValueType.UInt32:
                milliseconds = (int)Math.Min(value.GetUInt32(), int.MaxValue);
                break;
            default:
                return null;
        }

        return milliseconds is >= 50 and <= 5000 ? milliseconds : null;
    }

    private async Task ReadDoubleClickAsync(Settings settings)
    {
        try
        {
            var version = await settings.GetVersionAsync();
            var value = version >= 2
                ? await settings.ReadOneAsync(MouseNamespace, DoubleClickKey)
                : await settings.ReadAsync(MouseNamespace, DoubleClickKey);
            Store(ToDoubleClickTime(value));
        }
        catch (DBusExceptionBase)
        {
            // No portal, or none that answers for this key: Avalonia's own default stands.
        }
    }

    private void OnSettingChanged((string Namespace, string Key, VariantValue Value) setting)
    {
        if (setting is (MouseNamespace, DoubleClickKey, var value))
            Store(ToDoubleClickTime(value));
    }

    private void Store(int? milliseconds) =>
        Interlocked.Exchange(ref _doubleClickMilliseconds, milliseconds ?? -1);
}
