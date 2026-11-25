# Desktop Widgets

Light desktop widgets for Windows 10/11. One small process (about 15-25 MB of RAM), no install, no extra runtime.

## Download
Go to **Releases > DesktopWidgets (latest build)** and download `DesktopWidgets.exe`. Double-click it.
(If SmartScreen warns: More info > Run anyway. The app is not code-signed.)

## Widgets
Clock, System (CPU + RAM), Network speed, Disks, Battery, Calendar, Notes, Stopwatch.
Clock, System, Network and Calendar show on first run. Turn the others on from the tray icon.

## Control
- Drag a widget to move it.
- Right-click a widget: opacity, size (70%-200%), accent color, refresh rate, always on top, lock position, hide.
- Tray icon right-click: show/hide each widget, Start with Windows, reset positions, exit.
- Settings are saved in `%APPDATA%\DesktopWidgets`.

## Why it is light
Native .NET Framework 4.8 (already in Windows), no browser engine, no web views. Hidden widgets do no work. Slow the refresh rate to save even more.

## Build
`dotnet build DesktopWidgets.csproj -c Release` on Windows. GitHub Actions builds every push.
