# Changelog

## Unreleased

- **Workshop tags are set.** SteamCMD cannot send tags, so uploads never changed them and RimWorld mods did not show
  up under a new version's filter (such as 1.7). After each upload the app now sets the tags through the Steam client,
  the way RimWorld's own uploader does: `Mod` plus one tag per `supportedVersions` entry in `About/About.xml`. Version
  tags the mod no longer lists are taken off; any other tag is kept. Steam has to be running and signed in as the
  account that owns the mod. A failed tag update is a warning, not a failed upload.
- **Update Workshop tags** on a mod's right-click menu fixes the tags of mods already on Steam without uploading them.
- Turn it off under **Options > Uploading**. Custom layouts can set tags too, with `Tags` and `VersionTagsKey` in
  `.steamhelper.json`.

## 1.3.2

- **RimWorld version-folder builds are uploaded.** Mods that build into version folders (`1.6/Assemblies`,
  `1.7/Assemblies`) went up without their `.dll` files when packaging from git, because most mod repos ignore `*.dll`.
  The RimWorld layout now picks up `*/Assemblies/*.dll` as well as the root `Assemblies/` folder.
- **Left-out files are caught before upload.** Git packaging now compares the package with the mod folder. A `.dll`
  that is in the folder but not in the package stops the upload with a message naming it, and any other left-out file
  is a warning in the log. **Preview package...** lists left-out files at the end.

## 1.3.1

