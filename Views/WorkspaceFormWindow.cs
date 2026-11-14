using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UserIdentityDesktop.Converters;
using UserIdentityDesktop.ViewModels;

namespace UserIdentityDesktop.Views;

internal sealed class WorkspaceFormWindow : Window
{
    private readonly WindowMotion motion;
    private readonly TaskCompletionSource closed = new();
    public Control Editor { get; }
    public WorkspaceFormKind Kind { get; }
    public WorkspaceFormWindow(Control editor, WorkspaceViewModel model, WorkspaceFormKind kind)
    {
        Editor = editor; Kind = kind; DataContext = model;
        Title = kind switch { WorkspaceFormKind.Calendar => "New schedule", WorkspaceFormKind.Vacation => "Plan your time off", _ => "New announcement" };
        ComposeAppearance.Apply(this);
        Width = 1100; Height = 840; MinWidth = 440; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        editor.Classes.Add("compose-scroll");
        if(editor is ScrollViewer scroller)scroller.AllowAutoHide=false;
        editor.DataContext = model; editor.IsVisible = true; editor.Margin = new Thickness(12, 6); editor.MaxHeight = double.PositiveInfinity;
        if(editor is ScrollViewer {Content:Control body}) {body.MaxWidth=1400;body.HorizontalAlignment=HorizontalAlignment.Stretch;body.VerticalAlignment=VerticalAlignment.Top;}
        var feedbackText = new TextBlock { TextWrapping = TextWrapping.Wrap }; feedbackText.Bind(TextBlock.TextProperty, new Binding(nameof(model.Feedback)));
        var errors = new Dictionary<string,(Control Input,TextBlock Message,StackPanel Parent)>();
        var goToField = new Button { [!Button.ContentProperty] = UserIdentityDesktop.Localization.LocalizedStrings.Bind("Go to field"), HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        goToField.Click += (_, _) =>
        {
            if (model.FormValidationIssue is {} issue && errors.TryGetValue(issue.Field,out var field))
            { field.Input.BringIntoView();if(field.Input is ComposeDateTimePicker dateTime)dateTime.FocusTrigger();else field.Input.Focus();field.Message.BringIntoView(); }
        };
        var statusContent = new StackPanel { Spacing = 8 };statusContent.Children.Add(feedbackText);statusContent.Children.Add(goToField);
        var feedback = new Border { Name = "FormStatus", Focusable = true, Margin = new Thickness(24,0,24,12), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3,0,0,0), Child = new ScrollViewer { MaxHeight = 80, Content = statusContent } };
        feedback.Bind(Border.BackgroundProperty,this.GetResourceObservable("ForgeBgSecondary"));feedback.Bind(Border.BorderBrushProperty,this.GetResourceObservable("ForgePrimary"));
        feedback.Bind(IsVisibleProperty,new Binding(nameof(model.Feedback)) { Converter = NullToBoolConverter.Instance });AutomationProperties.SetName(feedback,"Form status");
        var progress = new ProgressBar { IsIndeterminate = true, Height = 3 }; progress.Bind(IsVisibleProperty,new Binding(nameof(model.IsLoading)));
        var footerButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footerButtons.Bind(IsEnabledProperty, new Binding(nameof(model.IsLoading)) { Converter = BoolInverterConverter.Instance });
        var cancel = new Button { [!Button.ContentProperty] = UserIdentityDesktop.Localization.LocalizedStrings.Bind("Cancel"), MinHeight = 40 }; cancel.Classes.Add("quiet"); cancel.Click += (_, _) => _ = CloseAnimatedAsync(); footerButtons.Children.Add(cancel);
        if (kind == WorkspaceFormKind.Calendar) footerButtons.Children.Add(new Button { [!Button.ContentProperty] = UserIdentityDesktop.Localization.LocalizedStrings.Bind("Review availability"), MinHeight = 40, Command = model.ReviewEventCommand });
        var submit = new Button { Name = "FormSubmit", MinHeight = 40, Content = kind switch { WorkspaceFormKind.Calendar => "Save schedule", WorkspaceFormKind.Vacation => "Review request", _ => "Publish announcement" }, Command = kind switch { WorkspaceFormKind.Calendar => model.SaveEventCommand, WorkspaceFormKind.Vacation => model.ReviewVacationRequestCommand, _ => model.PublishCommand } };
        submit.Classes.Add("fire-btn");
        if (kind == WorkspaceFormKind.Calendar) submit.Bind(IsEnabledProperty,new Binding(nameof(model.CanSaveCalendarForm)));
        if (kind == WorkspaceFormKind.Announcement) submit.Bind(ContentControl.ContentProperty,new Binding(nameof(model.AnnouncementSubmitLabel)));
        footerButtons.Children.Add(submit);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap }; hint.Classes.Add("form-help");
        hint.Bind(TextBlock.TextProperty,new Binding(kind switch { WorkspaceFormKind.Calendar => nameof(model.CalendarReviewHint), WorkspaceFormKind.Vacation => nameof(model.VacationReviewHint), _ => nameof(model.AnnouncementPublishSummary) }));
        var footerStack = new StackPanel { Spacing = 12, Margin=new Thickness(0,20,0,0) };if (kind != WorkspaceFormKind.Vacation) footerStack.Children.Add(hint);footerStack.Children.Add(footerButtons);
        var formBody = editor is ScrollViewer { Content:Border {Child:Panel single} } ? single
            : (editor as ScrollViewer)?.Content is Panel grouped ? grouped.Children.OfType<Border>().Last(x=>x.Classes.Contains("compose-card")).Child as Panel : null;
        formBody?.Children.Add(footerStack);
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        layout.Children.Add(progress);layout.Children.Add(feedback);Grid.SetRow(feedback,1);layout.Children.Add(editor);Grid.SetRow(editor,2);Grid.SetColumn(editor,0);Grid.SetRowSpan(editor,1);
        Content = layout; motion = new WindowMotion(this);
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control), Command = kind switch { WorkspaceFormKind.Calendar => model.SubmitCalendarFormCommand, WorkspaceFormKind.Vacation => model.ReviewVacationRequestCommand, _ => model.PublishCommand } });
        var dateOpenHandlers=new Dictionary<CalendarDatePicker,EventHandler<PointerPressedEventArgs>>();
        SizeChanged += (_, _) => ArrangeFields();
        Opened += (_, _) =>
        {
            ArrangeFields();
            foreach (var input in editor.GetVisualDescendants().OfType<Control>().Where(x=>x.Tag is string).ToArray())
            {
                if (input.Parent is not StackPanel parent || input.Tag is not string field || errors.ContainsKey(field)) continue;
                var message = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, IsVisible = false };
                message.Bind(TextBlock.ForegroundProperty,this.GetResourceObservable("ForgeErrorText"));
                parent.Children.Insert(parent.Children.IndexOf(input)+1,message);errors[field]=(input,message,parent);
                if (input is CalendarDatePicker picker)
                {
                    picker.DateValidationError += DateInputError;
                    EventHandler<PointerPressedEventArgs> open=(_,e)=>{if(e.Source is Control source && (source is TextBox || source.GetVisualAncestors().OfType<TextBox>().Any()) && e.GetCurrentPoint(picker).Properties.IsLeftButtonPressed){picker.IsDropDownOpen=true;e.Handled=true;}};
                    dateOpenHandlers[picker]=open;picker.AddHandler(PointerPressedEvent,open,RoutingStrategies.Tunnel);
                }
            }
            ShowValidation();Focus();
        };
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) =>
        {
            model.PropertyChanged -= ModelChanged;
            formBody?.Children.Remove(footerStack);
            foreach(var pair in dateOpenHandlers)pair.Key.RemoveHandler(PointerPressedEvent,pair.Value);
            foreach (var field in errors.Values) { if (field.Input is CalendarDatePicker picker) picker.DateValidationError -= DateInputError; DataValidationErrors.SetErrors(field.Input,null);field.Parent.Children.Remove(field.Message); }
            closed.TrySetResult();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            // CalendarDatePicker closes its popup before this event reaches the window.
            // Consume that same Escape here, preserving the form and its draft.
            if (e.Source is Control source && (source is Calendar || source.GetVisualAncestors().OfType<Calendar>().Any())) { e.Handled=true;return; }
            var dateTime=editor.GetVisualDescendants().OfType<ComposeDateTimePicker>().FirstOrDefault(x=>x.IsOpen);
            if(dateTime!=null){dateTime.IsOpen=false;e.Handled=true;return;}
            var picker=editor.GetVisualDescendants().OfType<CalendarDatePicker>().FirstOrDefault(x=>x.IsDropDownOpen);
            if(picker!=null){picker.IsDropDownOpen=false;e.Handled=true;return;}
            e.Handled=true;_=CloseAnimatedAsync();
        };
        void ModelChanged(object? sender,PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(model.FormValidationIssue)) ShowValidation();
            if (e.PropertyName == nameof(model.Feedback) && !string.IsNullOrWhiteSpace(model.Feedback))
                Dispatcher.UIThread.Post(() => { if (IsVisible && feedback.IsVisible) feedback.Focus(); });
        }
        void DateInputError(object? sender,CalendarDatePickerDateValidationErrorEventArgs e)
        {
            e.ThrowException = false;
            model.Feedback = "That date could not be read. The previous date was kept. Choose a day from the calendar or enter a date such as " + DateTime.Today.ToString("yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture) + ".";
        }
        void ShowValidation()
        {
            var issue = model.FormValidationIssue;
            foreach (var (name,field) in errors)
            {
                var message = (issue?.Field == nameof(model.EventTime) ? nameof(model.EventDate) : issue?.Field) == name ? issue?.Message : null;
                field.Message.Text = message;field.Message.IsVisible = message != null;
                DataValidationErrors.SetErrors(field.Input,message == null ? null : new object[]{message});
            }
            goToField.IsVisible = issue != null && errors.ContainsKey(issue.Field);
        }
    }
    private void ArrangeFields()
    {
        foreach (var pair in Editor.GetVisualDescendants().OfType<Grid>().Where(x=>x.Classes.Contains("form-pair")))
        {
            var wide = Bounds.Width >= 640;
            pair.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");pair.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            for (var index=0;index<pair.Children.Count;index++) { Grid.SetColumn(pair.Children[index],wide?index:0);Grid.SetRow(pair.Children[index],wide?0:index); }
        }
    }
    public async Task CloseAnimatedAsync() { await motion.CloseAsync<object?>(null);await closed.Task; }
}
