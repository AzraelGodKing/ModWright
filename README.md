# SteamCMD Helper

Windows desktop app for uploading Steam Workshop mod updates without remembering any commands. Open it,
tick the mods, press **Upload**. It shows each mod's version and Workshop id, checks the changelog before
sending anything, handles the Steam login, and shows progress bars and a live log.

It works for **any game with a Steam Workshop**: you give it the game's Steam app ID and tell it how your
mods are laid out. The upload logic is built in; the only outside tool it needs is SteamCMD.

SteamCMD Helper is an independent tool. It is not affiliated with or endorsed by Valve Corporation or Steam.
Steam and SteamCMD are trademarks of Valve Corporation.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (running needs the .NET 8 runtime, which the SDK includes)
- `git` on PATH if your repo uses the default packaging (`UseGitArchive`)
- SteamCMD: required for uploading. If it is missing at start-up the app offers to download it for you
  (also **Options > Install SteamCMD**)
- A Steam account that owns the game

## Download

Each [GitHub Release](../../releases) has:

- `SteamCmdHelper-<version>-win-x64.exe`: a single self-contained exe; no .NET install needed.
- `SteamCmdHelper-<version>-win-x64-framework-dependent.zip`: a small folder build that needs the
  [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0). Use it if Windows or your antivirus
  refuses to launch the large unsigned exe.

At start-up the app asks GitHub for the latest release and, when it is newer, offers **Update now**, **Release notes**,
or **Later**, with a box to stop reminding you about that version. **Update now** downloads the release file that matches
how you run the app (the standalone exe, or the framework-dependent zip for the folder build), checks it against the
release's `SHA256SUMS.txt` and restarts: the new version installs itself once the old one has closed, then opens. If
anything fails, the old files are put back. When the app's folder
cannot be written to (for example under Program Files), it offers **Download** through your browser instead.

Tick **Install new versions automatically** under **Options > About** to skip the question: a newer release is then
downloaded and checked in the background and installed when you close the app (or press **Restart now** there). It is off
by default. Only the public releases list and the release files are read; nothing about you or your mods is sent. Turn
the check off, or **Check now**, under **Options > About**. From a script, `SteamCmdHelper update` does the same.

### Install with winget or Scoop

```
winget install AzraelGodKing.SteamCMDHelper
```

```
scoop bucket add steamcmdhelper https://github.com/AzraelGodKing/SteamCMDHelper
scoop install steamcmdhelper
```

Both install the standalone exe and put `steamcmdhelper` on your PATH, so the command line below works from any
terminal. `winget upgrade` and `scoop update` pick up new releases.

## Build and run

```powershell
dotnet build SteamCmdHelper.csproj -c Release
```

The program is `bin\Release\net8.0-windows\SteamCmdHelper.exe`. Make a shortcut to it.

