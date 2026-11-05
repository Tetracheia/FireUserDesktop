using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace UserIdentityDesktop.Views;

/// <summary>Animate the selected live page, with cancellable direction and page-local focus memory.</summary>
public sealed class TabMotion : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<TabMotion,TabControl,bool>("Enabled");
    private static readonly AttachedProperty<SurfaceMotion?> MotionProperty = AvaloniaProperty.RegisterAttached<TabMotion,TabControl,SurfaceMotion?>("Motion");
    private static readonly AttachedProperty<int> GenerationProperty = AvaloniaProperty.RegisterAttached<TabMotion,TabControl,int>("Generation");
    private static readonly AttachedProperty<int> IndexProperty = AvaloniaProperty.RegisterAttached<TabMotion,TabControl,int>("Index",-1);
    public static bool GetEnabled(TabControl control)=>control.GetValue(EnabledProperty);
    public static void SetEnabled(TabControl control,bool value)=>control.SetValue(EnabledProperty,value);
    static TabMotion()
    {
        EnabledProperty.Changed.AddClassHandler<TabControl>((control,_)=>
        {
            control.SelectionChanged-=Changed;control.DetachedFromVisualTree-=Detached;control.GotFocus-=Focused;Stop(control);
            if(!GetEnabled(control))return;
            control.SetValue(MotionProperty,new SurfaceMotion());control.SetValue(IndexProperty,control.SelectedIndex);
            control.SelectionChanged+=Changed;control.DetachedFromVisualTree+=Detached;control.GotFocus+=Focused;
        });
        MotionPreferences.ReducedProperty.Changed.AddClassHandler<TabControl>((control,_)=>{if(MotionPreferences.GetReduced(control))Stop(control);});
    }
    private static void Detached(object? sender,VisualTreeAttachmentEventArgs e){if(sender is TabControl tabs)Stop(tabs);}
    private static void Focused(object? sender,FocusChangedEventArgs e)
    {
        if(sender is TabControl tabs && tabs.SelectedContent is Control page && e.Source is Control source)NavigationFocus.Remember(page,source);
    }
    public static void Stop(TabControl control)
    {
        control.SetValue(GenerationProperty,control.GetValue(GenerationProperty)+1);control.GetValue(MotionProperty)?.Cancel();
    }
    private static void Changed(object? sender,SelectionChangedEventArgs e)
    {
        if(sender is not TabControl tabs || e.Source!=tabs)return;
        var previous=(e.RemovedItems.Cast<object>().FirstOrDefault() as TabItem)?.Content as Control;
        var focused=TopLevel.GetTopLevel(tabs)?.FocusManager?.GetFocusedElement() as Control;
        var restoreFocus=focused==null || !focused.IsEffectivelyVisible || previous!=null && NavigationFocus.Contains(previous,focused);
        var forward=tabs.SelectedIndex>=tabs.GetValue(IndexProperty);tabs.SetValue(IndexProperty,tabs.SelectedIndex);
        Stop(tabs);var generation=tabs.GetValue(GenerationProperty);var selected=tabs.SelectedItem;
        Dispatcher.UIThread.Post(()=>
        {
            if(!tabs.IsAttachedToVisualTree() || !GetEnabled(tabs) || tabs.GetValue(GenerationProperty)!=generation || tabs.SelectedItem!=selected)return;
            var content=tabs.SelectedContent as Control ?? (selected as TabItem)?.Content as Control;
            if(content==null)return;
            _=tabs.GetValue(MotionProperty)!.EnterAsync(content,SurfaceMotion.Reduced(tabs),forward:forward);
            if(restoreFocus)
            {
                var current=TopLevel.GetTopLevel(tabs)?.FocusManager?.GetFocusedElement() as Control;
                // A user can focus the rail/search after selection but before layout.
                // Never let the queued page update override that newer intent.
                if(current==null || !current.IsEffectivelyVisible || ReferenceEquals(current,focused) && previous!=null && NavigationFocus.Contains(previous,current))
                    NavigationFocus.Restore(content);
            }
        },DispatcherPriority.Loaded);
    }
}
