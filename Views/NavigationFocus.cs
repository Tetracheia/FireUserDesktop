using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace UserIdentityDesktop.Views;

/// <summary>Remember focus per live page; never retain an obsolete view or focus a hidden control.</summary>
internal static class NavigationFocus
{
    private static readonly AttachedProperty<WeakReference<Control>?> LastProperty = AvaloniaProperty.RegisterAttached<Control,WeakReference<Control>?>("LastNavigationFocus",typeof(NavigationFocus));
    internal static bool Contains(Control root,Control control) => ReferenceEquals(root,control) || control.GetVisualAncestors().Contains(root);
    internal static void Remember(Control root,Control control)
    { if(Contains(root,control))root.SetValue(LastProperty,new WeakReference<Control>(control)); }
    internal static bool Restore(Control root)
    {
        if(root.GetValue(LastProperty)?.TryGetTarget(out var remembered)==true && Contains(root,remembered) && Valid(remembered))
        { remembered.BringIntoView();return remembered.Focus(NavigationMethod.Tab); }
        var first=root.GetVisualDescendants().OfType<Control>().FirstOrDefault(Valid);
        if(first==null)return false;
        first.BringIntoView();return first.Focus(NavigationMethod.Tab);
    }
    private static bool Valid(Control control) => control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled && control.IsAttachedToVisualTree();
}
