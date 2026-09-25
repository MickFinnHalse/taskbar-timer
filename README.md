# Taskbar Timer

A countdown that shows **`14:23`** as readable text sitting just left of the system tray,
always on top, with no dependence on Windows notifications.

Built because nothing off the shelf does this. Windows 11 removed the deskband API, so no
third-party app can be hosted *inside* the taskbar any more — every real timer app either
gives you a tray **icon** (a glyph, not a number) or relies on toasts, which are useless
with notifications switched off.

## What it does

- Renders the remaining time as text, positioned automatically against the notification area
- Always on top, and re-asserts that every two seconds so the taskbar cannot bury it
- No taskbar button of its own, and stays out of Alt-Tab
- Alerts by **flash**, **sound**, or **both** — your choice, no toasts involved
- Follows the Windows light/dark setting

## Build

```
powershell -ExecutionPolicy Bypass -File "H:\My Drive\Master\Illumin-Ed\Tools\Claude\taskbar-timer\build.ps1"
```

Compiles with the C# compiler already in `C:\Windows\Microsoft.NET\`. No SDK, no NuGet,
no .NET download. **Writes (overwriting) `TaskbarTimer.exe` in this folder — nothing else.**

## Use

| Action | Result |
|---|---|
| Left click | Start / pause |
| Left click while alerting | Silence it |
| Left drag | Move it; it stops following the tray |
| Right click | Presets, Custom…, Reset, alert mode, startup, exit |

Right-click → **Snap back to the tray** returns it to automatic positioning after a drag.

**Custom durations** accept loose input: `25`, `45m`, `1h30m`, `90s`, `2:30`.
A bare number means minutes. The same syntax works on the command line:

```
TaskbarTimer.exe 25
```

## Settings

`settings.ini` lands next to the exe on first save. Hand-editable — close the timer first.

| Key | Meaning |
|---|---|
| `DurationSeconds` | Default countdown |
| `Alert` | `Flash`, `Sound` or `Both` |
| `Volume` | 0–100, alarm only — does not touch system volume |
| `AlertTimeoutSeconds` | How long the alarm runs; `0` = until dismissed |
| `AutoPosition` | `true` follows the tray, `false` uses `ManualX` / `ManualY` |
| `FontSize` | Points, default 11 |
| `FlashTickMs` | Flash frame interval, default 250 |
| `SoundFile` | Full path to a `.wav` to loop; blank uses the built-in tone |

Sound deliberately goes through `SoundPlayer`, which uses the ordinary audio path rather
than the notification pipeline — so it still sounds with notifications disabled.

## Colours

| State | Background | Digits |
|---|---|---|
| Running | follows Windows theme | `#1E9E44` green |
| Final 10% of the set duration | follows Windows theme | `#C22A1E` red |
| Paused / not started | follows Windows theme | `#FFAB35` amber |
| Alarming | red, flashing | white |

Only the alarm fills the tile. Every other state leaves the background theme-coloured so
it sits in the taskbar instead of on top of it.

The warning threshold is proportional: 3 minutes into a 30-minute countdown, 30 seconds
into a 5-minute one.

## Why the volume control works the way it does

`SoundPlayer` has no volume property. `waveOutSetVolume` would change the volume of the
whole output device — turning down everything else you are listening to — and WPF's
`MediaPlayer` needs a `Dispatcher` that a WinForms message loop does not provide. So
volume is applied to the sample amplitudes when the clip is generated, in
[AlertSound.cs](src/AlertSound.cs). Changing volume rebuilds the clip, which costs about
a millisecond. A custom `SoundFile` is scaled the same way if it is 16-bit PCM; anything
else plays at its own level.

## Notes

- **Start with Windows** writes one `HKCU\...\Run` entry named `IllumEdTaskbarTimer`.
  Unticking it removes that entry. Nothing else in the registry is touched.
- Only one instance runs at a time; launching a second one exits silently.
- If a full-screen app is running, it will cover the timer — that is unavoidable for any
  overlay, since the taskbar itself is hidden in that state.
