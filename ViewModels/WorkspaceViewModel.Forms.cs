using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace UserIdentityDesktop.ViewModels;

public sealed record FormValidationIssue(string Field, string Message);

public partial class WorkspaceViewModel
{
    [ObservableProperty] private FormValidationIssue? _formValidationIssue;
    private InvalidOperationException InvalidForm(string field,string message)
    { FormValidationIssue = new(field,message);return new InvalidOperationException(message); }
    private void ClearFormIssue(string field)
    {
        if (FormValidationIssue?.Field != field) return;
        if (Feedback == FormValidationIssue.Message) Feedback = "";
        FormValidationIssue = null;
    }
    partial void OnAnnouncementTitleChanged(string value) => ClearFormIssue(nameof(AnnouncementTitle));
    partial void OnAnnouncementBodyChanged(string value) => ClearFormIssue(nameof(AnnouncementBody));
    partial void OnAnnouncementCategoryChanged(string value) => ClearFormIssue(nameof(AnnouncementCategory));
    partial void OnExpiryDateChanged(DateTimeOffset? value) => ClearFormIssue(nameof(ExpiryDate));
    partial void OnExpiryTimeChanged(TimeSpan? value) => ClearFormIssue(nameof(ExpiryDate));
    partial void OnPublishDateChanged(DateTimeOffset? value) => ClearFormIssue(nameof(ExpiryDate));
    partial void OnPublishTimeChanged(TimeSpan? value) => ClearFormIssue(nameof(ExpiryDate));
    public string VacationPlanHelp => "Weekends, public holidays and existing reservations are excluded. Times shown in " + TimeZoneInfo.Local.Id + ".";
    public string FormTimeZone => "Times are shown in " + TimeZoneInfo.Local.DisplayName + ".";
    public bool CanSaveCalendarForm => Reviewed && (!HasCalendarConflicts || AcknowledgeConflicts);
    public bool HasCalendarConflicts => Conflicts.Count > 0;
    public string CalendarReviewHint => Reviewed
        ? HasCalendarConflicts ? "Availability reviewed. Acknowledge the overlaps before saving." : "Availability reviewed. Save schedule to finish."
        : "Review availability before saving. Changes to the schedule need a fresh review.";
    public string VacationReviewHint => VacationReview == null
        ? "Review request to check your shifts and balance before confirming."
        : "Review request to confirm the calculated shifts and balance.";
    public bool ScheduleAnnouncement
    {
        get => PublishDate != null;
        set => PublishDate = value ? PublishDate ?? DateTimeOffset.Now.Date : null;
    }
    public string AnnouncementSubmitLabel => ScheduleAnnouncement ? "Schedule announcement" : "Publish announcement";
    public string AnnouncementPublishSummary => ScheduleAnnouncement
        ? "Scheduled publication. You will review the message, timing and recipients before confirming."
        : "Publishes immediately after you review and confirm the message and recipients.";
    public string AnnouncementPreviewTitle => string.IsNullOrWhiteSpace(AnnouncementTitle) ? "Your announcement title" : AnnouncementTitle;
    public string AnnouncementPreviewBody => string.IsNullOrWhiteSpace(AnnouncementBody) ? "Write your message to see how it will read." : AnnouncementBody;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(FilteredAnnouncementAudience))] private string _announcementAudienceSearch = "";
    public PersonSelection[] FilteredAnnouncementAudience => Audience.Where(x => x.Name.Contains(AnnouncementAudienceSearch.Trim(), StringComparison.CurrentCultureIgnoreCase)).ToArray();
    public string AnnouncementAudienceSummary => $"{Audience.Count(x => x.Selected)} of {Audience.Count} recipients selected";
    public bool NoMatchingAnnouncementAudience => FilteredAnnouncementAudience.Length == 0;
    private readonly HashSet<PersonSelection> observedAudience = new();
    private void RefreshAudienceBindings()
    {
        var current = Audience.ToHashSet();
        foreach (var person in observedAudience.Where(x => !current.Contains(x)).ToArray())
        { person.PropertyChanged -= AudiencePersonChanged; observedAudience.Remove(person); }
        foreach (var person in Audience) if (observedAudience.Add(person)) person.PropertyChanged += AudiencePersonChanged;
        OnPropertyChanged(nameof(FilteredAnnouncementAudience));OnPropertyChanged(nameof(AnnouncementAudienceSummary));OnPropertyChanged(nameof(NoMatchingAnnouncementAudience));
    }
    partial void OnAnnouncementAudienceSearchChanged(string value) => OnPropertyChanged(nameof(NoMatchingAnnouncementAudience));
    private void AudiencePersonChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(PersonSelection.Selected)) { ClearFormIssue(nameof(Audience)); OnPropertyChanged(nameof(AnnouncementAudienceSummary)); } }
    [RelayCommand] private void SelectVisibleAnnouncementAudience() { foreach (var person in FilteredAnnouncementAudience) person.Selected = true; }
    [RelayCommand] private void ClearAnnouncementAudience() { foreach (var person in Audience) person.Selected = false; }
    public string[] CalendarKinds { get; } = ["Event", "Meeting"];
    public int CalendarKindIndex {get=>IsMeeting?1:0;set=>IsMeeting=value==1;}
    private DateTime? CalendarEndLocal
    {
        get
        {
            if(EventDate is not {} date || EventTime is not {} time || !int.TryParse(DurationMinutes,out var minutes) || minutes is < -10080 or > 10080)return null;
            var start=DateTime.SpecifyKind(date.Date.Add(time),DateTimeKind.Unspecified);
            if(TimeZoneInfo.Local.IsInvalidTime(start)||TimeZoneInfo.Local.IsAmbiguousTime(start))return null;
            try{return TimeZoneInfo.ConvertTimeFromUtc(TimeZoneInfo.ConvertTimeToUtc(start).AddMinutes(minutes),TimeZoneInfo.Local);}catch(ArgumentException){return null;}
        }
    }
    public DateTimeOffset? EventEndDate
    {
        get=>CalendarEndLocal is {} end?new DateTimeOffset(end.Date):null;
        set{if(value is {} date)SetCalendarEnd(date.Date+(CalendarEndLocal?.TimeOfDay ?? EventTime ?? TimeSpan.Zero));}
    }
    public TimeSpan? EventEndTime
    {
        get=>CalendarEndLocal?.TimeOfDay;
        set{if(value is {} time && (CalendarEndLocal?.Date ?? EventDate?.Date) is {} date)SetCalendarEnd(date+time);}
    }
    private void SetCalendarEnd(DateTime end)
    {
        if(EventDate is not {} date || EventTime is not {} time)return;
        var start=DateTime.SpecifyKind(date.Date+time,DateTimeKind.Unspecified);end=DateTime.SpecifyKind(end,DateTimeKind.Unspecified);
        if(TimeZoneInfo.Local.IsInvalidTime(start)||TimeZoneInfo.Local.IsAmbiguousTime(start)||TimeZoneInfo.Local.IsInvalidTime(end)||TimeZoneInfo.Local.IsAmbiguousTime(end))
        {DurationMinutes="0";FormValidationIssue=new(nameof(DurationMinutes),"This local time falls in a daylight-saving transition. Choose another time.");Feedback=FormValidationIssue.Message;return;}
        DurationMinutes=((int)(TimeZoneInfo.ConvertTimeToUtc(end)-TimeZoneInfo.ConvertTimeToUtc(start)).TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    private void NotifyCalendarEnd(){OnPropertyChanged(nameof(EventEndDate));OnPropertyChanged(nameof(EventEndTime));}
    private void InvalidateCalendarFormReview() { if (Reviewed) Feedback = ""; Reviewed = false; AcknowledgeConflicts = false; Conflicts.Clear(); hasConflicts = false; }
    partial void OnEventTitleChanged(string value) { ClearFormIssue(nameof(EventTitle)); InvalidateCalendarFormReview(); }
    partial void OnEventDescriptionChanged(string value) { ClearFormIssue(nameof(EventDescription)); InvalidateCalendarFormReview(); }
    partial void OnEventDateChanged(DateTimeOffset? value) { ClearFormIssue(nameof(EventDate)); InvalidateCalendarFormReview(); NotifyCalendarEnd(); }
    partial void OnEventTimeChanged(TimeSpan? value) { ClearFormIssue(nameof(EventTime)); InvalidateCalendarFormReview(); NotifyCalendarEnd(); }
    partial void OnDurationMinutesChanged(string value) { ClearFormIssue(nameof(DurationMinutes)); InvalidateCalendarFormReview(); NotifyCalendarEnd(); }
    partial void OnIsMeetingChanged(bool value) { ClearFormIssue(nameof(IsMeeting)); InvalidateCalendarFormReview(); OnPropertyChanged(nameof(CalendarKindIndex)); }
}
