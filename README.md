# SteamCMD Helper

Windows desktop app for uploading RimWorld mod updates to the Steam Workshop without remembering any
commands. Open it, tick the mods, press **Upload**. It shows each mod's version and Workshop id, checks
the changelog before sending anything, handles the Steam login, and shows progress bars and a live log.

It does the work through `scripts\steam_publish.ps1` in your mods repo (one copy of the upload logic),
and uses the mod's `About\changelog.txt` as the Workshop release notes.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build (the published `.exe` needs nothing)
- A mods repo that contains `scripts\steam_publish.ps1` (branch `feat/steam-workshop-upload` of `rimworld_mods` until it is merged)

## Build and run

From this folder:

```powershell
dotnet build -c Release
```

The program is `bin\Release\net8.0-windows\SteamCmdHelper.exe` (a small file; it needs the .NET 8 runtime,
which the SDK includes). Make a shortcut to it on the desktop. `dotnet run` also works while developing.

Optional self-contained single-file build, which needs no .NET install:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Output is under `bin\Release\net8.0-windows\win-x64\publish\`. On the author's PC Windows refused to launch
that large unsigned file ("Access is denied"), so the normal build above is the one to use there.
## Using it

1. **Mods repo**: pick the folder that holds your mods (default `F:\Repositories\rimworld_mods`). The current git branch is shown.
2. **Steam account**: type your Steam login name. The status line says whether a saved login exists.
   If SteamCMD is missing, **Install SteamCMD** downloads it into the repo's `_tools\steamcmd\`.
3. **Log in** happens automatically on the first upload. A SteamCMD window opens; type your password and the
   Steam Guard code there and wait for it to close. This app never sees your password. Afterwards the login is
   cached and uploads go straight through. **Check login** tests the saved login at any time.
4. Tick the mods and press **Upload**. A confirmation lists what will be sent.
   **Dry run** does every check and builds the package but uploads nothing.

Each row shows the mod's problems before you start (red): missing or empty changelog, changelog version not
matching `About.xml`, mod not built yet (no DLL), and so on. A grey warning means uncommitted changes in that
mod, which the script does not upload (it packages from git `HEAD` plus the built DLL).

### Mods not on Steam yet

Tick **Allow creating new items**. Those mods then show as "Ready to create (private)". They are created
private, with the title from `About.xml`, `About\Preview.png`, and the description from
`About\steam_description.txt` (or the `About.xml` description). The new id is saved to
`About\PublishedFileId.txt`; commit it. Never repeat a create after a failure without checking your Steam
profile first, or you can end up with a duplicate item.

## Safety

- The password and Steam Guard code are typed into SteamCMD's own window, never into this app, and are not saved.
- Settings (repo path, account name) live in `%LocalAppData%\SteamCmdHelper\settings.json`.
- SteamCMD keeps the login cache in its own folder. In `rimworld_mods` that folder is git-ignored; keep it that way.
- Uploads publish immediately to subscribers. Use Dry run first when unsure.
