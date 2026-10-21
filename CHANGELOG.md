# Changelog

## Unreleased

- **The Drop Pod: a new tab for the RimWorld mods you play with.** It finds RimWorld through your Steam libraries (or a
  folder you pick), reads every mod from the base game and DLC, the Mods folder and the Workshop, and lists each one's
  name, author, package ID, where it came from, the game versions it supports and its place in the current load order.
  Mods with a broken About.xml show in the warning colour with what is wrong, mods made for another game version are
  greyed out, and package IDs installed twice are named at the top. Ctrl+Tab now steps through Upload, Drop Pod and
  Options.
- **The Drop Pod sorts and saves your load order.** Tick mods to make them active, move them with Up and Down
  (Alt+Up/Down), and Sort orders them so each loads after what it needs and its loadAfter list and before its
  loadBefore list (the version-specific lists for your game version included), with the base game and DLC first.
  Where no rule decides it keeps your order, or goes A to Z. Mods whose rules go round in a loop are named instead of
  silently dropped. Save to game writes ModsConfig.xml, keeping the old one as a backup, and refuses while RimWorld is
  running (it would overwrite the list when it closes). You can add your own rules file (More > Use my own load-order
  rules file) to put mods after or before others, or first or last; nothing is bundled.
- **Modlists.** Save the active mods as a named modlist and switch between them in one click. Export one to a file,
  import a file (also RimWorld's own saved .rml lists), or share it as a single line of text that starts with
  DROPPOD1: and paste one in. Mods a list names that aren't installed are listed when you switch.
- **A closer look at About.xml before uploading.** Besides a missing or malformed packageId, the RimWorld check now
  warns when About.xml has no name, author, description or supportedVersions, when a version is written as "1.5.4104"
  instead of "1.5", when a dependency has no packageId, no Workshop or download link, or an id RimWorld can't use, and
  when loadAfter, loadBefore or incompatibleWith name the mod itself, something that isn't a packageId, a mod that is
  also a dependency, or the same mod both before and after. Options > Uploading can make these warnings block the
  upload too, and the new `SteamCmdHelper lint` command runs every check without uploading (`--strict` for the same
  rule). The check reads About.xml with the same parser as the Drop Pod.
- **A new Kats theme, and the theme is now a drop-down.** Options > Appearance lists Dark, Light, Kats and Match Windows
  in one drop-down instead of a row of buttons, so more themes fit. Kats pairs gunmetal grey with a vivid orange
  (#FF5C00) for buttons, ticks and selection, with dark text on the orange so it stays easy to read. The brand bar
  follows a dark theme's own colours; only the light theme keeps the dark bar.

## 1.6.0

- **The space beside the mod list is put to use.** On a wide window the list's columns ended about halfway across and
  the rest was empty. A details panel now sits there: the selected mod's preview image at a size you can see, the
  version to upload and the one on Steam, its subscribers, favourites, views, size and visibility on Steam, its last
  upload from this PC and its Workshop tags, what it still needs, the change note and description it will upload with,
  and buttons for its Workshop page, version, Steam page, stats, folder, package preview and upload history. With no
  mod selected it counts the list by status, the ticked mods and the subscribers across them. Drag the bar between the
  list and the panel to resize it (the width is remembered), or turn it off in Options > Appearance. When the window
  is too narrow for both, the panel stays hidden so the columns keep their room.
- **The Mods card's title row fits again.** Since the Groups button joined it, the row was wider than the window at its
  default size, so the MODS heading and the start of the filter box were cut off at the left. Its buttons are now the
  compact kind the Log card uses, and the filter box is a little narrower.

## 1.5.0

- **Steam comes back online after uploading.** Steam lets an account sign in once at a time, so SteamCMD's login left
  the Steam client disconnected until you restarted it. When Steam was running before an upload, it is now closed and
  started again in the tray, where its saved login signs it back in: before the tags, required items, gallery and
  translations that go through it, and once the batch ends. It is left alone while a game is running or when Steam
  signs in as a different account. On by default (Options > Uploading), also for command-line and scheduled uploads;
  **Restart Steam now** there does it by hand, and Check setup warns when Steam has no saved login to sign back in with.
- **A tidier right-click menu.** The mod list's menu had grown to 18 items in one long list. The everyday ones (Workshop
  page, version, changelog entry) stay at the top, and the rest are grouped: **On Steam** (open, stats and comments,
  tags, required items, checking and downloading Steam's copy, game betas), **Package** (preview, upload history, kept
  packages, shrinking the preview image), **Open**, **Copy** and **Ticked mods**. Ctrl+K still finds every item, named
  with its group ("Copy: Workshop ID"), and submenus follow the dark or light theme.
- **The request window fits its contents.** With Report a bug or Suggest a feature picked, the request row was wider
  than the window, so the summary box, the "About" choice and the Cancel button were cut off at the right, worst with
  larger text. The window now widens to fit, its drop-downs share the row, and the explanations wrap at its width.

## 1.4.1

- **What's new reads properly.** The window that shows the changes after an update (and from Options > About) broke
  every item mid-sentence where this changelog wraps its lines, with stray indents and no bold. It now shows a heading
  per version, real bullets that wrap to the window's width, the lead phrase in bold and commands in a fixed-width
  font. The release notes on GitHub no longer break mid-sentence either.

## 1.4.0

- **Secrets never ship.** Every package is searched before upload for things that must never become public: Steam's
  own saved login (`ssfn*` files, a `config.vdf` with a login cache), private keys and key stores, `.env` files, the
  request key, and tokens with a known shape (GitHub, Discord webhooks, Slack, AWS, Google, Linear, Anthropic). One of
  those stops the upload and names the file and line, never the secret. Looser shapes (an OpenAI-style key, a Steam
  Web API key) are warned about.
- **The confirmation warns about more.** With git packaging it says when the branch is behind its upstream (you would
  upload older work) or has commits that are not pushed while GitHub releases are on. Unclosed or stray Steam BBCode
  tags in a change note or a new description are pointed out, and a new item's preview whose real format Steam does
  not take (a WebP, BMP or TIFF, whatever its extension says), that is tiny, or whose extension does not match is
  flagged in the list.
- **Preview as on Steam.** The upload confirmation and the Workshop page editor open the change note or description
  in the browser the way Steam shows BBCode: headings, lists, links, quotes, code, spoilers and tables.
- **RimWorld: translation gaps and Harmony.** Keys the English `Keyed` files have and a language lacks (players see
  the raw key), and keys English no longer has, are warned about per language. So are a mod that ships its own
  `0Harmony.dll` and one whose code uses Harmony without listing `brrainz.harmony` in `modDependencies`.
- **Add a game version to ticked mods.** When a new RimWorld version comes out, one action adds it to
  `supportedVersions` of every ticked mod, copies the newest version folder to it and adds it to `loadFolders.xml`
  (`SteamCmdHelper game-version 1.7 --all`, `--no-copy` for About.xml only).
- **Change the version of ticked mods** at once (patch, minor, major or a number), each with its changelog line.
- **Mod groups.** **Groups** in the Mods card saves the ticked mods as a named group for that mods folder and ticks a
  group in one click; `upload --group Core` on the command line.
- **Command palette.** Ctrl+K (or Ctrl+Shift+P) finds any button, menu item or Options section by name and runs it.
- **A notification when a run ends** while the window is in the background, with how it went (Options > Uploading).
- **After uploading.** Upload tags can be pushed to the remote straight away (Options > Git & GitHub;
  `upload --tag --push-tags`). A command per mods folder runs after each successful upload with the mod's name,
  folder, version, Workshop id and link, commit and change note in `SCH_*` environment variables
  (`upload --run-after`). And a zip of exactly what each upload sent can be kept (the newest 5 per mod; **Uploaded
  packages** on the right-click menu).
- **Lists and history out.** **Copy the mod list as** Markdown, Steam BBCode or CSV (`list --markdown`, `list --csv`),
  and the upload history as CSV from the history window, Options > About or `history --csv`.
  `SteamCmdHelper note MyMod` prints the change note exactly as Steam would get it.
- **Check setup.** Options > About > **Check setup** (and `SteamCmdHelper doctor`) goes through everything an upload
  needs: the mods folder, its layout and app ID, how many mods need fixing, SteamCMD and the saved login (really
  logging in from the window, or with `doctor --login`), git when the layout packages with it, gh when GitHub releases
  are on, the Discord link, the Steam client when tags or required items go through it, the Thunderstore token, free
  disk space and the data folder. It says what to fix and changes nothing.
- **Upload later.** **Later...** next to Upload schedules the ticked mods for a time you pick through Windows Task
  Scheduler, which runs the command line with your saved login, so the window need not stay open; if the PC is off
  then, it runs as soon as it can. The same window lists and cancels scheduled uploads
  (`SteamCmdHelper schedule --at "2026-10-21 18:00" MyMod`, `schedule --list`, `schedule --cancel 1`).

## 1.3.8

- **Thunderstore too.** For BepInEx games, a mod whose folder has Thunderstore's manifest.json, icon.png and README.md
  can be published to Thunderstore from the same package as Steam (right-click > **Publish to Thunderstore**, or
  after every Steam upload). The version to upload goes into the packaged manifest, what Thunderstore would refuse
  (name, version, description length, icon size, dependency format) is named first, and a version Thunderstore
  already has is skipped. Team, community and categories are set per mods folder in **Options > Uploading**; the
  service account token is kept encrypted for your Windows user.
- **The app in German, Russian and Chinese.** Options > Appearance > Language switches the windows' buttons, labels,
  menus and tooltips to Deutsch, Русский or 简体中文 from the next start (log lines and messages stay in English).
  **Make a translation file** writes every text the windows showed, filled in where the language has it, so a
  translation can be fixed or a new language added by putting the file in the data folder's languages folder.
- **New mod.** Ctrl+N (or **New mod...** on the right-click menu and the empty list) makes a ready-to-upload mod folder
  for the current layout. For RimWorld that is an About.xml with a packageId made from the author and name, the
  supported versions (the newest any of your mods supports by default), a placeholder preview, the changelog entry
  and the description file, and optionally a C# project with Harmony that builds into the newest version's Assemblies
  folder. Paradox mods get a descriptor.mod. The author is remembered for the next one.
- **Required items are set on the Workshop page.** After each upload, the item's "Required items" on Steam are set from
  the Workshop links in the mod's metadata (RimWorld's `modDependencies`), so players who subscribe are offered what the
  mod needs. Like tags, this goes through the Steam client, which has to be running as the item's owner. Items this app
  added are taken off when the mod stops listing them; items added by hand on Steam stay. **Update required items** on
  a mod's right-click menu does it without uploading, and **Options > Uploading** can turn it off.
