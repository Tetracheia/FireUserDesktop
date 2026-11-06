using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using UserIdentityDesktop.Core;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.ViewModels;
using UserIdentityDesktop.Views;
using Xunit;
namespace UserIdentityDesktop.Tests;

public sealed partial class ChatPresentationTests
{
    private static (WorkspaceViewModel Vm, IChatService Service) Model()
    {
        var auth = Substitute.For<IAuthService>(); auth.CurrentUser.Returns(new UserData { Id = Guid.NewGuid(), FirstName = "Kirollos", LastName = "Adel" });
        var service = Substitute.For<IChatService>();
        var vm = new ChatViewModel(auth,service,Substitute.For<IUserService>(),Substitute.For<IImageService>(),Substitute.For<IFileService>(),Substitute.For<INotificationService>());
        var workspace = new WorkspaceViewModel(vm,Substitute.For<ICollaborationApi>(),Substitute.For<INativeMediaSession>(),auth,service) { ReduceMotion = true, CallingEnabled = true };
        var names = new[] { "Dina Nabil", "Product design", "Omar Hassan", "Mariam Youssef", "Engineering", "Ahmed Samir" };
        foreach (var name in names) { var person = new RecipientItemViewModel { ReceiverId = Guid.NewGuid(), ReceiverName = name, LastMessage = name == "Dina Nabil" ? "The updated screens look great." : "Thanks, see you tomorrow", AttachmentsCount = name == "Dina Nabil" ? 2 : 0, LastSent = DateTime.UtcNow.AddMinutes(-12), IsSeen = true, Status = UserStatus.Online }; vm.Recipients.Add(person); vm.FilteredRecipients.Add(person); }
        vm.IsConnected = true; vm.SelectedRecipient = vm.Recipients[0]; Dispatcher.UIThread.RunJobs();
        var content = new[] { "Morning! I’ve shared the updated workspace designs.", "Just opened them. The new navigation feels much clearer.", "I simplified the call controls too — what do you think?", "Much better. Let’s use the same spacing across the calendar.", "Here are the latest files for the review.", "Perfect. I’ll take a look before our meeting.", "Sounds good. See you at 3!" };
        for (var i = 0; i < content.Length; i++)
        {
            var mine = i % 2 == 1; var message = new ChatMessage { MessageId = "message-" + i, Sender = mine ? vm.CurrentUser.Id : vm.SelectedRecipient.ReceiverId, SenderName = mine ? "Kirollos Adel" : "Dina Nabil", Receiver = mine ? vm.SelectedRecipient.ReceiverId : vm.CurrentUser.Id, Text = content[i], TimeSent = DateTime.UtcNow.AddMinutes(-20+i*2), Seen = mine };
            if (i == 2) message.Reactions = new() { new ChatReaction(vm.CurrentUser.Id,"👍") };
            if (i == 3) { message.ReplyToMessageId = "message-2"; message.ReplyToSenderName = "Dina Nabil"; }
            if (i == 4) message.Attachments = new() { new ChatAttachment { Id = Guid.NewGuid(), FileName = "Workspace review.pdf", FileSize = 241200, MimeType = "application/pdf" }, new ChatAttachment { Id = Guid.NewGuid(), FileName = "Meeting notes.pdf", MimeType = "application/pdf", FileSize = 16000 } };
            vm.Messages.Add(new ChatMessageItemViewModel(message,mine,service,Substitute.For<IFileService>(),Substitute.For<IImageService>()));
        }
        return (workspace,service);
    }
    [AvaloniaFact]
    public async Task ConversationSnapshotsSortNewestFirstAndKeepSelectionDuringReordering()
    {
        var (workspace,service)=Model(); var chat=workspace.Chat;
        foreach(var item in chat.Recipients)item.LastSent=new DateTime(2025,1,1);
        var selected=chat.SelectedRecipient!;
        var oldest=chat.Recipients[1];var newest=chat.Recipients[2];
        oldest.LastSent=new DateTime(2026,1,1); selected.LastSent=new DateTime(2026,2,1); newest.LastSent=new DateTime(2026,3,1);
        chat.SearchText=" "; Dispatcher.UIThread.RunJobs();
        Assert.Same(newest,chat.FilteredRecipients[0]);Assert.Same(oldest,chat.FilteredRecipients[2]);Assert.Same(selected,chat.SelectedRecipient);
        service.GetRecipientsAsync().Returns(new List<RecipientDto> {new(){ReceiverId=oldest.ReceiverId,ReceiverName=oldest.ReceiverName,LastSent=new DateTime(2026,4,1),LastMessage="Latest"}});
        await chat.LoadRecipientsAsync();Dispatcher.UIThread.RunJobs();
        Assert.Same(oldest,chat.FilteredRecipients[0]);Assert.Same(selected,chat.SelectedRecipient);
        await workspace.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task ComposerPasteStagesWholeFilesAndTextRemainsEditable()
    {
        var (workspace,_)=Model();var view=new ChatView{DataContext=workspace.Chat};var owner=new Window{Content=view};owner.Show();Dispatcher.UIThread.RunJobs();
        var path=System.IO.Path.GetTempFileName();await System.IO.File.WriteAllTextAsync(path,"Complete attachment contents");
        try
        {
            var file=await owner.StorageProvider.TryGetFileFromPathAsync(new Uri(path));Assert.NotNull(file);
            await owner.Clipboard!.SetFilesAsync(new[]{file});
            var input=view.FindControl<TextBox>("MessageInputBox")!;input.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));
            await Task.Yield();Dispatcher.UIThread.RunJobs();
            var pending=Assert.Single(workspace.Chat.PendingAttachments);Assert.Equal(path,pending.FilePath);Assert.Equal("Complete attachment contents",await System.IO.File.ReadAllTextAsync(pending.FilePath));
            workspace.Chat.RemovePendingAttachment(pending);Assert.True(System.IO.File.Exists(path));
            input.Text="Hello world";input.SelectionStart=6;input.SelectionEnd=11;
            await owner.Clipboard.SetTextAsync("team");input.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));await Task.Yield();Dispatcher.UIThread.RunJobs();
            Assert.Equal("Hello team",input.Text);Assert.Empty(workspace.Chat.PendingAttachments);
        }
        finally {System.IO.File.Delete(path);owner.Close();await workspace.DisposeAsync();}
    }

    [AvaloniaFact]
    public async Task PastedImageIsStagedAsACompletePngAndRemovedWithThePreview()
    {
        var (workspace,_)=Model();var view=new ChatView{DataContext=workspace.Chat};var owner=new Window{Content=view};owner.Show();Dispatcher.UIThread.RunJobs();
        using var image=new WriteableBitmap(new PixelSize(4,3),new Vector(96,96));
        try
        {
            await owner.Clipboard!.SetBitmapAsync(image);
            view.FindControl<TextBox>("MessageInputBox")!.RaiseEvent(new RoutedEventArgs(TextBox.PastingFromClipboardEvent));await Task.Yield();Dispatcher.UIThread.RunJobs();
            var attachment=Assert.Single(workspace.Chat.PendingAttachments);Assert.True(workspace.Chat.ShowSendAction);
            using(var saved=new Bitmap(attachment.FilePath))Assert.Equal(new PixelSize(4,3),saved.PixelSize);
            workspace.Chat.RemovePendingAttachment(attachment);Assert.False(System.IO.File.Exists(attachment.FilePath));Assert.Empty(workspace.Chat.PendingAttachments);
        }
        finally {owner.Close();await workspace.DisposeAsync();}
    }

    [AvaloniaTheory]
    [InlineData(false)] [InlineData(true)]
    public async Task ChatPreview_UsesQuietMessagesAndNativeActionAccess(bool light)
    {
        var (workspace,_) = Model(); var view = new ChatView { DataContext = workspace.Chat };
        var window = new Window { Width = 1260, Height = 850, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark, Content = view };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var rows = view.GetVisualDescendants().OfType<Grid>().Where(x => x.Classes.Contains("msg-row")).ToArray(); Assert.NotEmpty(rows);
        var bubbles = rows.SelectMany(x=>x.GetVisualDescendants().OfType<Border>()).Where(x=>x.Classes.Contains("msg-bubble")).ToArray();
        Assert.Contains(bubbles,x=>x.Classes.Contains("mine") && x.Background is Avalonia.Media.LinearGradientBrush && x.CornerRadius == new CornerRadius(16,16,4,16));
        Assert.Contains(bubbles,x=>!x.Classes.Contains("mine") && x.BorderThickness == new Thickness(1) && x.CornerRadius == new CornerRadius(16,16,16,4));
        Assert.DoesNotContain(rows.SelectMany(x => x.GetVisualDescendants().OfType<Button>()), x => x.Content?.ToString() is "Edit" or "Delete" or "React" or "Details");
        Save(window,light ? "chat-light.png" : "chat-dark.png");
        window.Close(); await workspace.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task RightClickMessage_OpensNativeMenu_AndReplyTargetsClickedMessage()
    {
        var (workspace,_) = Model(); var view = new ChatView { DataContext = workspace.Chat }; var window = new Window { Width=1260, Height=850, Content=view, RequestedThemeVariant=ThemeVariant.Dark };
        window.Show(); Dispatcher.UIThread.RunJobs();
        var row = view.GetVisualDescendants().OfType<Grid>().First(x => x.Classes.Contains("msg-row") && x.DataContext is ChatMessageItemViewModel { IsMine: true });
        var point = row.TranslatePoint(new Point(20,20),window)!.Value;
        window.MouseMove(point); window.MouseDown(point,MouseButton.Right); window.MouseUp(point,MouseButton.Right); Dispatcher.UIThread.RunJobs();
        var menu = Assert.IsType<ContextMenu>(row.ContextMenu); Assert.True(menu.IsOpen);
        var headers = menu.Items.OfType<MenuItem>().Select(x=>x.Header?.ToString()).ToArray(); Assert.Contains("Reply",headers); Assert.Contains("Edit message",headers); Assert.Contains("Message details",headers); Assert.DoesNotContain("React",headers);
        var reactions = Assert.IsType<StackPanel>(menu.Items.OfType<MenuItem>().First().Header);
        Assert.Equal(new[]{"👍","❤️","😂","😢","😡"}, reactions.Children.OfType<Button>().Select(x=>x.Content));
        Assert.Equal(Avalonia.Layout.Orientation.Horizontal, reactions.Orientation);
        Save(window,"chat-message-context.png");
        var reply = menu.Items.OfType<MenuItem>().Single(x=>x.Header?.ToString()=="Reply"); reply.Command!.Execute(null);
        Assert.Same(row.DataContext,workspace.Chat.ReplyingMessage); menu.Close();
        window.Close(); await workspace.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task IncomingMessageMenu_RestrictsEditDelete_AndOffersAttachmentDownload()
    {
        var (workspace,_) = Model(); var view = new ChatView { DataContext=workspace.Chat }; var window = new Window { Width=1260,Height=850,Content=view }; window.Show(); Dispatcher.UIThread.RunJobs();
        var row=view.GetVisualDescendants().OfType<Grid>().Single(x=>x.Classes.Contains("msg-row") && x.DataContext is ChatMessageItemViewModel { HasAttachments:true });
        var button=row.GetVisualDescendants().OfType<Button>().Single(x=>x.Classes.Contains("message-more")); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu=Assert.IsType<ContextMenu>(button.ContextMenu); Assert.True(menu.IsOpen);
        var headers=menu.Items.OfType<MenuItem>().Select(x=>x.Header?.ToString()).ToArray(); Assert.DoesNotContain("Edit message",headers); Assert.DoesNotContain("Delete message…",headers);
        var download=menu.Items.OfType<MenuItem>().Single(x=>x.Header?.ToString()=="Download attachment"); Assert.Equal(2, download.Items.Count); menu.Close();
        workspace.Chat.OpenConversationDetails(1); Dispatcher.UIThread.RunJobs(); Assert.True(view.FindControl<Border>("ConversationDrawer")!.IsVisible); Assert.Equal(1,workspace.Chat.ConversationDetailsTab);
        Save(window,"chat-members-drawer.png"); workspace.Chat.CloseConversationDetailsCommand.Execute(null); Assert.False(workspace.Chat.ShowConversationDetails);
        window.Close(); await workspace.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task DeleteFromContextMenu_RequiresConfirmation_AndCancelDoesNotDelete()
    {
        var (workspace,service)=Model(); var view=new ChatView { DataContext=workspace.Chat }; var owner=new Window { Width=1260,Height=850,Content=view }; owner.Show(); Dispatcher.UIThread.RunJobs();
        var row=view.GetVisualDescendants().OfType<Grid>().First(x=>x.Classes.Contains("msg-row") && x.DataContext is ChatMessageItemViewModel { IsMine:true });
        var item=(ChatMessageItemViewModel)row.DataContext!; var menu=MessageMenus.Create(workspace.Chat,item,row);
        var delete=menu.Items.OfType<MenuItem>().Single(x=>x.Header?.ToString()=="Delete message…");
        var command=Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IAsyncRelayCommand>(delete.Command);
        var request=command.ExecuteAsync(null); Dispatcher.UIThread.RunJobs(); var dialog=Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
        await service.DidNotReceive().DeleteMessageAsync(Arg.Any<string>()); dialog.Close(false); await request;
        await service.DidNotReceive().DeleteMessageAsync(Arg.Any<string>());
        request=command.ExecuteAsync(null); Dispatcher.UIThread.RunJobs(); dialog=Assert.Single(owner.OwnedWindows.OfType<MessageDialog>());
        dialog.Close(true); await request; await service.Received(1).DeleteMessageAsync(item.Id);
        owner.Close(); await workspace.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task ParticipantRightClick_ProvidesFocusAndFullscreen_WithoutLeavingCall()
    {
        var (workspace,_)=Model(); workspace.InCall=true;
        var tile=new VideoTileViewModel("peer · screen · share") { Name="Dina · Screen share", Frame=new VideoPicture("peer · screen · share",2,2,new byte[16]) }; workspace.Videos.Add(tile);
        var window=new CallWindow { DataContext=workspace }; window.Show(); Dispatcher.UIThread.RunJobs();
        var card=window.GetVisualDescendants().OfType<Border>().Single(x=>x.Classes.Contains("participant-card")); var point=card.TranslatePoint(new Point(24,24),window)!.Value;
        window.MouseMove(point); window.MouseDown(point,MouseButton.Right); window.MouseUp(point,MouseButton.Right); Dispatcher.UIThread.RunJobs();
        var menu=Assert.IsType<ContextMenu>(card.ContextMenu); Assert.True(menu.IsOpen); Assert.Null(workspace.FocusedVideo);
        menu.Items.OfType<MenuItem>().Single(x=>x.Header?.ToString()=="Full screen").Command!.Execute(null); menu.Close(); Dispatcher.UIThread.RunJobs();
        Assert.Same(tile,workspace.FocusedVideo); Assert.Equal(OperatingSystem.IsLinux()?WindowState.Normal:WindowState.FullScreen,window.WindowState);Assert.Contains("share-fullscreen",window.Classes); Assert.True(workspace.InCall);
        window.CloseAfterCall(); Dispatcher.UIThread.RunJobs(); await workspace.DisposeAsync();
    }
    [AvaloniaFact]
    public async Task EditorsOpenInOwnedWindows_KeepDrafts_AndLeaveTheCalendarFullWidth()
    {
        var (workspace,_) = Model(); workspace.SelectedWorkspaceTab = 2;
        var view = new WorkspaceView { DataContext = workspace }; var window = new Window { Width = 1360, Height = 850, Content = view }; window.Show(); Dispatcher.UIThread.RunJobs();
        var create = view.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "New schedule");
        create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        var editor = Assert.Single(view.EditorWindows); Assert.Same(window, editor.Owner); Assert.Equal(WorkspaceFormKind.Calendar, editor.Kind);
        Assert.Single(view.FindControl<Grid>("CalendarLayout")!.ColumnDefinitions); workspace.EventTitle = "Draft preserved";
        await editor.CloseAnimatedAsync(); Assert.Empty(view.EditorWindows); Assert.Equal("Draft preserved", workspace.EventTitle);
        window.Width = 850; create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        editor = Assert.Single(view.EditorWindows); Assert.Same(window, editor.Owner); Assert.Equal("Draft preserved", workspace.EventTitle); await editor.CloseAnimatedAsync();
        workspace.SelectedWorkspaceTab = 3; Dispatcher.UIThread.RunJobs();
        view.GetVisualDescendants().OfType<Button>().Single(button => button.Content?.ToString() == "Compose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Dispatcher.UIThread.RunJobs();
        editor = Assert.Single(view.EditorWindows); Assert.Equal(WorkspaceFormKind.Announcement, editor.Kind); await editor.CloseAnimatedAsync();
        window.Close(); await workspace.DisposeAsync();
    }
    private static void Save(Window window,string file)
    {
        Dispatcher.UIThread.RunJobs(); using var bitmap=window.CaptureRenderedFrame(); Assert.NotNull(bitmap);
        if (Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is {} directory) { Directory.CreateDirectory(directory); using var output=File.Create(Path.Combine(directory,file)); bitmap.Save(output,new PngBitmapEncoderOptions()); }
    }
}