- **Setup.exe that updates like other desktop apps.** Each release now also has `SteamCmdHelper-<version>-Setup.exe`
  (built with [Velopack](https://velopack.io)). It installs the app for your Windows user with Start menu and desktop
  shortcuts, no administrator rights needed, and updates it with a separate updater that swaps versions after the app
  has closed and only downloads what changed. The standalone exe and the framework-dependent zip still update
  themselves as before.
- **Safer self-update.** Updating from 1.2.1 to 1.3.0 could crash the closing window and leave only a
  `.update-old` copy of the exe behind. The updater now waits until the old copy has really closed before replacing
  it, checks that the new exe is in place and matches the download, and if it is missing or will not start, puts the
  old version back, starts it and says why. Each step is written to `update.log`.

## 1.3.0

- **What changed since the last upload.** Each upload remembers which files it sent, so the next upload of that item
  lists in the log what was added, changed and removed since then, and **Preview package...** marks each file (removed
  files are listed at the end).
- **Nothing changed?** When a package is the same, file for file, as the last upload from this PC and no Workshop page
  change goes with it, the app asks before uploading it again (subscribers would only get a new change note). Skipped
  mods show as `SKIPPED` in the summary. The command line uploads it with a warning.
- **Size jumps** are warned about: a package less than half or more than twice the size of the last upload (by at
  least 1 MB), or one that lost many of its files, usually means a missing build folder or something added by mistake.
- **Paths that break for some players** are warned about: paths over 160 characters (they can pass Windows'
  260-character limit once installed), names that differ only in capital letters, and names Windows refuses
  (`CON`, `NUL`, `aux.ogg`, `<>:"|?*`, a trailing dot or space).
- **Required files must ship.** A package without the layout's marker file (RimWorld's `About/About.xml`, a Paradox
  `descriptor.mod`) is refused, as it would break the mod for everyone who updates. Custom layouts can list more with
  `RequiredFiles` (paths or globs).
- **Markdown change notes look right on Steam.** When the changelog is a `.md` file, headings, bold, italics,
  strike-through, bullet and numbered lists, links, quotes, rules and code blocks are turned into Steam's BBCode for
  the change note. Untick it under **Options > Change notes**.
- **Change note template** (Options > Change notes, per mods folder): text wrapped around every note sent to Steam,
  with `{note}`, `{name}`, `{version}`, `{date}` and `{game}` filled in. The command line uses it too.
- **Same version as on Steam?** The upload confirmation warns when Steam already has the version a ticked mod would
  upload, which usually means the version was not raised.
- **Version mismatches** are warned about in the list: a changelog with an entry newer than the version to upload,
  and an `About/Manifest.xml` whose version differs from `About.xml`'s.
- **Replace the preview image** of an item already on Steam: **Workshop page...** has a **Preview image** row (the
  mod's own preview file, or any PNG, JPG or GIF up to 1 MB), sent with the next upload. Before, the preview was only
  sent when an item was created.
- **Keep the description in sync.** When a mod's description file differs from its Workshop page (or, for items Steam
  won't show, from what this PC last sent), the upload offers to send it too. **Options > Workshop page** can do it
  every time without asking (the command line follows that) or never.
- **Build step** (Options > Uploading, per mods folder): a command such as `dotnet build -c Release` runs in the mods
  folder before anything is packaged. If it fails, nothing is uploaded. The command line runs it too (`--no-build`
  skips it).
- **Upload from a branch or tag** (Options > Uploading, or `--ref` on the command line): git packaging takes that
  branch or tag instead of the checked-out commit, with the version and change note read from it. Upload tags and
  GitHub releases go on the commit that was uploaded.
- **Roll back**: **Upload history...** has **Upload this version again**, which re-uploads an earlier version from its
  upload tag (or the commit at that time) with its old change note marked as rolled back.
- **Test items**: **Upload to test items** (or `--test`) sends the ticked mods to private copies on Steam, created on
  first use and kept in `workshop_id.test.txt`, so a build can be checked in game before subscribers get it.
- **Automatic retry**: an update that fails because Steam timed out, was busy or could not be reached is tried once
  more after 30 seconds (Options > Uploading).
- **Tick changed** ticks only the mods whose version Steam does not have yet.

- **Change notes keep their line breaks on Steam.** Uploads used to show a literal `\n` wherever the changelog had a
  new line, because SteamCMD reads the build file without escape sequences. Line breaks are now written as real line
  breaks, backslashes are left as typed, and straight double quotes become curly ones (a straight quote would end the
  value). The same applies to a Workshop page's title and description.
- **"Uploaded via SteamCMD Helper"** is added to the end of each change note on Steam, linking to the newest release.
  Turn it off under Options > Change notes. The upload history and Discord announcements keep the entry as written.
- **Command line:** `upload --tag`, `--commit-ids` and `--announce` do the steps the window offers after an upload:
  tag what was uploaded, commit the Workshop id files of new items, and post to Discord and GitHub releases.
- **Command line:** `upload --note "..."` uploads with that change note, and `upload --note-from-git` drafts each
  mod's note from its commits since the last upload. `upload --json` prints a JSON summary of what happened to each
  mod (status, Workshop id, tag) on standard output, with the progress on standard error.
- **Past logs.** Every upload run's log is saved, from the window and the command line; **Past logs** in the Log card
  lists them by time and what was uploaded. The newest 50 are kept.

## 1.2.1

- **Updating the app no longer crashes it.** The running app used to replace its own exe and then crash with "Something
  went wrong" (a missing `System.Text.Encoding.CodePages`) as it closed, because .NET still reads parts of the program
  from that file. Now the downloaded version installs itself after the old one has closed, then starts. Updates from
  1.2.0 and earlier still run the old way once, so that one update may still show the error; the new version is
  installed regardless.
- **Opens faster.** The standalone exe is no longer compressed (it unpacked the whole .NET runtime on every start)
  and is compiled ahead of time; the mods folder is read after the window appears instead of before; and git no
  longer searches for untracked files when checking for uncommitted changes. The download is bigger as a result.
- **Command line:** `upload --changed` uploads only the mods whose version Steam does not have yet and exits `0`
  when there is nothing to do, so a CI job can run it after every push. `list --json` prints the mods (version,
  Workshop id, ready, problems and warnings) as JSON for scripts.
- **Change the version** (F2, or double-click it) has **Patch**, **Minor** and **Major** buttons that fill in the
  next version. It can now also write the version into a JSON marker file (such as `mod.json`) and a `version=` line
  (such as a Paradox `descriptor.mod`), not only XML.
- **Command line:** `version <mod> patch|minor|major|<number>` sets the version to upload, and `--note "..."` adds the
  new version's changelog entry where the layout needs one (RimWorld).
- **Draft from git commits** (in the upload confirmation and the "What changed?" box for a new version) writes the
  change note from the commits that touched the mod since its last upload from this PC, one line each, leaving out
  fixup, WIP and merge commits. It starts from the mod's upload tag when there is one, else from the upload's time.

## 1.2.0

- **Shrink preview image under 1 MB** (right-click a mod): re-saves an oversized preview just small enough for Steam,
  scaling it down only as far as needed. The original is kept in `%LocalAppData%\SteamCmdHelper\preview-backups`.
- After updating an existing item, the app asks Steam whether the item's "updated" time moved and says so in the
  log. A clean SteamCMD run that never printed `Success.` now counts as uploaded when Steam shows the update arrived,
  instead of being reported as failed.
- Uploads are no longer reported as failed when SteamCMD prints "Success." on the same line as "Committing
  update..." (as it does on Windows). Steam had taken those updates; the app just missed the confirmation.
- Refreshing the list no longer repeats every mod's "fix ..." line in the log; a mod's problems are logged again only
  when they change.
- A mod that is not on Steam yet now says to turn on **Create new items (private)** when that is all that stops it
  being ticked, instead of saying it is ready to create.

## 1.1.0

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
