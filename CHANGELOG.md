# Changelog

## 1.1.0

- Works for any Steam game: configurable app id and folder layout via `.steamhelper.json` (Edit layout file button).
- Upload logic is built into the app; the PowerShell script and the rimworld_mods repo are no longer required.
- No hardcoded paths; SteamCMD installs to `%LocalAppData%\SteamCmdHelper` by default.
- `--package` headless dry-run check.


## 1.0.0

- First version: mod list with versions, Workshop ids and pre-upload checks, Steam login detection with a
  one-time login window, dry run, create-new-item option, progress bars and a live log.
