using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UserIdentityDesktop.Views;

/// <summary>Shell view swaps share the same live-surface motion and focus rules as workspace tabs.</summary>
public sealed class ContentMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty=AvaloniaProperty.RegisterAttached<ContentMotion,ContentControl,bool>("Enabled");
    private static readonly AttachedProperty<SurfaceMotion?> MotionProperty=AvaloniaProperty.RegisterAttached<ContentMotion,ContentControl,SurfaceMotion?>("Motion");
    private static readonly AttachedProperty<int> GenerationProperty=AvaloniaProperty.RegisterAttached<ContentMotion,ContentControl,int>("Generation");
    public static bool GetEnabled(ContentControl control)=>control.GetValue(EnabledProperty);
    public static void SetEnabled(ContentControl control,bool value)=>control.SetValue(EnabledProperty,value);
    static ContentMotion()=>EnabledProperty.Changed.AddClassHandler<ContentControl>((control,_)=>
    {
        control.AttachedToVisualTree-=Attached;control.DetachedFromVisualTree-=Detached;control.PropertyChanged-=Changed;control.GotFocus-=Focused;Stop(control);
        if(!GetEnabled(control))return;
        control.SetValue(MotionProperty,new SurfaceMotion());control.AttachedToVisualTree+=Attached;control.DetachedFromVisualTree+=Detached;control.PropertyChanged+=Changed;control.GotFocus+=Focused;
    });
    private static void Focused(object? sender,FocusChangedEventArgs e)
    {if(sender is ContentControl control && e.Source is Control source)NavigationFocus.Remember(control,source);}
    private static void Attached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is ContentControl control)Schedule(control);}
    private static void Detached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is ContentControl control)Stop(control);}
    private static void Changed(object? sender,AvaloniaPropertyChangedEventArgs e)
    {
        if(sender is not ContentControl control)return;
        if(e.Property==ContentControl.ContentProperty)Schedule(control);
        else if(e.Property==MotionPreferences.ReducedProperty && MotionPreferences.GetReduced(control))Stop(control);
    }
    private static void Stop(ContentControl control)
    {control.SetValue(GenerationProperty,control.GetValue(GenerationProperty)+1);control.GetValue(MotionProperty)?.Cancel();}
    private static void Schedule(ContentControl control)
    {
        Stop(control);var generation=control.GetValue(GenerationProperty);var content=control.Content;
        Dispatcher.UIThread.Post(()=>
        {
            if(!control.IsAttachedToVisualTree() || !GetEnabled(control) || generation!=control.GetValue(GenerationProperty) || !ReferenceEquals(content,control.Content))return;
            var surface=control.GetVisualChildren().OfType<Control>().FirstOrDefault();if(surface==null)return;
            _=control.GetValue(MotionProperty)!.EnterAsync(surface,SurfaceMotion.Reduced(control));
            var focused=TopLevel.GetTopLevel(control)?.FocusManager?.GetFocusedElement() as Control;
            if(focused==null || !focused.IsEffectivelyVisible)NavigationFocus.Restore(control);
        },DispatcherPriority.Loaded);
    }
}
