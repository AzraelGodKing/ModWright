using System.Text.Json;

namespace SteamCmdHelper.DropPod;

/// <summary>One saved state of the active mods: when, why, and the ids in load order.</summary>
public sealed class OrderSnapshot
{
    public DateTime At { get; set; }
    /// <summary>What wrote it ("Save to game", "Switched to Heavy run").</summary>
    public string Label { get; set; } = "";
    public List<string> Active { get; set; } = new();

    /// <summary>The file it was read from (not saved).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public string File { get; set; } = "";
}

/// <summary>How one list differs from another, counted for a one-line summary.</summary>
public sealed record OrderChange(int Added, int Removed, int Moved)
{
    public bool None => Added + Removed + Moved == 0;

    public override string ToString()
    {
        if (None) return "no change";
        var parts = new List<string>();
        if (Added > 0) parts.Add($"{Added} added");
        if (Removed > 0) parts.Add($"{Removed} removed");
        if (Moved > 0) parts.Add($"{Moved} moved");
        return string.Join(", ", parts);
    }
}

/// <summary>
/// The load-order history: a snapshot each time the Drop Pod writes the game's mod list, so any earlier list can be put
/// back (the game's own ModsConfig.xml backups only go back a few saves). One folder per game and instance.
/// </summary>
public sealed class OrderHistory
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public string Folder { get; }
    public int Keep { get; }

    public OrderHistory(string folder, int keep = 100)
    {
        Folder = folder;
        Keep = keep;
    }

    /// <summary>The history folder for a game, or one of its instances.</summary>
    public static string FolderFor(string root, string appId, string? instance) =>
        Path.Combine(root, appId + (string.IsNullOrEmpty(instance) ? "" : "-" + SafeFiles.SafeName(instance, "instance")));

    /// <summary>Keeps <paramref name="active"/> unless it is the same as the newest snapshot. Returns the snapshot, or null when nothing changed.</summary>
    public OrderSnapshot? Record(IReadOnlyList<string> active, string label, DateTime? now = null)
    {
        var newest = All().FirstOrDefault();
        if (newest != null && newest.Active.SequenceEqual(active)) return null;
        var at = now ?? DateTime.Now;
        var snap = new OrderSnapshot { At = at, Label = label, Active = active.ToList() };
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, $"{at:yyyyMMdd-HHmmss-fff}.json");
        for (var n = 2; System.IO.File.Exists(file); n++) file = Path.Combine(Folder, $"{at:yyyyMMdd-HHmmss-fff}-{n}.json");
        System.IO.File.WriteAllText(file, JsonSerializer.Serialize(snap, Opts));
        snap.File = file;
        foreach (var old in Files().Skip(Keep)) try { System.IO.File.Delete(old); } catch (IOException) { }
        return snap;
    }

    /// <summary>Every snapshot, newest first. Files that can't be read are skipped.</summary>
    public List<OrderSnapshot> All()
    {
        var list = new List<OrderSnapshot>();
        foreach (var f in Files())
        {
            try
            {
                if (JsonSerializer.Deserialize<OrderSnapshot>(System.IO.File.ReadAllText(f), Opts) is { } s) { s.File = f; list.Add(s); }
            }
            catch (Exception e) when (e is JsonException or IOException) { }
        }
        return list;
    }

    IEnumerable<string> Files() =>
        Directory.Exists(Folder) ? Directory.GetFiles(Folder, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal) : Enumerable.Empty<string>();

    /// <summary>
    /// How <paramref name="to"/> differs from <paramref name="from"/>: mods added, removed, and moved (shared mods out of
    /// their old relative order, counted as the fewest that have to move: shared mods minus the longest run kept in order).
    /// </summary>
    public static OrderChange Compare(IReadOnlyList<string> from, IReadOnlyList<string> to)
    {
        var a = from.Distinct().ToList();
        var b = to.Distinct().ToList();
        var aSet = a.ToHashSet();
        var bSet = b.ToHashSet();
        var shared = a.Where(bSet.Contains).ToList();
        var pos = shared.Select((k, i) => (k, i)).ToDictionary(x => x.k, x => x.i);
        var seq = b.Where(aSet.Contains).Select(k => pos[k]).ToList();
        return new OrderChange(b.Count(k => !aSet.Contains(k)), a.Count(k => !bSet.Contains(k)), shared.Count - LongestIncreasing(seq));
    }

    static int LongestIncreasing(List<int> seq)
    {
        var tails = new List<int>();
        foreach (var x in seq)
        {
            var i = tails.BinarySearch(x);
            if (i < 0) i = ~i;
            if (i == tails.Count) tails.Add(x); else tails[i] = x;
        }
        return tails.Count;
    }
}

/// <summary>Ctrl+Z / Ctrl+Y for edits to the active list that are not saved to the game yet.</summary>
public sealed class EditUndo
{
    readonly Stack<List<string>> _undo = new();
    readonly Stack<List<string>> _redo = new();
    public int Limit { get; }

    public EditUndo(int limit = 200) => Limit = limit;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Call before changing the list, with the list as it was.</summary>
    public void Push(IReadOnlyList<string> before)
    {
        if (_undo.TryPeek(out var top) && top.SequenceEqual(before)) return;
        _undo.Push(before.ToList());
        if (_undo.Count > Limit)
        {
            var keep = _undo.Take(Limit).Reverse().ToList();
            _undo.Clear();
            foreach (var k in keep) _undo.Push(k);
        }
        _redo.Clear();
    }

    public List<string>? Undo(IReadOnlyList<string> current)
    {
        if (!_undo.TryPop(out var prev)) return null;
        _redo.Push(current.ToList());
        return prev;
    }

    public List<string>? Redo(IReadOnlyList<string> current)
    {
        if (!_redo.TryPop(out var next)) return null;
        _undo.Push(current.ToList());
        return next;
    }

    public void Clear() { _undo.Clear(); _redo.Clear(); }
}
