using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using UserIdentityDesktop.Core;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.Service.Media;
using UserIdentityDesktop.ViewModels;
using UserIdentityDesktop.Views;
using Xunit;
namespace UserIdentityDesktop.Tests;
public sealed class CallStageInteractionTests
{
    [AvaloniaFact]
    public async Task FullscreenFooter_RemainsOutsideSharedVideoAndUsesRemoteIcon()
    {
        var (vm,_)=Create();var screen=new VideoTileViewModel("one · screen · track"){Frame=new("one · screen · track",16,9,new byte[16*9*4])};vm.Videos.Add(screen);vm.FocusedVideo=screen;
        var window=new CallWindow{DataContext=vm,Width=1200,Height=800};window.Show();Dispatcher.UIThread.RunJobs();
        window.FindControl<Button>("FullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
        var footer=window.FindControl<Border>("FullscreenControls")!;var video=window.FindControl<ContentControl>("FocusedStage")!.GetVisualDescendants().OfType<NativeVideoView>().Single();
        Assert.True(footer.IsVisible);Assert.True(footer.Bounds.Height>=76);
        Assert.True(video.TranslatePoint(new Point(0,video.Bounds.Height),window)!.Value.Y<=footer.TranslatePoint(default,window)!.Value.Y);
        Assert.Equal("Remote",DesktopIconography.Glyph("Request remote control"));Assert.Equal("Remote",DesktopIconography.Glyph("Share screen"));
        var point=footer.TranslatePoint(new Point(footer.Bounds.Width/2,footer.Bounds.Height-2),window)!.Value;
        window.MouseMove(point);Dispatcher.UIThread.RunJobs();Assert.Equal(1,window.FindControl<Border>("FullscreenToolbar")!.Opacity);
        window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Light;Dispatcher.UIThread.RunJobs();Save(window,"remote-fullscreen-footer.png");
        window.RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark;Dispatcher.UIThread.RunJobs();Save(window,"remote-fullscreen-footer-dark.png");
        window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();
    }
    [Theory]
    [InlineData(true,true,true,true,true)][InlineData(false,true,true,true,false)][InlineData(true,false,true,true,false)][InlineData(true,true,false,true,false)][InlineData(true,true,true,false,false)]
    public void KeyboardRouting_RequiresForegroundFocusAndGrant(bool active,bool foreground,bool focused,bool grant,bool expected)=>Assert.Equal(expected,RemoteKeyboardCapture.CanRoute(active,foreground,focused,grant));
    [AvaloniaFact]
    public void RemoteKeys_ForwardWindowsAndClipboardGesturesOnlyWhenEnabled()
    {
        var inputs=new List<RemoteInput>();var view=new NativeVideoView{RemoteControlEnabled=true,Frame=new("peer · screen · track",2,2,new byte[16]),InputCommand=new CommunityToolkit.Mvvm.Input.RelayCommand<RemoteInput>(x=>inputs.Add(x!))};
        Assert.Equal(227,RemoteKeyboardCapture.MapKey(91));Assert.Equal(231,RemoteKeyboardCapture.MapKey(92));Assert.Equal(43,RemoteKeyboardCapture.MapKey(9));
        view.ForwardKey(227,true,false);view.ForwardKey(227,false,false);view.ForwardKey(6,true,true);view.ForwardKey(25,true,true);
        Assert.Equal(new[]{2,2,4,5},inputs.Select(x=>x.Kind));Assert.Equal(227,inputs[0].Code);Assert.False(inputs[1].Down);
        view.RemoteControlEnabled=false;view.ForwardKey(227,true,false);Assert.Equal(4,inputs.Count);
    }
    [AvaloniaFact]
    public async Task IncomingControlRequest_ShowsIndependentToast_AndApproveRejectAnswerExactRequester()
    {
        var (vm, media) = Create(); var window = new CallWindow { DataContext = vm }; window.Show();
        var peer = new NativePeer("one", true, true, Guid.NewGuid().ToString("N"));
        var request = new RemoteControlStatus(new[] { peer }, "one", null, null) { RequestExpiresAt = DateTimeOffset.UtcNow.AddSeconds(30) };
        media.RemoteControlChanged += Raise.Event<Action<RemoteControlStatus>>(request); Dispatcher.UIThread.RunJobs();
        var toast = Assert.IsType<RemoteControlRequestWindow>(window.ControlRequestToast);
        Assert.True(toast.IsVisible); Assert.True(toast.Topmost); Assert.False(toast.ShowActivated);
        Assert.Contains("Alice Example", toast.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).First(x => x?.Contains("requests keyboard") == true));
        Assert.Contains("Automatically rejects in", toast.FindControl<TextBlock>("Countdown")!.Text);
        toast.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark; Dispatcher.UIThread.RunJobs(); Save(toast, "remote-control-request-dark.png");
        toast.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light; Dispatcher.UIThread.RunJobs(); Save(toast, "remote-control-request-light.png");
        var approve = toast.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Approve"));
        Assert.Same(vm.AllowControlCommand, approve.Command); await vm.AllowControlCommand.ExecuteAsync(null);
        await media.Received(1).AnswerControlAsync("one", true);
        media.RemoteControlChanged += Raise.Event<Action<RemoteControlStatus>>(request with { RequestedBy = null, Controller = "one" }); Dispatcher.UIThread.RunJobs();
        Assert.False(toast.IsVisible);
        media.RemoteControlChanged += Raise.Event<Action<RemoteControlStatus>>(request); Dispatcher.UIThread.RunJobs();
        var secondToast = Assert.IsType<RemoteControlRequestWindow>(window.ControlRequestToast);
        var reject = secondToast.GetVisualDescendants().OfType<Button>().Single(x => Equals(x.Content, "Reject"));
        Assert.Same(vm.DenyControlCommand, reject.Command); await vm.DenyControlCommand.ExecuteAsync(null); await media.Received(1).AnswerControlAsync("one", false);
        window.CloseAfterCall(); Dispatcher.UIThread.RunJobs(); Assert.False(secondToast.IsVisible); await vm.DisposeAsync();
    }
    [Theory]
    [InlineData(1920,1080,1,1920,1080)]
    [InlineData(3840,2160,2,1920,1080)]
    [InlineData(1920,1080,1.5,1280,720)]
    [InlineData(800,450,2,400,225)]
    public void BorderlessMonitorSizeConvertsPhysicalPixelsToLogicalUnits(int width,int height,double scale,double expectedWidth,double expectedHeight)
    {
        Assert.Equal(new Size(expectedWidth,expectedHeight),BorderlessPresentation.ClientSize(new PixelRect(-width,-height,width,height),scale));
    }
    [AvaloniaTheory]
    [InlineData(WindowState.Normal,"escape")]
    [InlineData(WindowState.Maximized,"escape")]
    [InlineData(WindowState.Normal,"button")]
    [InlineData(WindowState.Maximized,"button")]
    [InlineData(WindowState.Normal,"end")]
    [InlineData(WindowState.Maximized,"end")]
    public async Task LinuxBorderlessShareFitsWorkAreaAndRestoresWindow(WindowState initial,string exit)
    {
        if(!OperatingSystem.IsLinux())return;
        var (vm,media)=Create();vm.InCall=true;
        var track="one · screen · track";
        var screen=new VideoTileViewModel(track){Frame=new(track,16,9,new byte[16*9*4])};vm.Videos.Add(screen);vm.FocusedVideo=screen;
        vm.ShowCallMembers=vm.ShowCallChat=true;
        var window=new CallWindow{DataContext=vm,Width=900,Height=650,MinWidth=800,MinHeight=600,MaxWidth=1800,MaxHeight=1000};window.Show();
        window.Position=new PixelPoint(123,87);Dispatcher.UIThread.RunJobs();
        var position=window.Position;var width=window.Width;var height=window.Height;var decorations=window.WindowDecorations;
        window.WindowState=initial;Dispatcher.UIThread.RunJobs();
        var monitor=(window.Screens.ScreenFromWindow(window)??window.Screens.Primary)!;
        var states=new List<WindowState>();window.PropertyChanged+=(_,e)=>{if(e.Property==Window.WindowStateProperty)states.Add(window.WindowState);};
        try
        {
            window.FindControl<Button>("FullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowState.Normal,window.WindowState);Assert.Contains("share-fullscreen",window.Classes);
            Assert.Equal(WindowDecorations.None,window.WindowDecorations);Assert.True(window.CanResize);Assert.False(window.Topmost);
            Assert.Equal(monitor.WorkingArea.Position,window.Position);Assert.Equal(monitor.WorkingArea.Width/monitor.Scaling,window.Width);Assert.Equal(monitor.WorkingArea.Height/monitor.Scaling,window.Height);
            Assert.Equal(0,window.MinWidth);Assert.Equal(0,window.MinHeight);Assert.True(double.IsPositiveInfinity(window.MaxWidth));Assert.True(double.IsPositiveInfinity(window.MaxHeight));
            Assert.False(vm.ShowCallChat);Assert.False(vm.ShowCallMembers);
            if(exit=="escape")window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});
            else if(exit=="button")window.FindControl<Button>("ExitFullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else { media.ParticipantLeft+=Raise.Event<Action<string>>(track); Dispatcher.UIThread.RunJobs(); Assert.Contains("share-fullscreen", window.Classes); window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape}); }
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(WindowState.FullScreen,states);Assert.DoesNotContain("share-fullscreen",window.Classes);
            Assert.Equal(initial,window.WindowState);Assert.Equal(decorations,window.WindowDecorations);Assert.True(window.CanResize);
            Assert.Equal(position,window.Position);Assert.Equal(width,window.Width);Assert.Equal(height,window.Height);
            Assert.Equal(800,window.MinWidth);Assert.Equal(600,window.MinHeight);Assert.Equal(1800,window.MaxWidth);Assert.Equal(1000,window.MaxHeight);
            Assert.True(vm.ShowCallChat);Assert.True(vm.ShowCallMembers);Assert.True(vm.InCall);
            await media.DidNotReceive().DisconnectAsync();
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task EmergencyEscapeStopsForwardingAndLeavesPresentationWhileRevokeIsBlocked()
    {
        var (vm,media)=Create();vm.InCall=true;
        var track="one · screen · track";
        vm.Videos.Add(new(track,media.SendRemoteInput){Frame=new(track,16,9,new byte[16*9*4])});
        var window=new CallWindow{DataContext=vm};window.Show();
        var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        media.RevokeControlAsync().Returns(_=>{started.TrySetResult();return release.Task;});
        try
        {
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{new NativePeer("one",true,true,"scope")},null,null,"one"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen,window.WindowState);Assert.Contains("share-fullscreen",window.Classes);
            var video=window.FindControl<ContentControl>("FocusedStage")!.GetVisualDescendants().OfType<NativeVideoView>().Single();
            video.ForwardKey(4,true,false);media.Received(1).SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Code==4));
            video.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape,KeyModifiers=KeyModifiers.Control|KeyModifiers.Alt});
            Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
            Assert.False(release.Task.IsCompleted);
            video.ForwardKey(5,true,false);media.DidNotReceive().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Code==5));
            // A queued latency/status notification cannot re-enable a paused grant.
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{new NativePeer("one",true,true,"scope")},null,null,"one"));
            Dispatcher.UIThread.RunJobs();Assert.False(vm.Videos.Single(x=>x.Identity==track).CanControl);
            Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
            Assert.Same(started.Task,await Task.WhenAny(started.Task,Task.Delay(3000,TestContext.Current.CancellationToken)));
        }
        finally{release.TrySetResult();window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task LinuxPointerEchoDoesNotResendTheSameSourcePixel()
    {
        if(!OperatingSystem.IsLinux())return;
        var inputs=new List<RemoteInput>();
        var video=new NativeVideoView{Width=200,Height=200,RemoteControlEnabled=true,Frame=new("peer · screen · track",2,2,new byte[16]),InputCommand=new CommunityToolkit.Mvvm.Input.RelayCommand<RemoteInput>(x=>inputs.Add(x!))};
        var window=new Window{Width=200,Height=200,Content=video};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            using var rendered=window.CaptureRenderedFrame();
            window.MouseMove(new Point(20,20));Dispatcher.UIThread.RunJobs();
            Assert.Single(inputs,x=>x.Kind==0);
            await Task.Delay(25,TestContext.Current.CancellationToken);
            window.MouseMove(new Point(21,21));Dispatcher.UIThread.RunJobs();
            Assert.Single(inputs,x=>x.Kind==0);
            await Task.Delay(25,TestContext.Current.CancellationToken);
            window.MouseMove(new Point(150,150));Dispatcher.UIThread.RunJobs();
            Assert.Equal(2,inputs.Count(x=>x.Kind==0));Assert.True(inputs.Last().X>.5);
        }
        finally{window.Close();Dispatcher.UIThread.RunJobs();}
    }
    [AvaloniaFact]
    public async Task LinuxVideoDeliveryYieldsToQueuedKeyboardInput()
    {
        if(!OperatingSystem.IsLinux())return;
        var (vm,media)=Create();vm.InCall=true;
        var inputRan=false;
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture("one · screen · new",16,9,new byte[16*9*4]));
        Dispatcher.UIThread.Post(()=>{Assert.DoesNotContain(vm.Videos,x=>x.Identity=="one · screen · new");inputRan=true;},DispatcherPriority.Input);
        Dispatcher.UIThread.RunJobs();
        Assert.True(inputRan);Assert.NotNull(Assert.Single(vm.Videos,x=>x.Identity=="one · screen · new").Frame);
        await vm.DisposeAsync();
    }
    private static (WorkspaceViewModel Vm,INativeMediaSession Media) Create()
    {
        var auth=Substitute.For<IAuthService>();auth.CurrentUser.Returns(new UserData{Id=Guid.NewGuid()});var chat=Substitute.For<IChatService>();var media=Substitute.For<INativeMediaSession>();
        var vm=new ChatViewModel(auth,chat,Substitute.For<IUserService>(),Substitute.For<IImageService>(),Substitute.For<IFileService>(),Substitute.For<INotificationService>());
        var workspace=new WorkspaceViewModel(vm,Substitute.For<ICollaborationApi>(),media,auth,chat){ReduceMotion=true};
        workspace.Videos.Add(new("one"){Name="Alice Example"});workspace.Videos.Add(new("two"){Name="Bob Example"});workspace.Videos.Add(new("three"){Name="Dina Example"});return(workspace,media);
    }
    [AvaloniaFact]
    public async Task ParticipantFocus_KeepsOtherPeopleVisibleAndSecondClickRestoresGallery()
    {
        var (vm,_)=Create();var window=new CallWindow{DataContext=vm};window.Show();Dispatcher.UIThread.RunJobs();var alice=vm.Videos[0];
        vm.FocusVideoCommand.Execute(alice);Dispatcher.UIThread.RunJobs();Assert.Same(alice,vm.FocusedVideo);Assert.Equal(new[]{"two","three"},vm.GalleryVideos.Select(x=>x.Identity));
        var gallery=window.FindControl<ScrollViewer>("ParticipantGallery")!;Assert.True(gallery.IsVisible);Assert.Equal(240,gallery.Bounds.Width);Assert.Equal(1,vm.GalleryColumns);
        Assert.Contains(gallery.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Bob Example");Assert.Contains(gallery.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Dina Example");
        Save(window,"native-participant-focus.png");
        vm.FocusVideoCommand.Execute(alice);Dispatcher.UIThread.RunJobs();Assert.Null(vm.FocusedVideo);Assert.Equal(3,vm.GalleryVideos.Length);
        vm.FocusVideoCommand.Execute(alice);vm.ClearVideoFocusCommand.Execute(null);Assert.Null(vm.FocusedVideo);
        window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task ExplicitFullscreenSupportsCameraAndShareWithPersistentKeyboardAccessibleControls()
    {
        var (vm,media)=Create();var window=new CallWindow{DataContext=vm,Width=1200,Height=800};window.Show();Dispatcher.UIThread.RunJobs();
        vm.FocusVideoCommand.Execute(vm.Videos[0]);window.WindowState=WindowState.FullScreen;Dispatcher.UIThread.RunJobs();Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
        window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.F11});Dispatcher.UIThread.RunJobs();Assert.Contains("share-fullscreen",window.Classes);window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Dispatcher.UIThread.RunJobs();Assert.DoesNotContain("share-fullscreen",window.Classes);
        var screen=new VideoTileViewModel("one · screen · track"){Name="Alice's screen",Frame=new("one · screen · track",2,2,new byte[16])};vm.Videos.Add(screen);vm.FocusedVideo=screen;Dispatcher.UIThread.RunJobs();
        window.FindControl<Button>("FullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.Equal(OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen,window.WindowState);Assert.Contains("share-fullscreen",window.Classes);
        var controls=window.FindControl<Border>("FullscreenControls")!;var toolbar=window.FindControl<Border>("FullscreenToolbar")!;Assert.Equal(1,toolbar.Opacity);Assert.False(window.FindControl<ScrollViewer>("ParticipantGallery")!.IsVisible);
        var point=controls.TranslatePoint(new Point(controls.Bounds.Width/2,controls.Bounds.Height-4),window)!.Value;window.MouseMove(point);Dispatcher.UIThread.RunJobs();Assert.Equal(1,toolbar.Opacity);
        window.MouseMove(new Point(4,4));Dispatcher.UIThread.RunJobs();Assert.Equal(1,toolbar.Opacity);
        window.FindControl<Button>("ExitFullscreenButton")!.Focus();Dispatcher.UIThread.RunJobs();Assert.Equal(1,toolbar.Opacity);Save(window,"native-share-hover-controls.png");
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{new NativePeer("one",true,true,Guid.NewGuid().ToString("N"))},null,null,null));Dispatcher.UIThread.RunJobs();
        Assert.True(vm.CanRequestFocusedControl);window.FindControl<Button>("FullscreenRequestControl")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await media.Received(1).RequestControlAsync("one");
        window.FindControl<Button>("ExitFullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);Assert.True(window.FindControl<ScrollViewer>("ParticipantGallery")!.IsVisible);
        window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task NamedShareContextRequestRequiresGrantThenFocusesFullscreenAndReleasesOnRevoke()
    {
        var (vm,media)=Create();vm.InCall=true;
        var owner=Guid.NewGuid().ToString();var track=owner+" · screen · track";
        var peer=new NativePeer(owner,true,true,"scope");
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,null));Dispatcher.UIThread.RunJobs();
        Assert.Equal("Participant's screen",Assert.Single(vm.RemotePeers).ScreenLabel);
        media.ParticipantJoined+=Raise.Event<Action<MediaParticipant>>(new MediaParticipant(owner,"Kirollos Adel"));Dispatcher.UIThread.RunJobs();
        Assert.Equal("Kirollos Adel's screen",Assert.Single(vm.RemotePeers).ScreenLabel);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(track,2,2,new byte[16]));Dispatcher.UIThread.RunJobs();
        var screen=Assert.Single(vm.Videos.Where(x=>x.Identity==track));Assert.Equal("Kirollos Adel's screen",screen.Name);Assert.False(screen.CanControl);
        vm.ClearVideoFocusCommand.Execute(null);
        var window=new CallWindow{DataContext=vm,Width=1200,Height=800};window.Show();Dispatcher.UIThread.RunJobs();
        var card=window.GetVisualDescendants().OfType<Border>().Single(x=>x.Classes.Contains("participant-card")&&x.DataContext==screen);
        var point=card.TranslatePoint(new Point(24,24),window)!.Value;window.MouseMove(point);window.MouseDown(point,MouseButton.Right);window.MouseUp(point,MouseButton.Right);Dispatcher.UIThread.RunJobs();
        var menu=Assert.IsType<ContextMenu>(card.ContextMenu);var request=menu.Items.OfType<MenuItem>().Single(x=>x.Header?.ToString()=="Request remote control");
        Assert.True(request.Command!.CanExecute(request.CommandParameter));request.Command.Execute(request.CommandParameter);menu.Close();Dispatcher.UIThread.RunJobs();
        await media.Received(1).RequestControlAsync(owner);Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);Assert.False(screen.CanControl);
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,owner));Dispatcher.UIThread.RunJobs();
        Assert.Same(screen,vm.FocusedVideo);Assert.True(screen.CanControl);Assert.Equal(OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen,window.WindowState);Assert.Contains("share-fullscreen",window.Classes);Assert.Contains("Kirollos Adel",vm.RemoteControlLabel);Assert.DoesNotContain(owner,vm.RemoteControlLabel);
        var input=window.FindControl<ContentControl>("FocusedStage")!.GetVisualDescendants().OfType<NativeVideoView>().Single();Assert.True(input.RemoteControlEnabled);Assert.True(input.IsFocused);
        var context = new ContextRequestedEventArgs(); input.RaiseEvent(context); Dispatcher.UIThread.RunJobs();
        Assert.True(context.Handled); Assert.Null(window.FindControl<ContentControl>("FocusedStage")!.ContextMenu);
        using (var rendered = window.CaptureRenderedFrame()) Assert.NotNull(rendered);
        var remotePoint = input.TranslatePoint(new Point(input.Bounds.Width / 2, input.Bounds.Height / 2), window)!.Value;
        window.MouseMove(remotePoint); window.MouseDown(remotePoint, MouseButton.Right); window.MouseUp(remotePoint, MouseButton.Right); Dispatcher.UIThread.RunJobs();
        media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x => x.Target == track && x.Kind == 1 && x.Code == 2 && x.Down));
        media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x => x.Target == track && x.Kind == 1 && x.Code == 2 && !x.Down));
        Assert.Null(window.FindControl<ContentControl>("FocusedStage")!.ContextMenu);
        input.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.A});media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Target==track&&x.Kind==2&&x.Code==4&&x.Down));
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,null));Dispatcher.UIThread.RunJobs();Assert.False(screen.CanControl);Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
        media.ClearReceivedCalls();input.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.B});media.DidNotReceive().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind==2));
        window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task GrantBeforeScreenFrameWaitsForThatShareAndRenamingNeverShowsIdentifiers()
    {
        var (vm,media)=Create();vm.InCall=true;var owner=Guid.NewGuid().ToString();var peer=new NativePeer(owner,true,true,"scope");
        var window=new CallWindow{DataContext=vm};window.Show();Dispatcher.UIThread.RunJobs();
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,owner));Dispatcher.UIThread.RunJobs();Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(owner+" · screen · track",2,2,new byte[16]));Dispatcher.UIThread.RunJobs();Assert.Equal(OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen,window.WindowState);Assert.Contains("share-fullscreen",window.Classes);Assert.True(vm.FocusedVideo!.CanControl);
        media.ParticipantJoined+=Raise.Event<Action<MediaParticipant>>(new MediaParticipant(owner,"Dina Example"));Dispatcher.UIThread.RunJobs();Assert.Equal("Dina Example's screen",vm.FocusedVideo.Name);Assert.Equal("Dina Example's screen",Assert.Single(vm.RemotePeers).ScreenLabel);
        window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Dispatcher.UIThread.RunJobs();
        Assert.Contains("share-fullscreen",window.Classes);media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind==2&&x.Code==41&&x.Down));
        window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.F11});Assert.Contains("share-fullscreen",window.Classes);
        media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind==2&&x.Code==68&&x.Down));
        window.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape,KeyModifiers=KeyModifiers.Control|KeyModifiers.Alt});Dispatcher.UIThread.RunJobs();Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,owner));Dispatcher.UIThread.RunJobs();Assert.NotEqual(WindowState.FullScreen,window.WindowState);Assert.DoesNotContain("share-fullscreen",window.Classes);
        window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task ControlledCallRoutesScrollAndKeepsLocalExitButtonUsable()
    {
        var (vm,media)=Create();vm.InCall=true;const string track="one · screen · track";
        vm.Videos.Add(new(track,vm.ForwardRemoteKeyboard){Frame=new(track,16,9,new byte[16*9*4])});var window=new CallWindow{DataContext=vm};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{new NativePeer("one",true,true,"scope")},null,null,"one"));Dispatcher.UIThread.RunJobs();
            using(var rendered=window.CaptureRenderedFrame())Assert.NotNull(rendered);
            var text=new TextInputEventArgs{RoutedEvent=InputElement.TextInputEvent,Text="local text"};window.RaiseEvent(text);Assert.True(text.Handled);
            var view=window.FindControl<ContentControl>("FocusedStage")!.GetVisualDescendants().OfType<NativeVideoView>().Single();
            var point=view.TranslatePoint(new Point(view.Bounds.Width/2,view.Bounds.Height/2),window)!.Value;
            window.MouseWheel(point,new Vector(.5,-1));
            media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind==6&&x.DeltaX==.5&&x.DeltaY==-1&&x.PositionIncluded));
            var button=window.FindControl<Button>("ExitFullscreenButton")!;
            var local=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),window)!.Value;
            window.MouseDown(local,MouseButton.Left);window.MouseUp(local,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("share-fullscreen",window.Classes);
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [AvaloniaTheory]
    [InlineData("track")][InlineData("owner")][InlineData("revoke")]
    public async Task LossOfControlledShareRestoresWindowAndRejectsStaleGrantInput(string reason)
    {
        var (vm,media)=Create();vm.InCall=true;const string track="one · screen · track";
        vm.Videos.Add(new(track){Frame=new(track,16,9,new byte[16*9*4])});
        var window=new CallWindow{DataContext=vm};window.Show();Dispatcher.UIThread.RunJobs();
        var peer=new NativePeer("one",true,true,"scope");
        try
        {
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,"one"){InputEpoch=7});Dispatcher.UIThread.RunJobs();
            Assert.Contains("share-fullscreen",window.Classes);
            media.ClearReceivedCalls();vm.ForwardRemoteKeyboard(new(track,2,Code:4,Down:true,ControlEpoch:6));
            media.DidNotReceive().SendRemoteInput(Arg.Any<RemoteInput>());
            vm.ForwardRemoteKeyboard(new(track,2,Code:4,Down:true,ControlEpoch:7));media.Received(1).SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Code==4));
            if(reason=="revoke")media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{peer},null,null,null));
            else media.ParticipantLeft+=Raise.Event<Action<string>>(reason=="track"?track:"one");
            Dispatcher.UIThread.RunJobs();Assert.Null(vm.ControlledScreen);Assert.DoesNotContain("share-fullscreen",window.Classes);Assert.NotEqual(WindowState.FullScreen,window.WindowState);
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [Theory]
    [InlineData(ScreenCaptureBackend.Windows,ScreenShareKind.Screen,0,0,1920,1080,true)]
    [InlineData(ScreenCaptureBackend.X11,ScreenShareKind.Screen,0,0,1920,1080,true)]
    [InlineData(ScreenCaptureBackend.MacOS,ScreenShareKind.Screen,0,0,1920,1080,true)]
    [InlineData(ScreenCaptureBackend.Windows,ScreenShareKind.Screen,1920,0,1920,1080,false)]
    [InlineData(ScreenCaptureBackend.Windows,ScreenShareKind.Window,0,0,1920,1080,false)]
    [InlineData(ScreenCaptureBackend.Portal,ScreenShareKind.Screen,0,0,1920,1080,false)]
    [InlineData(ScreenCaptureBackend.Windows,ScreenShareKind.Screen,0,0,1280,720,false)]
    public void ControlHosting_RequiresTheSupportedPrimaryDesktop(ScreenCaptureBackend backend,ScreenShareKind kind,int x,int y,int width,int height,bool allowed)
    {
        Assert.Equal(allowed,RemoteControlPolicy.SupportsDesktopSource(new("","","",new("source","Source",kind,backend,x,y,width,height)),1920,1080));
    }
    [Fact]
    public void BrowserController_NeedsHandshakeAndExplicitGrantAndCannotOfferHosting()
    {
        var policy=new RemoteControlPolicy();policy.Sharing(true);Assert.False(policy.Request("browser"));
        policy.Hello("browser",new RemotePacket("hello",CanHost:false,Sharing:false));Assert.True(policy.Request("browser"));
        var input=new RemotePacket("input",Scope:policy.LocalScope,Sequence:1,Input:new("host · screen",0,.5,.5));Assert.False(policy.Authorize("browser",input));
        var grant=policy.Approve("browser");Assert.True(policy.Authorize("browser",input with{Grant=grant.Grant}));Assert.False(policy.Authorize("another",input with{Grant=grant.Grant,Sequence=2}));Assert.Throws<InvalidOperationException>(()=>policy.BeginRequest("browser"));
        policy.Revoke();Assert.False(policy.Authorize("browser",input with{Grant=grant.Grant,Sequence=2}));
    }
    [Theory]
    [InlineData(-1920,0,1920,1032,1,1920,1032)]
    [InlineData(0,48,3840,2112,2,1920,1056)]
    [InlineData(64,-1080,1856,1080,1.5,1237.3333333333333,720)]
    public void BorderlessWorkAreaExcludesDockAndPreservesMonitorOrigin(int x,int y,int width,int height,double scale,double logicalWidth,double logicalHeight)
    {
        var area = new PixelRect(x,y,width,height);
        Assert.Equal(area, BorderlessPresentation.PresentationArea(new PixelRect(-1920,-1080,3840,2160),area));
        var size = BorderlessPresentation.ClientSize(area,scale);
        Assert.Equal(logicalWidth,size.Width,6); Assert.Equal(logicalHeight,size.Height,6);
        var monitor = new PixelRect(-1920,0,1920,1080);
        Assert.Equal(monitor,BorderlessPresentation.PresentationArea(monitor,default));
    }
    [AvaloniaTheory]
    [InlineData(true,1200,800)] [InlineData(false,760,560)]
    public async Task FullscreenSwitchesEverySourceAndGalleryWithoutExitingAndKeepsActionsInsideViewport(bool dark,int width,int height)
    {
        var (vm,media)=Create();vm.InCall=true;vm.CallTitle="Fire User · Design review";
        var first=new VideoTileViewModel("one · screen · track"){Name="Alice’s screen",Frame=SyntheticFrame("one · screen · track",32,66,93)};
        var second=new VideoTileViewModel("two · screen · track"){Name="Bob’s screen",Frame=SyntheticFrame("two · screen · track",95,42,25)};
        vm.Videos.Add(first);vm.Videos.Add(second);vm.Videos[0].Frame=SyntheticFrame("one",34,57,44);
        var window=new CallWindow{DataContext=vm,RequestedThemeVariant=dark?Avalonia.Styling.ThemeVariant.Dark:Avalonia.Styling.ThemeVariant.Light};window.Show();
        try
        {
            window.FindControl<Button>("FullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            window.Width=width;window.Height=height;Dispatcher.UIThread.RunJobs();
            var picker=window.FindControl<ComboBox>("FullscreenSourcePicker")!;
            Assert.Equal(vm.Videos.Count,picker.Items.Count);Assert.True(window.FindControl<Border>("PresentationHeader")!.IsVisible);
            foreach(var source in new[]{first,second,vm.Videos[0]})
            {
                picker.SelectedItem=source;Dispatcher.UIThread.RunJobs();Assert.Same(source,vm.FocusedVideo);
                Assert.Same(source,window.FindControl<ContentControl>("FocusedStage")!.Content);Assert.Contains("share-fullscreen",window.Classes);
            }
            picker.SelectedItem=second;Dispatcher.UIThread.RunJobs();Save(window,$"fullscreen-source-{(dark?"dark":"light")}.png");
            var members=window.FindControl<Border>("FullscreenToolbar")!.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Members"));
            var chat=window.FindControl<Border>("FullscreenToolbar")!.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Chat"));
            members.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));chat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowCallChat);if(width<1100)Assert.False(vm.ShowCallMembers);Assert.Contains("share-fullscreen",window.Classes);
            chat.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));vm.ShowCallMembers=false;
            vm.ClearVideoFocusCommand.Execute(null);Dispatcher.UIThread.RunJobs();Assert.True(window.FindControl<ScrollViewer>("ParticipantGallery")!.IsVisible);
            Assert.False(window.FindControl<ContentControl>("FocusedStage")!.IsVisible);Assert.Contains("share-fullscreen",window.Classes);
            Assert.All(window.FindControl<ScrollViewer>("ParticipantGallery")!.GetVisualDescendants().OfType<NativeVideoView>(),x=>Assert.False(x.RemoteControlEnabled));
            var toolbar=window.FindControl<Border>("FullscreenControls")!;Assert.Equal(1,window.FindControl<Border>("FullscreenToolbar")!.Opacity);
            var bottom=toolbar.TranslatePoint(new Point(0,toolbar.Bounds.Height),window)!.Value.Y;
            Assert.InRange(bottom,0,window.ClientSize.Height);Assert.True(window.FindControl<ScrollViewer>("FullscreenToolbarScroll")!.Bounds.Height<=height*.4+1);
            Save(window,$"fullscreen-gallery-{(dark?"dark":"light")}.png");
            var activeSource=vm.FocusedVideo;
            media.VideoReceived+=Raise.Event<Action<VideoPicture>>(SyntheticFrame("three · screen · new",50,50,50));Dispatcher.UIThread.RunJobs();
            // Once a camera or share is selected, an arriving stream must not steal it.
            picker.SelectedItem=first;media.VideoReceived+=Raise.Event<Action<VideoPicture>>(SyntheticFrame("four · screen · new",50,50,50));Dispatcher.UIThread.RunJobs();Assert.Same(first,vm.FocusedVideo);
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task ApprovedSessionsRemainDimmedUntilOwnershipAndToolbarClicksNeverReachRemoteDevice()
    {
        var (vm,media)=Create();vm.InCall=true;
        const string first="one · screen · track",second="two · screen · track";
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(SyntheticFrame(first,32,66,93));media.VideoReceived+=Raise.Event<Action<VideoPicture>>(SyntheticFrame(second,95,42,25));Dispatcher.UIThread.RunJobs();
        var peers=new[]{new NativePeer("one",true,true,"scope1"){MultiControl=true},new NativePeer("two",true,true,"scope2"){MultiControl=true}};
        var status=new RemoteControlStatus(peers,null,null,"one"){Sessions=new[]{new RemoteSession("one",false),new RemoteSession("two",false)},InputActive=false,InputEpoch=1};
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(status);Dispatcher.UIThread.RunJobs();
        var window=new CallWindow{DataContext=vm,RequestedThemeVariant=Avalonia.Styling.ThemeVariant.Dark};window.Show();
        try
        {
            window.FindControl<Button>("FullscreenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            var tile=vm.Videos.Single(x=>x.Identity==first);Assert.True(tile.WaitingForControl);
            var video=window.FindControl<ContentControl>("FocusedStage")!.GetVisualDescendants().OfType<NativeVideoView>().Single();Assert.False(video.RemoteInputActive);Assert.Equal(.5,video.Opacity);
            using(var frame=window.CaptureRenderedFrame())Assert.NotNull(frame);
            media.ClearReceivedCalls();video.ForwardKey(4,true,false);media.DidNotReceive().SendRemoteInput(Arg.Any<RemoteInput>());
            var point=video.TranslatePoint(new Point(video.Bounds.Width/2,video.Bounds.Height/2),window)!.Value;
            window.MouseDown(point,MouseButton.Left);window.MouseUp(point,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Target==first&&x.Kind==1&&x.Down));Save(window,"fullscreen-control-passive.png");
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(status with{InputActive=true,InputEpoch=2,Sessions=new[]{new RemoteSession("one",true),new RemoteSession("two",false)}});Dispatcher.UIThread.RunJobs();
            Assert.False(tile.WaitingForControl);Assert.True(video.RemoteInputActive);Save(window,"fullscreen-control-active.png");
            media.ClearReceivedCalls();
            var chat=window.FindControl<Border>("FullscreenToolbar")!.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Chat"));
            var local=chat.TranslatePoint(new Point(chat.Bounds.Width/2,chat.Bounds.Height/2),window)!.Value;
            window.MouseDown(local,MouseButton.Left);window.MouseUp(local,MouseButton.Left);Dispatcher.UIThread.RunJobs();
            Assert.True(vm.ShowCallChat);media.DidNotReceive().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind!=3));
            media.Received().SendRemoteInput(Arg.Is<RemoteInput>(x=>x.Kind==3));
            window.FindControl<ComboBox>("FullscreenSourcePicker")!.SelectedItem=vm.Videos.Single(x=>x.Identity==second);Dispatcher.UIThread.RunJobs();
            media.Received().SelectControlTarget("two");Assert.Contains("share-fullscreen",window.Classes);Assert.True(vm.Videos.Single(x=>x.Identity==second).CanControl);
            Assert.Equal(2,vm.ControlSessions.Count);
            var sessionsButton=window.FindControl<Button>("ControlSessionsButton")!;
            sessionsButton.Flyout!.ShowAt(sessionsButton);Dispatcher.UIThread.RunJobs();
            var flyout=Assert.IsType<Flyout>(sessionsButton.Flyout);
            var end=Assert.IsAssignableFrom<Control>(flyout.Content).GetVisualDescendants().OfType<Button>().First(x=>Equals(x.Content,"End participant session"));
            Assert.Same(vm.EndControlSessionCommand,end.Command);Assert.Equal("one",Assert.IsType<RemoteControlSessionItem>(end.CommandParameter).Identity);
            Assert.True(end.Command!.CanExecute(end.CommandParameter));end.Command.Execute(end.CommandParameter);
            await media.Received(1).RevokeControlSessionAsync("one");sessionsButton.Flyout.Hide();
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public void BorderlessExitRestoresFixedWindowAndQueuedFitCannotResizeItAgain()
    {
        var window=new Window{Width=900,Height=650,CanResize=false};window.Show();window.Position=new PixelPoint(80,90);
        var presentation=new BorderlessPresentation(window);
        try
        {
            presentation.Enter();Assert.True(window.CanResize);presentation.Exit();Dispatcher.UIThread.RunJobs();
            Assert.False(presentation.IsActive);Assert.False(window.CanResize);Assert.Equal(900,window.Width);Assert.Equal(650,window.Height);
            Assert.Equal(new PixelPoint(80,90),window.Position);Assert.Equal(WindowDecorations.Full,window.WindowDecorations);
        }
        finally{window.Close();}
    }
    [AvaloniaFact]
    public async Task EmergencyStopCannotBeReenabledByQueuedUnselectedSessions()
    {
        var (vm,media)=Create();var track="one · screen · track";vm.Videos.Add(new(track));
        var status=new RemoteControlStatus(new[]{new NativePeer("one",true,true,"scope")},null,null,"one")
        {Sessions=new[]{new RemoteSession("one",false)},InputActive=false,InputEpoch=1};
        try
        {
            media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(status);Dispatcher.UIThread.RunJobs();Assert.True(vm.Videos.Single(x=>x.Identity==track).CanControl);
            vm.PauseRemoteInput();media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(status with{Controlling=null});Dispatcher.UIThread.RunJobs();
            Assert.False(vm.Videos.Single(x=>x.Identity==track).CanControl);Assert.Null(vm.ControlledScreen);
        }
        finally{await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task StopControlRemainsAvailableWhileMediaIsBusyAndBlocksInputImmediately()
    {
        var (vm,media)=Create();vm.InCall=true;const string track="one · screen · track";vm.Videos.Add(new(track));
        var pending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        media.SetMicrophoneAsync(true,Arg.Any<MediaDevices>(),Arg.Any<CancellationToken>()).Returns(pending.Task);
        media.RemoteControlChanged+=Raise.Event<Action<RemoteControlStatus>>(new RemoteControlStatus(new[]{new NativePeer("one",true,true,"scope")},null,null,"one"){InputEpoch=7});Dispatcher.UIThread.RunJobs();
        var microphone=vm.ToggleMicrophoneCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.MediaBusy);await vm.StopControlCommand.ExecuteAsync(null);await media.Received(1).RevokeControlAsync();
            Assert.True(vm.MediaBusy);Assert.False(vm.Videos.Single(x=>x.Identity==track).CanControl);
            media.ClearReceivedCalls();vm.ForwardRemoteKeyboard(new(track,2,Code:4,Down:true,ControlEpoch:7));media.DidNotReceive().SendRemoteInput(Arg.Any<RemoteInput>());
        }
        finally{pending.TrySetResult();await microphone;await vm.DisposeAsync();}
    }
    private static VideoPicture SyntheticFrame(string identity,byte red,byte green,byte blue)
    {
        const int width=640,height=360;var pixels=new byte[width*height*4];
        for(var y=0;y<height;y++)for(var x=0;x<width;x++)
        {var i=(y*width+x)*4;var line=x%80<2||y%60<2;pixels[i]=(byte)(line?red+50:red);pixels[i+1]=(byte)(line?green+50:green);pixels[i+2]=(byte)(line?blue+50:blue);pixels[i+3]=255;}
        return new(identity,width,height,pixels);
    }
    private static void Save(Window window,string name)
    {
        if(Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is not {} path)return;
        Directory.CreateDirectory(path);Dispatcher.UIThread.RunJobs();using var frame=window.CaptureRenderedFrame();Assert.NotNull(frame);using var file=File.Create(Path.Combine(path,name));frame.Save(file,new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task CallChatUsesSharedBubblesWithSenderTimeAndMessageActions(bool light)
    {
        var (vm,_)=Create();vm.InCall=true;vm.ShowCallChat=true;
        var peer=Guid.NewGuid();vm.Chat.SelectedRecipient=new(){ReceiverId=peer,ReceiverName="Maya Hassan"};Dispatcher.UIThread.RunJobs();
        var incoming=new ChatMessageItemViewModel(new ChatMessage{MessageId="incoming",Sender=peer,SenderName="Maya Hassan",Receiver=vm.Chat.CurrentUser.Id,Text="The screen is ready.",TimeSent=new DateTime(2025,2,8,9,35,0,DateTimeKind.Utc)},false,Substitute.For<IChatService>(),Substitute.For<IFileService>(),Substitute.For<IImageService>());
        var outgoing=new ChatMessageItemViewModel(new ChatMessage{MessageId="outgoing",Sender=vm.Chat.CurrentUser.Id,Text="I can see it now.",TimeSent=DateTime.UtcNow,Seen=true,IsEdited=true,ReplyToMessageId="incoming",ReplyToSenderName="Maya Hassan"},true,Substitute.For<IChatService>(),Substitute.For<IFileService>(),Substitute.For<IImageService>());
        vm.Chat.Messages.Add(incoming);vm.Chat.Messages.Add(outgoing);
        var window=new CallWindow{DataContext=vm,Width=1400,Height=900,RequestedThemeVariant=light?Avalonia.Styling.ThemeVariant.Light:Avalonia.Styling.ThemeVariant.Dark};window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var stream=window.FindControl<ListBox>("CallMessageStream")!;var cards=stream.GetVisualDescendants().OfType<ChatMessageCard>().ToArray();Assert.Equal(2,cards.Length);Assert.All(cards,x=>Assert.Same(vm.Chat,x.Chat));
            Assert.Contains(cards,x=>x.SenderLabel=="You");Assert.Contains(cards,x=>x.SenderLabel=="Maya Hassan");
            var rows=stream.GetVisualDescendants().OfType<Grid>().Where(x=>x.Classes.Contains("msg-row")).ToArray();Assert.Contains(rows,x=>x.Classes.Contains("mine")&&x.HorizontalAlignment==Avalonia.Layout.HorizontalAlignment.Right);Assert.Contains(rows,x=>x.Classes.Contains("theirs")&&x.HorizontalAlignment==Avalonia.Layout.HorizontalAlignment.Left);
            var dates=stream.GetVisualDescendants().OfType<TextBlock>().Where(x=>Avalonia.Automation.AutomationProperties.GetName(x)=="Message sent at"&&x.IsEffectivelyVisible).ToArray();Assert.Equal(2,dates.Length);Assert.Contains(dates,x=>x.Text!.Contains("2025")&&x.Text.Contains(WorkClock.Time(incoming.TimeSent.ToLocalTime())));
            Assert.Contains(stream.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text=="Edited"&&x.IsEffectivelyVisible);Assert.Contains(stream.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text==outgoing.ReplyLabel&&x.IsEffectivelyVisible);
            var actions=stream.GetVisualDescendants().OfType<Button>().First(x=>Avalonia.Automation.AutomationProperties.GetName(x)=="Message actions");actions.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();
            Assert.NotNull(actions.ContextMenu);actions.ContextMenu!.Close();Dispatcher.UIThread.RunJobs();Save(window,$"call-chat-metadata-{(light?"light":"dark")}.png");
        }
        finally{window.CloseAfterCall();Dispatcher.UIThread.RunJobs();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory][InlineData("mute")][InlineData("unpublish")]
    public async Task RemoteCameraOffDiscardsQueuedAndLateFramesWithoutStoppingTheShare(string stop)
    {
        var (vm,media)=Create();vm.InCall=true;const string camera="one · camera · old";const string share="one · screen · presentation";
        var alice=vm.Videos.Single(x=>x.Identity=="one");vm.FocusedVideo=alice;
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(camera,16,9,new byte[16*9*4]));Dispatcher.UIThread.RunJobs();Assert.NotNull(alice.Frame);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(camera,16,9,new byte[16*9*4]));
        if(stop=="mute")media.ParticipantUpdated+=Raise.Event<Action<MediaParticipantUpdate>>(new MediaParticipantUpdate("one","camera","0"));else media.ParticipantLeft+=Raise.Event<Action<string>>(camera);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(camera,16,9,new byte[16*9*4]));media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(share,16,9,new byte[16*9*4]));Dispatcher.UIThread.RunJobs();
        Assert.Null(alice.Frame);Assert.False(alice.HasFrame);Assert.Same(alice,vm.FocusedVideo);Assert.Contains(vm.Videos,x=>x.Identity==share&&x.HasFrame);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(new VideoPicture(camera,16,9,new byte[16*9*4]));Dispatcher.UIThread.RunJobs();Assert.Null(alice.Frame);
        media.ParticipantUpdated+=Raise.Event<Action<MediaParticipantUpdate>>(new MediaParticipantUpdate("one","camera","1"));var renewed=new VideoPicture("one · camera · new",16,9,new byte[16*9*4]);media.VideoReceived+=Raise.Event<Action<VideoPicture>>(renewed);Dispatcher.UIThread.RunJobs();Assert.Same(renewed,alice.Frame);
        vm.Chat.UnregisterEvents();await vm.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task UnpublishedShareCannotReturnFromAQueuedFrame()
    {
        var (vm,media)=Create();vm.InCall=true;const string share="one · screen · old";var frame=new VideoPicture(share,16,9,new byte[16*9*4]);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(frame);Dispatcher.UIThread.RunJobs();Assert.Contains(vm.Videos,x=>x.Identity==share);
        media.VideoReceived+=Raise.Event<Action<VideoPicture>>(frame);media.ParticipantLeft+=Raise.Event<Action<string>>(share);media.VideoReceived+=Raise.Event<Action<VideoPicture>>(frame);Dispatcher.UIThread.RunJobs();Assert.DoesNotContain(vm.Videos,x=>x.Identity==share);
        vm.Chat.UnregisterEvents();await vm.DisposeAsync();
    }

}
