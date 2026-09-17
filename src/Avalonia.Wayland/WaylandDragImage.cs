using System;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Avalonia.Wayland;

/// <summary>
/// A picture to show under the pointer while a drag is in progress.
/// </summary>
/// <remarks>
/// Wayland lets a drag source attach an icon surface to <c>wl_data_device.start_drag</c>, and
/// compositors composite it under the cursor. Avalonia's cross-platform drag API has no
/// drag-image parameter and adding one would change public API on every backend, so this rides
/// along on the <see cref="IDataTransfer"/> the drag already carries: add an item in
/// <see cref="Format"/> and the Wayland backend picks it up.
///
/// <para>
/// The format is an <i>in-process</i> one, so it is never advertised to other clients and never
/// serialized — a receiving application sees exactly the formats it would have seen without it.
/// </para>
///
/// <para>
/// Raw premultiplied BGRA rather than an encoded image, because the backend has no decoder: it
/// copies these bytes straight into a shared-memory buffer and hands the buffer to the
/// compositor. <see cref="FromBitmap"/> does the conversion for a caller that has a
/// <see cref="Bitmap"/>.
/// </para>
/// </remarks>
public sealed class WaylandDragImage
{
    /// <summary>The data format a drag image travels in.</summary>
    public static DataFormat<WaylandDragImage> Format { get; } =
        DataFormat.CreateInProcessFormat<WaylandDragImage>("Avalonia.Wayland.DragImage");

    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="premultipliedBgra">
    /// <c>width * height * 4</c> bytes of premultiplied BGRA, one row after another with no
    /// padding. Premultiplied because that is what <c>wl_shm</c>'s ARGB8888 means.
    /// </param>
    public WaylandDragImage(int width, int height, byte[] premultipliedBgra)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "a drag image needs a positive size");
        if (premultipliedBgra.Length < width * height * 4)
            throw new ArgumentException("not enough pixels for the given size", nameof(premultipliedBgra));

        Width = width;
        Height = height;
        Pixels = premultipliedBgra;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Premultiplied BGRA, row-major, stride <c>Width * 4</c>.</summary>
    public byte[] Pixels { get; }

    /// <summary>Builds a drag image out of a rendered bitmap.</summary>
    /// <remarks>
    /// The bitmap must be readable — one produced by <c>RenderTargetBitmap</c> or decoded from
    /// a file is. Answers null rather than throwing if the pixels cannot be read, because a
    /// drag with no picture is still a working drag.
    /// </remarks>
    public static WaylandDragImage? FromBitmap(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        if (size.Width <= 0 || size.Height <= 0)
            return null;

        var stride = size.Width * 4;
        var pixels = new byte[stride * size.Height];
        try
        {
            unsafe
            {
                fixed (byte* buffer = pixels)
                {
                    bitmap.CopyPixels(
                        new PixelRect(size),
                        (IntPtr)buffer,
                        pixels.Length,
                        stride);
                }
            }
        }
        catch (Exception)
        {
            // A bitmap backed by something that cannot be read back. Not worth failing the
            // drag over; the drag simply has no picture, which is where this started.
            return null;
        }

        return new WaylandDragImage(size.Width, size.Height, pixels);
    }
}
