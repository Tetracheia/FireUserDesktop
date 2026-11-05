using System;
using System.Collections.Generic;
using System.Linq;
using UserIdentityDesktop.Core.Models;

namespace UserIdentityDesktop.Service.Media;

/// <summary>Per-peer consent sessions; only the current input owner may operate each hosted device.</summary>
public sealed class RemoteControlPolicy(TimeProvider? clock = null)
{
    private sealed class Grant(string scope, string token, DateTimeOffset expires, bool active = true, long epoch = 0)
    {
        public readonly string Scope = scope, Token = token;
        public readonly DateTimeOffset Expires = expires;
        public bool Active = active;
        public long Epoch = epoch, Sequence, MoveSequence, Sent, SentMove;
        public DateTimeOffset RateWindow;
        public int RateCount, MoveRateCount;
    }
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<string, Grant> hosted = new(StringComparer.Ordinal), sessions = new(StringComparer.Ordinal);
    public Dictionary<string, NativePeer> Peers { get; } = new(StringComparer.Ordinal);
    public string? LocalScope { get; private set; }
    public string? RequestedBy { get; private set; }
    public string? Controller { get; private set; }
    public string? Controlling { get; private set; }
    private readonly HashSet<string> outgoingRequests = new(StringComparer.Ordinal);
    private long nextInputEpoch, ownershipEpoch;
    public long InputEpoch { get; private set; }
    public bool InputActive => Controlling is {} peer && sessions.TryGetValue(peer, out var grant) && Valid(grant) && grant.Active;
    public DateTimeOffset? RequestExpiresAt { get; private set; }
    private bool Valid(Grant grant) => time.GetUtcNow() < grant.Expires;
    public RemoteControlStatus Status => new(Peers.Values.ToArray(), RequestedBy, Controller, Controlling)
    {
        RequestExpiresAt = RequestExpiresAt, InputEpoch = InputEpoch, InputActive = InputActive,
        Sessions = sessions.Where(x => Valid(x.Value)).Select(x => new RemoteSession(x.Key, x.Value.Active)).ToArray(),
        ApprovedControllers = hosted.Where(x => Valid(x.Value)).Select(x => x.Key).ToArray()
    };
    public void Sharing(bool enabled) { Revoke(); LocalScope = enabled ? Guid.NewGuid().ToString("N") : null; }
    public bool Hello(string sender, RemotePacket packet)
    {
        if (packet.Version != 1 || packet.Type != "hello" || sender.Length is < 1 or > 256 || (!Peers.ContainsKey(sender) && Peers.Count >= 64)) return false;
        if (packet.Sharing && !Guid.TryParseExact(packet.Scope, "N", out _)) return false;
        var first = !Peers.ContainsKey(sender);
        if (sessions.TryGetValue(sender, out var session) && (!packet.Sharing || !packet.CanHost || session.Scope != packet.Scope)) RemoveSession(sender);
        // Capability changes never upgrade an already issued exclusive grant.
        if (Peers.TryGetValue(sender, out var old) && old.MultiControl != packet.MultiControl) { RemoveHosted(sender); RemoveSession(sender); }
        Peers[sender] = new(sender, packet.CanHost, packet.Sharing, packet.Scope)
        { FastInput = packet.FastInput, ClipboardSupport = packet.ClipboardSupport, ExtendedInput = packet.ExtendedInput, MultiControl = packet.MultiControl };
        return first;
    }
    public bool Request(string sender)
    {
        if (LocalScope == null || !Peers.TryGetValue(sender, out var peer) || RequestedBy != null || hosted.ContainsKey(sender) || hosted.Count >= 16) return false;
        // Legacy clients retain exclusive control. Multiparty arbitration is negotiated.
        if (hosted.Count > 0 && (!peer.MultiControl || hosted.Keys.Any(x => !Peers[x].MultiControl))) return false;
        RequestedBy = sender; RequestExpiresAt = time.GetUtcNow().AddSeconds(30); return true;
    }
    public string? ExpireRequest()
    {
        if (RequestedBy is not {} sender || time.GetUtcNow() < RequestExpiresAt) return null;
        Deny(sender); return sender;
    }
    public RemotePacket Approve(string sender)
    {
        ExpireRequest();
        if (LocalScope == null || RequestedBy != sender || !Peers.TryGetValue(sender, out var peer)) throw new InvalidOperationException("This remote-control request is no longer available.");
        var grant = new Grant(LocalScope, Guid.NewGuid().ToString("N"), time.GetUtcNow().AddMinutes(5), !peer.MultiControl);
        hosted[sender] = grant; if (!peer.MultiControl) Controller = sender;
        RequestedBy = null; RequestExpiresAt = null;
        return new("grant", Scope: LocalScope, Grant: grant.Token, InputActive: grant.Active, OwnershipEpoch: grant.Epoch);
    }
    public RemotePacket BeginRequest(string peer)
    {
        if (!Peers.TryGetValue(peer, out var remote) || !remote.CanHost || !remote.Sharing) throw new InvalidOperationException("Remote control requires a participant sharing a supported native desktop with control enabled.");
        if (outgoingRequests.Count >= 16 && !outgoingRequests.Contains(peer)) throw new InvalidOperationException("Finish a pending remote-control request before starting another.");
        outgoingRequests.Add(peer); return new("request", Scope: remote.Scope);
    }
    public bool IsExpectedGrant(string sender, RemotePacket packet) => outgoingRequests.Contains(sender) &&
        Peers.TryGetValue(sender, out var peer) && peer.Sharing && peer.Scope == packet.Scope && Guid.TryParseExact(packet.Grant, "N", out _);
    public bool AcceptGrant(string sender, RemotePacket packet)
    {
        if (!IsExpectedGrant(sender, packet)) return false;
        var peer = Peers[sender];
        sessions[sender] = new(peer.Scope!, packet.Grant!, time.GetUtcNow().AddMinutes(5), !peer.MultiControl || packet.InputActive, packet.OwnershipEpoch);
        Controlling = sender; InputEpoch = ++nextInputEpoch; outgoingRequests.Remove(sender); return true;
    }
    public void SelectTarget(string? peer)
    {
        var next = peer != null && sessions.TryGetValue(peer, out var grant) && Valid(grant) ? peer : null;
        if (next == Controlling) return;
        Controlling = next; InputEpoch = next == null ? 0 : ++nextInputEpoch;
    }
    public RemotePacket? YieldTarget() => Controlling is {} peer ? YieldSession(peer) : null;
    public RemotePacket? YieldSession(string peer)
    {
        if (!sessions.TryGetValue(peer, out var grant) || !Valid(grant) || !grant.Active || !Peers[peer].MultiControl) return null;
        grant.Active = false;
        return new("yield", Scope: grant.Scope, Grant: grant.Token, Sequence: ++grant.Sent, OwnershipEpoch: grant.Epoch);
    }
    private bool BoundHosted(string sender, RemotePacket packet, out Grant grant)
    {
        if (hosted.TryGetValue(sender, out grant!) && Valid(grant) && LocalScope != null && packet.Scope == LocalScope && packet.Grant == grant.Token) return true;
        return false;
    }
    public bool Acquire(string sender, RemotePacket packet)
    {
        if (!Peers.TryGetValue(sender, out var peer) || !peer.MultiControl || !BoundHosted(sender, packet, out var grant) || packet.OwnershipEpoch != grant.Epoch || packet.Sequence <= grant.Sequence || packet.Sequence - grant.Sequence > 10000) return false;
        // First mouse press claims the device; it is never injected into the host.
        if (packet.Input is not { Kind: 1, Down: true, Code: >= 1 and <= 5 } input || !double.IsFinite(input.X) || !double.IsFinite(input.Y) || input.X is < 0 or > 1 || input.Y is < 0 or > 1) return false;
        if (!AllowRate(grant, false)) return false;
        grant.Sequence = packet.Sequence;
        Controller = sender; ++ownershipEpoch;
        foreach (var item in hosted) { item.Value.Active = item.Key == sender; item.Value.Epoch = ownershipEpoch; }
        return true;
    }
    public bool Yield(string sender, RemotePacket packet)
    {
        if (sender != Controller || !BoundHosted(sender, packet, out var grant) || packet.OwnershipEpoch != grant.Epoch || packet.Sequence <= grant.Sequence || packet.Sequence - grant.Sequence > 10000) return false;
        grant.Sequence = packet.Sequence; grant.Active = false; Controller = null; ++ownershipEpoch;
        foreach (var item in hosted.Values) item.Epoch = ownershipEpoch;
        return true;
    }
    public IReadOnlyList<(string Peer, RemotePacket Packet)> OwnershipPackets() => hosted
        .Where(x => Valid(x.Value) && Peers[x.Key].MultiControl)
        .Select(x => (x.Key, new RemotePacket("ownership", Scope: x.Value.Scope, Grant: x.Value.Token, InputActive: x.Key == Controller, OwnershipEpoch: x.Value.Epoch))).ToArray();
    public bool AcceptOwnership(string sender, RemotePacket packet)
    {
        if (!sessions.TryGetValue(sender, out var grant) || !Valid(grant) || !Peers.TryGetValue(sender, out var peer) || !peer.MultiControl || packet.Scope != grant.Scope || packet.Grant != grant.Token || packet.OwnershipEpoch <= grant.Epoch) return false;
        grant.Active = packet.InputActive; grant.Epoch = packet.OwnershipEpoch;
        if (Controlling == sender) InputEpoch = ++nextInputEpoch;
        return true;
    }
    public RemotePacket? Outgoing(RemoteInput input)
    {
        if (Controlling == null || !sessions.TryGetValue(Controlling, out var grant) || !Valid(grant) || !input.Target.StartsWith(Controlling + " · ", StringComparison.Ordinal) || !Peers.TryGetValue(Controlling, out var peer)) return null;
        if (!grant.Active)
            return peer.MultiControl && input.Kind == 1 && input.Down ? new("acquire", Scope: grant.Scope, Grant: grant.Token, Sequence: ++grant.Sent, Input: input, OwnershipEpoch: grant.Epoch) : null;
        var move = input.Kind == 0 && peer.FastInput;
        return new(move ? "move" : input.Kind == 3 ? "release" : "input", Scope: grant.Scope, Grant: grant.Token, Sequence: move ? ++grant.SentMove : ++grant.Sent, Input: move ? input with { Target = "" } : input, PointerSequence: move ? 0 : grant.SentMove, OwnershipEpoch: grant.Epoch);
    }
    private bool AllowRate(Grant grant, bool move)
    {
        var now = time.GetUtcNow();
        if (now - grant.RateWindow >= TimeSpan.FromSeconds(1)) { grant.RateWindow = now; grant.RateCount = grant.MoveRateCount = 0; }
        return move ? ++grant.MoveRateCount <= 240 : ++grant.RateCount <= 120;
    }
    public bool Authorize(string sender, RemotePacket packet)
    {
        if (sender != Controller || !BoundHosted(sender, packet, out var grant) || !grant.Active || packet.OwnershipEpoch != grant.Epoch) return false;
        var move = packet.Type == "move"; var previous = move ? grant.MoveSequence : grant.Sequence;
        if (packet.Sequence <= previous || packet.Sequence - previous > 10000) return false;
        var input = packet.Input;
        if (input == null || !double.IsFinite(input.X) || !double.IsFinite(input.Y) || input.X is < 0 or > 1 || input.Y is < 0 or > 1 || !(input.Kind is >= 0 and <= 3 or 6)) return false;
        if (move && input.Kind != 0) return false;
        if (input.Kind == 1 && input.Code is < 1 or > 5) return false;
        if (input.Kind == 2 && !RemoteInputCodes.IsKey(input.Code)) return false;
        if (input.Kind == 6 && (!double.IsFinite(input.DeltaX) || !double.IsFinite(input.DeltaY) || Math.Abs(input.DeltaX) > 10 || Math.Abs(input.DeltaY) > 10 || input.Code != 0 || input.Down)) return false;
        if (input.Kind != 6 && (input.DeltaX != 0 || input.DeltaY != 0)) return false;
        if (!AllowRate(grant, move)) return false;
        if (move) grant.MoveSequence = packet.Sequence;
        else { grant.Sequence = packet.Sequence; if ((input.Kind is 1 or 3 or 6) && packet.PointerSequence > grant.MoveSequence && packet.PointerSequence - grant.MoveSequence <= 10000) grant.MoveSequence = packet.PointerSequence; }
        return true;
    }
    public RemotePacket? ClipboardPacket(RemoteClipboardMessage message, string? destination = null, RemotePacket? binding = null)
    {
        if (Controller is {} controller && (destination == null || destination == controller) && hosted.TryGetValue(controller, out var grant) && Valid(grant) && Peers[controller].ClipboardSupport && (binding == null || IsHostedGrantPeer(controller, binding)))
            return new("clipboard", Scope: grant.Scope, Grant: grant.Token, Clipboard: message, OwnershipEpoch: grant.Epoch);
        if (binding != null && (Controlling == null || !IsControlledGrantPeer(Controlling, binding))) return null;
        if (destination != null && destination != Controlling) return null;
        return ControllerClipboardPacket(message);
    }
    public RemotePacket? ControllerClipboardPacket(RemoteClipboardMessage message)
    {
        if (InputActive && Controlling is {} host && sessions.TryGetValue(host, out var grant) && Peers[host].ClipboardSupport)
            return new("clipboard", Scope: grant.Scope, Grant: grant.Token, Clipboard: message, OwnershipEpoch: grant.Epoch);
        return null;
    }
    public bool IsHostedGrantPeer(string sender, RemotePacket packet) => sender == Controller && BoundHosted(sender, packet, out var grant) && grant.Active && packet.OwnershipEpoch == grant.Epoch;
    public bool IsControlledGrantPeer(string sender, RemotePacket packet) => sender == Controlling && sessions.TryGetValue(sender, out var grant) && Valid(grant) && grant.Active && packet.Grant == grant.Token && packet.Scope == grant.Scope && packet.OwnershipEpoch == grant.Epoch && Peers.TryGetValue(sender, out var peer) && peer.Sharing && peer.Scope == grant.Scope;
    public bool IsGrantPeer(string sender, RemotePacket packet) => IsHostedGrantPeer(sender, packet) || IsControlledGrantPeer(sender, packet);
    public void Deny(string sender) { if (RequestedBy == sender) { RequestedBy = null; RequestExpiresAt = null; } outgoingRequests.Remove(sender); }
    public string[] HostedIdentities => hosted.Keys.ToArray();
    public string[] SessionIdentities => sessions.Keys.ToArray();
    public bool HasHosted(string sender) => hosted.ContainsKey(sender);
    public void RemoveHosted(string sender)
    {
        hosted.Remove(sender); if (Controller == sender) Controller = null;
    }
    public void RemoveSession(string sender) { sessions.Remove(sender); if (Controlling == sender) { Controlling = null; InputEpoch = 0; } }
    public void Remove(string sender) { RemoveHosted(sender); RemoveSession(sender); Peers.Remove(sender); Deny(sender); }
    public IReadOnlyList<string> ExpireGrants()
    {
        var expired = hosted.Where(x => !Valid(x.Value)).Select(x => x.Key).ToArray();
        foreach (var peer in expired) RemoveHosted(peer);
        foreach (var peer in sessions.Where(x => !Valid(x.Value)).Select(x => x.Key).ToArray()) RemoveSession(peer);
        return expired;
    }
    public void Revoke() { hosted.Clear(); Controller = null; RequestedBy = null; RequestExpiresAt = null; }
    public void StopControlling() { InputEpoch = 0; Controlling = null; sessions.Clear(); outgoingRequests.Clear(); }
    public void Clear() { Revoke(); StopControlling(); LocalScope = null; Peers.Clear(); }
    public static bool SupportsDesktopSource(MediaDevices devices, int width, int height)
    {
        if (width <= 0 || height <= 0) return false;
        if (devices.ShareSource is not {} source) return string.IsNullOrWhiteSpace(devices.Screen);
        return source.Kind == ScreenShareKind.Screen && source.Backend is ScreenCaptureBackend.Windows or ScreenCaptureBackend.X11 or ScreenCaptureBackend.MacOS && source.X == 0 && source.Y == 0 && source.Width == width && source.Height == height;
    }
    public static bool TryMapScreenPoint(double x, double y, int width, int height, out double screenX, out double screenY)
    {
        screenX = screenY = 0;
        if (width <= 0 || height <= 0 || !double.IsFinite(x) || !double.IsFinite(y)) return false;
        var scale = Math.Min(960d / width, 540d / height);
        var contentWidth = width * scale; var contentHeight = height * scale;
        screenX = (x * 960 - (960 - contentWidth) / 2) / contentWidth;
        screenY = (y * 540 - (540 - contentHeight) / 2) / contentHeight;
        return screenX is >= 0 and <= 1 && screenY is >= 0 and <= 1;
    }
}
