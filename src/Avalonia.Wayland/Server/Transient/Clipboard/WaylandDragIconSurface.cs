using System;
using System.Runtime.InteropServices;
using NWayland.Protocols.Wayland;
using static Avalonia.Wayland.Server.Interop.UnsafeNativeMethods;

namespace Avalonia.Wayland.Server.Transient.Clipboard;

/// <summary>
/// The surface a drag's icon is drawn on, and the shared-memory buffer behind it.
/// </summary>
/// <remarks>
/// A drag icon is an ordinary <c>wl_surface</c> with one buffer, given the <c>dnd_icon</c> role
/// by <c>wl_data_device.start_drag</c>. It has no shell surface and is never in a window's
/// tree: the compositor composites it under the pointer for as long as the drag lasts.
///
/// <para>
/// Attached at (0, 0), so the compositor's accumulated offset stays zero and the icon's
/// top-left sits at the pointer. A hotspot would be a <c>wl_surface.offset</c> — and
/// <c>attach</c> with a non-zero position is a protocol error from version 5 onwards, so it is
/// not a thing to do by accident.
/// </para>
/// </remarks>
internal sealed class WaylandDragIconSurface : IDisposable
{
    private readonly WlSurface _surface;
    private readonly WlBuffer _buffer;
    private bool _disposed;

    private WaylandDragIconSurface(WlSurface surface, WlBuffer buffer)
    {
        _surface = surface;
        _buffer = buffer;
    }

    /// <summary>The surface to pass to <c>start_drag</c>.</summary>
    public WlSurface Surface => _surface;

    /// <summary>
    /// Builds an icon surface, or null if the memory for it could not be had.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception throughout. A drag that cannot show a picture is still a
    /// drag worth starting, and failing the whole gesture over the decoration would be a poor
    /// trade.
    /// </remarks>
    public static WaylandDragIconSurface? Create(WaylandGlobals globals, WaylandDragImage image)
    {
        var stride = image.Width * 4;
        var length = stride * image.Height;

        var fd = memfd_create("avalonia-wayland-drag-icon", 0);
        if (fd == -1)
            return null;

        IntPtr map;
        if (ftruncate(fd, length) != 0
            || (map = mmap(IntPtr.Zero, length, PROT_READ | PROT_WRITE, MAP_SHARED, fd, IntPtr.Zero))
                == new IntPtr(-1))
        {
            close(fd);
            return null;
        }

        try
        {
            Marshal.Copy(image.Pixels, 0, map, length);
        }
        finally
        {
            // Unmapped as soon as the pixels are in: the compositor reads through its own
            // mapping of the same fd, and holding ours open serves nothing.
            munmap(map, length);
        }

        WlSurface? surface = null;
        try
        {
            using var pool = globals.WlShm.CreatePool(fd, length);
            // ARGB8888 rather than XRGB: a drag icon is expected to have transparent corners,
            // and an opaque format would draw a rectangle around it.
            var buffer = pool.CreateBuffer(0, image.Width, image.Height, stride,
                WlShm.FormatEnum.Argb8888, null);

            surface = globals.WlCompositor.CreateSurface(null);
            surface.Attach(buffer, 0, 0);
            surface.DamageBuffer(0, 0, image.Width, image.Height);
            surface.Commit();
            return new WaylandDragIconSurface(surface, buffer);
        }
        catch (Exception)
        {
            surface?.Dispose();
            return null;
        }
        finally
        {
            // The pool has copied what it needs; the compositor holds its own reference to
            // the fd through the buffer.
            close(fd);
        }
    }

    /// <summary>Tears the icon down. Called when the drag finishes, however it finishes.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _surface.Dispose();
            _buffer.Dispose();
        }
        catch (Exception)
        {
            // A compositor that has already gone away takes the objects with it.
        }
    }
}
