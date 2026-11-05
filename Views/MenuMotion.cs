using System;
using Avalonia;
using Avalonia.Controls;

namespace UserIdentityDesktop.Views;

/// <summary>Popup animation is cancelled on dismissal or disable; enabling never stacks event handlers.</summary>
public sealed class MenuMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<MenuMotion,ContextMenu,bool>("Enabled");
    private static readonly AttachedProperty<SurfaceMotion?> MotionProperty=AvaloniaProperty.RegisterAttached<MenuMotion,ContextMenu,SurfaceMotion?>("Motion");
    public static bool GetEnabled(ContextMenu menu)=>menu.GetValue(EnabledProperty);
    public static void SetEnabled(ContextMenu menu,bool value)=>menu.SetValue(EnabledProperty,value);
    static MenuMotion()=>EnabledProperty.Changed.AddClassHandler<ContextMenu>((menu,_)=>
    {
        menu.Opened-=Opened;menu.Closed-=Closed;menu.DetachedFromVisualTree-=Detached;menu.GetValue(MotionProperty)?.Cancel();
        if(!GetEnabled(menu))return;
        menu.SetValue(MotionProperty,new SurfaceMotion());menu.Opened+=Opened;menu.Closed+=Closed;menu.DetachedFromVisualTree+=Detached;
    });
    private static void Opened(object? sender,EventArgs e)
    {
        if(sender is not ContextMenu menu || !GetEnabled(menu))return;
        var reduced=SurfaceMotion.Reduced(menu);menu.Classes.Set("reduced-motion",reduced);
        _=menu.GetValue(MotionProperty)!.EnterAsync(menu,reduced,SurfaceMotionKind.Popup);
    }
    private static void Closed(object? sender,EventArgs e){if(sender is ContextMenu menu)menu.GetValue(MotionProperty)?.Cancel();}
    private static void Detached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is ContextMenu menu)menu.GetValue(MotionProperty)?.Cancel();}
}
