using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using UserIdentityDesktop.Core;
using UserIdentityDesktop.Core.Locale;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.ViewModels;
using UserIdentityDesktop.Views;
using Xunit;
namespace UserIdentityDesktop.Tests;
public sealed class LocalePreferenceTests
{
    [AvaloniaFact]
    public void OpenNativeViewChangesLanguageAndDirectionImmediately()
    {
        LocaleCatalog.Set("en");var view=new LoginView();var window=new Window {Content=view};window.Show();Dispatcher.UIThread.RunJobs();
        var english=view.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
        try {
            LocaleCatalog.Set("ar");Dispatcher.UIThread.RunJobs();Assert.Equal(FlowDirection.RightToLeft,view.FlowDirection);
            Assert.NotEqual(english,view.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray());
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),t=>t.Text==LocaleCatalog.Text("Email address"));
            LocaleCatalog.Set("el");Dispatcher.UIThread.RunJobs();Assert.Equal(FlowDirection.LeftToRight,view.FlowDirection);
            Assert.NotEqual("Language",LocaleCatalog.Text("Language"));Assert.Equal("Alice's own words",LocaleCatalog.Text("Alice's own words"));
        }finally{window.Close();LocaleCatalog.Set("en");Dispatcher.UIThread.RunJobs();}
    }
    [AvaloniaFact]
    public async Task LocalePersistsAsPartialPatchAndStaleReadCannotUndoSelection()
    {
        var api=Substitute.For<ICollaborationApi>();var read=new TaskCompletionSource<AppearancePreferences>();
        api.AppearanceAsync(Arg.Any<CancellationToken>()).Returns(read.Task);
        api.SaveAppearanceAsync(Arg.Any<AppearancePreferences>(),Arg.Any<CancellationToken>()).Returns(new AppearancePreferences(null,null,"el"));
        using var vm=new AppearanceSettingsViewModel(api);var loading=vm.RefreshAsync();vm.Locale="el";read.SetResult(new(null,null,"ar"));await loading;Assert.Equal("el",vm.Locale);
        api.AppearanceAsync(Arg.Any<CancellationToken>()).Returns(new AppearancePreferences(null,null,"el"));
        await vm.FlushAsync();await api.Received(1).SaveAppearanceAsync(new AppearancePreferences(null,null,"el"),Arg.Any<CancellationToken>());
        Assert.Equal("el",LocaleCatalog.Current);LocaleCatalog.Set("en");
    }
}