- **RimWorld mod checks.** Before upload, RimWorld mods are checked for what would give players red errors: an XML file
  in `Defs`, `Patches`, `Languages` or `About` that does not parse, or a missing `packageId`, stops the upload with the
  file and line. A `packageId` RimWorld would refuse, two mods with the same `packageId`, a `modDependencies` entry
  without one, `loadFolders.xml` entries for folders that are not there or versions it leaves out, and version folders
  that `supportedVersions` does not list are warned about.
- **Workshop pages of ticked mods...** (right-click, with two or more mods ticked) sets one visibility, replaces a
  phrase or adds text at the end of every ticked mod's description at once. The changes wait for each mod's next upload
  and show in the upload confirmation, like single edits. An item whose page Steam won't show keeps its description.
- **Workshop stats.** Right-click a mod > **Stats and comments...** charts current subscribers, favourites, unique
  visitors and subscribers ever, for one mod or all of them together, with the change over 7 and 30 days. The counts
  come from Steam's public item details once a day; favourites also show in the row tooltip.
- **New comments.** **Count comments** in the same window counts the comments on every mod through the Steam client and
  names the mods with new ones in the log and their row tooltip; **Open comments** opens the item's comments and marks
  them read. **Options > Workshop page** can count them at start-up while Steam is running (at most every 6 hours).
