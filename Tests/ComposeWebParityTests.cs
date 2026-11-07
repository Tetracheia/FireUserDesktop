using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using NSubstitute;
using UserIdentityDesktop.Converters;
using UserIdentityDesktop.Core.Models;
using UserIdentityDesktop.ViewModels;
using UserIdentityDesktop.Views;
using Xunit;

namespace UserIdentityDesktop.Tests;

public sealed partial class WorkspaceInteractionTests
{
    [Theory]
    [InlineData("en-US",2026,11,7)][InlineData("en-GB",2026,11,7)][InlineData("ar-EG",2026,11,7)]
    [InlineData("en-US",2026,7,11)][InlineData("en-US",2028,2,29)]
    public void WebDateFormatRoundTripsWithoutSwappingDayAndMonth(string culture,int year,int month,int day)
    {
        var converter=new CalendarDisplayDateConverter{Format="dd-MM-yyyy"};var date=new DateTime(year,month,day);var locale=CultureInfo.GetCultureInfo(culture);
        var display=converter.Convert(date,typeof(string),null,locale);Assert.Equal(date,converter.ConvertBack(display,typeof(DateTime),null,locale));
    }
    [AvaloniaTheory]
    [InlineData(1391,false)][InlineData(1391,true)][InlineData(1920,false)][InlineData(480,false)]
    public async Task VacationMatchesWebReferenceBalanceAndDayCardGrid(int width,bool light)
    {
        var (vm,api,_)=Create();var clock=ClockDisplaySettings.Shared.TwelveHour;ClockDisplaySettings.Shared.TwelveHour=true;
        var shifts=new[]{8,9,10,11,12,15}.Select(day=>new VacationShift(new DateOnly(2026,11,day),new DateTime(2026,11,day,7,0,0,DateTimeKind.Utc),new DateTime(2026,11,day,15,0,0,DateTimeKind.Utc),1)).ToList();
        var preview=new VacationPreview(shifts,"reference-preview",new(){new(2026,35,0,0,41,0,6)});
        api.VacationReadinessAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<CancellationToken>()).Returns(new VacationReadiness(true,null));
        api.PreviewVacationRangeAsync(Arg.Any<Guid>(),Arg.Any<DateOnly>(),Arg.Any<DateOnly>(),Arg.Any<string>(),Arg.Any<CancellationToken>()).Returns(preview);
        vm.VacationStart=new DateTimeOffset(new DateTime(2026,11,7));vm.VacationEnd=new DateTimeOffset(new DateTime(2026,11,15));
        var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1400,Height=1000,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            vm.ShowVacationEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);form.Width=width;form.Height=766;Dispatcher.UIThread.RunJobs();
            var panels=form.GetVisualDescendants().OfType<ComposeAutoGrid>().ToArray();var balances=panels.Single(x=>x.Children.Count==5);var days=panels.Single(x=>x.Children.Count==6);
            var expectedColumns=width>=1391?5:1;
            Assert.Equal(expectedColumns,days.Children.Count(x=>Math.Abs(x.Bounds.Top-days.Children[0].Bounds.Top)<1));
            Assert.True(days.Children[5].Bounds.Top>days.Children[0].Bounds.Top);Assert.InRange(Math.Abs(days.Children[0].Bounds.Width-days.Children[5].Bounds.Width),0,.1);
            if(width>=1391){Assert.All(balances.Children,x=>Assert.InRange(x.Bounds.Height,89,94));Assert.All(balances.Children,x=>Assert.InRange(Math.Abs(x.Bounds.Width-balances.Children[0].Bounds.Width),0,.1));}
            Assert.Equal(6,form.GetVisualDescendants().OfType<Border>().Count(x=>x.Classes.Contains("vacation-day-card")));
            var primary=form.GetVisualDescendants().OfType<Button>().Single(x=>x.Name=="FormSubmit");Assert.Contains(primary.GetVisualAncestors().OfType<Border>(),x=>x.Name=="VacationRequestCard");
            Assert.Equal(light?Color.Parse("#FFFFFF"):Color.Parse("#1F2125"),Assert.IsAssignableFrom<ISolidColorBrush>(form.GetVisualDescendants().OfType<Border>().Single(x=>x.Name=="VacationRequestCard").Background).Color);
            Save(form,$"web-compose-vacation-{width}-{(light?"light":"dark")}.png");
            ((ScrollViewer)form.Editor).ScrollToEnd();Dispatcher.UIThread.RunJobs();Save(form,$"web-compose-vacation-{width}-{(light?"light":"dark")}-actions.png");await form.CloseAnimatedAsync();
        }
        finally{owner.Close();ClockDisplaySettings.Shared.TwelveHour=clock;vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory]
    [InlineData(WorkspaceFormKind.Calendar,false)][InlineData(WorkspaceFormKind.Calendar,true)]
    [InlineData(WorkspaceFormKind.Announcement,false)][InlineData(WorkspaceFormKind.Announcement,true)]
    public async Task AllComposeFlowsUseSingleWebCardAndInlineActionsWithoutReopenDuplicates(WorkspaceFormKind kind,bool light)
    {
        var (vm,_,_)=Create();vm.EventTitle="Design review";vm.EventDescription="Review the latest screens.";vm.AnnouncementTitle="Team update";vm.AnnouncementBody="The design review starts next week.";
        var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view,Width=1400,Height=1000,RequestedThemeVariant=light?ThemeVariant.Light:ThemeVariant.Dark};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            for(var attempt=0;attempt<2;attempt++)
            {
                if(kind==WorkspaceFormKind.Calendar)vm.ShowCalendarEditor=true;else vm.ShowAnnouncementEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);
                var card=Assert.Single(form.GetVisualDescendants().OfType<Border>(),x=>x.Classes.Contains("compose-card"));
                var submit=Assert.Single(form.GetVisualDescendants().OfType<Button>(),x=>x.Name=="FormSubmit");Assert.Contains(card,submit.GetVisualAncestors());
                Assert.All(form.GetVisualDescendants().OfType<CheckBox>(),check=>Assert.Null(check.ContentTemplate));
                Assert.Equal(2,form.GetVisualDescendants().OfType<ComposeDateTimePicker>().Count());Assert.DoesNotContain(form.GetVisualDescendants().OfType<TextBlock>(),x=>x.Text?.StartsWith("01  ")==true);
                if(attempt==0)Save(form,$"web-compose-{kind}-{(light?"light":"dark")}.png");await form.CloseAnimatedAsync();
            }
            Assert.Equal("Design review",vm.EventTitle);Assert.Equal("Team update",vm.AnnouncementTitle);
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaTheory][InlineData(false)][InlineData(true)]
    public async Task CombinedDateTimePopupRetainsSelectionUntilDoneAndCanClearScheduledPublication(bool twelveHour)
    {
        var (vm,_,_)=Create();var original=ClockDisplaySettings.Shared.TwelveHour;ClockDisplaySettings.Shared.TwelveHour=twelveHour;
        var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            vm.ShowCalendarEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);var starts=form.GetVisualDescendants().OfType<ComposeDateTimePicker>().Single(x=>Equals(x.Tag,"EventDate"));
            starts.IsOpen=true;Dispatcher.UIThread.RunJobs();starts.Calendar.SelectedDate=new DateTime(2027,2,8);Dispatcher.UIThread.RunJobs();Assert.True(starts.IsOpen);Assert.Equal(new DateTime(2027,2,8),vm.EventDate!.Value.Date);
            var popup=starts.GetVisualDescendants().OfType<Popup>().Single();var time=popup.Child!.GetVisualDescendants().OfType<TimePicker>().Single();time.SelectedTime=new TimeSpan(17,30,0);Dispatcher.UIThread.RunJobs();Assert.Equal(new TimeSpan(17,30,0),vm.EventTime);Assert.Equal(twelveHour?"12HourClock":"24HourClock",time.ClockIdentifier);
            popup.Child.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Done")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.False(starts.IsOpen);Assert.True(form.IsVisible);await form.CloseAnimatedAsync();
            vm.ScheduleAnnouncement=true;vm.ShowAnnouncementEditor=true;Dispatcher.UIThread.RunJobs();form=Assert.Single(view.EditorWindows);var publication=form.GetVisualDescendants().OfType<ComposeDateTimePicker>().Single(x=>x.Clearable);publication.IsOpen=true;Dispatcher.UIThread.RunJobs();
            popup=publication.GetVisualDescendants().OfType<Popup>().Single();popup.Child!.GetVisualDescendants().OfType<Button>().Single(x=>Equals(x.Content,"Clear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.Null(vm.PublishDate);Assert.False(vm.ScheduleAnnouncement);Assert.False(publication.IsOpen);await form.CloseAnimatedAsync();
        }
        finally{owner.Close();ClockDisplaySettings.Shared.TwelveHour=original;vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task EndDateFieldsPreserveTheExistingDurationGuardAndInvalidateReview()
    {
        var (vm,api,_)=Create();vm.EventDate=new DateTimeOffset(new DateTime(2027,2,8));vm.EventTime=new TimeSpan(9,0,0);vm.DurationMinutes="60";vm.EventTitle="Planning";
        try
        {
            vm.EventEndDate=new DateTimeOffset(new DateTime(2027,2,9));vm.EventEndTime=new TimeSpan(17,0,0);Assert.Equal("1920",vm.DurationMinutes);
            vm.Reviewed=true;vm.EventEndTime=new TimeSpan(16,0,0);Assert.False(vm.Reviewed);Assert.Equal("1860",vm.DurationMinutes);
            vm.EventEndDate=vm.EventDate;vm.EventEndTime=new TimeSpan(8,0,0);await vm.ReviewEventCommand.ExecuteAsync(null);Assert.Equal(nameof(vm.DurationMinutes),vm.FormValidationIssue!.Field);
            await api.DidNotReceive().ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>());
            vm.DurationMinutes=int.MaxValue.ToString();Assert.Null(vm.EventEndDate);vm.EventEndDate=vm.EventDate;vm.EventEndTime=new TimeSpan(10,0,0);Assert.Equal("60",vm.DurationMinutes);
        }
        finally{vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }
    [AvaloniaFact]
    public async Task EndDateFieldsMatchTheSavedUtcInstantAcrossLocalClockTransitions()
    {
        var (vm,api,_)=Create();var start=new DateTime(2026,10,29,12,0,0);var end=new DateTime(2026,10,30,12,0,0);
        vm.EventDate=new DateTimeOffset(start.Date);vm.EventTime=start.TimeOfDay;vm.DurationMinutes="60";vm.EventTitle="Transition review";vm.IsMeeting=false;
        api.ReviewAsync(Arg.Any<CalendarWrite>(),Arg.Any<CancellationToken>()).Returns(new CalendarReview(new(),"review"));
        try
        {
            vm.EventEndDate=new DateTimeOffset(end.Date);vm.EventEndTime=end.TimeOfDay;
            var startUtc=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(start,DateTimeKind.Unspecified));var endUtc=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(end,DateTimeKind.Unspecified));
            Assert.Equal(((int)(endUtc-startUtc).TotalMinutes).ToString(CultureInfo.InvariantCulture),vm.DurationMinutes);Assert.Equal(end.Date,vm.EventEndDate!.Value.Date);Assert.Equal(end.TimeOfDay,vm.EventEndTime);
            await vm.ReviewEventCommand.ExecuteAsync(null);await api.Received(1).ReviewAsync(Arg.Is<CalendarWrite>(x=>x.StartUtc==startUtc&&x.EndUtc==endUtc),Arg.Any<CancellationToken>());
        }
        finally{vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }

    [AvaloniaFact]
    public async Task ComposeFormsFollowSharedAppearanceChangesWhileOpen()
    {
        var (vm,_,_)=Create();var view=new WorkspaceView{DataContext=vm};var owner=new Window{Content=view};owner.Show();Dispatcher.UIThread.RunJobs();
        try
        {
            vm.ShowCalendarEditor=true;Dispatcher.UIThread.RunJobs();var form=Assert.Single(view.EditorWindows);vm.Appearance.BeginAdjusting();vm.Appearance.Brightness=32;Dispatcher.UIThread.RunJobs();
            Assert.Equal(NativeAppearance.Palette(32)["ForgeBg"],Assert.IsAssignableFrom<ISolidColorBrush>(form.Background).Color);
            vm.Appearance.Brightness=100;Dispatcher.UIThread.RunJobs();Assert.Equal(ThemeVariant.Light,form.ActualThemeVariant);Assert.Equal(NativeAppearance.Palette(100)["ForgeTextPrimary"],Assert.IsAssignableFrom<ISolidColorBrush>(form.Foreground).Color);await form.CloseAnimatedAsync();
        }
        finally{owner.Close();vm.Chat.UnregisterEvents();await vm.DisposeAsync();}
    }

}
