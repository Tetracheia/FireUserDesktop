using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.Data;

namespace UserIdentityDesktop.Service.Media;

public sealed partial class NativeMediaSession
{
    private readonly object remoteLock = new();
    private readonly RemoteControlPolicy control = new();
    private readonly LiveKitNative.DataCallback dataCallback;
    private RemoteInputInbox? incomingData;
    private Channel<(string Destination, RemotePacket Packet)>? outgoingData;
    private Task remoteReader = Task.CompletedTask, remoteWriter = Task.CompletedTask;
    private Channel<(string Destination, RemotePacket Packet)>? outgoingMoves;
    private Task moveWriter = Task.CompletedTask;
    private double? inputRtt;
    private long lastProbe;
    private long lastLegacyMove,lastExtendedNotice;
    private Timer? grantExpiry;
    private Timer? requestExpiry;
    private Timer? inputLease;
    private long lastInputTick;
    private bool canHost;
    private ScreenCastPortal? waylandInput;
    private long waylandGrantEpoch;
    private int desktopWidth, desktopHeight;
    private RemoteControlStatus CurrentControlStatus => control.Status with
    {
        InputRoundTripMs = inputRtt,
        HostUnavailableReason = control.LocalScope != null && !canHost
            ? OperatingSystem.IsLinux() && ScreenSources.IsWayland
                ? waylandInput?.RemoteControlRequested==true ? "The desktop did not approve keyboard and pointer control for this screen. Stop sharing and request system permission again." : "Start a desktop screen share and approve the system keyboard and pointer permission."
                : "This share cannot host remote control. Share the supported primary desktop and allow operating-system input permissions."
            : null
    };
    public event Action<RemoteControlStatus>? RemoteControlChanged;

