# Changelog

## 1.0.0

First release.

- Mod list with name, version, Workshop id and pre-upload checks; Steam login detection with a one-time login
  window in SteamCMD (this app never sees your password); dry run, create-new-item option, progress bars and a live log.
- Works for any Steam game: Steam app ID box (game name looked up from Steam) and layouts that describe how the mods
  are laid out. Mod metadata can come from XML, JSON or plain-text/Lua files; mods can sit in a sub-folder; changelog
  and version are optional.
- Built-in layouts (Generic, RimWorld, experimental Golf With Your Friends) are part of the program and locked: the
  repo's `.steamhelper.json` only records which one is used, and layouts that name a game fix the app ID. Only the
  **Custom** layout is stored in full and editable. Without a layout file, RimWorld or Generic is auto-detected.
  Experimental layouts ask for confirmation before a real upload.
- The **?** button requests a built-in layout for another game. Builds with the Linear request key send it straight
  from the app (one per minute, showing exactly what will be sent); other builds open a pre-filled Linear issue or copy the text.
- The mods repo path can only be changed with **Browse...** (or by double-clicking it), so it cannot be mistyped.
- Upload success is confirmed, not guessed: SteamCMD must exit 0, print no failure line (`ERROR!`, `...FAILED`,
  `Failed to update/upload/commit`), print `Success.`, and for a new item return its Workshop id. Harmless lines that
  merely contain "error" or "failed" (a file named `Error.png`, SteamCMD's SDL notice) do not fail an upload.
- When creating a new item, the id Steam assigns is saved even if the run fails afterwards, so it is never created twice.
- Install SteamCMD downloads to `%LocalAppData%\SteamCmdHelper` and checks the download before installing: complete
  length, every zip entry passes its CRC, `steamcmd.exe` is present, and it carries a valid Authenticode signature from Valve.
- Temporary package folders are removed after dry runs, failed checks, packaging errors and cancellation. A failed
  upload keeps its folder for inspection; such folders are swept after 7 days.
- Headless checks: `--selftest`, `--package`, `--layouttest`, `--urltest`, with `--repo <folder>`.
- Unit tests in `tests/SteamCmdHelper.Tests` (`dotnet test`, runs on any OS).
