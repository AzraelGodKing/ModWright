using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace SteamCmdHelper.DropPod;

/// <summary>One code file (DLL) of a mod: its name and the namespaces of the types in it.</summary>
public sealed record ModAssembly(string Path, string Name, IReadOnlyList<string> Namespaces);

/// <summary>
/// Reads which namespaces each mod's DLLs define (from the DLL's metadata; nothing is loaded or run), so a stack trace
/// line in the game's log can be traced back to the mod whose code it is.
/// </summary>
public static class ModAssemblies
{
    /// <summary>Libraries many mods carry a copy of; their code says nothing about which mod is at fault.</summary>
    static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase) { "0Harmony", "Harmony", "HarmonyMod", "0MultiplayerAPI", "Mono.Cecil", "Newtonsoft.Json", "System.ValueTuple" };

    /// <summary>The DLLs in a mod's Assemblies folders (also inside its version and load folders, a few levels down).</summary>
    public static List<string> Files(string modFolder)
    {
        var list = new List<string>();
        void Walk(string dir, int depth)
        {
            string[] subs;
            try { subs = Directory.GetDirectories(dir); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
            foreach (var sub in subs)
            {
                var name = System.IO.Path.GetFileName(sub);
                if (name.Equals("Assemblies", StringComparison.OrdinalIgnoreCase))
                {
                    try { list.AddRange(Directory.GetFiles(sub, "*.dll")); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
                else if (depth < 3 && !name.StartsWith('.') && !name.Equals("Textures", StringComparison.OrdinalIgnoreCase) && !name.Equals("Sounds", StringComparison.OrdinalIgnoreCase))
                    Walk(sub, depth + 1);
            }
        }
        Walk(modFolder, 0);
        return list;
    }

    /// <summary>Reads a DLL's name and namespaces. Null for shared libraries and files that are not .NET code.</summary>
    public static ModAssembly? Read(string dll)
    {
        try
        {
            using var fs = new FileStream(dll, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var pe = new PEReader(fs);
            if (!pe.HasMetadata) return null;
            var md = pe.GetMetadataReader();
            var name = md.IsAssembly ? md.GetString(md.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(dll);
            if (Shared.Contains(name)) return null;
            var spaces = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var h in md.TypeDefinitions)
            {
                var t = md.GetTypeDefinition(h);
                if (t.GetDeclaringType().IsNil && md.GetString(t.Namespace) is { Length: > 0 } ns) spaces.Add(ns);
            }
            return new ModAssembly(dll, name, spaces.ToList());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException) { return null; }
    }

    /// <summary>Every assembly of every mod, by mod key.</summary>
    public static Dictionary<string, List<ModAssembly>> ForMods(IEnumerable<ModManifest> mods, CancellationToken ct = default)
    {
        var result = new Dictionary<string, List<ModAssembly>>();
        foreach (var m in mods.Where(m => m.Source != ModSource.Official).DistinctBy(m => m.Key))
        {
            ct.ThrowIfCancellationRequested();
            var list = Files(m.Folder).Select(Read).OfType<ModAssembly>().ToList();
            if (list.Count > 0) result[m.Key] = list;
        }
        return result;
    }
}
