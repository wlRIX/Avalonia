using Avalonia.Wayland.Server.Persistent;
using NWayland;
using NWayland.Protocols.Wayland;
using NWayland.Protocols.XdgActivationV1;

namespace Avalonia.Wayland.Server.Transient;

/// <summary>
/// One round of the <c>xdg_activation_v1</c> handshake: ask the compositor for
/// a token, wait for it, then hand it straight back as an activation request
/// for the target surface.
/// </summary>
/// <remarks>
/// The protocol splits activation in two because the usual case is
/// cross-process — a launcher gets a token and passes it to the program it
/// starts, which redeems it. Activating your own window is the degenerate case
/// where both halves are the same client, so both happen here and the token
/// never leaves the process.
///
/// Worker thread only, and self-owning: the instance keeps itself alive through
/// the listener the compositor holds, and drops that reference in
/// <see cref="Finish"/>. A token is single-use (<c>already_used</c> is a
/// protocol error), so nothing here is reusable and each call builds a new one.
///
/// What the compositor does with the request is deliberately not our business.
/// <c>wlrix-compositor</c> raises the window without taking keyboard focus,
/// which is what the IRIX file manager wants; others steal focus outright, and
/// a compositor that ignores activation entirely is also conforming. There is
/// no reply either way, so there is nothing to report back.
/// </remarks>
internal sealed class XdgActivationRequest
{
    private readonly WlSurface _target;
    private XdgActivationV1? _activation;
    private XdgActivationTokenV1? _token;

    private XdgActivationRequest(XdgActivationV1 activation, WlSurface target)
    {
        _activation = activation;
        _target = target;
    }

    /// <summary>
    /// Starts a request to activate <paramref name="target"/>. Does nothing if
    /// the compositor never advertised <c>xdg_activation_v1</c> or if there is
    /// no seat to attribute the request to.
    /// </summary>
    /// <param name="globals">The bound globals for the current connection.</param>
    /// <param name="target">The surface to raise.</param>
    /// <param name="requesting">
    /// The surface asking for the activation. Usually the currently focused
    /// window of this client; compositors may refuse a request whose
    /// requesting surface is not focused, which is the protocol's defence
    /// against background windows stealing focus.
    /// </param>
    /// <param name="appId">The <c>xdg_toplevel</c> app id, if one was set.</param>
    public static void Start(WaylandGlobals globals, WlSurface target, WlSurface? requesting, string? appId)
    {
        if (globals.XdgActivation is not { } activation)
            return;

        // set_serial is not optional in practice: a token with no seat behind
        // it is one the compositor cannot attribute to a user action, and
        // refusing those is exactly how activation avoids becoming a
        // focus-stealing primitive.
        if (globals.InputDispatcher.FindActivationSeat() is not { } seat)
            return;

        var request = new XdgActivationRequest(activation, target);
        var token = activation.GetActivationToken(new TokenListener(request), globals.Connection.Queue);
        request._token = token;

        token.SetSerial(seat.Serial, seat.Seat);
        if (requesting != null)
            token.SetSurface(requesting);
        if (appId != null)
            token.SetAppId(appId);
        token.Commit();
    }

    private void OnDone(string token)
    {
        // The compositor answers exactly one done event, but a disconnect can
        // race it; _activation is nulled by Finish, so a late event is dropped
        // rather than sent on a dead proxy.
        if (_activation is not { } activation)
            return;

        // And the window the token was for can go away while the token is in
        // flight, which is not an edge case: closing a modal dialog activates
        // its owner, and an owner that closes in the same breath — a file
        // dialog answering the moment its overwrite prompt is confirmed —
        // destroys the surface before the compositor's done event arrives.
        // Passing a destroyed proxy as an argument throws inside the request
        // builder, on the worker thread, where nothing catches it and the
        // process goes down with it. Dropping a request whose target no longer
        // exists is the whole of what it could have meant anyway.
        if (_target.IsDisposed)
        {
            Finish();
            return;
        }

        activation.Activate(token, _target);
        Finish();
    }

    private void Finish()
    {
        _token?.Destroy();
        _token = null;
        // Not destroyed: the activation global is owned by WaylandGlobals and
        // shared by every window. Only the per-request token is ours.
        _activation = null;
    }

    private sealed class TokenListener(XdgActivationRequest self) : XdgActivationTokenV1.Listener
    {
        protected override void Done(XdgActivationTokenV1 eventSender, string token) => self.OnDone(token);
    }
}