    private void StartData(CancellationToken ct)
    {
        incomingData = new RemoteInputInbox();
        outgoingData = Channel.CreateBounded<(string, RemotePacket)>(128);
        outgoingMoves=Channel.CreateBounded<(string,RemotePacket)>(new BoundedChannelOptions(1){FullMode=BoundedChannelFullMode.DropOldest,SingleReader=true});
        moveWriter=Task.Run(async()=>
        {
            try
            {
                await foreach(var item in outgoingMoves.Reader.ReadAllAsync(ct))
                {
                    if(!Connected)continue;
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(item.Packet,ApiPayload.Json);
                    LiveKitNative.Check(LiveKitNative.fu_send_input(room,item.Destination,bytes,bytes.Length),"pointer channel");
                }
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){}
            catch{Error?.Invoke("Remote pointer interrupted. Request control again.");lock(remoteLock){RevokeLocal();control.StopControlling();RemoteControlChanged?.Invoke(CurrentControlStatus);}}
        });
        remoteReader = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in incomingData.ReadAllAsync(ct))
                {
                    if(OperatingSystem.IsLinux() && ScreenSources.IsWayland && item.Packet.Type is "input" or "release" or "move") { await HandleWaylandInputAsync(item.Sender,item.Packet);continue; }
                    if (item.Packet.Type is "acquire" or "yield") { await HandleOwnershipAsync(item.Sender, item.Packet); continue; }
                    lock (remoteLock)
                    {
                        var (sender, packet) = item;
                        if (!Connected || packet.Version != 1) continue;
                        if (packet.Type == "hello")
                        {
                            var previousOwner = control.Controller;
                            if (control.Hello(sender, packet)) SendHello(sender);
                            if (previousOwner != control.Controller) ReleaseHostedInput();
                        }
                        else if (packet.Type == "request" && canHost && packet.Scope == control.LocalScope)
                        {
                            if (control.Request(sender))
                            {
                                requestExpiry?.Dispose();
                                requestExpiry = new Timer(_ =>
                                {
                                    lock (remoteLock)
                                    {
                                        if (control.ExpireRequest() is not {} expired) return;
                                        requestExpiry?.Dispose(); requestExpiry = null;
                                        QueuePacket(expired, new RemotePacket("deny"));
                                        RemoteControlChanged?.Invoke(CurrentControlStatus);
                                    }
                                }, null, TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(250));
                            }
                            else if (control.RequestedBy != sender) QueuePacket(sender, new RemotePacket("deny"));
                        }
                        else if (packet.Type == "grant")
                        {
                            if (!control.IsExpectedGrant(sender, packet)) continue;
                            var previous = control.Controlling;
                            if (previous != null && previous != sender && control.YieldTarget() is {} yield) QueuePacket(previous, yield);
                            if (control.AcceptGrant(sender, packet)) { ResetClipboard(); EnsureGrantExpiry(); }
                        }
                        else if (packet.Type == "ownership" && control.AcceptOwnership(sender, packet))
                        {
                            ResetClipboard();
                            // A click acknowledgement can arrive after the viewer changed source.
                            if (sender != control.Controlling && control.YieldSession(sender) is {} yield) QueuePacket(sender, yield);
                        }
                        else if (packet.Type == "deny") control.Deny(sender);
                        else if (packet.Type == "revoke")
                        {
                            RemovePeerControl(sender);
                            control.Deny(sender);
                        }
                        else if(packet.Type=="latency"&&control.Controlling==sender&&control.IsGrantPeer(sender,packet)&&packet.Probe>0)
                        {
                            inputRtt=Math.Max(0,Environment.TickCount64-packet.Probe);
                        }
                        else if (packet.Type is "input" or "release" or "move" && canHost && control.Authorize(sender, packet))
                        {
                            lastInputTick = Environment.TickCount64;
                            if (packet.Type == "release") LiveKitNative.fu_release_input();
                            else if (packet.Input is { } input)
                            {
                                var x = input.X; var y = input.Y;
                                if (input.Kind == 0 && !RemoteControlPolicy.TryMapScreenPoint(x, y, desktopWidth, desktopHeight, out x, out y)) continue;
                                if((input.Kind is 1 or 6) && input.PositionIncluded)
                                {
                                    if(RemoteControlPolicy.TryMapScreenPoint(input.X,input.Y,desktopWidth,desktopHeight,out var px,out var py))
                                    {if(LiveKitNative.fu_apply_input(0,px,py,0,0)!=0){RevokeLocal();continue;}}
                                    else if(input.Down||input.Kind==6)continue;
                                }
                                if (LiveKitNative.fu_apply_input(input.Kind, input.Kind==6?input.DeltaX:x, input.Kind==6?input.DeltaY:y, input.Code, input.Down ? 1 : 0) != 0) RevokeLocal();
                            }
                            if(packet.Probe>0&&control.Controller==sender)QueuePacket(sender,new RemotePacket("latency",Scope:packet.Scope,Grant:packet.Grant,Probe:packet.Probe));
                            continue;
                        }
                        if (control.RequestedBy == null) { requestExpiry?.Dispose(); requestExpiry = null; }
                        if(control.Controller==null&&control.Controlling==null)ResetClipboard();
                        RemoteControlChanged?.Invoke(CurrentControlStatus);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { Error?.Invoke("Remote control stopped. Request permission again."); lock (remoteLock) {RevokeLocal();control.StopControlling();RemoteControlChanged?.Invoke(CurrentControlStatus);} }
        });
        remoteWriter = Task.Run(async () =>
        {
            try
            {
                await foreach (var item in outgoingData.Reader.ReadAllAsync(ct))
                {
                    if (!Connected) continue;
                    try
                    {
                        var data = JsonSerializer.SerializeToUtf8Bytes(item.Packet, ApiPayload.Json);
                        LiveKitNative.Check(LiveKitNative.fu_send_data(room, item.Destination, data, data.Length), "data channel");
                    }
                    catch when (!ct.IsCancellationRequested)
                    {
                        // A transient send failure must not permanently kill capability negotiation.
                        // Discard failed input and revoke consent; never replay control packets.
                        lock (remoteLock) { RevokeLocal(); control.StopControlling(); RemoteControlChanged?.Invoke(CurrentControlStatus); }
                        Error?.Invoke("Remote control interrupted. Request permission again after the call reconnects.");
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { Error?.Invoke("The remote-control data channel stopped. Leave and rejoin the call."); lock (remoteLock) { RevokeLocal(); control.StopControlling(); RemoteControlChanged?.Invoke(CurrentControlStatus); } }
        });
    }
    private async Task HandleWaylandInputAsync(string sender,RemotePacket packet)
    {
        ScreenCastPortal? portal;long epoch;RemoteInput input;
        lock(remoteLock)
        {
            if(!Connected||packet.Version!=1||!canHost||!control.Authorize(sender,packet)||packet.Input is not {} incoming)return;
            lastInputTick=Environment.TickCount64;input=incoming;
            if(packet.Type=="release")input=input with{Kind=3};
            if(input.Kind==0){if(!RemoteControlPolicy.TryMapScreenPoint(input.X,input.Y,desktopWidth,desktopHeight,out var x,out var y))return;input=input with{X=x,Y=y};}
            portal=waylandInput;epoch=waylandGrantEpoch;
            if(portal==null)return; // A Wayland session never injects through X11/XTest.
        }
        try
        {
            if((input.Kind is 1 or 6)&&input.PositionIncluded)
            {
                if(!RemoteControlPolicy.TryMapScreenPoint(input.X,input.Y,desktopWidth,desktopHeight,out var px,out var py)){if(input.Down||input.Kind==6)return;}
                else if(!await portal.ApplyInputAsync(input with{Kind=0,X=px,Y=py,DeltaX=0,DeltaY=0},epoch))throw new InvalidOperationException("The pointer position could not be applied.");
            }
            if(await portal.ApplyInputAsync(input,epoch))
            {
                lock(remoteLock)if(packet.Probe>0&&control.Controller==sender&&control.IsGrantPeer(sender,packet))QueuePacket(sender,new("latency",Scope:packet.Scope,Grant:packet.Grant,Probe:packet.Probe));
                return;
            }
        }
        catch { Error?.Invoke("The desktop stopped remote input. Request permission again."); }
        lock(remoteLock)
        {
            if(ReferenceEquals(portal,waylandInput)&&epoch==waylandGrantEpoch){RevokeLocal();if(!portal.CanControl){canHost=false;SendHello();}RemoteControlChanged?.Invoke(CurrentControlStatus);}
        }
    }
    private void ReleaseHostedInput()
    {
        if(waylandInput!=null)_=waylandInput.ReleaseInputAsync();else if(room!=IntPtr.Zero)LiveKitNative.fu_release_input();
    }
    private void OnData(IntPtr sender, IntPtr data, int length)
    {
        try
        {
            if (length is < 1 or > 15360 || lifetime?.IsCancellationRequested != false) return;
            var bytes = new byte[length]; Marshal.Copy(data, bytes, 0, length);
            var packet = JsonSerializer.Deserialize<RemotePacket>(bytes, ApiPayload.Json);
            if(packet?.Type=="clipboard")
            {
                var identity=Marshal.PtrToStringUTF8(sender)??"";
                lock(remoteLock)if(Connected&&packet.Version==1&&packet.Clipboard is {} message&&control.IsGrantPeer(identity,packet))ClipboardSession(identity,packet)?.Receive(message);
                return;
            }
            if(length>4096)return;
            if(packet?.Type=="move")
            {
                lock(remoteLock)if(!control.IsHostedGrantPeer(Marshal.PtrToStringUTF8(sender)??"",packet))return;
            }
            if (packet != null && incomingData?.TryWrite((Marshal.PtrToStringUTF8(sender) ?? "", packet)) == false)
            { lock (remoteLock) { RevokeLocal(); RemoteControlChanged?.Invoke(CurrentControlStatus); } }
        }
        catch { }
    }
    private void QueuePacket(string destination, RemotePacket packet)
    {
        if (outgoingData?.Writer.TryWrite((destination, packet)) != true)
        {
            // Fail closed on backpressure; never leave queued control input behind.
            control.StopControlling(); RevokeLocal();
            RemoteControlChanged?.Invoke(CurrentControlStatus);
            Error?.Invoke("Remote control paused because its data channel is busy.");
        }
    }
    private void SendHello(string destination = "") => QueuePacket(destination,
        new RemotePacket("hello", CanHost: canHost, Sharing: control.LocalScope != null, Scope: control.LocalScope,FastInput:true,ClipboardSupport:clipboardStore!=null,ExtendedInput:true,MultiControl:true));
    private void RevokeLocal()
    {
        var controllers = control.HostedIdentities;
        if(room!=IntPtr.Zero)LiveKitNative.fu_interactive_screen(room,0);
        ResetClipboard(); inputRtt=null;
        control.Revoke();
        requestExpiry?.Dispose(); requestExpiry = null;
        inputLease?.Dispose(); inputLease = null;
        waylandGrantEpoch=0;
        if(waylandInput!=null)waylandInput.DisableInput();else if (room != IntPtr.Zero) LiveKitNative.fu_release_input();
        foreach (var controller in controllers) if (Connected) outgoingData?.Writer.TryWrite((controller, new RemotePacket("revoke")));
    }
    private void SetRemoteSharing(bool sharing, MediaDevices devices,ScreenCastPortal? portal=null)
    {
        lock (remoteLock)
        {
            var oldController = control.Controller;
            RevokeLocal(); control.Sharing(sharing);
            waylandInput=sharing?portal:null;
            canHost = sharing && portal==null && !(OperatingSystem.IsLinux()&&ScreenSources.IsWayland) && LiveKitNative.fu_input_supported() == 1 && LiveKitNative.fu_desktop_bounds(out desktopWidth, out desktopHeight) == 0 && RemoteControlPolicy.SupportsDesktopSource(devices,desktopWidth,desktopHeight);
            if(OperatingSystem.IsLinux()&&ScreenSources.IsWayland&&sharing&&portal!=null){desktopWidth=portal.LogicalWidth;desktopHeight=portal.LogicalHeight;canHost=devices.ShareSource?.Kind==ScreenShareKind.Screen&&portal.CanControl;}
            if (oldController != null) QueuePacket(oldController, new RemotePacket("revoke"));
            SendHello(); RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
    }
    private void BroadcastOwnership()
    {
        foreach (var (peer, packet) in control.OwnershipPackets()) QueuePacket(peer, packet);
    }
    private async Task HandleOwnershipAsync(string sender, RemotePacket packet)
    {
        ScreenCastPortal? portal;
        long epoch;
        lock (remoteLock)
        {
            if (!Connected || !canHost || packet.Version != 1) return;
            var changed = packet.Type == "acquire" ? control.Acquire(sender, packet) : control.Yield(sender, packet);
            if (!changed) return;
            ResetClipboard(); lastInputTick = Environment.TickCount64;
            portal = waylandInput;
            portal?.DisableInput(); waylandGrantEpoch = 0;
            epoch = control.OwnershipPackets().FirstOrDefault(x => x.Peer == sender).Packet?.OwnershipEpoch ?? 0;
            if (portal == null) ReleaseHostedInput();
        }
        if (portal != null) await portal.ReleaseInputAsync();
        lock (remoteLock)
        {
            if (!Connected) return;
            // Revocation/share termination while waiting for OS release wins.
            if (packet.Type == "acquire" && control.Controller == sender &&
                control.IsHostedGrantPeer(sender, packet with { OwnershipEpoch = epoch }) &&
                portal != null && ReferenceEquals(portal, waylandInput))
                waylandGrantEpoch = portal.EnableInput();
            BroadcastOwnership(); RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
    }
    private void RemovePeerControl(string peer)
    {
        if (control.Controller == peer)
        {
            waylandInput?.DisableInput(); waylandGrantEpoch = 0;
            ReleaseHostedInput();
        }
        control.RemoveHosted(peer); control.RemoveSession(peer); ResetClipboard();
        BroadcastOwnership();
    }
    private void EnsureGrantExpiry()
    {
        if (grantExpiry != null) return;
        grantExpiry = new Timer(_ =>
        {
            lock (remoteLock)
            {
                var previousOwner = control.Controller;
                var sessions = control.SessionIdentities;
                var expired = control.ExpireGrants();
                var removed = sessions.Except(control.SessionIdentities).ToArray();
                if (previousOwner != control.Controller) { waylandInput?.DisableInput(); waylandGrantEpoch = 0; ReleaseHostedInput(); }
                if (expired.Count > 0 || removed.Length > 0)
                {
                    ResetClipboard();
                    foreach (var peer in expired.Concat(removed).Distinct()) QueuePacket(peer, new("revoke"));
                    BroadcastOwnership(); RemoteControlChanged?.Invoke(CurrentControlStatus);
                }
                if (control.HostedIdentities.Length == 0 && control.SessionIdentities.Length == 0)
                { grantExpiry?.Dispose(); grantExpiry = null; }
            }
        }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }
    public void SelectControlTarget(string? identity)
    {
        lock (remoteLock)
        {
            if (control.Controlling == identity) return;
            if (control.Controlling is {} previous)
            {
                var release = control.YieldTarget() ?? control.Outgoing(new(previous + " · screen · ", 3));
                if (release != null) QueuePacket(previous, release);
            }
            control.SelectTarget(identity); ResetClipboard();
            RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
    }
    public Task RequestControlAsync(string identity)
    {
        lock (remoteLock)
        {
            if (!Connected) throw new InvalidOperationException("Join a call first.");
            QueuePacket(identity, control.BeginRequest(identity)); RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
        return Task.CompletedTask;
    }
    public Task AnswerControlAsync(string identity, bool allow)
    {
        lock (remoteLock)
        {
            if (!Connected || !canHost) throw new InvalidOperationException("Share your default desktop and enable operating-system input permissions first.");
            if (control.ExpireRequest() is {} expired)
            {
                QueuePacket(expired, new RemotePacket("deny"));
                RemoteControlChanged?.Invoke(CurrentControlStatus);
            }
            if (control.RequestedBy != identity) return Task.CompletedTask;
            requestExpiry?.Dispose(); requestExpiry = null;
            if (allow)
            {
                if(waylandInput!=null && waylandGrantEpoch == 0)waylandGrantEpoch=waylandInput.EnableInput();
                QueuePacket(identity, control.Approve(identity));
                LiveKitNative.Check(LiveKitNative.fu_interactive_screen(room,1),"interactive screen");
                lastInputTick = Environment.TickCount64;
                inputLease?.Dispose();
                inputLease = new Timer(_ =>
                {
                    lock (remoteLock)
                        if (control.Controller != null && Environment.TickCount64 - lastInputTick > 1500) ReleaseHostedInput();
                }, null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
                EnsureGrantExpiry();
            }
            else { control.Deny(identity); QueuePacket(identity, new RemotePacket("deny")); }
            RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
        return Task.CompletedTask;
    }
    public Task RevokeControlSessionAsync(string identity)
    {
        lock (remoteLock)
        {
            RemovePeerControl(identity);
            if (Connected) QueuePacket(identity, new("revoke"));
            RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
        return Task.CompletedTask;
    }
    public Task RevokeControlAsync()
    {
        lock (remoteLock)
        {
            var sessions = control.SessionIdentities;
            RevokeLocal(); control.StopControlling();
            foreach (var peer in sessions) QueuePacket(peer, new RemotePacket("revoke"));
            RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
        return Task.CompletedTask;
    }
    public void SendRemoteInput(RemoteInput input)
    {
        lock (remoteLock)
        {
            if (!Connected || input.ControlEpoch!=0&&input.ControlEpoch!=control.InputEpoch) return;
            if(RemoteInputCodes.RequiresExtension(input)&&control.Controlling is {} host&&control.Peers.TryGetValue(host,out var capability)&&!capability.ExtendedInput)
            {
                if(Environment.TickCount64-lastExtendedNotice>3000){lastExtendedNotice=Environment.TickCount64;Error?.Invoke("Update the other desktop client to use scrolling, extra mouse buttons and extended keyboard keys.");}
                return;
            }
            if(input.Kind is 4 or 5){if(control.InputActive)ClipboardGesture(input.Kind==5);return;}
            if((input.Kind is 1 or 6) && input.PositionIncluded&&control.Controlling is {} oldPeer&&control.Peers.TryGetValue(oldPeer,out var oldCapability)&&!oldCapability.FastInput)
            {
                var position=control.Outgoing(input with{Kind=0,Code=0,Down=false,PositionIncluded=false});
                if(position!=null)QueuePacket(oldPeer,position);
            }
            if(input.Kind==0&&control.Controlling is {} peerId&&control.Peers.TryGetValue(peerId,out var peer)&&!peer.FastInput)
            {if(Environment.TickCount64-lastLegacyMove<16)return;lastLegacyMove=Environment.TickCount64;}
            var destination = control.Controlling; var packet = control.Outgoing(input);
            if (packet != null && destination != null)
            {
                if(Environment.TickCount64-lastProbe>=1000){lastProbe=Environment.TickCount64;packet=packet with{Probe=lastProbe};}
                if(packet.Type=="move")outgoingMoves?.Writer.TryWrite((destination,packet));else QueuePacket(destination, packet);
            }
        }
    }
    private void RemotePeerEvent(int kind, string identity)
    {
        lock (remoteLock)
        {
            if (kind == 1) { RemovePeerControl(identity); control.Remove(identity); }
            if (kind == 2) { RevokeLocal(); control.Clear(); }
            if (kind == 4 && Connected) SendHello(identity);
            if(control.Controller==null&&control.Controlling==null)ResetClipboard();
            if (control.RequestedBy == null) { requestExpiry?.Dispose(); requestExpiry = null; }
            RemoteControlChanged?.Invoke(CurrentControlStatus);
        }
    }
}
