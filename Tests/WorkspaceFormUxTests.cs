using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.Views;
using UserIdentityDesktop.ViewModels;
using Xunit;

namespace UserIdentityDesktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task CalendarPopupUsesFieryDaysAndLocalDateBindingsInsteadOfSegmentedSelectors(bool light)
    {
        var (vm,_,_)=Create();var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1100,Height=900,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();
        try
        {
            vm.ShowCalendarEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);
            Assert.Empty(form.GetVisualDescendants().OfType<DatePicker>());
            var picker=Assert.Single(form.GetVisualDescendants().OfType<CalendarDatePicker>());
            var day=new DateTime(DateTime.Today.Year,DateTime.Today.Month,17);
            picker.SetCurrentValue(CalendarDatePicker.SelectedDateProperty,day);Dispatcher.UIThread.RunJobs();Assert.Equal(day,vm.EventDate!.Value.Date);
            picker.Text="2027-02-11";Dispatcher.UIThread.RunJobs();Assert.Equal(new DateTime(2027,2,11),vm.EventDate!.Value.Date);
            picker.SetCurrentValue(CalendarDatePicker.SelectedDateProperty,day);Dispatcher.UIThread.RunJobs();
            picker.IsDropDownOpen=true;Dispatcher.UIThread.RunJobs();
            var popup=Assert.Single(picker.GetVisualDescendants().OfType<Popup>());var calendar=Assert.IsType<Calendar>(popup.Child);
            Assert.True(calendar.Bounds.Width>=300);Assert.Equal(day,calendar.SelectedDate);
            var selected=Assert.Single(calendar.GetVisualDescendants().OfType<CalendarDayButton>(),x=>x.Classes.Contains(":selected"));
            var background=selected.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="Root").Background;
            Assert.IsAssignableFrom<ILinearGradientBrush>(background);
            var label=selected.GetVisualDescendants().OfType<ContentPresenter>().Single(x=>x.Name=="PART_ContentPresenter");
            Assert.Equal(Color.Parse("#FFFFFF"),Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            if(Environment.GetEnvironmentVariable("FIREUSER_TEST_ARTIFACTS") is {} path)
            {
                Directory.CreateDirectory(path);using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(calendar.Bounds.Width),(int)Math.Ceiling(calendar.Bounds.Height)),new Vector(96,96));bitmap.Render(calendar);
                using var file=File.Create(Path.Combine(path,$"fiery-calendar-popup-{(light?"light":"dark")}.png"));bitmap.Save(file,new PngBitmapEncoderOptions());
            }
            calendar.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Escape});Dispatcher.UIThread.RunJobs();
            Assert.False(picker.IsDropDownOpen);Assert.True(form.IsVisible);await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory]
    [InlineData(WorkspaceFormKind.Calendar,false)][InlineData(WorkspaceFormKind.Vacation,false)][InlineData(WorkspaceFormKind.Announcement,false)]
    [InlineData(WorkspaceFormKind.Calendar,true)][InlineData(WorkspaceFormKind.Vacation,true)][InlineData(WorkspaceFormKind.Announcement,true)]
    public async Task SubmissionFormsStackFieldsAndKeepActionsVisibleInCompactWindows(WorkspaceFormKind kind,bool light)
    {
        var (vm,_,_)=Create();vm.ScheduleAnnouncement=true;
        var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1100,Height=900,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();
        try
        {
            if(kind==WorkspaceFormKind.Calendar)vm.ShowCalendarEditor=true;
            else if(kind==WorkspaceFormKind.Vacation)vm.ShowVacationEditor=true;else vm.ShowAnnouncementEditor=true;
            Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);form.Width=480;form.Height=560;Dispatcher.UIThread.RunJobs();
            Assert.Empty(form.GetVisualDescendants().OfType<DatePicker>());
            Assert.Equal(kind==WorkspaceFormKind.Calendar?1:2,form.GetVisualDescendants().OfType<CalendarDatePicker>().Count());
            var pairs=form.GetVisualDescendants().OfType<Grid>().Where(x=>x.Classes.Contains("form-pair")).ToArray();Assert.NotEmpty(pairs);
            foreach(var pair in pairs){Assert.Single(pair.ColumnDefinitions);Assert.Equal(1,Grid.GetRow(pair.Children[1]));}
            var submit=form.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="FormSubmit");
            var point=submit.TranslatePoint(new Point(submit.Bounds.Width,submit.Bounds.Height),form)!.Value;
            Assert.InRange(point.X,1,form.ClientSize.Width);Assert.InRange(point.Y,1,form.ClientSize.Height);
            Save(form,$"submission-{kind}-compact-{(light?"light":"dark")}.png");await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task ScheduleReviewInvalidatesAfterEditsAndConflictsRequireAcknowledgement()
    {
        var (vm,api,_)=Create();vm.EventTitle="Planning";vm.IsMeeting=false;
        var conflict=new CalendarConflict(Guid.NewGuid(),"Meeting","Team stand-up",DateTime.UtcNow,DateTime.UtcNow.AddHours(1),false);
        api.ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>()).Returns(new CalendarReview(new(){conflict},"review"));
        try
        {
            await vm.ReviewEventCommand.ExecuteAsync(null);Assert.True(vm.Reviewed);Assert.False(vm.CanSaveCalendarForm);
            vm.AcknowledgeConflicts=true;Assert.True(vm.CanSaveCalendarForm);vm.DurationMinutes="45";
            Assert.False(vm.Reviewed);Assert.False(vm.CanSaveCalendarForm);Assert.Empty(vm.Conflicts);
            api.ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>()).Returns(new CalendarReview(new(),"next"));
            await vm.ReviewEventCommand.ExecuteAsync(null);Assert.True(vm.CanSaveCalendarForm);
            var person=new UserIdentityDesktop.ViewModels.PersonSelection(new WorkplacePerson(Guid.NewGuid(),"Maya"));vm.People.Add(person);person.Selected=true;
            Assert.False(vm.Reviewed);Assert.False(vm.CanSaveCalendarForm);
        }
        finally{vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task AudienceSearchKeepsHiddenSelectionsAndPublicationModeIsExplicit()
    {
        var (vm,_,_)=Create();var alice=new UserIdentityDesktop.ViewModels.PersonSelection(new WorkplacePerson(Guid.NewGuid(),"Alice")){Selected=true};var bob=new UserIdentityDesktop.ViewModels.PersonSelection(new WorkplacePerson(Guid.NewGuid(),"Bob"));
        try
        {
            vm.Audience.Add(alice);vm.Audience.Add(bob);vm.AnnouncementAudienceSearch="Bob";vm.SelectVisibleAnnouncementAudienceCommand.Execute(null);
            Assert.True(alice.Selected);Assert.True(bob.Selected);Assert.Equal("Bob",Assert.Single(vm.FilteredAnnouncementAudience).Name);Assert.Equal("2 of 2 recipients selected",vm.AnnouncementAudienceSummary);
            vm.AnnouncementAudienceSearch="Nobody";Assert.True(vm.NoMatchingAnnouncementAudience);vm.ClearAnnouncementAudienceCommand.Execute(null);Assert.False(alice.Selected);Assert.False(bob.Selected);
            vm.ScheduleAnnouncement=true;Assert.NotNull(vm.PublishDate);Assert.Equal("Schedule announcement",vm.AnnouncementSubmitLabel);
            vm.ScheduleAnnouncement=false;Assert.Null(vm.PublishDate);Assert.Equal("Publish announcement",vm.AnnouncementSubmitLabel);
        }
        finally{vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task ValidationAppearsBesideTheFieldAndCanFocusItWithoutLosingTheDraft()
    {
        var (vm,_,_)=Create();vm.IsMeeting=false;var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view};owner.Show();
        try
        {
            vm.ShowCalendarEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);
            await vm.ReviewEventCommand.ExecuteAsync(null);Dispatcher.UIThread.RunJobs();Assert.Equal(nameof(vm.EventTitle),vm.FormValidationIssue!.Field);
            var input=form.GetVisualDescendants().OfType<TextBox>().Single(x=>Equals(x.Tag,nameof(vm.EventTitle)));
            Assert.True(DataValidationErrors.GetHasErrors(input));Assert.Contains(((StackPanel)input.Parent!).Children.OfType<TextBlock>(),x=>x.IsVisible&&x.Text==vm.Feedback);
            var jump=form.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Go to field"));jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Dispatcher.UIThread.RunJobs();Assert.True(input.IsKeyboardFocusWithin);
            vm.EventTitle="Design review";Assert.Null(vm.FormValidationIssue);Assert.False(DataValidationErrors.GetHasErrors(input));
            vm.DurationMinutes="zero";await vm.ReviewEventCommand.ExecuteAsync(null);Assert.Equal(nameof(vm.DurationMinutes),vm.FormValidationIssue!.Field);Assert.Equal("Design review",vm.EventTitle);
            Dispatcher.UIThread.RunJobs();Save(form,"submission-calendar-validation.png");await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task MultilineEnterDoesNotSubmitButSingleLineEnterStillReviewsTheForm()
    {
        var (vm,api,_)=Create();vm.EventTitle="Planning";vm.IsMeeting=false;
        api.ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>()).Returns(new CalendarReview(new(),"review"));
        var view=new WorkspaceView{DataContext=vm,ShowReviewAsync=(_,_,_)=>Task.FromResult(true)};var owner=new Window{Content=view};owner.Show();
        try
        {
            vm.ShowCalendarEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);
            var description=form.GetVisualDescendants().OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="Schedule description");
            description.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});await api.DidNotReceive().ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>());
            var title=form.GetVisualDescendants().OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="Schedule title");title.RaiseEvent(new KeyEventArgs{RoutedEvent=InputElement.KeyDownEvent,Key=Key.Enter});
            if(vm.SubmitCalendarFormCommand.ExecutionTask is {} task)await task;
            await api.Received(1).ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>());await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task SidebarStartsCollapsedAndOnlyHoverExpandsWithCallLogsAfterChat()
    {
        var (vm,_,_)=Create();var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1200,Height=850};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var rail=view.FindControl<Border>("NavigationRail")!;
            var buttons=rail.GetVisualDescendants().OfType<Button>().Where(x=>x.Classes.Contains("workspace-nav")).ToArray();
            Assert.Equal(new[]{"0","6","1","2","3","5","4"},buttons.Select(x=>x.Tag?.ToString()));
            Assert.DoesNotContain("expanded",rail.Classes);
            buttons[0].Focus(NavigationMethod.Tab);Dispatcher.UIThread.RunJobs();Assert.DoesNotContain("expanded",rail.Classes);
            rail.RaiseEvent(new PointerEventArgs(InputElement.PointerEnteredEvent,rail,new Pointer(1,PointerType.Mouse,true),rail,default,0,default,default));Assert.Contains("expanded",rail.Classes);
            buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Equal(6,vm.SelectedWorkspaceTab);
            rail.RaiseEvent(new PointerEventArgs(InputElement.PointerExitedEvent,rail,new Pointer(1,PointerType.Mouse,true),rail,default,0,default,default));Dispatcher.UIThread.RunJobs();Assert.DoesNotContain("expanded",rail.Classes);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task VacationMatchesAngularPlanAndHidesInternalIds(bool light)
    {
        var (vm,_,_)=Create();var request=new VacationRequest(Guid.NewGuid(),Guid.NewGuid(),0,"Family trip",2,1){RequesterName="Maya Hassan",DatesLabel="Mon, 8 Feb 2027 – Tue, 9 Feb 2027"};vm.Vacations.Add(request);
        vm.VacationStart=new DateTimeOffset(new DateTime(2027,2,8));vm.VacationEnd=vm.VacationStart;
        vm.VacationReview=new(new(){new(new DateOnly(2027,2,8),new DateTime(2027,2,8,7,0,0,DateTimeKind.Utc),new DateTime(2027,2,8,15,0,0,DateTimeKind.Utc),1)},"preview",new(){new(2027,20,2,3,25,0,4)});
        vm.VacationReady=true;vm.VacationInstructions="";
        vm.SelectedWorkspaceTab=2;vm.SelectedCalendarTab=1;
        // The tab snapshot is empty in this fixture; add the request after it settles.
        vm.Vacations.Add(request);
        var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1200,Height=850,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var labels=view.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text).ToArray();Assert.Contains("Maya Hassan",labels);Assert.DoesNotContain(request.UserId.ToString(),labels);Assert.DoesNotContain(request.Id.ToString(),labels);
            vm.ShowVacationEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);Assert.Equal(680,form.Height);var texts=form.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text).ToArray();
            Assert.Contains("Plan your time off",texts);Assert.Contains("Annual entitlement",texts);Assert.Contains("Available",texts);Assert.Contains("1 leave day",texts);Assert.Contains("2027 balance: 20 available → 19 after this request",texts);Assert.Contains("Reason (optional)",texts);
            var submit=form.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="FormSubmit");Assert.Equal("Review request",submit.Content);Assert.Same(vm.ReviewVacationRequestCommand,submit.Command);
            var reason=form.GetVisualDescendants().OfType<TextBox>().Single(x=>AutomationProperties.GetName(x)=="Vacation reason");
            Assert.True(reason.TranslatePoint(new Point(0,reason.Bounds.Height),form)!.Value.Y <= form.Editor.TranslatePoint(new Point(0,form.Editor.Bounds.Height),form)!.Value.Y);
            Assert.False(vm.ShowVacationPrerequisite);
            Save(form,$"vacation-angular-aligned-{(light?"light":"dark")}.png");await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task VacationReviewRequestConfirmsOnceAndNeverSubmitsOnCancelOrEmptyShifts()
    {
        var (vm,api,_)=Create();var confirms=0;var accept=false;vm.ConfirmActionAsync=(title,details)=>{confirms++;Assert.Equal("Review vacation request",title);Assert.Contains("after this request",details);return Task.FromResult(accept);};
        api.VacationReadinessAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<CancellationToken>()).Returns(new VacationReadiness(true,null));
        api.PreviewVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(new VacationPreview(new(){new(DateOnly.FromDateTime(DateTime.Today.AddDays(1)),DateTime.UtcNow.AddDays(1),DateTime.UtcNow.AddDays(1).AddHours(8),1)},"current-preview",new(){new(DateTime.Today.AddDays(1).Year,20,0,0)}));
        try
        {
            await vm.ReviewVacationRequestCommand.ExecuteAsync(null);Assert.Equal(1,confirms);await api.DidNotReceive().SubmitVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
            accept=true;await vm.ReviewVacationRequestCommand.ExecuteAsync(null);Assert.Equal(2,confirms);await api.Received(1).SubmitVacationRangeAsync(vm.Chat.CurrentUser.Id,Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),"","current-preview",Arg.Any<CancellationToken>());
            api.ClearReceivedCalls();api.PreviewVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(new VacationPreview(new(),"empty",new()));
            await vm.ReviewVacationRequestCommand.ExecuteAsync(null);Assert.Equal(2,confirms);await api.DidNotReceive().SubmitVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
        }
        finally{vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }

    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task VacationConfirmationUsesShiftCardsAndYearlyBalanceImpactBeforeSaving(bool light)
    {
        var (vm,api,_)=Create();var preview=new VacationPreview(new(){new(new DateOnly(2026,12,31),new DateTime(2026,12,31,7,0,0,DateTimeKind.Utc),new DateTime(2026,12,31,15,0,0,DateTimeKind.Utc),1),new(new DateOnly(2027,1,2),new DateTime(2027,1,2,7,0,0,DateTimeKind.Utc),new DateTime(2027,1,2,15,0,0,DateTimeKind.Utc),1)},"server-preview",new(){new(2026,8,0,0),new(2027,21,0,0)});
        api.VacationReadinessAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<CancellationToken>()).Returns(new VacationReadiness(true,null));api.PreviewVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(preview);
        vm.VacationStart=new DateTimeOffset(new DateTime(2026,12,31));vm.VacationEnd=new DateTimeOffset(new DateTime(2027,1,2));vm.VacationReason="Family time";
        var view=new WorkspaceView{DataContext=vm,ShowSubmissionMessageAsync=(_,_)=>Task.CompletedTask};var owner=new Window{Content=view,Width=1200,Height=850,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            vm.ShowVacationEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);
            var request=vm.ReviewVacationRequestCommand.ExecuteAsync(null);Dispatcher.UIThread.RunJobs();var review=Assert.Single(form.OwnedWindows.OfType<VacationReviewWindow>());
            Assert.Equal(2,review.GetVisualDescendants().OfType<Border>().Count(x=>x.Classes.Contains("vacation-shift-card")));
            var labels=review.GetVisualDescendants().OfType<TextBlock>().Select(x=>x.Text).ToArray();Assert.Contains("2 leave days",labels);Assert.Contains("8 days",labels);Assert.Contains("7 days",labels);Assert.Contains("21 days",labels);Assert.Contains("20 days",labels);Assert.Contains("Family time",labels);Assert.DoesNotContain("server-preview",labels);
            Save(review,$"vacation-review-cards-{(light?"light":"dark")}.png");review.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Back")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await request;await api.DidNotReceive().SubmitVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<string>(),Arg.Any<CancellationToken>());
            request=vm.ReviewVacationRequestCommand.ExecuteAsync(null);Dispatcher.UIThread.RunJobs();review=Assert.Single(form.OwnedWindows.OfType<VacationReviewWindow>());review.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="ConfirmVacationRequest").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await request;
            await api.Received(1).SubmitVacationRangeAsync(vm.Chat.CurrentUser.Id,new DateOnly(2026,12,31),new DateOnly(2027,1,2),"Family time","server-preview",Arg.Any<CancellationToken>());
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public void OrangeActionLabelsStayWhiteAndContrastInNormalHoverAndPressedStates(bool light)
    {
        var button=new Button{Content="Review request",Classes={"fire-btn"},Width=180,Height=48};var owner=new Window{Content=button,Width=240,Height=100,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            var presenter=button.GetVisualDescendants().OfType<ContentPresenter>().First();
            void White()=>Assert.Equal(Colors.White,Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Foreground).Color);
            White();var center=button.TranslatePoint(new Point(30,20),owner)!.Value;owner.MouseMove(center);Dispatcher.UIThread.RunJobs();White();owner.MouseDown(center,MouseButton.Left);Dispatcher.UIThread.RunJobs();White();owner.MouseUp(center,MouseButton.Left);
            var gradient=Assert.IsAssignableFrom<ILinearGradientBrush>(button.Background);
            static double Luminance(Color color){static double Linear(byte c){var v=c/255d;return v<=.04045?v/12.92:Math.Pow((v+.055)/1.055,2.4);}return .2126*Linear(color.R)+.7152*Linear(color.G)+.0722*Linear(color.B);}
            foreach(var stop in gradient.GradientStops)Assert.True(1.05/(Luminance(stop.Color)+.05)>=4.5);
        }
        finally{owner.Close();}
    }

}
