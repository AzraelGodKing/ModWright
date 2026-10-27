using System.Diagnostics;
using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <summary>
/// "SteamCmdHelper droppod ...": the Drop Pod without the window, for desktop shortcuts, Steam launch entries and
/// scripts. Lists modlists and instances, shows the active mods and their problems, switches the game to a modlist and
/// starts the game, each optionally for an instance.
/// </summary>
public sealed class DropPodCli
{
    public const string Usage = """
        Modwright: The Drop Pod from the command line

        Usage:
          SteamCmdHelper droppod lists                       the saved modlists (* marks the one in use)
          SteamCmdHelper droppod instances                   the instances
          SteamCmdHelper droppod active [--json]             the active mods in load order
          SteamCmdHelper droppod problems [--json]           missing dependencies, order and version problems
                                                             (exit code 1 when there are errors)
          SteamCmdHelper droppod switch <modlist>            make a modlist the game's active mods
          SteamCmdHelper droppod play [<modlist>]            start the game (after switching to the modlist)

        Options:
          --instance <name>      use an instance instead of the normal game
          --json                 print JSON, for scripts

        Exit codes: 0 done, 1 problems found or it failed, 2 bad arguments or the game not found.
        """;

    readonly IGameModule _game;
    readonly Func<GameInstall?> _detect;
    readonly Func<GameInstall, GameInstance, GameInstall> _applyInstance;
    readonly ModlistStore _lists;
    readonly InstanceStore _instances;
    readonly string _historyRoot;
    readonly AppSettings _settings;
    readonly Func<bool> _gameRunning;
    readonly Func<ProcessStartInfo, GameInstall, GameInstance?, int> _start;
    readonly Action _saveSettings;

    /// <param name="start">Starts the game; returns the exit code for the command.</param>
    public DropPodCli(IGameModule game, Func<GameInstall?> detect, Func<GameInstall, GameInstance, GameInstall> applyInstance, ModlistStore lists, InstanceStore instances,
        string historyRoot, AppSettings settings, Action saveSettings, Func<bool> gameRunning, Func<ProcessStartInfo, GameInstall, GameInstance?, int> start)
    {
        _game = game;
        _detect = detect;
        _applyInstance = applyInstance;
        _lists = lists;
        _instances = instances;
        _historyRoot = historyRoot;
        _settings = settings;
        _saveSettings = saveSettings;
        _gameRunning = gameRunning;
        _start = start;
    }

    public int Run(string[] args, TextWriter output)
    {
        void Say(string s) => output.WriteLine(s);
        string? command = null, instance = null, name = null;
        var json = false;
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--json") json = true;
            else if (a == "--instance" && i + 1 < args.Length) instance = args[++i];
            else if (a is "help" or "--help" or "-h") { Say(Usage); return 0; }
            else if (a.StartsWith("--", StringComparison.Ordinal)) return Bad($"unknown option {a}");
            else if (command == null) command = a.ToLowerInvariant();
            else if (name == null) name = a;
            else return Bad($"unexpected {a}");
        }
        if (command == null) { Say(Usage); return 0; }

        int Bad(string why)
        {
            Say("Error: " + why);
            Say("");
            Say(Usage);
            return 2;
        }

        if (command == "lists")
        {
            var current = _settings.CurrentModlists.GetValueOrDefault(_game.AppId);
            var all = _lists.All(_game.AppId);
            if (json) Say(JsonSerializer.Serialize(all.Select(l => new { name = l.Name, mods = l.Mods.Count, current = l.Name == current })));
            else if (all.Count == 0) Say("No modlists saved yet. Make one in the window: The Drop Pod > Modlist > Save as...");
            else foreach (var l in all) Say($"{(l.Name == current ? "*" : " ")} {l.Name}  ({l.Mods.Count} mods)");
            return 0;
        }
        if (command == "instances")
        {
            var all = _instances.All(_game.AppId);
            if (json) Say(JsonSerializer.Serialize(all.Select(i => new { name = i.Name, notes = i.Notes, frozen = Freezer.IsFrozen(i) })));
            else if (all.Count == 0) Say("No instances yet. Make one in the window: The Drop Pod > Instance > New...");
            else foreach (var i in all) Say($"  {i.Name}{(i.Notes.Length > 0 ? "  - " + i.Notes : "")}");
            return 0;
        }
        if (command is not ("active" or "problems" or "switch" or "play")) return Bad($"unknown command {command}");
        if (command == "switch" && name == null) return Bad("switch needs a modlist name");

