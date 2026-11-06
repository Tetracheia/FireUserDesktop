# Desktop presentation review — 6 November 2026

## Design contract

A calm, warm workspace: orange accents, Outfit, neutral light/dark surfaces, generous separation of tasks and compact but readable native controls. Keep the existing branded Avalonia presentation across Windows, Linux and macOS, with native windowing, file dialogs, tray, input and media integration. Do not emulate another OS or introduce a web runtime. Preserve view models, commands, validation, subscriptions and native services.

## Inventory and changes

| Area | Review and treatment |
| --- | --- |
| Main shell, login, navigation and people search | Shared 14px window baseline; centered search retained, narrower search in compact windows; existing 70px hover/focus rail and native chrome retained |
| Chat list/conversation/composer | List width follows available control width (248/300/336px); recipient title ellipsis; shared control/focus baseline; existing attachment, message and recent-first ordering retained |
| Calendar/meeting/vacation and announcement workflows | Shared typography, field/control targets and keyboard focus. Existing dedicated compose/form work in the working tree belongs to a separate ongoing change and is preserved |
| Profile, contacts, app/settings and system configuration links | Shared native styles, minimum input height, keyboard adorners and disabled appearance |
| Browser-only administration | User/group/application/scopes/rights/audit management remains in Angular; no additional native management screens or duplicated logic were added |
| Calls, sharing, participant/chat panels, attachment/media windows, review/confirmation windows and toasts | Native behavior retained; focus adorner/minimum control treatment applies with navigation/toast target exceptions |

`AdaptivePresentation` is a view-only window size class below 1040px. Compact cards/headings may adapt without adding view-model state or changing commands. Shared inputs have a 40px minimum, standard buttons 38px and square buttons 40px. Focus adorners follow Avalonia keyboard focus-visible behavior. Captions wrap with readable line height. Existing reduced-motion/theme services remain authoritative.

## Verification and visual gate

Release compilation and native bridge CTest were run on Linux. The full suite and focused real Avalonia rendering checks cover both themes, keyboard focus, compact-to-wide resizing and centered search. Screenshots captured from the rendered implementation are local test artifacts. Release build passed with no warnings/errors; all 5 native CTest checks passed. Final full managed suite: 545 passed, 6 skipped, 0 failed. The final run used `DOTNET_PROCESSOR_COUNT=2` to bound concurrency on this busy host. A pre-existing three-second shutdown timing check timed out once during simultaneous native builds, then passed in isolation and in the final full run; shutdown code was unchanged.

| Requirement | Evidence |
| --- | --- |
| Shared control sizing, both themes, focus and compact/wide resizing | `SharedControlsRemainReadableAndKeyboardAccessibleAsWindowResizes` |
| Usable chat list and composer at 800/1024/1600px | `ConversationResizeKeepsTheComposerAndBothPanesUsable` |
| Centered desktop search retained | `MainHeaderCentersPeopleSearchAndRemovesTheThemeShortcut` |

Existing working-tree compose/form changes were present during verification but are excluded from this change's commit.

Native Windows/macOS execution, per-monitor DPI changes, GNOME/KDE compositor differences, assistive-technology sessions and localized/RTL long-copy review remain unverified. No numerical reference-fidelity score or cross-platform release acceptance is claimed. Native platform and visual acceptance must be verified on those hosts before calling the complete multi-OS review finished.

Avalonia platform matrix checked: https://docs.avaloniaui.net/docs/supported-platforms
Focus behavior: https://docs.avaloniaui.net/docs/input-interaction/focus
