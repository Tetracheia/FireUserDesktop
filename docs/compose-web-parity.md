# Compose presentation shared with the Angular app

The approved direction is the supplied web vacation screenshot (6 November 2026), followed by the Angular workplace and calendar-picker source. Native desktop controls reproduce the web's charcoal/ash surfaces, Outfit typography, ember accents, rounded fields, equal balance cards and responsive day-card grid. This is one custom presentation across the desktop targets, with native windowing, focus and input. The previously requested white action labels remain in place.

Scope is the existing vacation, calendar event/meeting, and announcement compose flows, plus their review and attendee windows. Source references are `src/app/workplace/workplace.component.html`, `workplace.component.scss`, `shared/calendar-picker` and shared workplace controls in the sibling frontend. Existing unsupported recurrence/task types and broader announcement target modes remain capability gaps; this UI change does not claim new API/domain parity or expose inactive controls for those features.

## Layout and behavior

- Vacation: five equally distributed balance cards (130px minimum, 12px gaps), enclosing request card, heading/balance-year header, paired dates, leave-plan inset, day cards (220px minimum, 10px gaps), balance impact, optional reason and inline actions. The day area may grow to 340px before scrolling. It no longer shows only two rows of a narrow 116px list.
- Calendar/meeting: one card, Type/Title and Starts/Ends rows, description and involved employees. The existing exact-draft availability/conflict review remains authoritative. Ends maps to the existing UTC duration contract, including local clock transitions; invalid/ambiguous transition times cannot be submitted.
- Announcement: one card, Title/Category, Content, paired Publish/Expiry controls, available recipient selection and delivery flags. Clearing publication returns to immediate publication. Recipients retained by search remain selected.
- Compose controls: the date trigger has a leading calendar glyph, `dd-MM-yyyy` text and trailing chevron. Date/time popovers retain the selected day while time is edited, then close with Done. Day/month parsing is exact before culture fallback, preventing `07-11-2026` from being reinterpreted as July 11.
- Actions sit at the end of the form inside its card, as on the web. Compact windows scroll to them. A content width limit of 1400px prevents maximized windows from stretching fields indefinitely. Calendar/announcement cards keep their intrinsic height rather than stretching to fill unused window height.
- Shared compose styling also applies to native review/attendee dialogs. Generated desktop-only button icons and numbered section headings are removed from these surfaces. Existing draft retention, local-date conversion, Escape, keyboard submission, validation recovery, modal ownership and reduced motion remain in place.
- Open compose windows continue to observe the shared appearance preference. Intermediate brightness uses the existing `NativeAppearance.Palette` calculation; changing appearance does not require closing the form. Window subscriptions are removed on close.

Native template contracts were checked against [Avalonia 12.1.2 CalendarDatePicker](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Themes.Fluent/Controls/CalendarDatePicker.xaml) and [ScrollViewer](https://github.com/AvaloniaUI/Avalonia/blob/12.1.2/src/Avalonia.Themes.Fluent/Controls/ScrollViewer.xaml). CalendarDatePicker's required native parts and popup/date binding are retained. Auto-hide is disabled only for the outer compose scroller so its gutter behaves like the web page scrollbar. The combined field composes a native Calendar and TimePicker without a browser runtime.

## Requirement evidence

| Requirement | Tests |
| --- | --- |
| Five equal balance cards and wrapping leave days at reference, wide and compact widths; both appearances | `VacationMatchesWebReferenceBalanceAndDayCardGrid` |
| All existing compose flows share one card and inline actions; reopening keeps exactly one action set | `AllComposeFlowsUseSingleWebCardAndInlineActionsWithoutReopenDuplicates` |
| Shared date/time selection, Done and clearing publication; 12/24-hour settings | `CombinedDateTimePopupRetainsSelectionUntilDoneAndCanClearScheduledPublication` |
| Date formatting cannot swap month/day across cultures, including leap day | `WebDateFormatRoundTripsWithoutSwappingDayAndMonth` |
| End fields preserve validation/review and represent the actual saved UTC instant | `EndDateFieldsPreserveTheExistingDurationGuardAndInvalidateReview`, `EndDateFieldsMatchTheSavedUtcInstantAcrossLocalClockTransitions` |
| In-flight appearance changes | `ComposeFormsFollowSharedAppearanceChangesWhileOpen` |
| Generated-icon opt-out preserves explicit templates and button content | `TurningOffGeneratedButtonIconsRemovesOnlyTheAutomaticTemplate` |
| Compact keyboard/recovery flows and existing successful submissions | `SubmissionFormsStackFieldsAndKeepActionsVisibleInCompactWindows`, `ValidationAppearsBesideTheFieldAndCanFocusItWithoutLosingTheDraft`, `AllCreationSurfaces_OpenOwnedWindows_AndShowSuccessAfterSavingAndRefresh` |

The uploaded images and new captures are preserved under ignored `artifacts/compose-web-parity`. Fixtures match the supplied web example: 7–15 November 2026, six working shifts, entitlement 41, taken 6 and available 35. These are synthetic verification values; production values still come from the existing APIs. Captures use Outfit at 1x scale, `TZ=Africa/Cairo` and the 12-hour preference for the supplied reference case.

The reference is a macOS browser screenshot; the available runtime is Linux/Avalonia Skia. The macOS dock and browser/OS chrome are external to the application and are not recreated or scored. The dock obscures part of the reason field in the supplied reference; action/footer and popup states require separate reference captures before their exact visual fidelity can be certified. Light mode, other compose views and unavailable target-platform variants retain separate pending visual cases. A passing review of the visible vacation reference region must not be presented as a passing 95/100 gate for every compose/platform state.

## Reproduction

Run the normal Release build and desktop test project, setting `FIREUSER_TEST_ARTIFACTS` for captures and `TZ=Africa/Cairo` for the reference fixture. The current build host lacked X11 development packages. Official Fedora repository development RPMs were downloaded and extracted into `/tmp/fireuser-native-dev-root`; `CMAKE_INCLUDE_PATH` and `CMAKE_LIBRARY_PATH` point there during verification. No system packages, permissions, repository build targets or runtime environment configuration were changed. Normal native building and its five synthetic native tests still run automatically.

Final verification: **Release build passed with 0 warnings and 0 errors**; **537 desktop tests passed, 0 failed, 6 skipped**; **5 automatic native tests passed**. The native startup smoke test opened and closed a real Linux window using an isolated temporary profile. No real media capture or remote input was exercised. The supplied visible vacation reference region was reviewed with the fixed rubric (96.15/100, rubric judgment rather than pixel similarity). The aggregate visual gate remains **unverified** for the separate pending states/platforms listed in `artifacts/compose-web-parity/pending-cases.json`.