Branches: `main` is the released version and `test` is where new work comes together; pull requests go to `test`, and `test` is merged into `main` for each release. CI builds and tests every push to `main` or `test` and every pull request. For pull requests it also builds the base branch,
takes the same `--shot` screenshots, and lists in the job summary which windows changed (the changed pixels are in red
in the `screenshot-diffs` artifact). That step is informational and never fails the build. To release, bump `<Version>` in
`SteamCmdHelper.csproj` and add a matching `## <version>` section to `CHANGELOG.md` on `test`, then run
**Promote test and release** (see [Releasing](#releasing-maintainer)).

## Using it

Until everything below is in place, a **Get set up** box at the top of the Upload tab lists the four steps
(SteamCMD, Steam account, mods folder, app ID), each a link that does it, says how many are done and offers the next
one as a button. It goes away once they are done. Once the mods folder, app ID and mods are all found, the **Project**
card folds into one line (folder, game, layout, branch); **Change...** or a click on that line opens it again, which
leaves the mod list more room.

1. **Mods repo**: press **Browse...** (or Ctrl+O) and pick the folder that holds your mods (one sub-folder per mod), or
   drop the folder onto the window. Folders you used before are in the drop-down, so switching repos is one click.
   The path cannot be typed in, so it cannot be mistyped.
2. **Steam app ID**: the number in the game's store URL (`store.steampowered.com/app/294100/...` = RimWorld).
   The game name is looked up from Steam to confirm it. RimWorld-shaped repos are detected automatically.
3. **Layout**: pick a built-in layout (**Generic**, **RimWorld**, **Paradox (descriptor.mod)**, **Golf With Your Friends**). Built-in layouts are part of the program and are **locked**: they cannot be edited, and the layout fixes the app ID. For anything else pick **Custom**, then **Edit layout file...** (below).
   The choice is saved in `.steamhelper.json` in the repo root; for a built-in layout that file holds only its name (and your app ID for Generic).
4. **Steam account** (on the **Options** tab): type your Steam login name. The status line says whether a saved login
   exists; the same status shows next to the Upload button, and clicking it opens Options.
   **Install SteamCMD** downloads it to `%LocalAppData%\SteamCmdHelper\steamcmd` (or uses
   `<repo>\_tools\steamcmd` if that exists).
5. **Log in** happens automatically on the first upload. A SteamCMD window opens; type your password and the
   Steam Guard code there. This app never sees your password. Afterwards the login is cached.
   Logging in through SteamCMD can knock the regular Steam client offline; use Steam > Go Online afterwards.
6. Tick the mods (**Tick all** picks every mod that is ready) and press **Upload**. Several mods upload one after
   another with a single login check; if Steam refuses the login part-way, the rest are not tried. When more than
   one mod was picked, or anything failed, a summary lists what happened to each mod (it is also in the log).
   **Dry run** does every check and builds the package but uploads nothing. The taskbar button shows the progress
   and flashes when a run ends while the window is in the background. If anything failed or was not reached,
   **Retry failed** uploads just those mods again.

Before anything is sent, the confirmation lists each mod and what it will do on Steam, with its change note. Click a
mod to read or edit its note; an edit changes only what is sent to Steam, not the changelog file (**Use the changelog
entry** puts it back). In a git repo, **Draft from git commits** fills the note with one line per commit that touched
the mod since its last upload from this PC: since its upload tag (**Tag each upload in git**) when there is one, else
since that upload's time, else its newest ten commits. Fixup, squash, WIP and merge commits are left out. The same
button is in the box that asks what changed in a new RimWorld version. If git packaging would leave a mod's uncommitted changes out, the confirmation offers to commit
them first. While a mod uploads, the progress card shows the package size, how long it has been running and, once this
PC has a few uploads in its history, about how long is left. When Steam turns an upload down, the summary says what to
do (for example, log in again, wait out a login rate limit, or accept the Workshop agreement), and a refused login
offers **Log in again and retry**.

Every package is checked for files that are rarely meant for players: `.git` or `obj` folders, source art such as
`.psd` or `.blend`, debug and leftover files (`.pdb`, `Thumbs.db`, ...), archives, and very large files. These are
warnings in the log; only an empty package is refused, because it would remove every file from the item.
Paths that break for some players are warned about too: paths long enough to pass Windows' 260-character limit once
installed, names that differ only in capital letters (they overwrite each other on Windows and macOS), and names
Windows refuses (`CON`, `NUL`, `aux.ogg`, `<>:"|?*`, a trailing dot or space). A package that is missing the layout's
marker file (such as RimWorld's `About/About.xml`) or one of its `RequiredFiles` is refused, because the mod would break
for everyone who updates; that usually means a `.gitattributes` export-ignore rule or the `Exclude` list dropped it.
Right-click a mod and choose **Preview package...** to see every file and its size before uploading.

Each upload remembers which files it sent (`%LocalAppData%\SteamCmdHelper\manifests`), so the next one of the same item
says in the log what was added, changed and removed since then, and **Preview package...** marks each file. A package
that is much smaller or larger than last time, or that lost many files, is warned about. When nothing at all changed
since the last upload (and no Workshop page change goes with it), the app asks before uploading it again, since
subscribers would only get the change note; the command line uploads it with a warning.

In a git repo, after a new item is created the app offers to commit its Workshop ID file, so nobody creates the item
twice (**Options > Git** can do it without asking). Tick **Tag each upload in git** there to tag the uploaded commit
as `<mod folder>-v<version>`; tags stay local until you push them.

The list can be filtered by name, folder or Workshop ID (Ctrl+F), **Hide up to date** drops the mods Steam already
has, and clicking a column header sorts by it (again to reverse, a third time for folder order). Right-click a mod
to open its folder or changelog, copy its Workshop ID or link, see its upload history, preview its package, or open it
on Steam. The strip under the list spells out everything the selected mod needs, in full.

Everything works from the keyboard. **F1** (or **? > Keyboard shortcuts**) lists the shortcuts: **F5** refresh,
**Ctrl+O** pick the mods folder, **Ctrl+F** filter, **Space** tick the selected mod, **Enter** edit its Workshop page,
**F2** change its version, **Shift+F10** its right-click menu, **Ctrl+Enter** upload the ticked mods, **Esc** stop a
running upload, **Ctrl+Tab** switch tabs, and **Alt** with an underlined letter for any button or box. While you move
around with Tab or the arrow keys, the focused control has a clear ring around it. Screen readers are told when an
upload finishes and when a step is missing.

The log at the bottom stamps each line with the time and colours it by kind (blue notes, green success,
orange warnings, red errors). The drop-down next to its title shows everything, only warnings and errors, or only
errors; untick **Auto-scroll** to read back while an upload runs. Drag the bar between the mod list and the log (or Tab
to it and use the arrow keys) to give either more room; the log keeps that height. **Copy** and **Save...** take the
whole log whatever is shown, **Clear** empties it, and **SteamCMD logs** opens SteamCMD's own log folder, which is
where to look when Steam turns an upload down.

The app uses a dark theme by default. Under **Options > Appearance** pick **Light**, or **Match Windows** to follow
Windows' light or dark app mode (it switches when Windows does). When Windows High Contrast is on, the app uses its
contrast colours instead. **Text size** (100% to 175%) makes all text, and the layout with it, larger on top of Windows'
own display scaling; it is used from the next start. The choices are saved for your Windows user.

The checks are kept basic. A row turns red and cannot be uploaded only for things that would make the upload fail
or go wrong:

- a new item without its preview image, or a preview over 1 MB (Steam refuses both)
- an id file that is not a number, or a metadata file that cannot be read
- **RimWorld:** the same rules as the [Add Changenote](https://steamcommunity.com/sharedfiles/filedetails/?id=2161604688)
  mod. The version comes from `<version>` in `About/Manifest.xml`, or else `<modVersion>` in `About/About.xml`, and
  `About/Changelog.txt` must have an entry for that version: a line starting with the version number, plus the lines
  under it up to the next version line (lines starting with `#` are skipped). That entry becomes the Workshop change note.

Everything else is only a warning: other layouts' changelog issues, missing build output, and uncommitted tracked
changes (git packaging uses `HEAD`). Hover over a row to see its problems and warnings; problems are also listed in the log.

The list columns:

- **Steam version**: the version already on Steam. Steam does not store one, so it is read from the newest change
  note on the item's public changelog page (notes sent by this app for RimWorld, or by Add Changenote, start with the
  version). If that note has none, it shows what this PC last uploaded, unless Steam shows a later update (an upload
  from somewhere else), in which case it shows `?`. Private items show `?` until the first upload from this PC.
- **Updated**: when the item was last updated, read live from Steam's public item details (one request for
  the whole list, no login). `not public` means Steam hides it from anonymous readers: private, friends only, or removed.
  Hover a row for the title, visibility and subscriber count on Steam.
- **To upload**: the mod's current version. Double-click it (or press F2) to change it: type the new number, or press
  **Patch**, **Minor** or **Major** for the next one (1.2.3 becomes 1.2.4, 1.3.0 or 2.0.0). The new number is written
  to `<version>` in `About/Manifest.xml` (when it has one) and to the version in the marker file: an XML element such as
  `About/About.xml`'s, a JSON property such as `mod.json`'s, or a `version=` line such as a Paradox `descriptor.mod`'s. For RimWorld the app then asks what changed and adds that as the new version's entry in
  `About/Changelog.txt` (newest first, or at the end if the file lists the oldest version first), so the upload is
  not refused for a missing entry. If you skip it, double-click the row's **Status** to write it later.
- **Workshop ID**: from the mod's own id file (`About/PublishedFileId.txt` for RimWorld); `0` means the upload creates a new item.
- **Preview**: a thumbnail of the preview image; a red frame means it is over Steam's 1 MB limit, ✖ that it is
  missing (hover for the size). Right-click a mod and choose **Shrink preview image under 1 MB** to re-save an
  oversized preview just small enough (the original is kept in `%LocalAppData%\SteamCmdHelper\preview-backups`).
- **Status**: a badge, followed by the first problem or warning when there is one:
  `Fix` (red, can't be uploaded), `Failed` (red, failed in the last run), `Changed` (amber, Steam has an older
  version), `New` (blue, not on Steam yet), `Up to date` (green, Steam already has this version) or `Ready` (the
  version on Steam is not known yet).

### Workshop page: title, description and visibility

Select a mod and press **Workshop page...** (or right-click it, or double-click its name). The editor reads the item's
current title, description and visibility from Steam. Tick what to change; the changes are kept and sent with that
mod's next upload (the confirmation lists them), then cleared (an upload from the command line sends them too). Unticked fields stay as they are on Steam. For a mod not
on Steam yet, ticked fields replace the name, description file and private visibility it would be created with.
SteamCMD cannot change tags, so they are only shown (the game's own uploader sets them).

The same right-click menu has **Upload history...** (every upload of the mod made from this PC: when, which
version, and the change note sent) and **Open on Steam** (the item's page in your browser).

### Profiles, announcements and dependencies

**Options > Profiles** saves the current mods folder, Steam account and SteamCMD under a name; pick a profile to switch
all three at once (handy for several projects or a second Steam account).

**Options > Announcements** is set per mods folder. Paste a Discord channel's webhook link (channel settings >
Integrations > Webhooks) and each successful update is posted there with its name, version, change note and Workshop
link; **Send test** checks the link. Mentions are switched off, so an `@everyone` in a changelog pings nobody. Tick
**Make a GitHub release** to also create a release with the same notes on the uploaded commit, using the
[gh](https://cli.github.com) tool (run `gh auth login` once; the commit must be pushed). New items are created private,
so they are never announced. The webhook link is a secret, so it is kept in your settings, not in the repo.

Mods that name other Workshop items in their metadata (RimWorld's `modDependencies`, or any
`steamcommunity.com/sharedfiles/filedetails/?id=` link in the marker file) are checked against Steam when the list
loads. A dependency that is private, removed or banned shows as a warning, since subscribers could not get it.

The row tooltip shows each item's subscriber count and how it has changed this week and since the last upload from
this PC. Counts are saved once a day in `%LocalAppData%\SteamCmdHelper\stats.json`, so the trend fills in over time.

### Command line (scripts and CI)

The same exe uploads without opening the window. It uses the repo, account and SteamCMD picked in the window
unless you pass them, runs the same checks and upload code, and prints a summary at the end.

```powershell
SteamCmdHelper upload MyMod OtherMod          # by folder or display name
SteamCmdHelper upload --all --dry-run         # check and package every ready mod, upload nothing
SteamCmdHelper upload --changed               # only the mods whose version Steam does not have yet
SteamCmdHelper upload --all --repo D:\mods --account mysteamname --log upload.log
SteamCmdHelper list                           # each mod's version, Workshop id and status
SteamCmdHelper list --json                    # the same as JSON, for scripts
SteamCmdHelper version MyMod patch --note "- Fixed the thing"   # 1.2.3 -> 1.2.4, with its changelog entry
SteamCmdHelper update                         # install the newest release of the helper (--check: only report it)
SteamCmdHelper help                           # every option
```

- The Steam login must already be saved: log in once from the window (**Options > Log in...**). The command checks
  it once, then uploads each mod in turn.
- A named mod that needs fixing, or does not exist, stops the command before anything is uploaded. `--all` skips
  such mods and says why. `--changed` also skips mods whose version Steam already has, found the same way as the
  window's **Steam version** column (the newest change note on Steam, else this PC's uploads); a mod whose version on
  Steam is not known is uploaded. With nothing to upload it exits `0`, so it can run after every push. New items need `--allow-create`; experimental layouts need `--allow-experimental`.
- Exit codes: `0` everything worked, `1` a mod failed or was not run, `2` bad arguments or nothing to upload,
  `3` SteamCMD missing or not logged in.
- The app is a Windows program, so in a `cmd` window the prompt comes back before the output finishes. Use
  `start /wait SteamCmdHelper upload ...` (or `Start-Process -Wait` in PowerShell) when the next line needs the result,
  or `--log <file>`. Redirected output (CI steps, `> out.txt`) and exit codes work as usual.

### Mods not on Steam yet

Tick **Allow creating new items**. They are created private, with the title, description and preview image
from your layout. The new id is saved to the id file; commit it.

## Custom layouts: `.steamhelper.json`

Only a **Custom** layout is stored in full in the file and can be edited (the file says `"Preset": "Custom"`). Choosing Custom from a built-in layout copies that layout's fields as a starting point. A built-in layout named in the file is always read from the program, whatever else the file says.

The **Generic** layout expects, inside each mod folder: `CHANGELOG.md` (the Workshop change note),
`workshop_id.txt` (the item id, written for you on create), `preview.png` and `steam_description.txt`
(only for creating). Everything is configurable:

| Field | Generic default | Meaning |
|---|---|---|
| `AppId` / `GameName` | empty | Steam app id; the name is a label |
| `ModsFolder` | empty | Sub-folder of the repo that holds the mods (empty = repo root) |
| `ModMarker` | empty | File that marks a folder as a mod (e.g. `About/About.xml`, `mod.json`). Empty = every folder |
| `NameKey`, `VersionKey`, `DescriptionKey` | empty | How to read those from the marker file (see below). Empty name = folder name |
| `Changelog` | `CHANGELOG.md` | Release notes file |
| `ChangelogRequired` | true | When false, a missing changelog just sends an empty change note |
| `ChangelogFirstLineIsVersion` | false | Line 1 is the version (warns when it differs from the marker's version) and the rest is the note |
| `ChangelogByVersion` | false | Add Changenote style: one entry per version, and the entry for the current version is required and becomes the note (RimWorld: true) |
| `ManifestFile` | empty | A Mod Manager style manifest whose `<version>` wins over `VersionKey` (RimWorld: `About/Manifest.xml`) |
| `WorkshopIdFile` | `workshop_id.txt` | Stores the Workshop id |
| `WorkshopIdKey` | empty | When set, the id is that key inside `WorkshopIdFile` (read like `NameKey`, e.g. `remote_file_id`), and a new id is written back to it |
| `Preview`, `SteamDescriptionFile` | `preview.png`, `steam_description.txt` | Used when creating (empty `Preview` = none) |
| `BuiltFiles` | `[]` | Build outputs copied from the working tree, e.g. `["Assemblies/*.dll"]` (a warning when missing) |
| `RequiredFiles` | `[]` | Files every package must contain, as paths or globs (e.g. `["Assemblies/*.dll"]`). The marker file always counts. A package without one is refused |
| `UseGitArchive` | true | Package with `git archive HEAD` (honours `export-ignore`); false copies the folder using `Exclude` |
| `Exclude` | `.git`, `bin`, `obj`, ... | Skipped when `UseGitArchive` is false |

How the keys are read depends on the marker file's type:

- `.xml`: an XPath, e.g. `ModMetaData/name`
- `.json`: a dotted path, e.g. `info.title`
- anything else: a key name (`name` matches `name=Foo`, `name = "Foo"`, `name: Foo`), or a regex whose first group is the value

Example for a game whose mods have a `mod.json` and live in `mods/`:

```json
{ "AppId": "123456", "GameName": "My Game", "ModsFolder": "mods", "ModMarker": "mod.json",
  "NameKey": "info.title", "VersionKey": "info.version", "Preview": "" }
```

### Paradox games (experimental)

The **Paradox (descriptor.mod)** layout is for Stellaris (app 281990), Hearts of Iron IV (394360), Europa Universalis IV
(236850), Crusader Kings III (1158310), Crusader Kings II (203770) and Imperator: Rome (859580). Type the game's app ID,
as with Generic. Each mod folder holds a `descriptor.mod`, whose `name` and `version` are shown, and a `thumbnail.png`
preview. The Workshop id is the descriptor's `remote_file_id`; when a new item is created, its id is written back into
that line. There is no changelog convention, so a `CHANGELOG.md` is used when present and is otherwise not required.
Folders with a `descriptor.mod` are detected automatically. The games normally publish through their own launcher, so
try a new private item first.

### Golf With Your Friends (experimental)

The **Golf With Your Friends** layout (app ID 431240) treats each sub-folder as one map: a `Map` file (JSON; the map name and description are read from it), `Preview.jpg` and any `AdditionalN.jpg` screenshots. It is modelled on how a downloaded Workshop map looks on disk, but it is **unproven**: nobody has confirmed the game accepts a hand-uploaded item, and the game writes the item's `publishedID` into the `Map` file itself. Real uploads with it ask for confirmation first. Try a brand-new private item first and check it loads in the game.

### Tabletop Simulator is not supported

Tabletop Simulator (app 286160) does not work with this tool. A Workshop item for it is just a save `.json` and a `.png` thumbnail (its images and models are links to Steam cloud, Google Drive, GitHub and similar, not part of the item), and the game keeps no `steamapps\workshop\content` folder for it. It publishes through its own in-game uploader (Modding > Workshop Upload / Update Workshop), which SteamCMD's content-folder upload does not replace. Use the in-game uploader for TTS mods.

Only RimWorld is tested against real mods; the other presets are starting points. For other games, check the layout with **Dry run** first and
see the game's Workshop documentation for what a mod folder must contain.

## Requests: new games, bugs and features

The **?** button (top right, always visible) offers **Keyboard shortcuts** and **Send a request**, which opens a short form. Pick what the request is: **Support another game** (a layout request), **Report a bug** or **Suggest a feature**. Bugs and features also ask for a one-line summary and what part of the tool they are about (windows, uploading, or SteamCMD and login). For a layout request the form fills in the game name, app ID, your helper version and the file names (never contents) of one mod folder, then opens a pre-filled new issue in **Linear** (team `AZR`) in your browser. Nothing is created until you press Create in Linear, and you can edit or delete anything first. **Copy text instead** copies the text. Only people signed in to the Linear workspace can create the issue, so if you share the program, change `TeamKey` / `NewIssueUrl` in `RequestForm.cs` to your own (or tell people to use **Copy text instead** and send it to you).

### Sending requests from inside the app (maintainer setup)

Builds that carry a Linear key send requests straight from the app, with no browser or login. The key is baked in at build time and never committed:

- **GitHub builds** (CI and releases) read the repository secrets `LINEAR_CREATE_API_KEY` (a Linear personal API key restricted to **Create issues** in one dedicated team) and `LINEAR_TEAM_UUID` (that team's ID).
- **Optional routing:** the secrets `PROJECTID` (a Linear project ID) and `LABELIDS` (label IDs, separated by commas) pick the project and the labels every request gets. `LABELMAP` adds one label for the kind of request and one for the area, as `Name=label ID` pairs separated by commas, using the names `Layout`, `Bug`, `Feature`, `UI`, `Upload` and `SteamCMD` (any left out just add no label). Without these, requests go into the team alone.
- **Local builds** read the same names from a git-ignored `request.key` file in the project folder, one `NAME=value` per line. Environment variables with those names win over the file.

If Linear refuses the project or labels, for example because one was removed, the request is filed into the team alone instead, so it is never lost.

To check the secrets after setting or rotating them, run **Actions > Linear key check > Run workflow**. It files one test request into the project exactly as the app would and fails if the key, team or routing is wrong; delete the test issue afterwards.

The key is stored masked, but anyone with the exe could still extract it. Keep its scope that narrow and revoke it if it is abused. A build without a key opens a pre-filled issue instead; the build log says which (`Linear request key: baked in` or `none found`).

## Safety

- Password and Steam Guard code are typed into SteamCMD's own window, never into this app, and not saved.
- Settings (repo path, account name, theme) live in `%LocalAppData%\SteamCmdHelper\settings.json`. For portable
  mode, put an empty file named `portable.txt` next to `SteamCmdHelper.exe`: settings, history and the default SteamCMD
  then live in a `data` folder beside the exe instead (for a USB stick or a synced folder).
- If the app hits an unexpected error it saves a report to `%LocalAppData%\SteamCmdHelper\crashes` (the newest 20) and
  offers **Report it**, which opens the request form as a bug report with the details filled in for you to review.
  Nothing is sent unless you press Send.
- Only one window opens at a time, so two copies never run SteamCMD against the same login; starting it again brings
  the open window to the front. The command line is not affected.
- Self-updates are installed only when the download matches the release's SHA-256 checksum and version.
- Profiles and Discord webhook links are kept in the same settings file, not in your repo.
- The upload history lives in `%LocalAppData%\SteamCmdHelper\history.json` (the newest 1000 uploads).
- SteamCMD keeps the login cache in its own folder; keep it out of git.
- The layout file holds no secrets, but it is a new file in your repo: commit it or git-ignore it.
- Uploads publish immediately to subscribers. Use Dry run first when unsure.
- Install SteamCMD only installs a download that arrived whole and whose `steamcmd.exe` is signed by Valve.
- An upload counts as done only when SteamCMD exits cleanly and confirms it with `Success.` (and, for a new item,
  returns its id). Otherwise the row is marked failed and the package is kept in `%TEMP%\steamcmd_helper_*` to inspect.
  After updating an existing public or unlisted item, the app also asks Steam whether the item's "updated" time moved:
  that confirms the update in the log, and it counts a clean run whose `Success.` line went missing as uploaded.
- Headless checks: `SteamCmdHelper.exe [--repo <folder>] --selftest out.txt`, `... --package <ModFolder> out.txt`.
  `--shot <folder>` saves screenshots of the main window, the Options tab and the request form in both themes.

## Accessibility

Every window is laid out at 100% and scaled to the display's DPI, so it stays readable at 150% and 200% scaling, and
the main window fits the screen it opens on. Inputs carry names for screen readers (taken from their labels), the
progress bars report their percentage, and the Status column's text includes the reason a mod needs attention, not
just its badge.

## Tests

```
dotnet test tests/SteamCmdHelper.Tests
```

The tests compile the non-UI source files directly, so they run on Windows, Linux or macOS.

## Releasing (maintainer)

Run **Actions > Promote test and release > Run workflow** when `test` is ready to ship. It rebases `test` onto `main`,
checks that `<Version>` in `SteamCmdHelper.csproj` has a `## <version>` section in `CHANGELOG.md` and no tag yet, runs
the tests, then in one push fast-forwards `main`, tags it `v<version>` and resets `test` to the new `main`. The tag
starts the release below. It never changes the version: bump it on `test` first. Optional inputs: **version** stops the
run if `test` declares a different one, and **dry run** does every check and the tests but pushes nothing. If `test`
does not rebase cleanly, or someone pushes to `test` during the run, nothing is pushed.

It needs a `RELEASE_TOKEN` secret: a fine-grained personal access token for this repository with **Contents** and
**Workflows** set to read and write. The built-in Actions token cannot push commits that change workflow files, and a
tag it pushes does not start other workflows. If `main` has branch protection or a ruleset, the token's owner must be
allowed to push to it directly.

Pushing a `vX.Y.Z` tag by hand also works. Either way, the tag runs `.github/workflows/release.yml`, which tests,
builds, signs (when set up), and publishes the release. After a non-prerelease it also:

- opens a pull request pointing `bucket/steamcmdhelper.json` (the Scoop manifest) at the new exe and its SHA-256.
  This needs **Settings > Actions > General > Allow GitHub Actions to create and approve pull requests**.
- sends the new version to winget with `wingetcreate`, once a `WINGET_TOKEN` secret exists (a classic GitHub token
  with `public_repo`). The package must be in [winget-pkgs](https://github.com/microsoft/winget-pkgs) first: submit
  the manifests in `packaging/winget` once by hand (`wingetcreate submit packaging/winget`).

### Code signing

Unsigned exes trip SmartScreen and some antivirus. The release signs the exe, and the files in the framework-dependent
zip, through [SignPath](https://signpath.org) (free for open-source projects) when these are set:

- secret `SIGNPATH_API_TOKEN`
- variable `SIGNPATH_ORGANIZATION_ID`
- optional variables `SIGNPATH_PROJECT_SLUG` (default `SteamCMDHelper`) and `SIGNPATH_POLICY_SLUG`
  (default `release-signing`)

The SignPath project's artifact configuration should sign `standalone/SteamCmdHelper.exe` and
`portable/SteamCmdHelper.exe` / `portable/SteamCmdHelper.dll`. Without the secret, releases are built unsigned as before.

## License

MIT, see [LICENSE](LICENSE).