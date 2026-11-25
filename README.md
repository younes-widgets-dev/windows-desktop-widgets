# Desktop Widgets

Light desktop widgets for Windows 10/11. One small process (about 15-25 MB of RAM), no install, no extra runtime.

## Download
Go to **Releases > DesktopWidgets (latest build)** and download `DesktopWidgets.exe`. Double-click it.
(If SmartScreen warns: More info > Run anyway. The app is not code-signed.)

## Widgets
Clock (12 types: digital, digital+seconds, stacked, analog classic/minimal/roman, binary, words, rings, flip cards, date card, one line), System (CPU+RAM, bars or rings), Network speed, Disks, Battery, Calendar, Notes, Stopwatch.
You can add as many of each as you want (tray > Add widget, or right-click > Duplicate).

## Control everything
Right-click a widget > **Customize everything...**
- Shape: rounded, rectangle, pill, circle/ellipse, hexagon, octagon, diamond, cut corners, leaf, tab, squircle, parallelogram. Corner radius.
- Exact width and height in pixels, content/text size, inner padding.
- Background: solid, transparent tint, blur (glass), acrylic (frosted glass), with color and tint strength. Window opacity.
- Colors: text, secondary text, accent, border. Border width. Accent strip on/off.
- Any installed font.
- 12 theme presets (Dark, Light, Acrylic dark/light, Blur glass, Clear text-only, Neon, Terminal, Sunset, Pastel, Midnight, Paper), and "copy this look to all widgets".
- Clock: type, 12/24h, seconds, date, any time zone, label.
- Refresh rate, always on top, lock position.
Drag to move. Tray icon: show/hide, add, start with Windows, reset positions, exit. Settings are saved in `%APPDATA%\DesktopWidgets`.

## Why it is light
Native .NET Framework 4.8 (already in Windows), no browser engine, no web views. Hidden widgets do no work. Slow the refresh rate to save even more.

## Build
`dotnet build DesktopWidgets.csproj -c Release` on Windows. GitHub Actions builds every push.
