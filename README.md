# SteamCMD Helper

Windows desktop app for uploading Steam Workshop mod updates without remembering any commands. Open it,
tick the mods, press **Upload**. It shows each mod's version and Workshop id, checks the changelog before
sending anything, handles the Steam login, and shows progress bars and a live log.

It works for any Steam game's mods: the app id and the folder layout are configurable (see below). The
upload logic is built in; there is no script or other dependency besides SteamCMD itself.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (running needs the .NET 8 runtime, which the SDK includes)
- `git` on PATH if your repo uses the default packaging (`UseGitArchive`)
- SteamCMD: the app can download it for you (**Install SteamCMD**)

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

1. **Mods repo**: pick the folder that holds your mods (one sub-folder per mod).
2. **Steam account**: type your Steam login name. The status line says whether a saved login exists.
   **Install SteamCMD** downloads it to `%LocalAppData%\SteamCmdHelper\steamcmd` (or uses
   `<repo>\_tools\steamcmd` if that exists).
3. **Log in** happens automatically on the first upload. A SteamCMD window opens; type your password and the
   Steam Guard code there. This app never sees your password. Afterwards the login is cached.
4. Tick the mods and press **Upload**. **Dry run** does every check and builds the package but uploads nothing.

Rows show problems in red (missing changelog, changelog version not matching the mod's version, nothing
built, etc.). A warning means uncommitted tracked changes (git packaging uses `HEAD`).

### Mods not on Steam yet

Tick **Allow creating new items**. They are created private, with the title, description and preview image
from your layout. The new id is saved to the id file; commit it.

## Layout file: `.steamhelper.json`

Without a file, the RimWorld layout is assumed. For another game, press **Edit layout file...**, which writes
`.steamhelper.json` in the repo root with every default filled in. Fields:

| Field | Default | Meaning |
|---|---|---|
| `AppId` / `GameName` | 294100 / RimWorld | Steam app id and display name |
| `ModMarker` | `About/About.xml` | File that marks a folder as a mod (relative to the mod folder) |
| `NameXPath`, `VersionXPath`, `DescriptionXPath` | `ModMetaData/...` | Where to read name, version, description in the marker XML |
| `Changelog` | `About/changelog.txt` | Release notes file |
| `ChangelogFirstLineIsVersion` | true | First line is the version and must match the marker's version; the rest is the Workshop note |
| `WorkshopIdFile` | `About/PublishedFileId.txt` | Stores the Workshop id |
| `Preview`, `SteamDescriptionFile` | `About/Preview.png`, `About/steam_description.txt` | Used when creating |
| `BuiltFiles` | `["Assemblies/*.dll"]` | Build outputs copied from the working tree (not git) |
| `UseGitArchive` | true | Package with `git archive HEAD` (honours `export-ignore`); false copies the folder using `Exclude` |
| `Exclude` | `.git`, `bin`, `obj`, `*.md`, ... | Skipped when `UseGitArchive` is false |

## Safety

- Password and Steam Guard code are typed into SteamCMD's own window, never into this app, and not saved.
- Settings (repo path, account name) live in `%LocalAppData%\SteamCmdHelper\settings.json`.
- SteamCMD keeps the login cache in its own folder; keep it out of git.
- Uploads publish immediately to subscribers. Use Dry run first when unsure.
- Install SteamCMD only installs a download that arrived whole and whose `steamcmd.exe` is signed by Valve.
- An upload counts as done only when SteamCMD exits cleanly and confirms it with `Success.` (and, for a new item,
  returns its id). Otherwise the row is marked failed and the package is kept in `%TEMP%\steamcmd_helper_*` to inspect.
- Headless checks: `SteamCmdHelper.exe --selftest out.txt`, `SteamCmdHelper.exe --package <ModFolder> out.txt`.
## Tests

```
dotnet test tests/SteamCmdHelper.Tests
```

The tests compile the non-UI source files directly, so they run on Windows, Linux or macOS.

## License

MIT, see [LICENSE](LICENSE).
