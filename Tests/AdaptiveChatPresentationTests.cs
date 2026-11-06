using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using UserIdentityDesktop.Views;
using Xunit;
namespace UserIdentityDesktop.Tests;
public sealed partial class ChatPresentationTests
{
    [AvaloniaTheory]
    [InlineData(800,248,false)] [InlineData(1024,300,true)] [InlineData(1600,336,false)]
    public async Task ConversationResizeKeepsTheComposerAndBothPanesUsable(int width,int listWidth,bool dark)
    {
        var (workspace,_)=Model();var view=new ChatView { DataContext=workspace.Chat };
        var window=new Window { Width=width,Height=820,Content=view,RequestedThemeVariant=dark?ThemeVariant.Dark:ThemeVariant.Light };
        try
        {
            window.Show();Dispatcher.UIThread.RunJobs();
            var grid=view.FindControl<Grid>("ChatLayout")!;
            Assert.Equal(listWidth,grid.ColumnDefinitions[0].ActualWidth);
            var surface=view.FindControl<Grid>("ConversationSurface")!;Assert.True(surface.Bounds.Width>=500);
            var input=view.FindControl<TextBox>("MessageInputBox")!;Assert.True(input.Bounds.Width>=200);
            Assert.True(input.Focus());Assert.True(input.IsKeyboardFocusWithin);
            Save(window,$"adaptive-chat-{width}.png");
        }
        finally {window.Close();await workspace.DisposeAsync();}
    }
}
