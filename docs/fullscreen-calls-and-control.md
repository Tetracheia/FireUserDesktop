# Fullscreen calls and shared-device control

## Design contract

Fire User Connect is a collaboration workspace for chat, meetings, schedules and announcements. This refinement prioritizes calls, selecting camera/screen sources, consent and input ownership. The theme is warm ash in light appearance and obsidian with ember-orange and amber accents in dark appearance. Retain the sibling Angular/mobile endpoint palettes, Outfit typography, compact readable controls and short reduced-motion-aware transitions. At the owner’s request, action labels use white on a deeper ember gradient, with consistent white hover/pressed states. Bright orange and amber remain the accent palette; secondary captions have stronger contrast.

Use one custom Fire User presentation across Windows, macOS and Linux, with Avalonia controls and OS window behavior. This is a shared brand treatment over Fluent controls, not a claim of native WinUI/AppKit/libadwaita controls. Keep ViewModels and media/service logic shared; window placement, focus and native input remain presentation/platform adapters. Avalonia 12.1.2 / .NET 10 is retained. Official platform guidance checked 2026-11-04: https://docs.avaloniaui.net/docs/supported-platforms and https://docs.avaloniaui.net/docs/app-development/window-management. This host is Linux KDE Wayland with the existing X11 Avalonia backend; Windows/macOS require target-host verification.

## Fullscreen experience

F11 and the Full screen button work for the gallery, a camera or a shared screen. The header source picker switches among all current camera and screen tracks; Gallery shows all participants. New incoming shares do not replace an existing focus. Fullscreen keeps call identity/duration, a persistent wrapping toolbar, devices, microphone/camera/screen actions, chat, members, leave and exit. Connection recovery and feedback remain visible. The toolbar is outside the video and scrolls at short heights; it never depends on hover. Narrow windows show one auxiliary panel at a time. Escape exits viewing; Ctrl+Alt+Escape remains emergency control release.

Linux preserves the normal-window workaround for the earlier fullscreen recursion crash. A normal borderless window fills **WorkingArea**, excluding reserved dock/panel space, rather than requesting physical monitor Bounds that the compositor may clamp. Keep resizing enabled while the window manager applies the requested size: disabling it immediately can lock the previous client size, even when Width/Height show the requested dimensions. Refit after decoration changes and on screen configuration changes, converting pixels by each screen's scale. Restore original normal bounds, limits, decorations, resize policy and maximized state on exit. Windows/macOS retain native fullscreen. An auto-hiding overlay dock that reserves no work area remains compositor-controlled; real dock/compositor behavior must be tested on the target system.

## Consent sessions and input ownership

The existing version-1 envelope gains an optional `multiControl` capability. Only clients advertising it participate in click-to-claim ownership. Existing clients keep exclusive behavior; this change does not upgrade browser/mobile clients automatically.

Each host approves each participant separately for at most five minutes. A host can hold up to 16 approvals, but exactly one participant owns input at a time. New negotiated sessions begin passive: the shared view is dimmed, with a click-to-control hint. The first mouse press asks for ownership and is not injected as an OS click. Ownership updates are authenticated to the LiveKit sender, bound to the exact share scope and unique per-peer grant, and carry an increasing ownership epoch. Previous keys/buttons are released before ownership acknowledgement; delayed input and clipboard operations from earlier ownership epochs are refused. Pointer/reliable sequence fences and per-peer rate limits remain enforced.

A participant may retain approvals for several different shared devices in one call. Selecting another source yields the old device and selects the new approved session without discarding other approvals. Selecting a camera/gallery stops local routing. The keyboard hook operates only while the active controlled video owns keyboard focus; toolbar clicks and keys remain local. Clipboard authority is confined to the selected active owner and existing consent-bound transfer behavior.

The Control sessions menu lists approved participants, input ownership and a per-participant end action. Stop control revokes this client's outgoing sessions and hosted approvals. Share termination, departure, expiry, disconnect and errors invalidate the applicable grants and release held input. An unrelated participant leaving does not revoke other approvals. A changed share requires fresh approval. No unattended access or automatic OS permission bypass is introduced.

## Acceptance evidence

Expected visual cases: fullscreen gallery, selected camera, selected share, passive approved share, active control and recovery/feedback; light/dark; normal and compact viewport; each supported OS. Use deterministic synthetic frames and `FIREUSER_TEST_ARTIFACTS` for headless Avalonia screenshots. Headless captures verify layout/rendering, not native window-manager behavior. The mandatory 95/100 reference fidelity gate remains **unverified** until agreed reference images and target-platform captures are compared; do not infer that score from tests or compilation. No real input injection or real camera/screen capture is part of ordinary tests.

