using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using UserIdentityDesktop.ViewModels;

namespace UserIdentityDesktop.Views;

/// <summary>The same message presentation and actions in the main chat and call sidebar.</summary>
public partial class ChatMessageCard : UserControl
{
    public static readonly StyledProperty<ChatViewModel?> ChatProperty = AvaloniaProperty.Register<ChatMessageCard, ChatViewModel?>(nameof(Chat));
    public ChatViewModel? Chat { get => GetValue(ChatProperty); set => SetValue(ChatProperty,value); }
    public static readonly StyledProperty<string> SenderLabelProperty = AvaloniaProperty.Register<ChatMessageCard,string>(nameof(SenderLabel),"Participant");
    public string SenderLabel { get => GetValue(SenderLabelProperty); private set => SetValue(SenderLabelProperty,value); }
    private int senderGeneration;
    public ChatMessageCard()
    {
        InitializeComponent();
        DataContextChanged += (_,_) => ResolveSender();
        PropertyChanged += (_,e) => { if(e.Property==ChatProperty) ResolveSender(); };
        AttachedToVisualTree += (_,_) => ResolveSender();
        DetachedFromVisualTree += (_,_) => senderGeneration++;
    }
    private async void ResolveSender()
    {
        var generation=++senderGeneration;
        if(DataContext is not ChatMessageItemViewModel item)return;
        SenderLabel=item.IsMine?"You":string.IsNullOrWhiteSpace(item.Message.SenderName)?"Participant":item.Message.SenderName;
        if(item.IsMine || SenderLabel!="Participant" || Chat is not {} chat)return;
        try
        {
            var name=await chat.ResolveMessageSenderAsync(item.Sender);
            if(generation==senderGeneration && !string.IsNullOrWhiteSpace(name))SenderLabel=name;
        }
        catch { /* Retain a named fallback, never an internal account identifier. */ }
    }
    private void Message_ContextRequested(object? sender,ContextRequestedEventArgs e)
    {
        if(Chat is {} chat && sender is Control {DataContext:ChatMessageItemViewModel item} target)
        { MessageMenus.Open(target,MessageMenus.Create(chat,item,target),e.TryGetPosition(target,out _));e.Handled=true; }
    }
    private void MessageMenu_Click(object? sender,RoutedEventArgs e)
    {
        if(Chat is {} chat && sender is Control {DataContext:ChatMessageItemViewModel item} target)
            MessageMenus.Open(target,MessageMenus.Create(chat,item,target),false);
    }
    private async void JoinCall_Click(object? sender,RoutedEventArgs e)
    {
        if(Chat?.Workspace is {} workspace && DataContext is ChatMessageItemViewModel {Message.CallSummary:{} call})await workspace.JoinExistingCallAsync(call.CallId);
    }
}
