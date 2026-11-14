using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace UserIdentityDesktop.Views;
internal static class ReviewWindow
{
    public static Task<bool> ShowAsync(Window owner, string title, string details)
    {
        var window = new Window { Title = title, Width = 640, Height = 560, MinWidth = 400, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        ComposeAppearance.Apply(window);window.RequestedThemeVariant=owner.ActualThemeVariant;window.DataContext=owner.DataContext;
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("ForgeBg")); window.Bind(Window.ForegroundProperty, window.GetResourceObservable("ForgeTextPrimary"));
        var panel = new DockPanel { Margin = new Thickness(24) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { [!Button.ContentProperty] = UserIdentityDesktop.Localization.LocalizedStrings.Bind("Back") }; var confirm = new Button { [!Button.ContentProperty] = UserIdentityDesktop.Localization.LocalizedStrings.Bind("Confirm") }; confirm.Classes.Add("fire-btn"); buttons.Children.Add(cancel); buttons.Children.Add(confirm); DockPanel.SetDock(buttons, Dock.Bottom); panel.Children.Add(buttons);
        panel.Children.Add(new ScrollViewer { Content = new Border { Classes={"compose-card"},Child=new StackPanel { Spacing=16, Children={new TextBlock {Text=title,Classes={"compose-title"}},new TextBlock {Text=details,TextWrapping=Avalonia.Media.TextWrapping.Wrap,FontSize=14,LineHeight=22}}}} }); window.Content = panel;
        var motion = new WindowMotion(window); cancel.Click += async (_, _) => await motion.CloseAsync(false); confirm.Click += async (_, _) => await motion.CloseAsync(true);
        return window.ShowDialog<bool>(owner);
    }
}
