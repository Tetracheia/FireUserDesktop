using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using UserIdentityDesktop.Views;
using Xunit;

namespace UserIdentityDesktop.Tests;
public sealed class AdaptivePresentationTests
{
    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public void SharedControlsRemainReadableAndKeyboardAccessibleAsWindowResizes(bool dark)
    {
        var input=new TextBox { PlaceholderText="Search colleagues" };
        var button=new Button { Content="Continue" };
        var panel=new StackPanel { Spacing=16, Margin=new Thickness(24), Children={new TextBlock { Text="Workspace", Classes={"forge-heading"}},input,button} };
        var window=new Window { Width=800,Height=600,Content=panel,RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light };
        try
        {
            window.Show();Dispatcher.UIThread.RunJobs();
            Assert.True(AdaptivePresentation.GetEnabled(window));Assert.Contains("compact-window",window.Classes);
            Assert.Equal(14,window.FontSize);Assert.True(input.Bounds.Height>=40);Assert.True(button.Bounds.Height>=38);
            Assert.NotNull(button.FocusAdorner);Assert.True(button.Focus(NavigationMethod.Tab));Assert.True(button.IsKeyboardFocusWithin);
            Assert.True(input.Focus(NavigationMethod.Tab));Assert.True(input.IsKeyboardFocusWithin);
            if(Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is {} dir)
            {
                Directory.CreateDirectory(dir);using var bitmap=window.CaptureRenderedFrame();Assert.NotNull(bitmap);
                using var file=File.Create(Path.Combine(dir,$"adaptive-controls-{(dark?"dark":"light")}.png"));bitmap.Save(file,new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            window.Width=1280;Dispatcher.UIThread.RunJobs();Assert.DoesNotContain("compact-window",window.Classes);
            window.Width=800;Dispatcher.UIThread.RunJobs();Assert.Contains("compact-window",window.Classes);
            AdaptivePresentation.SetEnabled(window,false);Assert.DoesNotContain("compact-window",window.Classes);
        }
        finally {window.Close();}
    }
}
