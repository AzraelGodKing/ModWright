using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;

namespace SteamCmdHelper;

/// <summary>
/// The Launch Pad's tools that reach into RimWorld (clean-colony test, play it live, read a player's log, the About.xml
/// editor and the compatibility grid) and the upload helpers (bump hint, subscriber notes, checklist, preview image
/// maker, README to description, family release).
/// </summary>
public sealed partial class MainForm
{
    DevLinks LaunchPadLinks => new(Path.Combine(AppSettings.Folder, "droppod", "devlinks.json"));
    CleanTestRecord CleanTests => new(Path.Combine(AppSettings.Folder, "launchpad", "clean-tests.json"));
    bool _cleanTesting;
    readonly HashSet<string> _subscriberNoted = new(StringComparer.Ordinal);

    /// <summary>Adds the RimWorld submenu and the Package extras to the mod list's right-click menu; returns what refreshes them.</summary>
    Action AddLaunchPadMenu(ContextMenuStrip menu, ToolStripMenuItem package)
    {
        var rw = new ToolStripMenuItem("&RimWorld");
        var clean = rw.DropDownItems.Add("Test in a &clean colony", null, async (_, _) => { if (SelectedMod() is { } m) await CleanColonyTestAsync(m); });
        var live = rw.DropDownItems.Add("&Play it live in RimWorld", null, (_, _) => { if (SelectedMod() is { } m) TogglePlayLive(m); });
        rw.DropDownItems.Add(new ToolStripSeparator());
        rw.DropDownItems.Add("Edit &About.xml...", null, (_, _) => { if (SelectedMod() is { } m) EditAboutXml(m); });
        rw.DropDownItems.Add("Compatibility &grid...", null, (_, _) => ShowCompatibilityGrid());
        rw.DropDownItems.Add("Read a p&layer's log...", null, async (_, _) => await ReadPlayerLogAsync());
        menu.Items.Add(rw);

        package.DropDownItems.Add(new ToolStripSeparator());
        package.DropDownItems.Add("&Make a preview image...", null, (_, _) => { if (SelectedMod() is { } m) MakePreviewImage(m); });
        var readme = package.DropDownItems.Add("Workshop description from &README", null, (_, _) => { if (SelectedMod() is { } m) DescriptionFromReadme(m); });
        return () =>
        {
            var m = SelectedMod();
            clean.Enabled = !_cleanTesting;
            live.Text = Localizer.T(m != null && LaunchPadLinks.For(m.Dir) != null ? "Stop playing it &live" : "&Play it live in RimWorld");
            readme.Available = m != null && ReadmeOf(m) != null;
        };
    }

