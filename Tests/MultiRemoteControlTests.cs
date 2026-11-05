using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.Service.Media;
using Xunit;

namespace UserIdentityDesktop.Tests;

public sealed class MultiRemoteControlTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-11-04T12:00:00Z");
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private static RemoteControlPolicy Host(TimeProvider? clock = null)
    {
        var host = new RemoteControlPolicy(clock); host.Sharing(true); return host;
    }
    private static RemoteControlPolicy Approve(RemoteControlPolicy host, string hostId, string viewerId)
    {
        var viewer = new RemoteControlPolicy();
        host.Hello(viewerId, new("hello", MultiControl: true, FastInput: true, ClipboardSupport: true));
        viewer.Hello(hostId, new("hello", CanHost: true, Sharing: true, Scope: host.LocalScope, MultiControl: true, FastInput: true, ClipboardSupport: true));
        viewer.BeginRequest(hostId); Assert.True(host.Request(viewerId));
        Assert.True(viewer.AcceptGrant(hostId, host.Approve(viewerId))); return viewer;
    }
    private static RemotePacket Claim(RemoteControlPolicy viewer, string hostId) =>
        Assert.IsType<RemotePacket>(viewer.Outgoing(new(hostId + " · screen · track", 1, .5, .5, 1, true, true)));
    private static void Notify(RemoteControlPolicy host, string hostId, params (string Id, RemoteControlPolicy Viewer)[] viewers)
    {
        foreach (var (peer, packet) in host.OwnershipPackets())
            Assert.True(viewers.Single(x => x.Id == peer).Viewer.AcceptOwnership(hostId, packet));
    }
    [Fact]
    public void ApprovedViewersStartPassive_OnlyMousePressClaims_AndHandoffRejectsOldInput()
    {
        var host = Host(); var alice = Approve(host, "host", "alice"); var bob = Approve(host, "host", "bob");
        Assert.Equal(2, host.Status.ApprovedControllers.Count); Assert.Null(host.Controller);
        Assert.False(alice.InputActive); Assert.False(bob.InputActive);
        Assert.Null(alice.Outgoing(new("host · screen · track", 0, .2, .3)));
        Assert.Null(alice.Outgoing(new("host · screen · track", 2, Code: 4, Down: true)));
        var claim = Claim(alice, "host"); Assert.Equal("acquire", claim.Type);
        Assert.False(host.Authorize("alice", claim)); Assert.True(host.Acquire("alice", claim));
        Notify(host, "host", ("alice", alice), ("bob", bob));
        Assert.True(alice.InputActive); Assert.False(bob.InputActive);
        var delayed = alice.Outgoing(new("host · screen · track", 2, Code: 4, Down: true))!;
        Assert.True(host.Acquire("bob", Claim(bob, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        Assert.False(alice.InputActive); Assert.True(bob.InputActive); Assert.Equal("bob", host.Controller);
        Assert.False(host.Authorize("alice", delayed));
        Assert.True(host.Authorize("bob", bob.Outgoing(new("host · screen · track", 2, Code: 5, Down: true))!));
        Assert.Null(alice.ControllerClipboardPacket(new("gesture", "test")));
    }
    [Fact]
    public void ReturningOwnerCannotReplayInputOrOwnershipFromAnEarlierTurn()
    {
        var host = Host(); var alice = Approve(host, "host", "alice"); var bob = Approve(host, "host", "bob");
        Assert.True(host.Acquire("alice", Claim(alice, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        var oldNotice = host.OwnershipPackets().Single(x => x.Peer == "alice").Packet;
        var delayed = alice.Outgoing(new("host · screen · track", 1, .4, .4, 1, true, true))!;
        Assert.True(host.Acquire("bob", Claim(bob, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        Assert.True(host.Acquire("alice", Claim(alice, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        Assert.False(host.Authorize("alice", delayed)); Assert.False(alice.AcceptOwnership("host", oldNotice));
        Assert.True(host.Authorize("alice", alice.Outgoing(new("host · screen · track", 1, .6, .6, 1, true, true))!));
    }
    [Fact]
    public void ForgedReplayedExpiredAndWrongScopeClaimsNeverGainOwnership()
    {
        var clock = new Clock(); var host = Host(clock); var viewer = Approve(host, "host", "alice"); var claim = Claim(viewer, "host");
        Assert.False(host.Acquire("stranger", claim)); Assert.False(host.Acquire("alice", claim with { Grant = Guid.NewGuid().ToString("N") }));
        Assert.False(host.Acquire("alice", claim with { Scope = Guid.NewGuid().ToString("N") }));
        Assert.False(host.Acquire("alice", claim with { Input = claim.Input! with { X = double.NaN } }));
        Assert.True(host.Acquire("alice", claim)); Assert.False(host.Acquire("alice", claim));
        clock.Now = clock.Now.AddMinutes(5);
        Assert.False(host.Acquire("alice", claim with { Sequence = 2, OwnershipEpoch = 1 }));
        Assert.Contains("alice", host.ExpireGrants()); Assert.Null(host.Controller); Assert.Empty(host.Status.ApprovedControllers);
    }
    [Fact]
    public void IndependentDeviceSessionsSurviveSelectionAndPerDeviceRevocation()
    {
        var first = Host(); var second = Host(); var viewer = Approve(first, "first", "alice");
        second.Hello("alice", new("hello", MultiControl: true));
        viewer.Hello("second", new("hello", CanHost: true, Sharing: true, Scope: second.LocalScope, MultiControl: true));
        viewer.BeginRequest("second"); Assert.True(second.Request("alice")); Assert.True(viewer.AcceptGrant("second", second.Approve("alice")));
        Assert.Equal(2, viewer.Status.Sessions.Count);
        viewer.SelectTarget("first"); Assert.True(first.Acquire("alice", Claim(viewer, "first"))); Notify(first, "first", ("alice", viewer));
        var epoch = viewer.InputEpoch; var yield = viewer.YieldTarget()!; Assert.True(first.Yield("alice", yield));
        viewer.SelectTarget("second"); Assert.NotEqual(epoch, viewer.InputEpoch); Assert.True(second.Acquire("alice", Claim(viewer, "second"))); Notify(second, "second", ("alice", viewer));
        Assert.Null(first.Controller); Assert.Equal("alice", second.Controller);
        Assert.Null(viewer.Outgoing(new("first · screen · track", 1, .5, .5, 1, true)));
        viewer.RemoveSession("second"); Assert.Single(viewer.Status.Sessions); viewer.SelectTarget("first"); Assert.Equal("first", viewer.Controlling);
        viewer.SelectTarget(null); Assert.Null(viewer.Controlling); Assert.Single(viewer.Status.Sessions);
    }
    [Fact]
    public void RemovingOneApprovedViewerAndShareRestartDoNotLeaveReusableAuthority()
    {
        var host = Host(); var alice = Approve(host, "host", "alice"); var bob = Approve(host, "host", "bob");
        host.Remove("alice"); Assert.Equal("bob", Assert.Single(host.Status.ApprovedControllers));
        Assert.False(host.Acquire("alice", Claim(alice, "host"))); Assert.True(host.Acquire("bob", Claim(bob, "host")));
        var scope = host.LocalScope; host.Sharing(true); Assert.NotEqual(scope, host.LocalScope); Assert.Empty(host.Status.ApprovedControllers); Assert.Null(host.Controller);
        Assert.False(host.Acquire("bob", Claim(bob, "host")));
    }
    [Fact]
    public void ApprovalsExpireIndividually_AndNewApprovalDoesNotExtendOlderConsent()
    {
        var clock = new Clock(); var host = Host(clock); Approve(host, "host", "alice"); clock.Now = clock.Now.AddMinutes(1); Approve(host, "host", "bob");
        clock.Now = clock.Now.AddMinutes(4); Assert.Equal(new[] { "alice" }, host.ExpireGrants()); Assert.Equal("bob", Assert.Single(host.Status.ApprovedControllers));
        clock.Now = clock.Now.AddMinutes(1); Assert.Equal(new[] { "bob" }, host.ExpireGrants()); Assert.Empty(host.Status.ApprovedControllers);
    }
    [Fact]
    public void LegacyClientsRemainExclusive_AndUnknownPeersStillRequireConsent()
    {
        var host = Host(); host.Hello("legacy", new("hello")); Assert.True(host.Request("legacy")); var grant = host.Approve("legacy");
        Assert.True(grant.InputActive); Assert.Equal("legacy", host.Controller);
        host.Hello("new", new("hello", MultiControl: true)); Assert.False(host.Request("new"));
        Assert.False(host.Acquire("new", grant with { Type = "acquire", Sequence = 1, Input = new("host · screen · track", 1, .5, .5, 1, true) }));
        host.Revoke(); Approve(host, "host", "new"); Assert.False(host.Request("legacy"));
    }
    [Fact]
    public void ParallelRequestsAcceptEachExactHostAndUnsolicitedGrantCannotChangeSelection()
    {
        var viewer = new RemoteControlPolicy(); var first = Host(); var second = Host();
        foreach (var (id, host) in new[]{("first",first),("second",second)})
        {
            viewer.Hello(id,new("hello",CanHost:true,Sharing:true,Scope:host.LocalScope,MultiControl:true));
            host.Hello("alice",new("hello",MultiControl:true));viewer.BeginRequest(id);Assert.True(host.Request("alice"));
        }
        Assert.True(viewer.AcceptGrant("second",second.Approve("alice")));
        Assert.True(viewer.AcceptGrant("first",first.Approve("alice")));Assert.Equal(2,viewer.Status.Sessions.Count);
        var fake = new RemotePacket("grant",Scope:second.LocalScope,Grant:Guid.NewGuid().ToString("N"));
        Assert.False(viewer.IsExpectedGrant("second",fake));Assert.False(viewer.AcceptGrant("second",fake));Assert.Equal("first",viewer.Controlling);
    }
    [Fact]
    public void LateOwnershipAfterChangingSourceCanBeYieldedWithoutLosingApproval()
    {
        var host = Host(); var viewer = Approve(host,"host","alice");
        var claim = Claim(viewer,"host");viewer.SelectTarget(null);
        Assert.True(host.Acquire("alice",claim));Notify(host,"host",("alice",viewer));
        Assert.Null(viewer.Controlling);var release=viewer.YieldSession("host");Assert.NotNull(release);
        Assert.True(host.Yield("alice",release));Assert.Null(host.Controller);Assert.Single(host.Status.ApprovedControllers);
        viewer.SelectTarget("host");Assert.False(viewer.InputActive);Assert.Equal("acquire",Claim(viewer,"host").Type);
    }
    [Fact]
    public void CapabilityDowngradeRequiresFreshConsentAndCannotReuseAnOwnershipGrant()
    {
        var host = Host();var viewer=Approve(host,"host","alice");
        Assert.True(host.Acquire("alice",Claim(viewer,"host")));Notify(host,"host",("alice",viewer));
        var oldInput=viewer.Outgoing(new("host · screen · track",2,Code:4,Down:true))!;
        host.Hello("alice",new("hello",MultiControl:false));Assert.Empty(host.Status.ApprovedControllers);Assert.False(host.Authorize("alice",oldInput));
        viewer.Hello("host",new("hello",CanHost:true,Sharing:true,Scope:host.LocalScope,MultiControl:false));Assert.Empty(viewer.Status.Sessions);Assert.Null(viewer.Controlling);
    }
    [Fact]
    public void PassiveOrPreviousOwnershipCannotReadOrWriteClipboard()
    {
        var host = Host(); var alice = Approve(host, "host", "alice"); var bob = Approve(host, "host", "bob");
        var message = new RemoteClipboardMessage("gesture", "test"); Assert.Null(alice.ControllerClipboardPacket(message));
        Assert.True(host.Acquire("alice", Claim(alice, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        var binding = alice.ControllerClipboardPacket(message)!; Assert.True(host.IsHostedGrantPeer("alice", binding));
        Assert.True(host.Acquire("bob", Claim(bob, "host"))); Notify(host, "host", ("alice", alice), ("bob", bob));
        Assert.False(host.IsHostedGrantPeer("alice", binding)); Assert.Null(host.ClipboardPacket(message, "alice", binding));
        Assert.True(host.IsHostedGrantPeer("bob", bob.ControllerClipboardPacket(message)!));
    }
}
