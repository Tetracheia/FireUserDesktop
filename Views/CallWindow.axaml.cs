using System;
using Avalonia;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UserIdentityDesktop.Core.Models;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UserIdentityDesktop.ViewModels;

namespace UserIdentityDesktop.Views;

public partial class CallWindow : Window
{
    private bool closingAfterCall, leaving, presentationFullscreen, windowStateCheckQueued, controlFullscreen;
    private WindowState previousWindowState;
    private readonly BorderlessPresentation borderlessPresentation;
    private bool previousMembers, previousChat;
    private WorkspaceViewModel? observedModel;
    private RemoteControlRequestWindow? controlRequestToast;
    private RemoteKeyboardCapture? keyboardCapture;
    internal RemoteControlRequestWindow? ControlRequestToast => controlRequestToast;
    private async void ChooseCallAttendees_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceViewModel vm) return;
        vm.CallAttendees.AllowsMeetingGuests = vm.IsMeetingCall;
        await new AttendeePickerWindow(vm.CallAttendees, vm.ReduceMotion).ShowDialog(this);
    }
    public CallWindow()
    {
        InitializeComponent();
        borderlessPresentation=new(this);
        Opened += (_, _) => keyboardCapture = new RemoteKeyboardCapture(this);
        Closed += (_, _) => { keyboardCapture?.Dispose(); keyboardCapture = null; };
        var entryMotion = new SurfaceMotion();
        Opened += (_, _) => { if (Content is Control surface) _ = entryMotion.EnterAsync(surface, SurfaceMotion.Reduced(this), SurfaceMotionKind.Popup); };
        Closed += (_, _) => entryMotion.Cancel();
        PropertyChanged += (_, e) => { if (e.Property == MotionPreferences.ReducedProperty && MotionPreferences.GetReduced(this)) entryMotion.Cancel(); };
        SizeChanged += (_, _) => { UpdateGalleryLayout(); FullscreenToolbarScroll.MaxHeight = Math.Max(76, Bounds.Height * .4); };
        AddHandler(KeyDownEvent, PresentationKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent,(_,e)=>RouteControlKey(e,false),RoutingStrategies.Tunnel);
        AddHandler(TextInputEvent,(_,e)=>{if(IsActive&&observedModel?.ControlledScreen!=null&&RemoteVideoHasFocus)e.Handled=true;},RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent,RouteControlPointer,RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent,RouteControlPointer,RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent,RouteControlPointer,RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent,RouteControlPointer,RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            controlRequestToast?.Close(); controlRequestToast = null;
            ExitShareFullscreen();
            if (observedModel != null) { observedModel.PropertyChanged -= OnPresentationChanged; observedModel.Chat.RequestScrollToBottom -= ScrollCallChat; observedModel.ChooseScreenShareSourceAsync = null; }
            observedModel = DataContext as WorkspaceViewModel;
            if (observedModel != null) { observedModel.PropertyChanged += OnPresentationChanged; observedModel.Chat.RequestScrollToBottom += ScrollCallChat; observedModel.ChooseScreenShareSourceAsync = ChooseScreenShareSource; }
            UpdateGalleryLayout();
        };
        PropertyChanged += (_, e) =>
        {
            if (e.Property != WindowStateProperty || windowStateCheckQueued) return;
            // X11 raises state notifications from inside its native state setter.
            // Changing the state there recursively re-enters WindowState until stack overflow.
            windowStateCheckQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                windowStateCheckQueued = false;
                if (WindowState == WindowState.FullScreen && (OperatingSystem.IsLinux() || !presentationFullscreen)) { WindowState = WindowState.Normal; return; }
                if (presentationFullscreen && WindowState != (OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen)) ExitShareFullscreen(restoreWindowState: false);
            });
        };
        Closed += (_, _) => { controlRequestToast?.Close(); controlRequestToast = null; if (observedModel != null) { observedModel.PropertyChanged -= OnPresentationChanged; observedModel.Chat.RequestScrollToBottom -= ScrollCallChat; observedModel.ChooseScreenShareSourceAsync = null; } };
        Closing += async (_, args) =>
        {
            if (args.CloseReason is WindowCloseReason.OSShutdown or WindowCloseReason.ApplicationShutdown or WindowCloseReason.OwnerWindowClosing) return;
            if (closingAfterCall || DataContext is not WorkspaceViewModel { CallWindowVisible: true } vm) return;
            args.Cancel = true;
            if (leaving) return;
            leaving = true;
            try { await vm.LeaveCommand.ExecuteAsync(null); }
            catch (Exception ex) { vm.Feedback = ex.Message; }
            finally { CloseAfterCall(); }
        };
    }
    private async Task<ScreenShareSource?> ChooseScreenShareSource(IReadOnlyList<ScreenShareSource> discovered)
    {
        var sources = discovered.ToList();
        if (!OperatingSystem.IsMacOS() && !sources.Any(x => x.Backend == ScreenCaptureBackend.Portal))
        {
            var backend = OperatingSystem.IsWindows() ? ScreenCaptureBackend.Windows : ScreenCaptureBackend.X11;
            var index = 0;
            foreach (var screen in Screens.All)
            {
                var bounds = screen.Bounds;
                sources.Insert(index, new("monitor-" + index, $"Screen {++index} · {bounds.Width} × {bounds.Height}", ScreenShareKind.Screen, backend, bounds.X, bounds.Y, bounds.Width, bounds.Height));
            }
        }
        var picker = new ScreenSharePicker { DataContext = new ScreenSharePickerViewModel(sources), RequestedThemeVariant = ActualThemeVariant };
        return await picker.ShowDialog<ScreenShareSource?>(this);
    }
    private void Fullscreen_Click(object? sender, RoutedEventArgs e) => ToggleShareFullscreen();
    private void ToggleShareFullscreen()
    {
        if (presentationFullscreen) { ExitShareFullscreen(); return; }
        if (DataContext is not WorkspaceViewModel vm) return;
        previousWindowState = WindowState; previousMembers = vm.ShowCallMembers; previousChat = vm.ShowCallChat;
        vm.ShowCallMembers = vm.ShowCallChat = false;
        presentationFullscreen = true; Classes.Add("share-fullscreen");
        if(OperatingSystem.IsLinux())borderlessPresentation.Enter();
        else WindowState = WindowState.FullScreen;
    }
    private void ExitShareFullscreen(bool restoreWindowState = true)
    {
        if (!presentationFullscreen) return;
        presentationFullscreen = false;controlFullscreen = false; Classes.Remove("share-fullscreen");
        if(OperatingSystem.IsLinux())borderlessPresentation.Exit(restoreWindowState);
        else if(restoreWindowState)WindowState=previousWindowState;
        if (observedModel != null) { observedModel.ShowCallMembers = previousMembers; observedModel.ShowCallChat = previousChat; }
    }
    private void PresentationKeyDown(object? sender, KeyEventArgs e)
    {
        if(observedModel?.ControlledScreen!=null)
        {
            if(e.Key==Key.Escape && e.KeyModifiers==(KeyModifiers.Control|KeyModifiers.Alt))
            {
                foreach(var view in this.GetVisualDescendants().OfType<NativeVideoView>())view.SuspendRemoteInput();
                observedModel.PauseRemoteInput();ExitShareFullscreen();e.Handled=true;return;
            }
            if (RemoteVideoHasFocus) { RouteControlKey(e,true); return; }
        }
        if (e.KeyModifiers != KeyModifiers.None) return;
        if (e.Key == Key.Escape && presentationFullscreen) { ExitShareFullscreen(); e.Handled = true; }
        else if (e.Key == Key.F11 && (presentationFullscreen || observedModel != null)) { ToggleShareFullscreen(); e.Handled = true; }
    }
    internal bool RemoteVideoHasFocus => observedModel?.ControlledScreen?.InputActive == true && ControlledView()?.IsKeyboardFocusWithin == true;
    private NativeVideoView? ControlledView()=>FocusedStage.GetVisualDescendants().OfType<NativeVideoView>().FirstOrDefault(x=>x.RemoteControlEnabled);
    private void RouteControlKey(KeyEventArgs e,bool down)
    {
        if(!IsActive || observedModel?.ControlledScreen==null || !RemoteVideoHasFocus)return;
        if(keyboardCapture?.Capturing!=true)ControlledView()?.ForwardKeyEvent(e,down);
        e.Handled=true;
    }
    private void RouteControlPointer(object? sender,PointerEventArgs e)
    {
        if(!IsActive || observedModel?.ControlledScreen==null)return;
        if(e.Source is Control source && (source as Button ?? source.FindAncestorOfType<Button>()) is {} button && button.Classes.Contains("remote-release"))
        {
            if(e is PointerPressedEventArgs){observedModel.PauseRemoteInput();ExitShareFullscreen();}
            e.Handled=true;return;
        }
        // Local call controls must never become remote desktop clicks.
        if (e.Source is not Visual sourceVisual ||
            !(ReferenceEquals(sourceVisual, FocusedStage) || sourceVisual.GetVisualAncestors().Contains(FocusedStage))) return;
        ControlledView()?.ForwardPointer(e);
        e.Handled=true;
    }
    private void OnPresentationChanged(object? sender, PropertyChangedEventArgs e)
    {
        keyboardCapture?.Refresh();
        if (e.PropertyName == nameof(WorkspaceViewModel.ControlRequester))
        {
            controlRequestToast?.Close(); controlRequestToast = null;
            if (observedModel?.ControlRequester != null)
            {
                controlRequestToast = new RemoteControlRequestWindow { DataContext = observedModel, RequestedThemeVariant = ActualThemeVariant };
                controlRequestToast.Show();
                var area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea;
                if (area is {} bounds) controlRequestToast.Position = new(bounds.Right - (int)(controlRequestToast.Width * controlRequestToast.RenderScaling) - 20, bounds.Y + 24);
            }
        }
        if (e.PropertyName == nameof(WorkspaceViewModel.ControlledScreen))
        {
            if (observedModel?.ControlledScreen is {} controlled)
            {
                if (!presentationFullscreen && controlled.InputActive) ToggleShareFullscreen();
                controlFullscreen = presentationFullscreen;
                Dispatcher.UIThread.Post(() =>
                {
                    if (observedModel?.ControlledScreen != controlled || !controlled.CanControl) return;
                    Activate();
                    FocusedStage.GetVisualDescendants().OfType<NativeVideoView>().FirstOrDefault()?.Focus();
                }, DispatcherPriority.Loaded);
            }
            else if (controlFullscreen && (observedModel?.HasRemoteControl != true || !observedModel.Videos.Any(x => x.CanControl))) ExitShareFullscreen();
        }
        if(e.PropertyName==nameof(WorkspaceViewModel.ShowCallChat) && observedModel?.ShowCallChat==true)ScrollCallChat();
        if (e.PropertyName is nameof(WorkspaceViewModel.ParticipantSummary) or nameof(WorkspaceViewModel.ShowCallMembers) or nameof(WorkspaceViewModel.ShowCallChat) or nameof(WorkspaceViewModel.HasFocusedVideo)) UpdateGalleryLayout();

    }
    private void UpdateGalleryLayout()
    {
        if (observedModel == null) return;
        if (presentationFullscreen && Bounds.Width < 1100 && observedModel.ShowCallChat && observedModel.ShowCallMembers) observedModel.ShowCallMembers = false;
        var available = (Bounds.Width > 0 ? Bounds.Width : Width) - 40 - (observedModel.ShowCallMembers ? 266 : 0) - (observedModel.ShowCallChat ? 378 : 0);
        observedModel.GalleryColumns = observedModel.HasFocusedVideo ? 1 : Math.Max(1, Math.Min(Math.Max(1, observedModel.Videos.Count), Math.Min(3, (int)(available / 380))));
    }
    private void FocusedTile_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (observedModel?.FocusedVideo?.CanControl == true) { e.Handled = true; return; }
        if (sender is Control target && observedModel?.FocusedVideo is {} tile) { OpenTileMenu(target,tile,e.TryGetPosition(target,out _)); e.Handled = true; }
    }
    private void Tile_ContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control { DataContext: VideoTileViewModel { CanControl: true } }) { e.Handled = true; return; }
        if (sender is Control { DataContext: VideoTileViewModel tile } target) { OpenTileMenu(target,tile,true); e.Handled = true; }
    }
    private void TileMenu_Click(object? sender, RoutedEventArgs e) { if (sender is Control { DataContext: VideoTileViewModel tile } target) OpenTileMenu(target,tile,false); }
    private void OpenTileMenu(Control target, VideoTileViewModel tile, bool atPointer)
    {
        if (observedModel == null) return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = tile.IsScreen ? "Focus screen share" : "Focus participant", Command = observedModel.FocusVideoCommand, CommandParameter = tile });
        if (tile.IsScreen) menu.Items.Add(new MenuItem { Header = presentationFullscreen ? "Exit full screen" : "Full screen", Command = new CommunityToolkit.Mvvm.Input.RelayCommand(() => { if (presentationFullscreen) ExitShareFullscreen(); else { observedModel.FocusedVideo = tile; ToggleShareFullscreen(); } }) });
        if (tile.IsScreen && !tile.Identity.StartsWith("You · ",StringComparison.Ordinal))
            menu.Items.Add(new MenuItem { Header = "Request remote control", Command = observedModel.RequestScreenControlCommand, CommandParameter = tile });
        menu.Items.Add(new MenuItem { Header = "Return to gallery", Command = observedModel.ClearVideoFocusCommand });
        MessageMenus.Open(target,menu,atPointer);
    }
    private void ScrollCallChat() => Dispatcher.UIThread.Post(() =>
    {
        if(IsVisible && observedModel?.ShowCallChat==true && CallMessageStream.ItemCount>0)CallMessageStream.ScrollIntoView(CallMessageStream.ItemCount-1);
    },DispatcherPriority.Background);
    private void Tile_Pressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.Source is Control origin && (origin is Button || origin.FindAncestorOfType<Button>() != null)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && DataContext is WorkspaceViewModel vm && sender is Control { DataContext: VideoTileViewModel tile }) vm.FocusVideoCommand.Execute(tile);
    }
    private void FocusedTile_Pressed(object? sender, PointerPressedEventArgs e)
    {
        if (presentationFullscreen || observedModel?.FocusedVideo is not {} tile || tile.CanControl || e.Handled) return;
        if (e.Source is Control origin && (origin is Button || origin.FindAncestorOfType<Button>() != null)) return;
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) observedModel.FocusVideoCommand.Execute(tile);
    }
    private void FullscreenDevices_Click(object? sender, RoutedEventArgs e)
    {
        Devices_Click(sender, e);
        if (sender is Control target) CallDevicesButton.Flyout?.ShowAt(target);
    }
    private void FullscreenChat_Click(object? sender, RoutedEventArgs e)
    {
        if (observedModel is not {} vm) return;
        vm.ShowCallChat = !vm.ShowCallChat;
        if (vm.ShowCallChat && Bounds.Width < 1100) vm.ShowCallMembers = false;
    }
    private void FullscreenMembers_Click(object? sender, RoutedEventArgs e)
    {
        if (observedModel is not {} vm) return;
        vm.ShowCallMembers = !vm.ShowCallMembers;
        if (vm.ShowCallMembers && Bounds.Width < 1100) vm.ShowCallChat = false;
    }
    private void RequestFocusedControl_Click(object? sender, RoutedEventArgs e)
    {
        if (observedModel?.FocusedVideo is not {IsScreen:true} tile) return;
        if (observedModel.CanRequestScreenControl(tile)) observedModel.RequestScreenControlCommand.Execute(tile);
    }
    private void Devices_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm) vm.RefreshDevicesCommand.Execute(null);
    }
    public void CloseAfterCall() { closingAfterCall = true; Dispatcher.UIThread.Post(() => { if (IsVisible) Close(); }); }
}
