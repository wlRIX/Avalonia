using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Input;
using Avalonia.SourceGenerator;
using Avalonia.Wayland.Server.Interop;
using NWayland.Interop;
using NWayland.Protocols.Wayland;

namespace Avalonia.Wayland.Server.Transient;

partial class WaylandCursorManager : IDisposable
{
    /// <summary>
    /// The size to load when <c>XCURSOR_SIZE</c> says nothing, which is what every toolkit
    /// assumes in the same situation.
    /// </summary>
    private const int DefaultCursorSize = 24;

    private IntPtr _theme;
    private readonly Dictionary<StandardCursorType, CursorEntry?> _cursors = new();
    private readonly WlDisplay _display;
    private readonly WlCompositor _compositor;

    [DefinitelyNotARecord]
    internal readonly partial struct CursorEntry(WlSurface Surface, WlBuffer buffer, int HotspotX, int HotspotY);

    private static readonly Dictionary<StandardCursorType, string[]> CursorNames = new()
    {
        { StandardCursorType.Arrow, ["default", "left_ptr"] },
        { StandardCursorType.Ibeam, ["text", "xterm"] },
        { StandardCursorType.Wait, ["wait", "watch"] },
        { StandardCursorType.Cross, ["crosshair", "cross"] },
        { StandardCursorType.UpArrow, ["up_arrow", "sb_up_arrow"] },
        { StandardCursorType.SizeWestEast, ["ew-resize", "col-resize", "sb_h_double_arrow"] },
        { StandardCursorType.SizeNorthSouth, ["ns-resize", "row-resize", "sb_v_double_arrow"] },
        { StandardCursorType.SizeAll, ["all-scroll", "fleur"] },
        { StandardCursorType.No, ["not-allowed", "crossed_circle"] },
        { StandardCursorType.Hand, ["pointer", "hand2", "pointing_hand"] },
        { StandardCursorType.AppStarting, ["progress", "left_ptr_watch"] },
        { StandardCursorType.Help, ["help", "question_arrow"] },
        { StandardCursorType.TopSide, ["top_side", "n-resize"] },
        { StandardCursorType.BottomSide, ["bottom_side", "s-resize"] },
        { StandardCursorType.LeftSide, ["left_side", "w-resize"] },
        { StandardCursorType.RightSide, ["right_side", "e-resize"] },
        { StandardCursorType.TopLeftCorner, ["top_left_corner", "nw-resize"] },
        { StandardCursorType.TopRightCorner, ["top_right_corner", "ne-resize"] },
        { StandardCursorType.BottomLeftCorner, ["bottom_left_corner", "sw-resize"] },
        { StandardCursorType.BottomRightCorner, ["bottom_right_corner", "se-resize"] },
        // The dnd- names first, and that ordering is the point rather than an accident. A
        // theme that ships them has drawn them *for dragging*; the generic names are what a
        // theme falls back on when it has not. Taking `grabbing` first picked the SGI theme's
        // closedhand, which is itself an alias for fleur -- so a move drop showed the
        // four-way window-move arrows instead of the drag cursor sitting unused beside them,
        // and copy, move and link were three names for two pictures.
        { StandardCursorType.DragMove, ["dnd-move", "grabbing"] },
        { StandardCursorType.DragCopy, ["dnd-copy", "copy"] },
        { StandardCursorType.DragLink, ["dnd-link", "alias"] },
    };

