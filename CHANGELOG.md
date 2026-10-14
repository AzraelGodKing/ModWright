# Changelog

## Unreleased

- The mods repo path can only be changed with **Browse...** (or by double-clicking it); it can no longer be typed in.
- Upload success is now confirmed, not guessed: SteamCMD must exit 0, print no failure line (`ERROR!`, `...FAILED`,
  `Failed to update/upload/commit`), print `Success.`, and for a new item return its Workshop id. Harmless lines that
  merely contain "error" or "failed" (a file named `Error.png`, SteamCMD's SDL notice) no longer fail an upload.
- When creating a new item, the id Steam assigns is saved even if the run fails afterwards, so it is never created twice.
- Install SteamCMD checks the download before installing: complete length, every zip entry passes its CRC,
  `steamcmd.exe` is present, and it carries a valid Authenticode signature from Valve.
- Temporary package folders are removed after dry runs, failed checks, packaging errors and cancellation. A failed
  upload keeps its folder for inspection; such folders are swept after 7 days.
- Unit tests in `tests/SteamCmdHelper.Tests` (`dotnet test`, runs on any OS).

## 1.5.1

- The **?** button now opens a pre-filled issue in Linear (team AZR) instead of GitHub. Only people signed in to that Linear workspace can create it; **Copy text instead** works for everyone.

## 1.5.0

- Added a **?** button that opens a pre-filled GitHub issue to request a layout for another game (issue form in `.github/ISSUE_TEMPLATE`).
- `--urltest` headless check.

## 1.4.0

- Built-in layouts (Generic, RimWorld, Golf With Your Friends) are now part of the program and locked: the layout file only records which one is used, and the app ID is fixed by layouts that name a game.
- Only the new **Custom** layout is stored in full and editable. Old layout files that match a built-in layout become that locked layout.
- `--layouttest` headless check.

## 1.3.0

- Added an experimental Golf With Your Friends layout (app 431240). Real uploads with it ask for confirmation first.

## 1.2.2

- Removed the experimental Tabletop Simulator layout: its items are not built the way SteamCMD uploads content (documented in the README).

## 1.2.1

- Added an experimental Tabletop Simulator layout (app 286160).

## 1.2.0

- Works for any Steam game: Steam app ID box (game name looked up from Steam) and Generic / RimWorld layout presets.
- Mod metadata can come from XML, JSON or plain-text/Lua files; mods can sit in a sub-folder; changelog and version are optional.
- Layouts are auto-detected when there is no `.steamhelper.json` (RimWorld or Generic).
- Layout field names changed (`NameXPath` -> `NameKey` etc.); old 1.1.0 files are still read.
- `--repo` option for the headless checks.

## 1.1.0

- Works for any Steam game: configurable app id and folder layout via `.steamhelper.json` (Edit layout file button).
- Upload logic is built into the app; the PowerShell script and the rimworld_mods repo are no longer required.
- No hardcoded paths; SteamCMD installs to `%LocalAppData%\SteamCmdHelper` by default.
- `--package` headless dry-run check.


## 1.0.0

- First version: mod list with versions, Workshop ids and pre-upload checks, Steam login detection with a
  one-time login window, dry run, create-new-item option, progress bars and a live log.
