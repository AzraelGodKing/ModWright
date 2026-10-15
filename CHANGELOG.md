# Changelog

## Unreleased

- Batch uploads: **Tick all** / **Tick none** buttons, one login check for the whole batch, and a summary of what
  happened to each mod (uploaded, created, failed and why, not run) at the end, in the log and in a dialog. If Steam
  refuses the login part-way, the remaining mods are not tried.
- Command line for scripts and CI: `SteamCmdHelper upload <mod>... | --all`, `SteamCmdHelper list` and
  `SteamCmdHelper help`, with `--repo`, `--account`, `--steamcmd`, `--dry-run`, `--allow-create`,
  `--allow-experimental` and `--log`. Same checks and upload code as the window; exit code 0 (all done), 1 (a mod
  failed), 2 (bad arguments or nothing to upload) or 3 (SteamCMD missing or not logged in).
- Changing a RimWorld mod's version (double-click **Version to upload**) now asks what changed and writes the new
  version's entry into `About/Changelog.txt`, so the upload is not blocked for a missing entry. A row still missing
  its entry can be fixed by double-clicking its **Status**.
- **Workshop page...** (also right-click or double-click a mod's name) edits the item's title, description (with a
  counter for Steam's 8000-character limit) and visibility, starting from what Steam shows now. Ticked changes are
  sent with the mod's next upload and listed in the upload confirmation. Tags are shown but cannot be changed, as
  SteamCMD has no way to set them.
- At start-up the app checks GitHub for a newer release and offers to download it (or show its notes, or skip that
  version). It can be turned off, or run on demand with **Check now**, under **Options > About**.

## 1.0.0

First release.

- Dark theme by default, with a light theme under **Options > Appearance** (saved per Windows user). Dark title bar
  and scroll bars on Windows 10 1809 and later; the log keeps its console look in both themes.
- Cleaner main window: a top bar with **Upload** and **Options** tabs, grouped Project, Mods and progress panels,
  flat buttons with a highlighted Upload button, and slim progress bars. The Steam login (account name, Check login,
  Log in, Install SteamCMD) moved to the **Options** tab; its status stays next to the Upload button as a link there.
- At start-up, a missing SteamCMD is detected and the app offers to install it, warning that uploads won't work
  without it; if declined, the log and the status line say so until it is installed.
- A "not affiliated with Valve or Steam" notice at the bottom of the window, under Options > About and in the README.
- Pre-upload checks are basic: only a missing or over-1 MB preview on a new item, an unreadable id or metadata file,
  and for RimWorld the Add Changenote rules (version from `About/Manifest.xml` or `About/About.xml`, and an entry for it
  in `About/Changelog.txt`, which becomes the change note) block an upload. Everything else is a warning. Mods without
  an id file show Workshop ID `0` (a new item).
- Mod list columns: Uploaded version (read from the item's newest Steam change note, else this PC's last upload),
  Version to upload (double-click to edit; written to About/Manifest.xml and About.xml), Workshop ID, Preview (✔ / ✖
  for Steam's 1 MB limit) and a short Status (Ready, Fix, Ready to create). Details are in each row's tooltip.
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
