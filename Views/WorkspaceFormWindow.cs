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
        var description = kind switch
        {
            WorkspaceFormKind.Calendar => "Plan the details, choose participants and check availability before saving.",
            WorkspaceFormKind.Vacation => "Choose your dates. We count only the shifts you would work.",
            _ => "Write a clear message, choose who receives it and decide when it goes live."
        };
        Width = 760; Height = kind == WorkspaceFormKind.Vacation ? 680 : 840; MinWidth = 440; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; FontSize = 13;
        this.Bind(BackgroundProperty, this.GetResourceObservable("ForgeBg")); this.Bind(ForegroundProperty, this.GetResourceObservable("ForgeTextPrimary"));
        editor.DataContext = model; editor.IsVisible = true; editor.Margin = kind == WorkspaceFormKind.Vacation ? new Thickness(20, 14) : new Thickness(24, 20); editor.MaxHeight = double.PositiveInfinity;
        var title = new TextBlock { Name = "FormHeading", Text = Title, FontSize = 26, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        var intro = new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap }; intro.Classes.Add("form-help");
        var eyebrow = new TextBlock { Text = "FIRE USER  /  " + (kind == WorkspaceFormKind.Calendar ? "CALENDAR" : kind == WorkspaceFormKind.Vacation ? "TIME OFF" : "ANNOUNCEMENTS"), FontSize = 11, FontWeight = FontWeight.SemiBold };
        eyebrow.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ForgeTextSecondary"));
        var heading = new StackPanel { Spacing = 6 }; if (kind != WorkspaceFormKind.Vacation) heading.Children.Add(eyebrow); heading.Children.Add(title); heading.Children.Add(intro);
        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("4,*"), ColumnSpacing = 16 };
        var stripe = new Border { CornerRadius = new CornerRadius(2) }; stripe.Bind(Border.BackgroundProperty, this.GetResourceObservable("ForgePrimaryGradient"));headerGrid.Children.Add(stripe);headerGrid.Children.Add(heading);Grid.SetColumn(heading,1);
        var header = new Border { Child = headerGrid, Padding = kind == WorkspaceFormKind.Vacation ? new Thickness(20,14) : new Thickness(24,20), BorderThickness = new Thickness(0,0,0,1) };
        header.Bind(Border.BackgroundProperty,this.GetResourceObservable("ForgeSurface"));header.Bind(Border.BorderBrushProperty,this.GetResourceObservable("ForgeBorder"));
        var feedbackText = new TextBlock { TextWrapping = TextWrapping.Wrap }; feedbackText.Bind(TextBlock.TextProperty, new Binding(nameof(model.Feedback)));
        var errors = new Dictionary<string,(Control Input,TextBlock Message,StackPanel Parent)>();
        var goToField = new Button { Content = "Go to field", HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false };
        goToField.Click += (_, _) =>
        {
            if (model.FormValidationIssue is {} issue && errors.TryGetValue(issue.Field,out var field))
            { field.Input.BringIntoView();field.Input.Focus();field.Message.BringIntoView(); }
        };
        var statusContent = new StackPanel { Spacing = 8 };statusContent.Children.Add(feedbackText);statusContent.Children.Add(goToField);
        var feedback = new Border { Name = "FormStatus", Focusable = true, Margin = new Thickness(24,0,24,12), Padding = new Thickness(12), CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(3,0,0,0), Child = new ScrollViewer { MaxHeight = 80, Content = statusContent } };
        feedback.Bind(Border.BackgroundProperty,this.GetResourceObservable("ForgeBgSecondary"));feedback.Bind(Border.BorderBrushProperty,this.GetResourceObservable("ForgePrimary"));
        feedback.Bind(IsVisibleProperty,new Binding(nameof(model.Feedback)) { Converter = NullToBoolConverter.Instance });AutomationProperties.SetName(feedback,"Form status");
        var progress = new ProgressBar { IsIndeterminate = true, Height = 3 }; progress.Bind(IsVisibleProperty,new Binding(nameof(model.IsLoading)));
        var footerButtons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        footerButtons.Bind(IsEnabledProperty, new Binding(nameof(model.IsLoading)) { Converter = BoolInverterConverter.Instance });
        var cancel = new Button { Content = "Cancel", MinHeight = 40 }; cancel.Classes.Add("quiet"); cancel.Click += (_, _) => _ = CloseAnimatedAsync(); footerButtons.Children.Add(cancel);
        if (kind == WorkspaceFormKind.Calendar) footerButtons.Children.Add(new Button { Content = "Review availability", MinHeight = 40, Command = model.ReviewEventCommand });
        var submit = new Button { Name = "FormSubmit", MinHeight = 40, Content = kind switch { WorkspaceFormKind.Calendar => "Save schedule", WorkspaceFormKind.Vacation => "Review request", _ => "Publish announcement" }, Command = kind switch { WorkspaceFormKind.Calendar => model.SaveEventCommand, WorkspaceFormKind.Vacation => model.ReviewVacationRequestCommand, _ => model.PublishCommand } };
        submit.Classes.Add("fire-btn");
        if (kind == WorkspaceFormKind.Calendar) submit.Bind(IsEnabledProperty,new Binding(nameof(model.CanSaveCalendarForm)));
        if (kind == WorkspaceFormKind.Announcement) submit.Bind(ContentControl.ContentProperty,new Binding(nameof(model.AnnouncementSubmitLabel)));
        footerButtons.Children.Add(submit);
        var hint = new TextBlock { TextWrapping = TextWrapping.Wrap }; hint.Classes.Add("form-help");
        hint.Bind(TextBlock.TextProperty,new Binding(kind switch { WorkspaceFormKind.Calendar => nameof(model.CalendarReviewHint), WorkspaceFormKind.Vacation => nameof(model.VacationReviewHint), _ => nameof(model.AnnouncementPublishSummary) }));
        var footerStack = new StackPanel { Spacing = 12 };if (kind != WorkspaceFormKind.Vacation) footerStack.Children.Add(hint);footerStack.Children.Add(footerButtons);
        var footer = new Border { Child = footerStack, Padding = new Thickness(24,16), BorderThickness = new Thickness(0,1,0,0) };
        footer.Bind(Border.BorderBrushProperty,this.GetResourceObservable("ForgeBorder"));footer.Bind(Border.BackgroundProperty,this.GetResourceObservable("ForgeSurface"));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto") };
        layout.Children.Add(header);layout.Children.Add(progress);Grid.SetRow(progress,1);layout.Children.Add(editor);Grid.SetRow(editor,2);Grid.SetColumn(editor,0);Grid.SetRowSpan(editor,1);
        layout.Children.Add(feedback);Grid.SetRow(feedback,3);layout.Children.Add(footer);Grid.SetRow(footer,4);
        Content = layout; motion = new WindowMotion(this);
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.Enter, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control), Command = kind switch { WorkspaceFormKind.Calendar => model.SubmitCalendarFormCommand, WorkspaceFormKind.Vacation => model.ReviewVacationRequestCommand, _ => model.PublishCommand } });
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
                if (input is CalendarDatePicker picker) picker.DateValidationError += DateInputError;
            }
            ShowValidation();editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
        };
        model.PropertyChanged += ModelChanged;
        Closed += (_, _) =>
        {
            model.PropertyChanged -= ModelChanged;
            foreach (var field in errors.Values) { if (field.Input is CalendarDatePicker picker) picker.DateValidationError -= DateInputError; DataValidationErrors.SetErrors(field.Input,null);field.Parent.Children.Remove(field.Message); }
            closed.TrySetResult();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            // CalendarDatePicker closes its popup before this event reaches the window.
            // Consume that same Escape here, preserving the form and its draft.
            if (e.Source is Control source && (source is Calendar || source.GetVisualAncestors().OfType<Calendar>().Any())) { e.Handled=true;return; }
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
                var message = issue?.Field == name ? issue.Message : null;
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
            var wide = Bounds.Width >= 680;
            pair.ColumnDefinitions = new ColumnDefinitions(wide ? "*,*" : "*");pair.RowDefinitions = new RowDefinitions(wide ? "Auto" : "Auto,Auto");
            for (var index=0;index<pair.Children.Count;index++) { Grid.SetColumn(pair.Children[index],wide?index:0);Grid.SetRow(pair.Children[index],wide?0:index); }
        }
    }
    public async Task CloseAnimatedAsync() { await motion.CloseAsync<object?>(null);await closed.Task; }
}
