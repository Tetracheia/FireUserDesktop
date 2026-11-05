# Submission forms, fiery calendar pickers and focus

The calendar, vacation and announcement submission windows use the shared Fire User theme: Outfit, warm ash/white in light mode, charcoal/obsidian in dark mode, ember-orange and amber accents. The form header explains the task. Calendar and announcement cards group fields, persistent labels identify required values, help text explains policy and timing, and a wrapping footer keeps the next action accessible at short heights. Field pairs stack below 680 logical pixels. The original owned-window lifecycle and backend contracts remain in place.

Calendar sections cover details, timing, then people/availability. Save stays disabled until the exact draft is reviewed and any conflicts are acknowledged. Editing the title, description, date, time, duration, meeting mode or attendee selection invalidates that review. Vacation follows Angular’s `workplace.component.html` vacation request card: balance tiles, “Plan your time off”, paired date pickers, the calculated leave plan with per-year balance impact, then an optional reason. “Review request” obtains a fresh server preview and opens confirmation; cancelling never submits. Empty working-shift previews cannot submit. The server continues to calculate leave and determine approval. Announcement sections cover message, searchable audience, publication/expiry and delivery, with a receiver-style preview. “Select visible recipients” affects only matching recipients and preserves hidden selections; the selected total remains visible. The scheduling switch explicitly distinguishes immediate publication from a scheduled date.

Validation uses the existing authoritative checks in the ViewModel. A failed guard identifies its field, shows an inline message and retains the draft. The fixed status panel has a keyboard-accessible “Go to field” action. Remote/server errors without a field remain in the summary. Enter adds a newline in multiline descriptions/messages; single-line Enter retains the review/submit flow. Ctrl+Enter (Cmd+Enter on macOS) invokes that form's existing submit/review command. Escape dismisses an open calendar before closing the form.

## Date picker

All five submission date fields use Avalonia `CalendarDatePicker`, with a readable single date and a month popup instead of segmented month/day/year selectors. Keyboard month/year navigation and single-date selection remain native control behavior. Dates convert as **local calendar days**, never through UTC; the existing event-time/DST checks remain authoritative. ISO `yyyy-MM-dd` and current-culture typed dates are supported. An invalid typed date is reverted by the native picker, with visible feedback explaining that the previous date was kept.

The visual reference is the sibling Angular `src/app/shared/calendar-picker/calendar-picker.component.scss` and shared palette endpoints in `src/app/services/darkmode/theme-palette.ts`. The popup uses a rounded surface, 14px inset, rounded day cells, orange gradient selection, orange hover/focus/today indicators and left/right navigation. Native month/year selection is retained. Desktop day cells are 40px rather than Angular's 34px; white text on a deeper orange selected fill and stronger muted text are intentional contrast adaptations. This is a native Avalonia interaction styled like Fire User, not an Angular view embedded in the desktop.