    /// <summary>The Drop Pod's view of RimWorld, scanning it first when the Drop Pod hasn't yet. False (and says why) when RimWorld isn't found.</summary>
    async Task<bool> EnsureRimWorldAsync()
    {
        if (_dpScan == null || _dpUsual == null) await ScanDropPodAsync();
        if (_dpScan != null && _dpUsual != null) return true;
        MessageBox.Show(this, "RimWorld was not found. Open The Drop Pod and pick RimWorld's folder there first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    static bool Inside(string path, string folder) =>
        Path.GetFullPath(path).TrimEnd('\\', '/').StartsWith(Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Right-click > RimWorld > Test in a clean colony: the mod with only the game, the DLC and what it needs, in the
    /// "Launch Pad test" instance, started straight into a test map. When the game closes, its log is read for errors
    /// that point at the mod and the result is kept for the upload confirmation and the compatibility grid.
    /// </summary>
    async Task CleanColonyTestAsync(ModInfo m)
    {
        if (_cleanTesting || !await EnsureRimWorldAsync()) return;
        var (scan, usual) = (_dpScan!, _dpUsual!);
        if (GameRunning() != null) { MessageBox.Show(this, "RimWorld is already running. Close it first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_dpGame.ReadMod(m.Dir, ModSource.Local) is not { } mod) { Append($"{m.Name}: its About.xml could not be read, so it can't be tested.", LogKind.Error); return; }
        var (active, missing) = CleanTest.Plan(mod, scan, _dpGame);
        if (missing.Count > 0 && MessageBox.Show(this,
                $"{m.Name} needs {string.Join(", ", missing.Select(d => d.DisplayName ?? d.Id))}, which {(missing.Count == 1 ? "is" : "are")} not installed. Test without {(missing.Count == 1 ? "it" : "them")}?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        var gameMods = Launcher.ModsFolder(usual);
        bool linked = false;
        GameInstance inst;
        string hash;
        try
        {
            var store = DropPodInstances;
            inst = store.Get(_dpGame.AppId, CleanTest.InstanceName) ?? store.Create(_dpGame, usual, CleanTest.InstanceName, "", usual.DataFolder, null);
            _dpGame.WriteActiveMods(usual with { DataFolder = inst.DataFolder }, active);
            // The mod has to be where RimWorld looks: its dev folder linked into Mods for the test, unless it is there already.
            if (!Inside(m.Dir, gameMods) && LaunchPadLinks.For(m.Dir) == null) { LaunchPadLinks.Add(gameMods, m.Dir); linked = true; }
            hash = await Task.Run(() => CleanTest.FolderHash(m.Dir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Append($"Could not set up the clean-colony test for {m.Name}: {e.Message}", LogKind.Error);
            return;
        }
        var install = inst.Apply(usual, _dpGame, SteamLibraries.Find());
        var psi = Launcher.StartInfo(_dpGame, install, inst, "-quicktest");
        if (psi == null) { Append("RimWorld's program was not found in " + usual.GameFolder + ".", LogKind.Error); if (linked) LaunchPadLinks.Remove(m.Dir); return; }
        try { if (File.Exists(inst.LogFile)) File.Delete(inst.LogFile); } catch (IOException) { }
        _cleanTesting = true;
        try
        {
            Launcher.Start(psi, _dpGame, install, inst, _ => { try { BeginInvoke(async () => await FinishCleanTestAsync(m, mod, inst, hash, linked, install.Version ?? usual.Version)); } catch (InvalidOperationException) { } });
        }
        catch (Exception e)
        {
            _cleanTesting = false;
            if (linked) LaunchPadLinks.Remove(m.Dir);
            Append("Could not start RimWorld: " + e.Message, LogKind.Error);
            return;
        }
        Append($"Testing {m.Name} in a clean colony: RimWorld starts straight into a test map with {active.Count} mods. Look around, then close the game; its log is read then.", LogKind.Note);
    }

    async Task FinishCleanTestAsync(ModInfo m, ModManifest mod, GameInstance inst, string hash, bool linked, string? version)
    {
        _cleanTesting = false;
        if (linked) { try { LaunchPadLinks.Remove(m.Dir); } catch (IOException) { } }
        if (!File.Exists(inst.LogFile)) { Append($"The clean-colony test of {m.Name} left no log, so there is nothing to read.", LogKind.Warn); return; }
        var mods = (_dpScan?.Mods ?? new List<ModManifest>()).Where(x => x.Key != mod.Key).Append(mod).ToList();
        LogReport report;
        try { report = await Task.Run(() => LogAnalysis.Read(inst.LogFile, new ModIndex(mods, ModAssemblies.ForMods(mods)))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Append("Could not read the test's log: " + e.Message, LogKind.Error); return; }
        var r = CleanTest.Result(report, mod.Key, hash, version);
        CleanTests.Set(m.Dir, r);
        if (r.Errors == 0)
            Append($"{m.Name} passed its clean-colony test on RimWorld {GameVersion.Short(r.GameVersion)}" + (r.Warnings > 0 ? $" ({r.Warnings} warning{(r.Warnings == 1 ? "" : "s")} point at it)." : ": nothing in the log points at it."), LogKind.Ok);
        else
        {
            Append($"{m.Name}'s clean-colony test found {r.Errors} error{(r.Errors == 1 ? "" : "s")} pointing at it: {string.Join("; ", r.Titles.Take(3))}", LogKind.Error);
            OpenDropPodLog(inst.LogFile, watch: false);
        }
    }

    /// <summary>Right-click > RimWorld > Play it live: links the mod's folder into RimWorld's Mods folder (or takes the link out again).</summary>
    void TogglePlayLive(ModInfo m)
    {
        try
        {
            if (LaunchPadLinks.Remove(m.Dir)) { Append($"{m.Name} is no longer linked into RimWorld's Mods folder.", LogKind.Note); return; }
            if (_dpUsual == null) { _ = PlayLiveAfterScanAsync(m); return; }
            var mods = Launcher.ModsFolder(_dpUsual);
            if (Inside(m.Dir, mods)) { Append($"{m.Name} already lives in RimWorld's Mods folder, so the game sees every change already.", LogKind.Note); return; }
            var l = LaunchPadLinks.Add(mods, m.Dir);
            Append($"{m.Name} is linked into RimWorld as {l.Link}: each build is in the game at once. Tick it in The Drop Pod to play it. Right-click > RimWorld > Stop playing it live takes the link out (your folder stays).", LogKind.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Append($"Could not link {m.Name}: {e.Message}", LogKind.Error);
        }
    }

    async Task PlayLiveAfterScanAsync(ModInfo m)
    {
        if (await EnsureRimWorldAsync()) TogglePlayLive(m);
    }

    /// <summary>Right-click > RimWorld > Read a player's log: a log someone sent, showing only what points at your own mods.</summary>
    async Task ReadPlayerLogAsync()
    {
        using var f = new Form { Text = "Read a player's log", FormBorderStyle = FormBorderStyle.Sizable, StartPosition = FormStartPosition.CenterParent, MinimizeBox = false, MaximizeBox = false, ClientSize = new Size(560, 300), Font = Font };
        var l = new Label { AutoSize = true, MaximumSize = new Size(536, 0), Location = new Point(12, 12), Text = "Paste the log, or a link to it (pastebin, gist, hastebin, or any page that is just the text), or open the file a player sent:" };
        var t = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(12, 54), Size = new Size(536, 190), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom, MaxLength = 0, AccessibleName = "The log or a link" };
        var file = new Button { Text = "Open a &file...", AutoSize = true, Location = new Point(12, 258), Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        var ok = new Button { Text = "Read", DialogResult = DialogResult.OK, Location = new Point(392, 260), Width = 75, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(473, 260), Width = 75, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        file.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Title = "Open a player's log", Filter = "Logs and text|*.log;*.txt|All files|*.*" };
            if (d.ShowDialog(f) != DialogResult.OK) return;
            try { t.Text = File.ReadAllText(d.FileName); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MessageBox.Show(f, e.Message, f.Text); }
        };
        f.Controls.AddRange(new Control[] { l, t, file, ok, cancel });
        f.CancelButton = cancel;
        ok.Role(ThemeRole.Accent);
        Theme.Apply(f);
        if (f.ShowDialog(this) != DialogResult.OK || t.Text.Trim().Length == 0) return;

        string text;
        try { text = await PlayerLog.TextAsync(t.Text); }
        catch (IOException e) { MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var cfg = _cfg;
        var own = await Task.Run(() => _mods.Select(m => _dpGame.ReadMod(m.Dir, ModSource.Local)).OfType<ModManifest>().ToList());
        var mine = new HashSet<string>(own.Select(m => m.Key), StringComparer.Ordinal);
        var known = own.Concat((_dpScan?.Mods ?? new List<ModManifest>()).Where(m => !mine.Contains(m.Key))).ToList();
        var report = await Task.Run(() => LogAnalysis.Read(new StringReader(text), new ModIndex(known, ModAssemblies.ForMods(own))));
        var issues = PlayerLog.Mine(report, mine);
        var list = PlayerLog.ModList(text);
        var names = own.ToDictionary(m => m.Key, m => m.DisplayName);
        var w = new ListWindow("A player's log",
            $"{issues.Count} of the {report.Issues.Count} problems in this log point at your mods." +
            (report.GameVersion != null ? $" RimWorld {report.GameVersion}." : "") +
            (list.Count > 0 ? $" The log lists {list.Count} mods (Copy the mod list)." : " The log doesn't list the player's mods."),
            new[] { ("Kind", 70), ("Times", 50), ("Message", 420), ("Your mod", 160) },
            detailsOf: o => o is LogIssue i ? i.Text : "");
        foreach (var i in issues)
            w.AddRow(i, i.Level == LogLevel.Error ? Tone.Error : Tone.Warn, i.Level.ToString(), i.Count.ToString("N0"), i.Title,
                string.Join(", ", i.Mods.Where(b => mine.Contains(b.Mod)).Select(b => names.GetValueOrDefault(b.Mod, b.Mod))));
        if (list.Count > 0) w.AddButton("Copy the mod &list", () => Copy(string.Join(Environment.NewLine, list), "the player's mod list"));
        w.AddButton("&Copy the message", () => { if (w.Selected is LogIssue i) Copy(i.Text, "message"); }, needsRow: true);
        w.AddClose();
        w.Show(this);
    }

    /// <summary>Right-click > RimWorld > Edit About.xml: the common fields with live checks, and rules picked from the Drop Pod's library.</summary>
    void EditAboutXml(ModInfo m)
    {
        var path = Path.Combine(m.Dir, "About", "About.xml");
        string? xml = null;
        try { if (File.Exists(path)) xml = File.ReadAllText(path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Append("Could not read About.xml: " + e.Message, LogKind.Error); return; }
        AboutFields fields;
        try { fields = xml == null ? new AboutFields { Name = m.Name } : AboutEditor.Read(xml); }
        catch (Exception e) { Append("About.xml could not be read: " + e.Message, LogKind.Error); return; }
        using var f = new AboutForm(m.Name, fields, xml, () => _dpScan?.Mods.Where(x => x.Id.Length > 0).DistinctBy(x => x.Key).ToList() ?? new List<ModManifest>());
        if (f.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFiles.WriteWithBackup(path, AboutEditor.Write(xml, f.Result), keep: 3);
            Append($"{m.Name}: About.xml saved.", LogKind.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException) { Append("Could not save About.xml: " + e.Message, LogKind.Error); }
        RefreshAll();
    }

    /// <summary>Right-click > RimWorld > Compatibility grid: each mod's clean-colony result per game version it supports.</summary>
    void ShowCompatibilityGrid()
    {
        var own = _mods.Select(m => (Info: m, Mod: _dpGame.ReadMod(m.Dir, ModSource.Local))).Where(x => x.Mod != null).ToList();
        var versions = own.SelectMany(x => x.Mod!.SupportedVersions).Select(GameVersion.Short).Where(v => v.Length > 0).Distinct()
            .OrderBy(v => Version.TryParse(v, out var p) ? p : new Version(0, 0)).ToList();
        var installed = _dpScan?.Install.Version is { } iv ? GameVersion.Short(iv) : null;
        var cols = new List<(string, int)> { ("Mod", 200) };
        cols.AddRange(versions.Select(v => (v + (v == installed ? " (installed)" : ""), 110)));
        var rec = CleanTests;
        var w = new ListWindow("Compatibility grid",
            "The last clean-colony test of each mod on each RimWorld version it says it supports. Only the installed version can be tested; " +
            "to fill in another column, switch RimWorld's beta in Steam (Properties > Betas) and test again.",
            cols.ToArray(), size: new Size(Math.Min(1200, 260 + 110 * versions.Count), 480));
        foreach (var (info, mod) in own)
        {
            var hash = new Lazy<string>(() => CleanTest.FolderHash(info.Dir));
            var worst = Tone.Normal;
            var cells = new List<string> { info.Name };
            foreach (var v in versions)
            {
                if (!mod!.SupportedVersions.Select(GameVersion.Short).Contains(v)) { cells.Add("-"); continue; }
                var r = rec.Get(info.Dir, v);
                if (r == null) { cells.Add("not tested"); if (worst == Tone.Normal) worst = Tone.Muted; continue; }
                var stale = r.Hash != hash.Value ? " (changed since)" : "";
                cells.Add((r.Errors > 0 ? $"{r.Errors} errors" : "clean") + $" {r.At:d MMM}" + stale);
                if (r.Errors > 0) worst = Tone.Error;
                else if (stale.Length > 0 && worst != Tone.Error) worst = Tone.Warn;
            }
            w.AddRow(info, worst, cells.ToArray());
        }
        w.AddButton("&Test the selected mod", async () => { if (w.Selected is ModInfo m) { w.Close(); await CleanColonyTestAsync(m); } }, needsRow: true, primary: true);
        w.AddClose();
        w.Show(this);
    }

    /// <summary>Package > Make a preview image: a title card drawn over a picture or a colour, saved as the mod's preview under Steam's 1 MB.</summary>
    void MakePreviewImage(ModInfo m)
    {
        var target = m.PreviewPath.Length > 0 ? m.PreviewPath : Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.Preview));
        using var f = new PreviewMakerForm(m.Name, new Size(640, 360));
        if (f.ShowDialog(this) != DialogResult.OK || f.Result is not { } img) return;
        try
        {
            if (File.Exists(target)) Directory.CreateDirectory(PreviewImage.BackupRoot);
            if (File.Exists(target)) File.Copy(target, Path.Combine(PreviewImage.BackupRoot, $"{SafeFiles.SafeName(m.Folder, "mod")}-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(target)}"), true);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            img.Save(target, target.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || target.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                ? System.Drawing.Imaging.ImageFormat.Jpeg : System.Drawing.Imaging.ImageFormat.Png);
            var note = new FileInfo(target).Length > 1_000_000 ? " " + PreviewImage.Shrink(target, m.Folder, 1_000_000) : "";
            Append($"{m.Name}: preview image saved to {target}." + note + " The old one is kept in " + PreviewImage.BackupRoot + ".", LogKind.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { Append("Could not save the preview image: " + e.Message, LogKind.Error); }
        finally { img.Dispose(); }
        RefreshAll();
    }

    static string? ReadmeOf(ModInfo m) =>
        new[] { "README.md", "Readme.md", "readme.md" }.Select(n => Path.Combine(m.Dir, n)).FirstOrDefault(File.Exists);

    /// <summary>Package > Workshop description from README: the README as Steam BBCode, written to the mod's description file.</summary>
    void DescriptionFromReadme(ModInfo m)
    {
        if (ReadmeOf(m) is not { } readme) return;
        var target = Path.Combine(m.Dir, ProjectConfig.Norm(_cfg.SteamDescriptionFile));
        string bb;
        try { bb = MarkdownBBCode.Convert(File.ReadAllText(readme)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Append("Could not read the README: " + e.Message, LogKind.Error); return; }
        try { OpenPath(SteamText.WritePreview(m.Name, bb, "The README as the Workshop description")); } catch (Exception) { }
        if (MessageBox.Show(this, $"Write the README as Steam BBCode into {Path.GetRelativePath(m.Dir, target)}? The preview opened in your browser. " +
                "It goes to Steam with the next upload, as any change to the description file does.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            SafeFiles.WriteWithBackup(target, bb + "\n", keep: 3);
            Append($"{m.Name}: description written from the README ({bb.Length:N0} characters).", LogKind.Ok);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Append("Could not write the description: " + e.Message, LogKind.Error); }
    }

    /// <summary>The version this mod last went up as (this PC's history, else what Steam's changelog said), or null.</summary>
    string? LastUploadedVersion(ModInfo m)
    {
        if (!m.OnSteam) return null;
        return UploadHistory.For(UploadHistory.Load(), m.WorkshopId!).FirstOrDefault()?.Version ?? UploadedVersions.LoadRecord().GetValueOrDefault(m.WorkshopId!);
    }

    /// <summary>The bump the commits since the last upload point to (git repos only), or null.</summary>
    (ModScanner.Bump Part, string Reason)? SuggestedBump(ModInfo m)
    {
        try { return DraftNoteFn()?.Invoke(m) is { } n ? BumpSuggestion.Suggest(n.Note.Split('\n')) : null; }
        catch (Exception e) when (e is IOException or InvalidOperationException) { return null; }
    }

    /// <summary>The Launch Pad's lines for the upload confirmation: clean-colony test results and a version that looks too small for the commits.</summary>
    async Task<List<string>> LaunchPadChecksAsync(List<ModInfo> picked, bool test)
    {
        var lines = new List<string>();
        var rec = CleanTests;
        // Hashing reads the whole mod, so only for mods that were tested.
        var found = await Task.Run(() => picked.Select(m => (m, last: rec.Get(m.Dir))).Where(x => x.last != null)
            .Select(x => (x.m, w: CleanTest.Warning(x.last, CleanTest.FolderHash(x.m.Dir)))).ToList());
        lines.AddRange(found.Where(x => x.w != null).Select(x => $"{x.m.Name} {x.w}."));
        // Patches are checked against the game's own XML, so only when RimWorld is found (quietly skipped otherwise).
        if (_dpScan == null && _dpUsual == null) await ScanDropPodAsync();
        if (_dpScan is { } scan)
            lines.AddRange(await Task.Run(() => picked.SelectMany(m => PatchMisses(m, scan)).ToList()));
        if (!test && RepoIsGit)
            foreach (var m in picked.Where(m => m.OnSteam))
            {
                if (SuggestedBump(m) is not { Part: not ModScanner.Bump.Patch } s || LastUploadedVersion(m) is not { } last) continue;
                if (ModScanner.NextVersion(last, s.Part) is { } wanted && !BumpSuggestion.AtLeast(m.Version, wanted))
                    lines.Add($"{m.Name}'s commits since v{last} suggest a {s.Part.ToString().ToLowerInvariant()} release (v{wanted} or later; \"{s.Reason}\"), but it goes up as v{m.Version}.");
            }
        return lines;
    }

    /// <summary>
    /// The mod's XML run with only the game, the DLC and what it needs (as in a clean-colony test): patches that find
    /// nothing (players see red errors) and defs it takes over from a dependency.
    /// </summary>
    IEnumerable<string> PatchMisses(ModInfo m, LibraryScan scan)
    {
        if (_dpGame.ReadMod(m.Dir, ModSource.Local) is not { } mod) return Enumerable.Empty<string>();
        var byId = scan.ById();
        var (active, _) = CleanTest.Plan(mod, scan, _dpGame);
        var mods = active.Select(k => k == mod.Key ? mod : byId.GetValueOrDefault(k)).OfType<ModManifest>().ToList();
        var lines = new List<string>();
        foreach (var f in XmlPatchSim.Run(XmlMods(mods), scan.Install.Version).Where(f => f.Mods.Contains(mod.Key)))
        {
            var n = f.Targets.Count;
            if (f.Kind == XmlFindingKind.PatchFails)
                lines.Add($"{m.Name}: {n} patch{(n == 1 ? "" : "es")} in {f.Detail} find{(n == 1 ? "s" : "")} nothing in RimWorld {GameVersion.Short(scan.Install.Version ?? "")} with the DLC and the mod's dependencies, " +
                          $"so players get a red error (mark optional ones with MayRequire or <success>Always</success>): {PackageCheck.Examples(f.Targets)}.");
            else if (f.Kind == XmlFindingKind.DefOverride && f.Mods[1] == mod.Key && !RimWorldIds.IsOfficial(f.Mods[0]))
                lines.Add($"{m.Name} replaces {n} def{(n == 1 ? "" : "s")} its dependency {byId.GetValueOrDefault(f.Mods[0])?.DisplayName ?? f.Mods[0]} defines: {PackageCheck.Examples(f.Targets)}.");
        }
        return lines;
    }

    /// <summary>Logs subscriber milestones and drops, once per mod and day.</summary>
    void NoteSubscriberChanges(IEnumerable<ModInfo> mods, IReadOnlyDictionary<string, List<SubscriberPoint>> stats)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var m in mods)
        {
            if (m.WorkshopId == null || !stats.TryGetValue(m.WorkshopId, out var points) || !_subscriberNoted.Add(m.WorkshopId + today)) continue;
            if (SubscriberAlerts.Milestone(points) is { } n) Append($"{m.Name} passed {n:N0} subscribers.", LogKind.Ok);
            if (SubscriberAlerts.Drop(points, today) is { } drop) Append($"{m.Name} {drop}. Worth a look at its comments (right-click > On Steam > Stats and comments).", LogKind.Warn);
        }
    }

    /// <summary>Who needs whom among your mods, for releasing a family together: RimWorld packageIds and modDependencies, else Workshop ids.</summary>
    (Func<ModInfo, string?> Key, Func<ModInfo, IEnumerable<string>> Needs) FamilyOf()
    {
        var cfg = _cfg;
        var cache = new Dictionary<string, (string? Key, List<string> Needs)>(StringComparer.OrdinalIgnoreCase);
        (string? Key, List<string> Needs) Read(ModInfo m)
        {
            lock (cache)
            {
                if (cache.TryGetValue(m.Dir, out var hit)) return hit;
                (string?, List<string>) r;
                if (_dpGame.ReadMod(m.Dir, ModSource.Local) is { } man)
                    r = (man.Id.Length > 0 ? man.Id : null, man.Dependencies.Everything.Select(d => d.Id).Where(i => i.Length > 0).ToList());
                else r = (m.WorkshopId, Dependencies.Find(m, cfg));
                return cache[m.Dir] = r;
            }
        }
        return (m => Read(m).Key, m => Read(m).Needs);
    }
}
