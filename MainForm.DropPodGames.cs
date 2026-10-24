using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.SunHaven;

namespace SteamCmdHelper;

/// <summary>The Drop Pod's game picker, and what Sun Haven has instead of RimWorld's Workshop tools: Thunderstore.</summary>
public sealed partial class MainForm
{
    async Task PickDropPodGameAsync()
    {
        var picked = DropPodGames.All.FirstOrDefault(g => g.Name == _dpGamePick.SelectedItem as string);
        if (picked == null || picked.AppId == _dpGame.AppId) return;
        if (_dpBusy || _dpBisect != null || _dpDirty && !ConfirmDiscardDropPod()) { _dpGamePick.SelectedItem = _dpGame.Name; return; }
        _dpGame = picked;
        _s.DropPodGame = picked.AppId;
        _s.Save();
        _dpDirty = false;
        _dpEdits.Clear();
        _dpScan = null;
        _dpUsual = null;
        _dpInst = null;
        _dpActive = new();
        ShowDropPodGameControls();
        FillDropPodList();
        await ScanDropPodAsync();
    }

    /// <summary>Shows or hides what only RimWorld has (instances, saves, Insight).</summary>
    void ShowDropPodGameControls()
    {
        foreach (var c in _dpRimWorldOnly) c.Visible = DpRimWorld;
    }

    /// <summary>Missing dependencies that Thunderstore could have ("Author-Name").</summary>
    List<string> ThunderstoreFixes()
    {
        var installed = _dpScan?.ById();
        if (installed == null) return new();
        // The problem holds the id in lower case; Thunderstore wants it as the manifest wrote it.
        string AsWritten(ModProblem p) =>
            installed.TryGetValue(p.Mod, out var m) && m.Dependencies.Everything.FirstOrDefault(d => d.Key == p.Other) is { } d ? d.Id : p.Other!;
        return _dpFound.Where(p => p.Kind == ProblemKind.MissingDependency && p.Other is { } id && !installed.ContainsKey(id))
            .Select(AsWritten).Where(ThunderstoreMods.IsPackageId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Installs the missing dependencies (and theirs) from Thunderstore into BepInEx\plugins, then ticks them.</summary>
    async Task DownloadThunderstoreAsync(List<string> ids, List<string> keep)
    {
        if (_dpScan is not { } scan || ids.Count == 0) return;
        if (GameRunning() != null) { ShowDropPodGame($"Close {_dpGame.Name} first; plugins can't be added while it runs."); return; }
        _dpBusy = true;
        UpdateDropPodButtons();
        (List<string> Installed, List<string> NotFound) r;
        try
        {
            ShowDropPodGame($"Downloading {ids.Count} mod{(ids.Count == 1 ? "" : "s")} and what {(ids.Count == 1 ? "it needs" : "they need")} from Thunderstore...");
            r = await ThunderstoreMods.InstallAsync(ids, scan.Mods.Select(m => m.Key).ToHashSet(), scan.Install, GitHubHttp, CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            ShowDropPodGame("Downloading from Thunderstore failed: " + e.Message);
            return;
        }
        finally { _dpBusy = false; UpdateDropPodButtons(); }
        var added = r.Installed.Select(ModManifest.KeyOf).ToList();
        if (!await RescanWithActive(() => keep.Concat(added).Distinct().ToList())) return;
        ShowDropPodGame($"Installed {r.Installed.Count} from Thunderstore and ticked them: {string.Join(", ", r.Installed)}." +
                        (r.NotFound.Count > 0 ? " Not on Thunderstore: " + string.Join(", ", r.NotFound) + "." : "") + " Press Save to game to use the list.");
    }

    /// <summary>Asks Thunderstore about the installed plugins that came from it; the ticked updates are put in place, each old copy zipped first.</summary>
    async Task CheckThunderstoreUpdatesAsync()
    {
        if (_dpScan is not { } scan) return;
        var candidates = ThunderstoreMods.Candidates(scan.Mods);
        if (candidates.Count == 0) { ShowDropPodGame("No installed plugin came from Thunderstore (a folder named Author-Name with manifest.json), so there is nothing to check."); return; }
        _dpBusy = true;
        UpdateDropPodButtons();
        List<ThunderstoreUpdate> updates;
        try
        {
            ShowDropPodGame($"Asking Thunderstore about {candidates.Count} plugin{(candidates.Count == 1 ? "" : "s")}...");
            updates = await ThunderstoreMods.CheckAsync(candidates, GitHubHttp, CancellationToken.None);
        }
        finally { _dpBusy = false; UpdateDropPodButtons(); }
        if (updates.Count == 0) { ShowDropPodGame($"All {candidates.Count} plugins from Thunderstore are up to date."); return; }
        var backups = Path.Combine(AppSettings.Folder, "droppod", "mod-backups");
        using var w = new ListWindow("Updates on Thunderstore",
            "These plugins have a newer version on Thunderstore. Tick the ones to update: each is downloaded and put in place, " +
            "and the old copy is zipped into Modwright's mod-backups folder first.",
            new[] { ("Mod", 260), ("Installed", 90), ("On Thunderstore", 110), ("Page", 360) }, checkBoxes: true);
        foreach (var u in updates)
            w.AddRow(u, Tone.Normal, u.Mod.DisplayName, u.Mod.ModVersion, u.Package.Version, u.Package.Page).Checked = true;
        w.DoubleClicked = tag => { if (tag is ThunderstoreUpdate u) OpenUrl(u.Package.Page); };
        var go = false;
        w.AddButton("&Update the ticked ones", () => { go = true; w.Close(); }, needsRow: true, primary: true);
        w.AddButton("Open the &backups", () => OpenFolder(backups));
        w.AddClose();
        w.ShowDialog(this);
        if (!go || w.Ticked.Count == 0) return;
        if (GameRunning() != null) { ShowDropPodGame($"Close {_dpGame.Name} first; its plugins can't be replaced while it runs."); return; }
        var done = 0;
        var failed = new List<string>();
        foreach (var u in w.Ticked.OfType<ThunderstoreUpdate>())
        {
            ShowDropPodGame($"Updating {u.Mod.DisplayName} to {u.Package.Version} from Thunderstore...");
            try { await ThunderstoreMods.UpdateAsync(u, scan.Install, GitHubHttp, backups, CancellationToken.None); done++; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or HttpRequestException or InvalidDataException or TaskCanceledException) { failed.Add($"{u.Mod.DisplayName} ({e.Message})"); }
        }
        var keep = _dpActive.ToList();
        var wasDirty = _dpDirty;
        await RescanWithActive(() => keep);
        _dpDirty = wasDirty;
        ShowDropPodGame($"Updated {done} plugin{(done == 1 ? "" : "s")} from Thunderstore." + (failed.Count > 0 ? " Failed: " + string.Join("; ", failed) : ""));
    }
}
