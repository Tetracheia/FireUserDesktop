using Avalonia;
using Avalonia.Controls;
namespace UserIdentityDesktop.Views;

/// <summary>Presentation-only window size classes; commands and view models remain unchanged.</summary>
public sealed class AdaptivePresentation : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty = AvaloniaProperty.RegisterAttached<AdaptivePresentation,Window,bool>("Enabled");
    public static bool GetEnabled(Window window) => window.GetValue(EnabledProperty);
    public static void SetEnabled(Window window,bool value) => window.SetValue(EnabledProperty,value);
    static AdaptivePresentation() => EnabledProperty.Changed.AddClassHandler<Window>((window,args)=>
    {
        window.SizeChanged -= SizeChanged;
        if(args.GetNewValue<bool>()) { window.SizeChanged += SizeChanged; Update(window); }
        else window.Classes.Remove("compact-window");
    });
    private static void SizeChanged(object? sender,SizeChangedEventArgs args) { if(sender is Window window)Update(window); }
    private static void Update(Window window) => window.Classes.Set("compact-window",window.Bounds.Width<1040);
}
