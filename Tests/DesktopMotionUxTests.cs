using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UserIdentityDesktop.Views;
using Xunit;

namespace UserIdentityDesktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaFact]
    public async Task ReducedMotionChangeImmediatelyCancelsPagesAndOwnedWindows()
    {
        var (vm,_,_)=Create();vm.ReduceMotion=false;
        var first=new Border{Child=new Button{Content="First action"}};var second=new Border{Child=new Button{Content="Second action"}};
        var tabs=new TabControl{Items={new TabItem{Header="One",Content=first},new TabItem{Header="Two",Content=second}}};
        var owner=new Window{DataContext=vm,Content=tabs};owner.Show();Dispatcher.UIThread.RunJobs();
        var dialogSurface=new Border{Child=new Button{Content="Close"}};var dialog=new Window{DataContext=vm,Content=dialogSurface};var motion=new WindowMotion(dialog);
        try
        {
            tabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();Assert.NotNull(second.RenderTransform);
            dialog.Show(owner);Dispatcher.UIThread.RunJobs();Assert.NotNull(dialogSurface.RenderTransform);
            vm.ReduceMotion=true;Dispatcher.UIThread.RunJobs();
            Assert.True(MotionPreferences.GetReduced(owner));Assert.True(MotionPreferences.GetReduced(dialog));
            Assert.Null(second.RenderTransform);Assert.Null(dialogSurface.RenderTransform);Assert.Equal(1,second.Opacity);Assert.Equal(1,dialogSurface.Opacity);
            Assert.Contains("reduced-motion",dialog.Classes);var action=(Button)dialogSurface.Child!;Assert.True(action.Transitions==null||action.Transitions.Count==0);
            await motion.CloseAsync<object?>(null);Assert.False(dialog.IsVisible);
        }
        finally{dialog.Close();owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task RapidNavigationAndDisablingMotionRestoreExistingVisualState()
    {
        var (vm,_,_)=Create();vm.ReduceMotion=false;var transform=new TranslateTransform(2,3);
        var first=new Border{RenderTransform=transform,Opacity=.8,Child=new TextBox{Text="Keep my draft"}};var second=new Border{Child=new Button{Content="Second"}};
        var tabs=new TabControl{Items={new TabItem{Header="One",Content=first},new TabItem{Header="Two",Content=second}}};var owner=new Window{DataContext=vm,Content=tabs};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            tabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();tabs.SelectedIndex=0;tabs.SelectedIndex=1;tabs.SelectedIndex=0;Dispatcher.UIThread.RunJobs();
            Assert.Null(second.RenderTransform);TabMotion.SetEnabled(tabs,false);Dispatcher.UIThread.RunJobs();Assert.Same(transform,first.RenderTransform);Assert.Equal(.8,first.Opacity);
            TabMotion.SetEnabled(tabs,true);tabs.SelectedIndex=1;Dispatcher.UIThread.RunJobs();Assert.NotNull(second.RenderTransform);
            vm.ReduceMotion=true;Dispatcher.UIThread.RunJobs();Assert.Null(second.RenderTransform);Assert.Same(transform,first.RenderTransform);Assert.Equal("Keep my draft",((TextBox)first.Child!).Text);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task F6RestoresPageFocusAndNavigationDoesNotStealExternalSearchFocus()
    {
        var (vm,_,_)=Create();vm.SelectedWorkspaceTab=4;var view=new WorkspaceView{DataContext=vm};var search=new TextBox{Text="Keep search focus"};
        var layout=new Grid{RowDefinitions=new RowDefinitions("Auto,*")};layout.Children.Add(search);layout.Children.Add(view);Grid.SetRow(view,1);
        var owner=new Window{DataContext=vm,Content=layout,Width=1200,Height=800};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var tabs=view.FindControl<TabControl>("WorkspaceTabs")!;var page=Assert.IsAssignableFrom<Control>(tabs.SelectedContent);
            var last=page.GetVisualDescendants().OfType<Button>().First(x=>x.IsEffectivelyVisible&&x.IsEffectivelyEnabled);last.Focus(NavigationMethod.Tab);
            view.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.F6});Dispatcher.UIThread.RunJobs();var rail=view.FindControl<Border>("NavigationRail")!;
            Assert.True(rail.IsKeyboardFocusWithin);Assert.DoesNotContain("expanded",rail.Classes);
            owner.MouseMove(rail.TranslatePoint(new Point(20,100),owner)!.Value);owner.MouseMove(new Point(600,100));Dispatcher.UIThread.RunJobs();Assert.DoesNotContain("expanded",rail.Classes);
            view.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.F6});Dispatcher.UIThread.RunJobs();Assert.True(last.IsKeyboardFocusWithin);
            vm.SelectedWorkspaceTab=2;search.Focus();Dispatcher.UIThread.RunJobs();Assert.True(search.IsFocused);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task ShellSwapsCancelOldSurfaceAndKeepTheNewPageInteractive()
    {
        var (vm,_,_)=Create();vm.ReduceMotion=false;
        var first=new StackPanel{Children={new TextBox{Text="Old page"}}};var second=new StackPanel{Children={new TextBox{Text="New page"}}};
        var shell=new ContentControl{Content=first};ContentMotion.SetEnabled(shell,true);var owner=new Window{DataContext=vm,Content=shell};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            shell.Content=second;shell.Content=first;shell.Content=second;Dispatcher.UIThread.RunJobs();
            Assert.Same(second,shell.Content);Assert.True(((TextBox)second.Children[0]).Focus());
            vm.ReduceMotion=true;Dispatcher.UIThread.RunJobs();var presenter=Assert.Single(shell.GetVisualChildren().OfType<Control>());Assert.Null(presenter.RenderTransform);Assert.Equal(1,presenter.Opacity);
            Assert.True(((TextBox)second.Children[0]).IsKeyboardFocusWithin);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task PageTransitionRendersAtRecordedTimesWithoutChangingLayoutBounds()
    {
        var (vm,_,_)=Create();vm.ReduceMotion=false;var view=new WorkspaceView{DataContext=vm};var owner=new Window{DataContext=vm,Content=view,Width=1200,Height=800};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            vm.SelectedWorkspaceTab=2;Dispatcher.UIThread.RunJobs();var page=(Control)view.FindControl<TabControl>("WorkspaceTabs")!.SelectedContent!;var bounds=page.Bounds;var watch=System.Diagnostics.Stopwatch.StartNew();var captures=new System.Collections.Generic.List<object>();
            foreach(var instant in new[]{0,60,120,240,320})
            {
                var remaining=instant-watch.Elapsed.TotalMilliseconds;if(remaining>0)await Task.Delay(TimeSpan.FromMilliseconds(remaining),TestContext.Current.CancellationToken);Dispatcher.UIThread.RunJobs();Assert.Equal(bounds,page.Bounds);
                if(Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is {} folder)
                {Directory.CreateDirectory(folder);var before=watch.Elapsed.TotalMilliseconds;using var frame=owner.CaptureRenderedFrame();Assert.NotNull(frame);using var file=File.Create(Path.Combine(folder,$"page-transition-requested-{instant:000}ms.png"));frame.Save(file,new PngBitmapEncoderOptions());captures.Add(new{requested_ms=instant,capture_started_ms=before,capture_finished_ms=watch.Elapsed.TotalMilliseconds});}
            }
            if(Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is {} artifactFolder)File.WriteAllText(Path.Combine(artifactFolder,"page-transition-timings.json"),System.Text.Json.JsonSerializer.Serialize(captures));
            Assert.Null(page.RenderTransform);Assert.Equal(1,page.Opacity);Assert.True(page.IsEffectivelyVisible);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task RepeatedWindowCloseRequestsShareCompletionAndReducedMotionCanFinishTheExit()
    {
        var (vm,_,_)=Create();vm.ReduceMotion=false;var surface=new Border{Child=new Button{Content="Done"}};
        var window=new Window{DataContext=vm,Content=surface};var motion=new WindowMotion(window);window.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var first=motion.CloseAsync(1);var second=motion.CloseAsync(2);Assert.Same(first,second);
            vm.ReduceMotion=true;await first;Assert.False(window.IsVisible);Assert.Null(surface.RenderTransform);Assert.Equal(1,surface.Opacity);
        }
        finally{window.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
}
