using Avalonia.SourceGenerator;
using NWayland.Protocols.XdgShell;
namespace Avalonia.Wayland.Server.Persistent;

/// <summary>
/// UI→worker proxy interface for an xdg_surface (the part used from the UI thread).
/// </summary>
[GenerateCrossThreadProxy(
    typeof(WaylandDispatchPriority),
    "Avalonia.Wayland.Server.WaylandDispatchPriority.Normal",
    GeneratedClassName = "WXdgShellSurfaceProxy")]
internal interface IWXdgShellSurface : IWSurface
{
    void SetShadowExtents(Thickness extents);
    void SetPendingAckSerial(uint serial);
}

/// <summary>
/// UI→worker proxy interface for an xdg_toplevel.
/// </summary>
[GenerateCrossThreadProxy(
    typeof(WaylandDispatchPriority),
    "Avalonia.Wayland.Server.WaylandDispatchPriority.Normal",
    GeneratedClassName = "WXdgTopLevelProxy")]
internal interface IWXdgTopLevel : IWXdgShellSurface
{
    void SetMaximized();
    void UnsetMaximized();
    void SetFullscreen();
    void UnsetFullscreen();
    void SetMinimized();
    void SetParent(IWXdgTopLevel? parent);
    void Move(object? platformCookie);
    void Resize(object? platformCookie, XdgToplevel.ResizeEdgeEnum edge);
    /// <summary>
    /// Sets the toplevel's preferred minimum and maximum size.
    /// A <c>null</c> in either argument means "no constraint" for that
    /// bound (the worker forwards 0 to the compositor, which is the
    /// protocol-defined "unconstrained" value). Values are clamped
    /// to ≥1.
    /// </summary>
    void SetMinMaxSize(Size? minSize, Size? maxSize);

    /// <summary>
    /// Sets the toplevel's title. The compositor may display this in
    /// window decorations, task bars, etc. A <c>null</c> or empty value
    /// clears the title. The worker caches the value so it survives
    /// compositor reconnects.
    /// </summary>
    void SetTitle(string? title);

    /// <summary>
    /// Asks the compositor to activate this toplevel, through
    /// <c>xdg_activation_v1</c>.
    /// </summary>
    /// <remarks>
    /// Fire-and-forget by nature, not just by signature: the protocol defines
    /// no reply, and what "activate" means is the compositor's to decide —
    /// raise, focus, or merely flag the window as demanding attention. A
    /// compositor that does not advertise the global, or that judges the
    /// request unattributable, drops it silently.
    /// </remarks>
    void Activate();

    /// <summary>
    /// Tear down the worker's <c>zxdg_toplevel_decoration_v1</c> object
    /// (if any). Switches the compositor back to "client-side
    /// decorations on next commit" per the v1 spec. Also latches the
    /// worker into sticky-CSD mode: on reconnect, no new decoration
    /// object will be created for this toplevel — matches Qt's v1
    /// strategy where SSD cannot be re-enabled on a mapped toplevel
    /// (v1 forbids creating a decoration object after the toplevel has
    /// committed a buffer). Idempotent.
    /// </summary>
    void DestroyDecoration();
    /// <summary>
    /// Synchronously creates an xdg-foreign-v2 export of this toplevel; the returned
    /// façade's HandleTask completes asynchronously when the compositor delivers the
    /// handle event. Returns null if the exporter global isn't bound or the toplevel
    /// isn't currently mapped. Generated proxy auto-wraps the result into Task&lt;...&gt;.
    /// </summary>
    IWaylandXdgTopLevelExport? ExportToplevel();
}

/// <summary>
/// UI→worker proxy interface for an xdg_popup. The current
/// <see cref="XdgPopupPositionerParams"/> are configured before the surface
/// connects (or via re-positioner reconfigure later); the worker rebuilds
/// the protocol-level <c>xdg_positioner</c> from these on every connect.
/// </summary>
[GenerateCrossThreadProxy(
    typeof(WaylandDispatchPriority),
    "Avalonia.Wayland.Server.WaylandDispatchPriority.Normal",
    GeneratedClassName = "WXdgPopupProxy")]
internal interface IWXdgPopup : IWXdgShellSurface
{
    /// <summary>
    /// Updates the cached positioner parameters used to (re-)create the
    /// xdg_popup on connect. Called from the UI thread before the popup is
    /// shown for the first time, and when reposition is invoked.
    /// </summary>
    void UpdatePositioner(XdgPopupPositionerParams positioner);

    /// <summary>
    /// Asks for an explicit grab when the popup is created.
    /// </summary>
    /// <remarks>
    /// Sent as soon as the worker can, and recorded regardless so a reconnect recreates the popup
    /// grabbing. The deadline is the popup being mapped, not created: <c>xdg_popup.grab</c> is
    /// <c>invalid_grab</c>, "tried to grab after being mapped", and a popup is mapped by its first
    /// buffer. Called from the UI thread, typically just after the surface was created.
    /// </remarks>
    void RequestGrab();
}
