namespace SteamCmdHelper.DropPod;

/// <summary>Where a game is installed and where it keeps the player's own files.</summary>
/// <param name="GameFolder">The game's install folder.</param>
/// <param name="Version">The installed game version as the game writes it ("1.5.4104 rev435"), or null when unknown.</param>
/// <param name="WorkshopFolder">Steam's Workshop content folder for the game, or null when there is none.</param>
/// <param name="DataFolder">The player's config and saves folder.</param>
public sealed record GameInstall(string GameFolder, string? Version, string? WorkshopFolder, string DataFolder);

/// <summary>How to start the game: the program, its arguments and the folder to start it in.</summary>
public sealed record GameLaunch(string Exe, IReadOnlyList<string> Args, string WorkingDirectory);

/// <summary>A folder that holds mods, one per sub-folder, and where those mods come from.</summary>
public sealed record ModFolder(string Path, ModSource Source);

/// <summary>
/// Everything the Drop Pod needs to know about one game. The core (scanning, sorting, problem checks) works only through
/// this, so another game needs its own module and nothing else.
/// </summary>
public interface IGameModule
{
    string Name { get; }
    string AppId { get; }
    /// <summary>The game's process names (no ".exe"), to tell whether it is running before its files are changed.</summary>
    IReadOnlyList<string> ProcessNames { get; }

    /// <summary>Finds the install from Steam's libraries, or uses <paramref name="gameFolder"/> / <paramref name="dataFolder"/> when given. Null when the game is not found.</summary>
    GameInstall? Detect(SteamLibraries steam, string? gameFolder, string? dataFolder);

    /// <summary>The folders whose sub-folders are mods, in the order the game prefers copies (first wins).</summary>
    IReadOnlyList<ModFolder> ModFolders(GameInstall install);

    /// <summary>Reads one mod folder. Null when the folder is not a mod at all (no manifest).</summary>
    ModManifest? ReadMod(string folder, ModSource source);

    /// <summary>The ids of the active mods in load order, or null when the game has no list yet.</summary>
    IReadOnlyList<string>? ReadActiveMods(GameInstall install);

    /// <summary>Writes the active mods (ids in load order) where the game reads them, keeping a backup of the old list.</summary>
    void WriteActiveMods(GameInstall install, IReadOnlyList<string> ids);

    /// <summary>The id the game uses for an entry in its active list (RimWorld: lower case, without "_steam").</summary>
    string ActiveKey(string id);

    /// <summary>
    /// How to start the game from <paramref name="install"/>. With <paramref name="ownDataFolder"/> the game is told to
    /// keep its config, mod list and saves in the install's data folder (an instance) instead of its usual place;
    /// <paramref name="logFile"/>, when given, is where it writes its log. Null when the game's program is not found.
    /// </summary>
    GameLaunch? Launch(GameInstall install, bool ownDataFolder, string? logFile);

    /// <summary>Where the game writes its log when started normally from <paramref name="install"/>.</summary>
    string LogFile(GameInstall install);

    /// <summary>Files in the data folder worth copying into a new instance (options, key bindings), relative to it.</summary>
    IReadOnlyList<string> SettingsFiles { get; }

    /// <summary>The save game files of <paramref name="install"/>.</summary>
    IReadOnlyList<string> SaveFiles(GameInstall install);

    /// <summary>Reads a save's game version and mods. Never throws: a save that can't be read has <see cref="SaveInfo.Error"/> set.</summary>
    SaveInfo ReadSave(string path);

    /// <summary>Where a mod goes when no rule decides: lower is earlier (RimWorld: the base game, then each DLC).</summary>
    int SortTier(ModManifest mod);
}
