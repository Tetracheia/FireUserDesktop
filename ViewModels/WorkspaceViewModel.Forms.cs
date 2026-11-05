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
    private void InvalidateCalendarFormReview() { if (Reviewed) Feedback = ""; Reviewed = false; AcknowledgeConflicts = false; Conflicts.Clear(); hasConflicts = false; }
    partial void OnEventTitleChanged(string value) { ClearFormIssue(nameof(EventTitle)); InvalidateCalendarFormReview(); }
    partial void OnEventDescriptionChanged(string value) { ClearFormIssue(nameof(EventDescription)); InvalidateCalendarFormReview(); }
    partial void OnEventDateChanged(DateTimeOffset? value) { ClearFormIssue(nameof(EventDate)); InvalidateCalendarFormReview(); }
    partial void OnEventTimeChanged(TimeSpan? value) { ClearFormIssue(nameof(EventTime)); InvalidateCalendarFormReview(); }
    partial void OnDurationMinutesChanged(string value) { ClearFormIssue(nameof(DurationMinutes)); InvalidateCalendarFormReview(); }
    partial void OnIsMeetingChanged(bool value) { ClearFormIssue(nameof(IsMeeting)); InvalidateCalendarFormReview(); }
}
