# SteamCMD Helper

Windows desktop app for uploading Steam Workshop mod updates without remembering any commands. Open it,
tick the mods, press **Upload**. It shows each mod's version and Workshop id, checks the changelog before
sending anything, handles the Steam login, and shows progress bars and a live log.

It works for **any game with a Steam Workshop**: you give it the game's Steam app ID and tell it how your
mods are laid out. The upload logic is built in; the only outside tool it needs is SteamCMD.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (running needs the .NET 8 runtime, which the SDK includes)
- `git` on PATH if your repo uses the default packaging (`UseGitArchive`)
- SteamCMD: the app can download it for you (**Install SteamCMD**)
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
3. **Layout**: pick **Generic** (every sub-folder is a mod) or **RimWorld**, or press **Edit layout file...** for a custom layout (below).
   App ID and layout are saved in `.steamhelper.json` in the repo root.
4. **Steam account**: type your Steam login name. The status line says whether a saved login exists.
   **Install SteamCMD** downloads it to `%LocalAppData%\SteamCmdHelper\steamcmd` (or uses
   `<repo>\_tools\steamcmd` if that exists).
5. **Log in** happens automatically on the first upload. A SteamCMD window opens; type your password and the
   Steam Guard code there. This app never sees your password. Afterwards the login is cached.
   Logging in through SteamCMD can knock the regular Steam client offline; use Steam > Go Online afterwards.
6. Tick the mods and press **Upload**. **Dry run** does every check and builds the package but uploads nothing.

Rows show problems in red (missing changelog, changelog version not matching the mod's version, nothing
built, etc.). A warning means uncommitted tracked changes (git packaging uses `HEAD`).

### Mods not on Steam yet

Tick **Allow creating new items**. They are created private, with the title, description and preview image
from your layout. The new id is saved to the id file; commit it.

## Layout file: `.steamhelper.json`

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
| `ChangelogFirstLineIsVersion` | false | Line 1 is the version (must match the marker's version) and the rest is the note |
| `WorkshopIdFile` | `workshop_id.txt` | Stores the Workshop id |
| `Preview`, `SteamDescriptionFile` | `preview.png`, `steam_description.txt` | Used when creating (empty `Preview` = none) |
| `BuiltFiles` | `[]` | Build outputs copied from the working tree, e.g. `["Assemblies/*.dll"]`, and required to exist |
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

### Tabletop Simulator (experimental)

The **Tabletop Simulator** layout (app ID 286160) treats each sub-folder as one mod: its save `.json`, the images and models it uses, and `preview.png` / `steam_description.txt` for creating. Version and changelog are optional. This preset is **unproven**: TTS normally uploads from inside the game (Modding > Workshop Upload), which packages the item itself, and I could not confirm that a raw SteamCMD upload produces an item the game loads. The app asks for confirmation before any real upload with it. Try a brand-new private item first and check it in the game before updating anything you care about.

Only RimWorld is tested against real mods; the other presets are starting points. For other games, check the layout with **Dry run** first and
see the game's Workshop documentation for what a mod folder must contain.

## Safety

- Password and Steam Guard code are typed into SteamCMD's own window, never into this app, and not saved.
- Settings (repo path, account name) live in `%LocalAppData%\SteamCmdHelper\settings.json`.
- SteamCMD keeps the login cache in its own folder; keep it out of git.
- The layout file holds no secrets, but it is a new file in your repo: commit it or git-ignore it.
- Uploads publish immediately to subscribers. Use Dry run first when unsure.
- Install SteamCMD only installs a download that arrived whole and whose `steamcmd.exe` is signed by Valve.
- An upload counts as done only when SteamCMD exits cleanly and confirms it with `Success.` (and, for a new item,
  returns its id). Otherwise the row is marked failed and the package is kept in `%TEMP%\steamcmd_helper_*` to inspect.
- Headless checks: `SteamCmdHelper.exe [--repo <folder>] --selftest out.txt`, `... --package <ModFolder> out.txt`.

## Tests

```
dotnet test tests/SteamCmdHelper.Tests
```

The tests compile the non-UI source files directly, so they run on Windows, Linux or macOS.

## License

MIT, see [LICENSE](LICENSE).