    public WaylandCursorManager(WlDisplay display, WlShm shm, WlCompositor compositor)
    {
        _display = display;
        _compositor = compositor;

        // The desktop's theme, not "whatever this machine calls default".
        //
        // libwayland-cursor does not read XCURSOR_THEME itself -- it takes the theme name as an
        // argument and treats null as "default" -- so a client that passes null gets the machine
        // default (Adwaita on most distributions) however the session is themed. Every other
        // toolkit reads the variable itself; this is that.
        //
        // XCURSOR_PATH, which decides where a theme is searched for, *is* honored inside
        // libwayland-cursor, so only the name and size are ours to pass.
        var size = CursorSize();
        var name = Environment.GetEnvironmentVariable("XCURSOR_THEME");
        if (string.IsNullOrWhiteSpace(name))
            name = null;

        _theme = UnsafeNativeMethods.wl_cursor_theme_load(name, size, shm.Handle);
        if (_theme == IntPtr.Zero)
            throw new AvaloniaWaylandException("Failed to load default cursor theme");

        foreach (var type in CursorNames.Keys)
            LoadCursor(type);

        // A named theme that is not installed loads as an empty one rather than failing, and an
        // empty theme means every lookup below returns null -- which this class reads as "hide the
        // cursor". A misspelled XCURSOR_THEME would therefore leave the app with no pointer at
        // all, so fall back to the machine default and keep one.
        if (name is not null && (!_cursors.TryGetValue(StandardCursorType.Arrow, out var arrow) || arrow is null))
        {
            ReleaseCursors();
            UnsafeNativeMethods.wl_cursor_theme_destroy(_theme);

            _theme = UnsafeNativeMethods.wl_cursor_theme_load(null, size, shm.Handle);
            if (_theme == IntPtr.Zero)
                throw new AvaloniaWaylandException("Failed to load default cursor theme");

            foreach (var type in CursorNames.Keys)
                LoadCursor(type);
        }
    }

    /// <summary>
    /// The size from <c>XCURSOR_SIZE</c>, or <see cref="DefaultCursorSize"/>.
    /// </summary>
    /// <remarks>
    /// Nominal rather than literal: libwayland-cursor picks the nearest size the theme actually
    /// carries. A value that is not a positive number is ignored rather than passed on, since
    /// asking for size 0 matches a theme's smallest image and leaves a speck on screen.
    /// </remarks>
    private static int CursorSize()
    {
        var value = Environment.GetEnvironmentVariable("XCURSOR_SIZE");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size > 0
            ? size
            : DefaultCursorSize;
    }

    private void ReleaseCursors()
    {
        foreach (var entry in _cursors.Values)
            entry?.Surface.Dispose();
        _cursors.Clear();
    }

    private unsafe void LoadCursor(StandardCursorType type)
    {
        if (!CursorNames.TryGetValue(type, out var names))
            return;

        UnsafeNativeMethods.wl_cursor* cursor = null;
        foreach (var name in names)
        {
            cursor = UnsafeNativeMethods.wl_cursor_theme_get_cursor(_theme, name);
            if (cursor != null)
                break;
        }

        if (cursor == null || cursor->image_count == 0)
        {
            _cursors[type] = null;
            return;
        }

        var image = cursor->images[0];
        var buffer = UnsafeNativeMethods.wl_cursor_image_get_buffer(image);
        if (buffer == IntPtr.Zero)
        {
            _cursors[type] = null;
            return;
        }

        var surface = _compositor.CreateSurface(null);
        var wlBuffer =  WlBuffer.Import(this._display, null, buffer, true, null);
        surface.Attach(wlBuffer, 0, 0);
        surface.Commit();

        _cursors[type] = new CursorEntry(surface, wlBuffer, (int)image->hotspot_x, (int)image->hotspot_y);
    }

    /// <summary>
    /// Gets the cursor surface and hotspot for the given cursor type.
    /// Returns null for <see cref="StandardCursorType.None"/> or unknown cursors (hides cursor).
    /// Falls back to Arrow if the specific cursor is not available.
    /// </summary>
    internal CursorEntry? GetCursor(StandardCursorType type)
    {
        if (type == StandardCursorType.None)
            return null;

        if (_cursors.TryGetValue(type, out var entry))
            return entry ?? (type != StandardCursorType.Arrow ? GetCursor(StandardCursorType.Arrow) : null);

        return GetCursor(StandardCursorType.Arrow);
    }

    public void Dispose()
    {
        ReleaseCursors();

        if (_theme != IntPtr.Zero)
            UnsafeNativeMethods.wl_cursor_theme_destroy(_theme);
    }
}
