using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace UserIdentityDesktop.Views;

/// <summary>Work-area-sized normal window presentation, without native fullscreen or input grabs.</summary>
internal sealed class BorderlessPresentation
{
    private readonly Window window;
    private PixelPoint normalPosition,previousPosition;
    private Size normalSize,previousSize;
    private WindowState previousState;
    private WindowDecorations previousDecorations;
    private SizeToContent previousSizeToContent;
    private bool previousCanResize, restoringPosition;
    private double previousMinWidth,previousMinHeight,previousMaxWidth,previousMaxHeight;
    internal bool IsActive {get;private set;}
    internal BorderlessPresentation(Window window)
    {
        this.window=window;RememberNormalBounds();
        window.SizeChanged+=(_,_)=>RememberNormalBounds();
        window.PositionChanged+=(_,_)=>RememberNormalBounds();
        window.Resized += (_, e) =>
        {
            if (e.Reason == WindowResizeReason.User) { if (restoringPosition) window.CanResize = previousCanResize; restoringPosition = false; RememberNormalBounds(); }
            else RestorePositionAfterResize();
        };
    }
    private void RememberNormalBounds()
    {
        if(IsActive||restoringPosition||window.WindowState!=WindowState.Normal)return;
        normalPosition=window.Position;normalSize=new(window.ClientSize.Width > 0 ? window.ClientSize.Width : window.Width,window.ClientSize.Height > 0 ? window.ClientSize.Height : window.Height);
    }
    private void RestorePositionAfterResize()
    {
        if (!restoringPosition || IsActive || window.WindowState != WindowState.Normal ||
            Math.Abs(window.ClientSize.Width - previousSize.Width) > 1 || Math.Abs(window.ClientSize.Height - previousSize.Height) > 1) return;
        // The WM can clamp an early move while the old, screen-sized frame still
        // exists. Restore location only after the smaller client resize is applied.
        restoringPosition = false;
        window.CanResize = previousCanResize;
        window.Position = previousPosition;
        RememberNormalBounds();
    }
    private void ScreenGeometryChanged(object? sender, EventArgs e) => QueueFit();
    private void QueueFit() => Dispatcher.UIThread.Post(() => { if (IsActive) FitToScreen(); }, DispatcherPriority.Loaded);
    internal static PixelRect PresentationArea(PixelRect bounds, PixelRect workingArea) =>
        workingArea.Width > 0 && workingArea.Height > 0 ? workingArea : bounds;
    private void FitToScreen()
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        if (screen == null) return;
        // A normal window cannot claim compositor fullscreen privileges. Respect
        // dock/panel reservations instead of asking the WM to clamp monitor bounds.
        var area = PresentationArea(screen.Bounds, screen.WorkingArea);
        var size = ClientSize(area, screen.Scaling);
        window.CanResize = true;
        window.Width = size.Width; window.Height = size.Height;
        window.Position = area.Position;
        // Keep resize capability while the WM asynchronously applies the request.
        // Fixed-size hints here use the *old ClientSize* and can prevent the resize.
        // Borderless presentation has no resize decorations; normal policy is restored on exit.
    }
    internal static Size ClientSize(PixelRect monitor,double scaling)=>new(monitor.Width/scaling,monitor.Height/scaling);
    internal void Enter()
    {
        if(IsActive)return;
        restoringPosition = false;
        previousState=window.WindowState;previousPosition=normalPosition;previousSize=normalSize;
        previousDecorations=window.WindowDecorations;previousCanResize=window.CanResize;previousSizeToContent=window.SizeToContent;
        previousMinWidth=window.MinWidth;previousMinHeight=window.MinHeight;previousMaxWidth=window.MaxWidth;previousMaxHeight=window.MaxHeight;
        IsActive=true;
        window.WindowState=WindowState.Normal;window.WindowDecorations=WindowDecorations.None;
        window.SizeToContent=SizeToContent.Manual;
        window.MinWidth=window.MinHeight=0;window.MaxWidth=window.MaxHeight=double.PositiveInfinity;
        window.Screens.Changed += ScreenGeometryChanged;
        window.Closed += OnClosed;
        FitToScreen();
        // Decoration changes may be applied asynchronously by the window manager.
        QueueFit();
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        window.Screens.Changed -= ScreenGeometryChanged;
        window.Closed -= OnClosed;
        IsActive = false;
    }
    internal void Exit(bool restoreWindowState=true)
    {
        if(!IsActive)return;
        window.Screens.Changed -= ScreenGeometryChanged;
        window.Closed -= OnClosed;
        restoringPosition = restoreWindowState && previousState == WindowState.Normal;
        // Keep geometry tracking paused through restoration, including maximization.
        window.WindowDecorations=previousDecorations;window.CanResize=restoringPosition || previousCanResize;
        window.MinWidth=previousMinWidth;window.MinHeight=previousMinHeight;window.MaxWidth=previousMaxWidth;window.MaxHeight=previousMaxHeight;
        window.Width=previousSize.Width;window.Height=previousSize.Height;window.Position=previousPosition;
        window.SizeToContent=previousSizeToContent;
        if(restoreWindowState)window.WindowState=previousState;
        IsActive=false;
        RestorePositionAfterResize();
    }
}