        var usual = _detect();
        if (usual == null) { Say($"Error: {_game.Name} was not found. Open The Drop Pod in the window and pick its folder."); return 2; }
        GameInstance? inst = null;
        if (instance != null && (inst = _instances.Get(_game.AppId, instance)) == null)
        {
            Say($"Error: there is no instance called {instance}." + (_instances.All(_game.AppId) is { Count: > 0 } known ? " Instances: " + string.Join(", ", known.Select(i => i.Name)) + "." : ""));
            return 2;
        }
        var install = inst != null ? _applyInstance(usual, inst) : usual;

        if (name != null)
        {
            var list = _lists.All(_game.AppId).FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (list == null) { Say($"Error: there is no modlist called {name}. See: SteamCmdHelper droppod lists"); return 2; }
            if (_gameRunning()) { Say($"Error: {_game.Name} is running; close it first."); return 1; }
            var scan = ModLibrary.Scan(_game, install);
            var (installed, missing) = list.Match(scan, _game);
            try { _game.WriteActiveMods(install, installed); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Say("Error: could not write the active mods: " + e.Message); return 1; }
            try { new OrderHistory(OrderHistory.FolderFor(_historyRoot, _game.AppId, inst?.Name)).Record(installed, "Switched to " + list.Name + " (command line)"); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            if (inst == null) { _settings.CurrentModlists[_game.AppId] = list.Name; _saveSettings(); }
            Say($"Switched {(inst != null ? "the instance " + inst.Name : "the game")} to {list.Name}: {installed.Count} mods active." +
                (missing.Count == 0 ? "" : $" Not installed, left out: {string.Join(", ", missing.Select(m => m.Name ?? m.Id))}."));
            if (command == "switch") return 0;
        }

        if (command == "play")
        {
            if (_gameRunning()) { Say($"Error: {_game.Name} is already running."); return 1; }
            var psi = Launcher.StartInfo(_game, install, inst, inst?.LaunchArgs ?? _settings.DropPodLaunchArgs);
            if (psi == null) { Say($"Error: {_game.Name}'s program was not found in " + install.GameFolder + "."); return 1; }
            Say(inst != null ? $"Starting {_game.Name} with the instance {inst.Name}." : $"Starting {_game.Name}.");
            return _start(psi, install, inst);
        }

        var lib = ModLibrary.Scan(_game, install);
        var byId = lib.ById();
        var active = (lib.Active ?? Array.Empty<string>()).Select(_game.ActiveKey).Distinct().ToList();
        if (command == "active")
        {
            if (json)
                Say(JsonSerializer.Serialize(active.Select(k => byId.TryGetValue(k, out var m)
                    ? new { id = m.Id, name = m.DisplayName, workshopId = m.WorkshopId, installed = true }
                    : new { id = k, name = k, workshopId = (string?)null, installed = false })));
            else for (var i = 0; i < active.Count; i++) Say($"{i + 1,4}. {(byId.TryGetValue(active[i], out var m) ? $"{m.DisplayName} ({m.Id})" : active[i] + " (not installed)")}");
            return 0;
        }

        SortRules? rules = null;
        if (_settings.DropPodRulesFile.Length > 0)
            try { rules = SortRules.Parse(File.ReadAllText(_settings.DropPodRulesFile)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or JsonException) { }
        var found = Problems.Find(lib, active, _game, rules, _settings.DropPodIgnoreDuplicates);
        if (json) Say(JsonSerializer.Serialize(found.Select(p => new { kind = p.Kind.ToString(), level = p.Level.ToString(), mod = p.Mod, other = p.Other, message = p.Message, workshopId = p.WorkshopId })));
        else if (found.Count == 0) Say($"No problems with the {active.Count} active mods.");
        else foreach (var p in found) Say($"{p.Level,-7}  {p.Message}");
        return found.Any(p => p.Level == ProblemLevel.Error) ? 1 : 0;
    }
}
