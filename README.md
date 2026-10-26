# TIX Clock

A modern Windows 10/11 take on the TIX LED clock (originally made by Cube Root Labs).

> **Planned features**
> - Multiple clocks for different time zones, each with an optional short time zone label (e.g. UTC, EST, JST).

Count the lit squares in each block to read the time:

| Block | Squares | Shows |
|---|---|---|
| 1 | 3 | Hour tens |
| 2 | 9 | Hour units |
| 3 | 6 | Minute tens |
| 4 | 9 | Minute units |

The pattern reshuffles every 1 s, 4 s (default) or 60 s, but the count only changes with the time.

## Using it

- **Move:** drag the clock. **Resize:** Ctrl + scroll over it.
- **Quick options:** right-click the clock (24-hour, pattern speed, rotation, always on top, lock position, Settings, About).
- **Rotate:** normal, 90° clockwise, 180° or 90° anticlockwise. The clock turns around its centre and stays on screen.
- **Tray icon:** left-click shows or hides the clock; right-click for click-through, Settings and Exit.
- **Settings** (tabs for Clock, Display, Appearance, Night, Behaviour and About): colours per block and for unlit squares, colour presets, brightness, LED glow strength (0–100%),
  square or round LEDs, smooth fades between patterns, size, theme (Match Windows / Light / Dark),
  background (Acrylic / Mica on Windows 11 22H2+, otherwise solid), background opacity (0% leaves just the LEDs
  floating), hover tooltip (with or without the date), start with Windows.
- **Night dimming:** a lower brightness between two times, e.g. 22:00 to 07:00.
- **Click-through:** the mouse passes through the clock to whatever is underneath. It's only in settings and the
  tray menu, never the clock's own menu, so it can always be turned off again.
- **Full-screen apps:** the clock hides while a full-screen game, video or presentation is on its monitor.

Switching between a Solid background and Acrylic/Mica restarts the clock, as they need different kinds of window.

Settings are stored in `%APPDATA%\TixClock\settings.json`. "Start with Windows" adds a `TIX Clock` entry under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.

**Portable mode:** if `TixClock.settings.json` exists next to `TixClock.exe`, the app uses that instead, so the
folder can be copied to a USB stick or synced between PCs. Turn it on or off in Settings → Behaviour, which moves the
settings file for you. The folder has to be writable (not Program Files). "Start with Windows" still uses the
registry on each PC.

## Building

Needs the .NET 10 SDK. The published app needs the .NET 10 Desktop Runtime.

```
dotnet publish -c Release -o publish
```
