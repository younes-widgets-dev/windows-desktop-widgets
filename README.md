# Desktop Widgets

Light desktop widgets for Windows 10/11. One small process (about 15-25 MB of RAM), no install, no extra runtime.

## Download
Go to **Releases > DesktopWidgets (latest build)** and download `DesktopWidgets.exe`. Double-click it.
(If SmartScreen warns: More info > Run anyway. The app is not code-signed.)

## Widgets (14 types, add as many of each as you like)
Clock (12 types), System (CPU+RAM), **CPU detailed** (graph, per-core bars, processes, threads, uptime, CPU name), **RAM detailed** (used/free/total, commit, graph, top processes by memory), **GPU** (3D usage), **Disk activity** (read/write speed, busy %), Network speed, **Ping**, Disks (space), Battery, Calendar, Notes, Stopwatch, **System info** (PC, user, OS, CPU, RAM, screen, IP, uptime).
Tray icon > Add widget, or right-click > Duplicate.

## Control everything
Right-click a widget for quick options (Quick theme, Opacity, Size, Accent color, Refresh rate, Always on top, Lock) or **Customize everything...**
- Exact size: width and height in pixels, X/Y position, content size %, presets, "make all widgets the same width/size". Drag any edge or corner to resize.
- 22 shapes: rounded, rectangle, pill (3 kinds), circle/ellipse, hexagon, octagon, diamond, cut corners, leaf, tabs, squircle, parallelogram, pentagon, trapezoid, ticket, shield, star, triangle, diagonal corners. Exact corner radius.
- Background: solid, gradient (vertical/horizontal/diagonal), transparent tint, blur (glass), acrylic (frosted glass); color, tint strength, opacity.
- Colors (text, secondary, accent, border), border width, accent strip (position + thickness), title on/off, bold text, any installed font, inner padding.
- 32 theme presets and "copy this look to all widgets".
- Per-widget options (clock type/time zone/12-24h, calendar week start, CPU/RAM sections, ping host...).
Settings are saved in `%APPDATA%\DesktopWidgets`.

## Why it is light
Native .NET Framework 4.8 (already in Windows), no browser engine, no web views. Hidden widgets do no work. Slow the refresh rate to save even more.

## Build
`dotnet build DesktopWidgets.csproj -c Release` on Windows. GitHub Actions builds every push.