- **Gallery images and videos.** A `Gallery` folder in a RimWorld mod (any folder, for custom layouts: `GalleryFolder`)
  holds PNG, JPG or GIF images and a `videos.txt` of YouTube links. After each upload the item's extra images and
  videos on Steam are made to match it: new ones are added, changed images replaced, ones the folder no longer has
  removed. The folder is never packaged.
- **Descriptions in other languages.** A file next to the description file such as `steam_description.german.txt` (or
  `.de.txt`, `.zh.txt` and other short codes) is sent as that language's description, and a first line `Title: ...` as
  its title, so players with Steam in that language read the page in it. Only changed translations are sent again.
- Gallery and translations go through the Steam client like tags. **Update required items, gallery and languages** on
  the right-click menu sends them without uploading, and **Options > Uploading** can turn them off.
- **Dependency updates are pointed out.** The upload confirmation says when a Workshop item a mod needs (Harmony, a
  framework) was updated on Steam after the mod's own last upload, so it can be tried in game with it first.
- **Shared description footer** (Options > Workshop page > Description, per mods folder): text such as Discord, Ko-fi
  or GitHub links added to the end of every description sent from a description file, with `{name}`, `{id}` and
  `{game}` filled in. Changing it counts as a changed description, so each mod offers its new text at its next upload.
  New items are created with it too.
- **Move to another PC.** **Options > About > Export settings...** saves the settings, profiles, per-folder options and
  upload history in one zip; **Import settings...** on the new PC asks where each mods folder is now and keeps a backup
  of what it replaces. SteamCMD and its saved login are never included.
- **What's new.** The first start after an update shows the changelog of the versions just installed, once, and
  **Options > About > What's new...** shows every version's changes.