## Recorded Linux geometry

The native KDE/X11-window check reproduced the original failure: Width/Height requested 1920×1036 while ClientSize remained 1000×700. Keeping resize capability enabled during the asynchronous WM resize fixed the actual client area, not only the requested properties. Restoring position is deferred until the smaller client resize has completed, so the window manager cannot clamp a move against the old screen-sized frame.

Final native check on the available host: screen 1920×1080, work area (0,44) 1920×1036, borderless client 1920×1036 at (0,44). Exit restored client 1000×700 at its original (460,212), normal decorations and resize policy. These measurements concern this compositor configuration; auto-hide overlay docks and other target systems remain explicit validation cases.

Regression evidence: `FullscreenSwitchesEverySourceAndGalleryWithoutExitingAndKeepsActionsInsideViewport`, `ApprovedSessionsRemainDimmedUntilOwnershipAndToolbarClicksNeverReachRemoteDevice`, `BorderlessWorkAreaExcludesDockAndPreservesMonitorOrigin`, `LinuxBorderlessShareFitsWorkAreaAndRestoresWindow`, `BorderlessExitRestoresFixedWindowAndQueuedFitCannotResizeItAgain`, `EmergencyStopCannotBeReenabledByQueuedUnselectedSessions`. Policy-level multi-peer/device tests live in `MultiRemoteControlTests`, including passive approval, takeover/replay rejection, individual expiry, parallel requests, independent targets, clipboard ownership, capability downgrade and late acknowledgement after focus changes. These simulations do not replace an authenticated multi-client integration run.

The final native check also exercised an originally non-resizable window. Both normal and fixed-window cases filled the same 1920×1036 work area and restored the original client size/location; the fixed window restored `CanResize=false` only after its client resize completed. Stop/revoke bypasses busy camera/device command gating and immediately suspends local outgoing input; `StopControlRemainsAvailableWhileMediaIsBusyAndBlocksInputImmediately` covers that release path.

Final desktop validation: release build succeeded; **505 passed, 0 failed, 6 skipped (511 tests)**. Native build tests: five passed. Logs and the native normal/fixed restore measurements are retained locally in `artifacts/fullscreen-review`.

## Call chat and camera lifecycle follow-up

The call sidebar and main conversation now use the same native `ChatMessageCard`: sender name (or “You”), local date/time, sent/seen indicators, replies, edits, reactions, attachment bundles and message actions. Missing sender metadata resolves through the existing directory/profile path with per-chat in-flight caching; the fallback is “Participant”, never a raw internal account identifier. Earlier-year messages retain both date and time. The call composer uses the same surface and action treatment, attachment/reply affordances and existing send command. Opening the panel or receiving a scroll request reveals the latest message.

A camera-off event previously cleared the displayed frame but could be followed by a queued background frame, restoring the frozen image. Camera state now fences frame ingestion and dispatch, clears pending camera frames immediately, and retains a retired-publication fence for unsubscribed streams. Other screen shares remain active, and a new camera-on event restores delivery. Clearing a video control also releases its cached bitmap even when the control becomes hidden. No media timeout, polling, protocol/ABI change or real device capture is introduced.

| Requirement | Evidence |
| --- | --- |
| Shared call chat sender/time/actions and both themes | `CallChatUsesSharedBubblesWithSenderTimeAndMessageActions` |
| Queued/late camera frames after mute or unpublish; resume and independent sharing | `RemoteCameraOffDiscardsQueuedAndLateFramesWithoutStoppingTheShare` |
| Ended share cannot reappear from a queued frame | `UnpublishedShareCannotReturnFromAQueuedFrame` |
| White orange-action text in normal, hover and pressed states, contrast at gradient endpoints | `OrangeActionLabelsStayWhiteAndContrastInNormalHoverAndPressedStates` |

The original screenshot artifacts and contrast tables describe their recorded revision. New review evidence records the white-action palette separately. Live browser-to-native device verification still requires an authorized room; synthetic event tests prove the client’s frame-state handling without accessing a real camera.

Latest verification: Release build **0 warnings / 0 errors**; full desktop suite **518 passed / 0 failed / 6 skipped**. After compacting the vacation balance cards into two columns, the focused form/motion suite passed **32/32**. Both themes were rendered and inspected; updated artifacts are in ignored `artifacts/call-chat-review/`. White action label contrast across the measured opaque normal/hover/pressed fills is at least **5.35:1**. Native window startup is checked separately; live browser-to-native camera and other target OS tests remain unverified.