API/template sources checked against Avalonia 12.1.2: [CalendarDatePicker](https://docs.avaloniaui.net/controls/input/date-and-time/calendardatepicker), [Calendar source](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Controls/Calendar/Calendar.cs), [Fluent calendar templates](https://github.com/AvaloniaUI/Avalonia/tree/12.1.2/src/Avalonia.Themes.Fluent/Controls). Both required calendar parts are preserved: `PART_Root` is a Panel whose first child is `PART_CalendarItem`; missing that structure disables native selection and navigation.

UI/UX Pro Max's focused UX guidance was used for grouping, helper text, error recovery and validation. Its Avalonia forms query had no specific database match; native implementation choices follow the Avalonia plugin's form and control guidance.

## Focus and motion

Page changes use the existing 240ms Fire User cubic-bezier(.2,.8,.2,1) curve, with a small directional slide and a readable entry fade. Dialog/menu/flyout entry is 180ms; ordinary exits are 120ms with acceleration. Composer-specific mode animations retain their separate lifecycle. Only the current live page animates; outgoing pages do not remain interactive. Cancellation restores original transform, opacity and origin. Rapid changes replace older animation work, and repeated close requests share the same completion.

F6/Shift+F6 moves between the navigation rail and current page. Focus is remembered per live page through weak references. Queued navigation rechecks focus before restoring it, so it cannot override a newer click or focus in search. By the owner’s explicit preference, the rail starts collapsed and expands only while hovered; keyboard focus alone does not expand it. Accessible button names, tooltips, focus adorners and F6 navigation remain available. Call Logs appears directly after Chat while retaining its existing tab index 6. Orange keyboard focus adorners render outside control bounds without changing layout.

The Reduce motion setting and `FIREUSER_REDUCED_MOTION=1` propagate to owned windows and popup styling. Enabling reduced motion cancels active page/dialog motion and removes transitions immediately. Native popup dismissal and modal behavior stay authoritative.

## Verification and limits

Reproduce from this repository:

```sh
dotnet build UserIdentityDesktop.csproj -c Release
dotnet test Tests/UserIdentityDesktop.Tests.csproj -c Release
FIREUSER_TEST_ARTIFACTS=/absolute/path/to/evidence dotnet test Tests/UserIdentityDesktop.Tests.csproj -c Release --filter 'FullyQualifiedName~WorkspaceInteractionTests'
dotnet run --project UserIdentityDesktop.csproj -c Release
```

The artifact run saves actual Avalonia/Skia renders of all three forms, compact light/dark layouts, validation, the fiery calendar popup and page transition frame requests. `page-transition-timings.json` records actual capture times; screenshot encoding and headless job scheduling can exceed the requested sampling times. Those frames are not a desktop frame-rate benchmark.

Behavior evidence:

| Requirement | Regression evidence |
| --- | --- |
| Calendar popup, fiery selected day, ISO entry, local day and Escape | `CalendarPopupUsesFieryDaysAndLocalDateBindingsInsteadOfSegmentedSelectors` |
| All three forms at compact light/dark sizes, footer access | `SubmissionFormsStackFieldsAndKeepActionsVisibleInCompactWindows` |
| Exact-draft review and conflict acknowledgement | `ScheduleReviewInvalidatesAfterEditsAndConflictsRequireAcknowledgement` |
| Audience filtering and explicit publication mode | `AudienceSearchKeepsHiddenSelectionsAndPublicationModeIsExplicit` |
| Inline errors, recovery focus and retained values | `ValidationAppearsBesideTheFieldAndCanFocusItWithoutLosingTheDraft` |
| Multiline editing and single-line submission | `MultilineEnterDoesNotSubmitButSingleLineEnterStillReviewsTheForm` |
| Immediate reduced-motion changes across windows | `ReducedMotionChangeImmediatelyCancelsPagesAndOwnedWindows` |
| Rapid navigation, cancellation, preserved visual state | `RapidNavigationAndDisablingMotionRestoreExistingVisualState`, `ShellSwapsCancelOldSurfaceAndKeepTheNewPageInteractive` |
| F6, remembered focus and newer user intent | `F6RestoresPageFocusAndNavigationDoesNotStealExternalSearchFocus`, `Workspace_RendersEveryTab_InBothThemes` |
| Layout-stable frame sequence and safe close completion | `PageTransitionRendersAtRecordedTimesWithoutChangingLayoutBounds`, `RepeatedWindowCloseRequestsShareCompletionAndReducedMotionCanFinishTheExit` |

Linux headless renders were inspected for typography, layout, contrast, control bounds and state styling. Native Linux borderless geometry was independently exercised with synthetic media; see `fullscreen-calls-and-control.md`. Windows/macOS runtime behavior, screen readers, high-DPI/RTL variants, physical devices and authenticated multi-client media/control still require target-host validation.

The desktop skill's mandatory reference fidelity threshold remains **unverified**: no approved reference images exist for every screen/state/platform variant. No 95/100 score has been invented or inferred from compilation or test success. Pending visual cases include normal/compact light/dark forms, picker month/year/keyboard states, fullscreen source/control states and navigation variants on all supported targets. See the local evidence index under `artifacts/fullscreen-review` when generated.

Final available-host verification (2026-11-04): release build succeeded with zero warnings/errors; desktop tests **505 passed, 0 failed, 6 skipped (511 total)**; five automatic native build tests passed. The six skips cover Windows-only and authenticated network cases. The local evidence index includes raw build/test logs, image hashes, requested/actual transition capture times, palette role measurements, the synthetic native geometry harness and the pending visual-case manifest. No application publish or deployment was performed.

## Follow-up corrections, 5 November 2026

Vacation request rows show the resolved employee name, status, days, reason and dates; internal user/request GUIDs are not rendered. The Angular comparison source is `src/app/workplace/workplace.component.html` (vacation form and balance tiles) and its `.scss` (`.vacation-date-grid`, `.leave-plan`, `.balance-grid`). The desktop retains an owned native form window and native fiery date popup.

| Requirement | Evidence |
| --- | --- |
| Hover-only collapsed rail and Call Logs immediately after Chat, with original route index | `SidebarStartsCollapsedAndOnlyHoverExpandsWithCallLogsAfterChat` |
| Angular form structure and no visible internal vacation GUIDs in both themes | `VacationMatchesAngularPlanAndHidesInternalIds` |
| Fresh preview, one confirmation, cancellation and empty-shift refusal | `VacationReviewRequestConfirmsOnceAndNeverSubmitsOnCancelOrEmptyShifts` |

Local review artifacts are excluded from default MSBuild item discovery so saved native harness sources and their generated assembly metadata cannot break normal application builds.

The vacation window uses a compact 680px initial height, one task heading, horizontal balance tiles, 12px section gaps, a 72px minimum reason field and a shift list capped at 116px. Detailed shifts scroll within the plan; the shared sticky footer contains only Cancel and Review request. This removes the repeated heading, numbered sections and oversized nested cards from the earlier form. Both compact window themes are rendered by `SubmissionFormsStackFieldsAndKeepActionsVisibleInCompactWindows`; the populated compact vacation view is captured by `VacationMatchesAngularPlanAndHidesInternalIds`.

Follow-up verification: full suite **509 passed / 0 failed / 6 skipped**. After the final empty-prerequisite visibility guard, the focused `WorkspaceInteractionTests` suite passed **28/28**. Both populated 760×680 vacation renders were inspected; the full reason field is visible above the footer, checked in `VacationMatchesAngularPlanAndHidesInternalIds`. Updated captures and logs are in ignored `artifacts/ui-corrections/`. Windows-only and authenticated two-client transport tests remain skipped on this host.

The final vacation confirmation now uses an owned `VacationReviewWindow` instead of the generic plain-text review. It shows the selected date range, total leave days, per-year available/after balances, optional reason and individual day cards with local shift hours. The current server preview supplies all values. Back/Escape returns to the draft without a mutation; only Confirm vacation request submits its preview token. `VacationConfirmationUsesShiftCardsAndYearlyBalanceImpactBeforeSaving` exercises the actual form-to-dialog path, both themes, a cross-year request, cancellation and final submission. The dialog uses the owner’s theme and reduced-motion context.

Latest verification: Release build **0 warnings / 0 errors**; full desktop suite **518 passed / 0 failed / 6 skipped**. After compacting the vacation balance cards into two columns, the focused form/motion suite passed **32/32**. Both themes were rendered and inspected; updated artifacts are in ignored `artifacts/call-chat-review/`. White action label contrast across the measured opaque normal/hover/pressed fills is at least **5.35:1**. Native window startup is checked separately; live browser-to-native camera and other target OS tests remain unverified.