## 1.3.7

- **Refresh keeps your place.** Refreshing (F5, or the refresh after an upload, a version change or a changelog entry)
  keeps the mods you ticked, the selected mod and the scroll position, instead of ticking every mod again. Mods that
  just uploaded are unticked, so pressing Upload again doesn't send them twice.
- **One Steam login per upload.** When the login worked in the last 15 minutes, Upload goes straight to SteamCMD instead
  of starting it once more just to check. If Steam refuses the login after all, **Log in again and retry** appears as
  before.
- **The window never freezes on Refresh.** Every refresh reads the mods folder and git in the background.
- **A faster log.** New lines are drawn about ten times a second instead of one by one, so SteamCMD's output no longer
  slows the window down, and the log box is trimmed so a long session stays quick.
- **Upload checks ride along.** With **Check uploads** and one login per batch, Steam's copies are downloaded back in
  the same SteamCMD run as the uploads, so a batch of ten logs in once instead of eleven times.
- **Packages are built ahead.** A batch packages the next mods (up to three at a time) while the current one is
  checked and uploaded.
- **The list refreshes by itself.** When you come back to the window and a mod was changed, added or removed, or a
  commit was made, the list refreshes. It never does this during an upload or while a dialog is open.
- **The window is remembered.** Its size and place (and whether it was maximised), the list's column widths, the sort
  column and the last Options section come back the next time the app starts.
- **The list stays usable during an upload.** You can scroll it, select mods, read their details and use the search
  box and **Hide up to date** while an upload runs. Ticks and edits still wait until it's done.
- **Less waiting on git.** Whether the folder is a git repo is asked once per refresh or upload instead of for every mod.

## 1.3.6

- **Options are sorted into sections.** The Options tab now works like Steam's own settings window: a list down the
  left (Account, Uploading, Workshop page, SteamCMD, Git & GitHub, Appearance, About) and one section at a time on the
  right, with its name and what it is for at the top. Uploading is split into Before, While and After uploading (the
  Discord and GitHub release settings now live under After uploading), and cards whose settings belong to the current
  mods folder say so in their title. **Ctrl+PageDown** / **Ctrl+PageUp** (or Up and Down in the list) move between
  sections, and links such as the login status next to Upload open the right section. Every setting and its saved
  value is unchanged.

## 1.3.5

- **A new, more polished look.** The app now follows the style of Steam's own desktop client: deep slate-blue
  surfaces, a dark brand bar across the top (in the light theme too) with the name, a new icon and capital-letter tabs,
  card headings in capitals over a fading blue line, softly rounded buttons in light blue on slate, blue for the
  suggested next step and a green **Upload** button. A strip along the bottom shows the version next to the "not
  affiliated" line. The light theme gets the same treatment in light colours, and Windows High Contrast is unchanged.

## 1.3.4

- **One SteamCMD login per batch.** A new option (off by default, `--one-session` on the command line) packages and
  checks every ticked mod, then uploads them all in one SteamCMD run, so a batch logs in once instead of once per mod.
- **Check uploads on Steam.** An option (off by default, `--verify`) downloads each upload back with
  `workshop_download_item` and compares its files with what was sent. **Check what Steam hands out** on the right-click
  menu (and `verify <mod>`) does the same at any time.
- **Download from Steam.** Right-click a mod (or `download <mod|id> --to <folder>`) to save Steam's copy into an empty
  folder, for when the local copy is lost.
- **New game builds are noticed.** At start-up (at most every 12 hours) the app reads the game's branches with
  SteamCMD's anonymous login and says when there is a new public build or a beta changed. **Options > SteamCMD >
  Game branches** and `game-builds` list them.
- **Try in a game beta.** Installs a separate copy of the game on any branch with SteamCMD, and puts the selected mod
  and the Workshop items it depends on into its mods folder (`test-install` on the command line).
- **Upload from GitHub Actions.** **Options > Git > Set up GitHub Actions upload** (or `ci-setup`) writes a workflow
  that uploads changed mods when a `v*` tag is pushed. **Copy login secret** (or `login-secret`) gives the saved login
  for its secret.
- **Ownership check.** The first upload for a game checks that the account owns it (`licenses_print`) and warns when
  it doesn't, since Steam would refuse the upload.
- **No more hanging runs.** SteamCMD now runs with `@ShutdownOnFailedCommand` and `@NoPromptForPassword` whenever
  nobody can type into it, so an expired login ends a scheduled or CI run with an error instead of waiting.
- The README explains which features use Steam's anonymous login and which need your saved login.

## 1.3.3

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
