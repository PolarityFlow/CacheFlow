# CacheFlow v1.32

Simple browser cache cleaner for Windows.
**[PolarityFlow](https://www.polarityflow.com) · Adrian Zingg**

Project home: `G:\POLARITYFLOW\DEVELOPMENT\PROJECTS\2026\CacheFlow`

## What it does

- Scans the system for installed browsers:
  Chrome, Edge, Brave, Vivaldi, Chromium, Opera, Opera GX, Firefox, LibreWolf,
  Waterfox, DuckDuckGo (Microsoft Store version).
- Shows per browser: installed version, profile count, current cache size
  (✓ *Clean* badge when under 5 MB), last-cleared date and whether the browser
  is currently running.
- Checks for newer browser versions (Chrome, Edge, Brave, Firefox, Opera, Opera GX —
  official release feeds; needs internet, fails silently offline) and shows
  "↑ vX available" / "✓ up to date" per browser. Results are cached per session.
- A progress bar shows scan/clear activity; buttons are locked while working.
- **Clear selected** deletes cache folders only (HTTP cache, code cache, GPU/shader caches,
  service-worker caches). Browsers rebuild these automatically.

## Keep options

Passwords, visited sites (history), cookies/logins and autofill data are **kept by default**.
Uncheck a box to wipe that data too — the tool asks for confirmation first.

Notes:
- Firefox-family history is never touched (it lives in `places.sqlite` together with bookmarks).
- A running browser locks some files; CacheFlow skips locked files and tells you.
  Close the browser first for a full clean. (Edge often runs in the background on Windows 11.)

## Run

Double-click **`CacheFlow.exe`** — that's it.

No installation, no admin rights, no dependencies — fully portable. The exe is a
native C# WPF application targeting .NET Framework 4.x, which is part of Windows.
Last-cleared dates are stored in `cacheflow-state.json` next to the exe.

Windows SmartScreen note for downloaded copies: the exe is not code-signed, so
SmartScreen may show "Windows protected your PC" — click **More info → Run anyway**,
or right-click the ZIP → Properties → **Unblock** before extracting. The included
C# source (`src\Program.cs`) is the transparency guarantee: it is the exact code
inside the exe, and anyone can rebuild it with `build.bat`.

## Building from source

Run `build.bat`. It compiles `src\Program.cs` with the C# compiler that ships
with Windows (`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) — no SDK,
no NuGet, no internet required. The app mark (`assets\AppIcon_256.png`) is
embedded as a resource and `CacheFlow.ico` becomes the exe icon.

(The original PowerShell implementation is kept in `legacy_powershell\` for
reference; the C# version is the one that ships.)

## Donate

The ♥ Donate button opens a dialog with PayPal and crypto options. The links and
addresses are configured in the `Donate` array near the top of `src\Program.cs`.
Entries set to `""` are hidden. Rebuild after changing them.

## License

MIT — see `LICENSE.txt`. Free to use, modify, and redistribute for any purpose.
`LICENSE.txt` also includes a brief data-deletion disclaimer and governing-law clause.
Full Terms of Use and Disclaimer: [www.polarityflow.com/terms](https://www.polarityflow.com/terms)
