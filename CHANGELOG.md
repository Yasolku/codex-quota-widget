# Changelog

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
