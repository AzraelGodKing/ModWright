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

## Build and run

```powershell
dotnet build SteamCmdHelper.csproj -c Release
```

The program is `bin\Release\net8.0-windows\SteamCmdHelper.exe`. Make a shortcut to it.

CI builds and tests every push to `main` and every pull request. To release, bump `<Version>` in
`SteamCmdHelper.csproj`, add a matching `## <version>` section to `CHANGELOG.md`, then push a tag such as `v1.2.0`;
the release workflow checks both, runs the tests and publishes the release with those notes.

## Using it

1. **Mods repo**: press **Browse...** (or double-click the path) and pick the folder that holds your mods (one sub-folder per mod). The path cannot be typed in, so it cannot be mistyped.
2. **Steam app ID**: the number in the game's store URL (`store.steampowered.com/app/294100/...` = RimWorld).
   The game name is looked up from Steam to confirm it. RimWorld-shaped repos are detected automatically.
3. **Layout**: pick a built-in layout (**Generic**, **RimWorld**, **Golf With Your Friends**). Built-in layouts are part of the program and are **locked**: they cannot be edited, and the layout fixes the app ID. For anything else pick **Custom**, then **Edit layout file...** (below).
   The choice is saved in `.steamhelper.json` in the repo root; for a built-in layout that file holds only its name (and your app ID for Generic).
4. **Steam account** (on the **Options** tab): type your Steam login name. The status line says whether a saved login
   exists; the same status shows next to the Upload button, and clicking it opens Options.
   **Install SteamCMD** downloads it to `%LocalAppData%\SteamCmdHelper\steamcmd` (or uses
   `<repo>\_tools\steamcmd` if that exists).
5. **Log in** happens automatically on the first upload. A SteamCMD window opens; type your password and the
   Steam Guard code there. This app never sees your password. Afterwards the login is cached.
   Logging in through SteamCMD can knock the regular Steam client offline; use Steam > Go Online afterwards.
6. Tick the mods and press **Upload**. **Dry run** does every check and builds the package but uploads nothing.

The app uses a dark theme by default. Switch to the light theme under **Options > Appearance**; the choice is saved
for your Windows user.

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

- **Uploaded version**: the version already on Steam. Steam does not store one, so it is read from the newest change
  note on the item's public changelog page (notes sent by this app for RimWorld, or by Add Changenote, start with the
  version). If that note has none, it shows what this PC last uploaded, else `?`. Private items show `?` until the
  first upload from this PC.
- **Version to upload**: the mod's current version. Double-click it to change it; the new number is written to
  `<version>` in `About/Manifest.xml` (when it has one) and the version element of an XML marker file such as
  `About/About.xml`. For RimWorld, add a changelog entry for the new version before uploading.
- **Workshop ID**: from the mod's own id file (`About/PublishedFileId.txt` for RimWorld); `0` means the upload creates a new item.
- **Preview**: ✔ when the preview image exists and is within Steam's 1 MB limit, ✖ otherwise (hover for the size).
- **Status**: `Ready`, `Fix`, or `Ready to create (private)`.

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
| `Preview`, `SteamDescriptionFile` | `preview.png`, `steam_description.txt` | Used when creating (empty `Preview` = none) |
| `BuiltFiles` | `[]` | Build outputs copied from the working tree, e.g. `["Assemblies/*.dll"]` (a warning when missing) |
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

### Golf With Your Friends (experimental)

The **Golf With Your Friends** layout (app ID 431240) treats each sub-folder as one map: a `Map` file (JSON; the map name and description are read from it), `Preview.jpg` and any `AdditionalN.jpg` screenshots. It is modelled on how a downloaded Workshop map looks on disk, but it is **unproven**: nobody has confirmed the game accepts a hand-uploaded item, and the game writes the item's `publishedID` into the `Map` file itself. Real uploads with it ask for confirmation first. Try a brand-new private item first and check it loads in the game.

### Tabletop Simulator is not supported

Tabletop Simulator (app 286160) does not work with this tool. A Workshop item for it is just a save `.json` and a `.png` thumbnail (its images and models are links to Steam cloud, Google Drive, GitHub and similar, not part of the item), and the game keeps no `steamapps\workshop\content` folder for it. It publishes through its own in-game uploader (Modding > Workshop Upload / Update Workshop), which SteamCMD's content-folder upload does not replace. Use the in-game uploader for TTS mods.

Only RimWorld is tested against real mods; the other presets are starting points. For other games, check the layout with **Dry run** first and
see the game's Workshop documentation for what a mod folder must contain.

## Requesting another game

The **?** button (top right, always visible) opens a short form for asking for a built-in layout for another game. It fills in the game name, app ID, your helper version and the file names (never contents) of one mod folder, then opens a pre-filled new issue in **Linear** (team `AZR`) in your browser. Nothing is created until you press Create in Linear, and you can edit or delete anything first. **Copy text instead** copies the text. Only people signed in to the Linear workspace can create the issue, so if you share the program, change `TeamKey` / `NewIssueUrl` in `RequestForm.cs` to your own (or tell people to use **Copy text instead** and send it to you).

### Sending requests from inside the app (maintainer setup)

Builds that carry a Linear key send requests straight from the app, with no browser or login. The key is baked in at build time and never committed:

- **GitHub builds** (CI and releases) read the repository secrets `LINEAR_CREATE_API_KEY` (a Linear personal API key restricted to **Create issues** in one dedicated team) and `LINEAR_TEAM_UUID` (that team's ID).
- **Optional routing:** the secrets `PROJECTID` (a Linear project ID) and `LABELIDS` (label IDs, separated by commas) pick the project and labels requests are filed under. Without them requests go into the team alone.
- **Local builds** read the same names from a git-ignored `request.key` file in the project folder, one `NAME=value` per line. Environment variables with those names win over the file.

If Linear refuses the project or labels, for example because one was removed, the request is filed into the team alone instead, so it is never lost.

To check the secrets after setting or rotating them, run **Actions > Linear key check > Run workflow**. It files one test request into the project exactly as the app would and fails if the key, team or routing is wrong; delete the test issue afterwards.

The key is stored masked, but anyone with the exe could still extract it. Keep its scope that narrow and revoke it if it is abused. A build without a key opens a pre-filled issue instead; the build log says which (`Linear request key: baked in` or `none found`).

## Safety

- Password and Steam Guard code are typed into SteamCMD's own window, never into this app, and not saved.
- Settings (repo path, account name, theme) live in `%LocalAppData%\SteamCmdHelper\settings.json`.
- SteamCMD keeps the login cache in its own folder; keep it out of git.
- The layout file holds no secrets, but it is a new file in your repo: commit it or git-ignore it.
- Uploads publish immediately to subscribers. Use Dry run first when unsure.
- Install SteamCMD only installs a download that arrived whole and whose `steamcmd.exe` is signed by Valve.
- An upload counts as done only when SteamCMD exits cleanly and confirms it with `Success.` (and, for a new item,
  returns its id). Otherwise the row is marked failed and the package is kept in `%TEMP%\steamcmd_helper_*` to inspect.
- Headless checks: `SteamCmdHelper.exe [--repo <folder>] --selftest out.txt`, `... --package <ModFolder> out.txt`.
  `--shot <folder>` saves screenshots of the main window, the Options tab and the request form in both themes.

## Tests

```
dotnet test tests/SteamCmdHelper.Tests
```

The tests compile the non-UI source files directly, so they run on Windows, Linux or macOS.

## License

MIT, see [LICENSE](LICENSE).