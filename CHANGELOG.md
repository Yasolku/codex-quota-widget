# Changelog

## 3.5.1

- Add an original water-level Q application icon with nine resolutions (16–256 px); keep dynamic quota numbers in the tray and taskbar.
- Introduce a minimalist water-level floating ball; click to expand and click CODEX to collapse. Ball and panel sizes can be adjusted independently.
- Add light/dark themes, configurable time-based switching, smooth transitions and an independent water-wave switch.
- Detect Pro (including prolite), hide its 5-hour row, and retain Auto/Plus/Pro manual display options.
- Allow the main quota on the left or right, with a larger next-reset date and time alongside it.
- Keep the last successful quota on transient connection failures and clearly mark stale data.
- Cache drawing resources, use adaptive animation timing, stop animations when static/hidden, and replace periodic wake polling with an event wait.
- Cancel pending reads at shutdown, discard child stderr without accumulating it, and dispose initialization responses.
- Add synthetic UI regression and performance diagnostics; no real account samples or runtime settings are included.
- Make build.ps1 fail correctly when compilation fails.

## 3.0.1

- Recognize the current Codex `windowDurationMins` field in addition to `windowDurationMinutes`.
- Restore detection of the 300-minute and 10080-minute quota windows after the app-server schema abbreviation.

## 3.0.0

- Make the 5-hour quota the primary large display, progress bar, tray icon, taskbar icon, and alert target.
- Keep the weekly quota visible as a smaller secondary line with its own reset time.
- Recognize explicit 300-minute and 10080-minute rate-limit windows.

## 2.0.1

- Drain Codex child-process standard error to prevent pipe backpressure from causing refresh timeouts.
- Save settings through a same-directory temporary file and atomic replacement.
- Prevent settings write failures from crashing the widget, tray actions, or shutdown path.

## 2.0.0

- Switch the Windows release to a framework-dependent single-file build.
- Reduce the x64 executable from about 108 MB to about 209 KB.
- Require Microsoft .NET 9 Desktop Runtime instead of bundling the runtime.
- Preserve all v1 widget, tray, taskbar, startup, notification, and quota-reading features.
- Add detailed runtime, installation, privacy, and troubleshooting documentation.

## 1.0.1

- Discover versioned Codex Desktop CLI directories on Windows.
- Relaunching the EXE now restores an existing hidden widget.
- Ensure timed-out Codex child processes are terminated.
- Ensure unexpected refresh errors cannot permanently stop later refreshes.
- Keep the startup menu state synchronized with the actual registry entry.
- Dispose replaced tray menus instead of retaining them for the process lifetime.
- Rename the display setting from “透明度” to “不透明度”.

## 1.0.0

- Initial Windows floating widget and system-tray release.
