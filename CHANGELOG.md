# Changelog

## Unreleased

- **Update now** in the new-version prompt downloads the release, checks it against the release's SHA-256 checksums,
  installs it over the running copy (standalone exe or folder build) and restarts. Failed installs put the old files
  back. Opt in to **Install new versions automatically** under **Options > About** to download in the background and
  install when the app closes. `SteamCmdHelper update` does the same from a script.
- Unexpected errors are saved to `%LocalAppData%\SteamCmdHelper\crashes` and can be sent as a pre-filled bug report
  with **Report it**.
- Only one window opens at a time; starting the app again brings the open window to the front.
- **Options > Profiles**: save the mods folder, Steam account and SteamCMD under a name and switch between them.
- **Options > Announcements** (per mods folder): post each successful update to a Discord channel through a webhook,
  and optionally make a matching GitHub release with the gh tool.
- Workshop dependencies named in a mod's metadata are checked against Steam; private, removed or banned ones are
  warned about.
- The row tooltip shows subscriber growth this week and since the last upload, from daily counts kept on this PC.
- The upload confirmation is a window that lists each mod with its change note, which can be edited before it is sent
  (the changelog file is left alone). It offers to commit uncommitted changes first when git packaging would leave
  them out.
- Packages are checked for stray files (`.git`/`obj` folders, `.psd` and other source art, `.pdb` and leftovers,
  archives, very large files) and warned about in the log. An empty package is refused. **Preview package...** on the
  right-click menu shows every file and its size.
- After creating an item in a git repo, the app offers to commit its Workshop ID file. **Options > Git** can do that
  without asking and tag each upload as `<mod folder>-v<version>`.
- Failed uploads say what to do (log in again, wait out a rate limit, accept the Workshop agreement, check the item's
  owner or id), in the window and on the command line. A refused login offers **Log in again and retry**.
- The progress card shows the package size, the time taken so far and, from this PC's recent uploads, about how long
  is left.
- New **Paradox (descriptor.mod)** layout (experimental) for Stellaris, Hearts of Iron IV, Europa Universalis IV,
  Crusader Kings II/III and Imperator: Rome: name and version from `descriptor.mod`, the Workshop id from its
  `remote_file_id` (written back for new items), `thumbnail.png` as the preview. Custom layouts can read the id from a
  key the same way (`WorkshopIdKey`).
- Portable mode: an empty `portable.txt` next to the exe keeps settings, history and SteamCMD in a `data` folder beside it.
- Windows scale to the display's DPI (150%, 200%) instead of clipping text, and the main window fits small screens.
  Screen readers get names for inputs, progress values and the full status of each mod.
- Ease of access: everything works from the keyboard (**F1** lists the shortcuts; **Enter** and **F2** do what
  double-clicking a row does; **Esc** stops an upload; **Alt** + underlined letter for every button and box), a clear
  focus ring shows where Tab is, and screen readers are told when an upload finishes or a step is missing.
- **Options > Appearance**: **Match Windows** follows Windows' light or dark mode, Windows High Contrast is respected,
  and **Text size** makes everything larger (up to 175%).
- Look: the Project card folds into one line once it is set up and the bar between the mod list and the log can be
  dragged, so the list shows more mods; shorter column names leave the Status column room, with the selected mod's
  full status in a strip under the list; **Get set up** shows how many steps are done and the next one as a button;
  the log can show only warnings and errors and pause its auto-scroll; cards have a fine edge and one type scale.
- CI compares each pull request's screenshots with the base branch's and lists the windows that changed.
- Install with `winget install AzraelGodKing.SteamCMDHelper` or from the Scoop bucket in this repository; releases
  keep both up to date. Releases can be code-signed through SignPath, and the standalone exe is now compressed (about
  half the download).
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
- New **Updated on Steam** column: each item's last update date, read live from Steam's public item details in one
  request. Private, friends-only or removed items show `not public`; the row tooltip adds the title, visibility and
  subscriber count on Steam.
- Upload history: every successful upload from this PC is recorded with its time, version and change note.
  Right-click a mod for **Upload history...** or **Open on Steam**.
- **Uploaded version** falls back to that history, and shows `?` when Steam has a newer update than this PC's last
  upload. The tooltip says where the version came from.

- A **Get set up** box lists what is still missing (SteamCMD, Steam account, mods folder, app ID), each a link that
  does it, and goes away when all four are done. Missing pieces are pointed out next to where they are fixed instead
  of in pop-ups.
- Mods folder: recent folders are in a drop-down, and a folder can be dropped onto the window to open it.
- Mod list: a filter box, **Hide up to date**, sorting by any column, status badges (`Fix`, `Failed`, `Changed`,
  `New`, `Up to date`, `Ready`) with the first problem or warning beside them, preview thumbnails, and an empty
  state that says what to do. Right-click adds **Open mod folder**, **Open changelog**, **Copy Workshop ID** and
  **Copy Workshop link**.
- **Retry failed** uploads only the mods that failed or were not reached in the last run.
- Upload progress shows on the taskbar button, which flashes when a run ends in the background.
- The log has timestamps, **Copy**, **Save...**, **Clear** and **SteamCMD logs**.
- Shortcuts: F5 refresh, Ctrl+O pick the mods folder, Ctrl+F filter, Ctrl+Enter upload.
- Look: an app icon, a title bar that matches the theme on Windows 11, readable disabled buttons, themed right-click
  menus, the progress card only while it has something to show, **Dry run** and **Create new items** next to the
  Upload button, and an Options tab with its own SteamCMD and **Your data** cards.

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
