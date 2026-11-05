using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UserIdentityDesktop.Views;

/// <summary>Native popup lifetimes stay authoritative; only their presented surface animates.</summary>
public sealed class PopupMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<PopupMotion,Control,bool>("Enabled");
    private static readonly AttachedProperty<SurfaceMotion?> MotionProperty=AvaloniaProperty.RegisterAttached<PopupMotion,Control,SurfaceMotion?>("Motion");
    private static readonly AttachedProperty<int> GenerationProperty=AvaloniaProperty.RegisterAttached<PopupMotion,Control,int>("Generation");
    public static bool GetEnabled(Control control)=>control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control,bool value)=>control.SetValue(EnabledProperty,value);
    static PopupMotion()=>EnabledProperty.Changed.AddClassHandler<Control>((control,_)=>
    {
        control.AttachedToVisualTree-=Attached;control.DetachedFromVisualTree-=Detached;control.PropertyChanged-=Changed;Stop(control);
        if(!GetEnabled(control))return;
        control.SetValue(MotionProperty,new SurfaceMotion());control.AttachedToVisualTree+=Attached;control.DetachedFromVisualTree+=Detached;control.PropertyChanged+=Changed;
    });
    private static void Changed(object? sender,AvaloniaPropertyChangedEventArgs e)
    {if(sender is Control control && e.Property==MotionPreferences.ReducedProperty){control.Classes.Set("reduced-motion",SurfaceMotion.Reduced(control));if(SurfaceMotion.Reduced(control))Stop(control);}}
    private static void Detached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is Control control)Stop(control);}
    private static void Attached(object? sender,VisualTreeAttachmentEventArgs e)
    {
        if(sender is not Control control)return;
        Stop(control);var generation=control.GetValue(GenerationProperty);
        Dispatcher.UIThread.Post(()=>
        {
            if(!GetEnabled(control) || !control.IsAttachedToVisualTree() || generation!=control.GetValue(GenerationProperty))return;
            var reduced=SurfaceMotion.Reduced(control);control.Classes.Set("reduced-motion",reduced);
            _=control.GetValue(MotionProperty)!.EnterAsync(control,reduced,SurfaceMotionKind.Popup);
        },DispatcherPriority.Loaded);
    }
    private static void Stop(Control control)
    {control.SetValue(GenerationProperty,control.GetValue(GenerationProperty)+1);control.GetValue(MotionProperty)?.Cancel();}
}
