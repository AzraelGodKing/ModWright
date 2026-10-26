using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace SteamCmdHelper;

/// <summary>What "New mod..." should make.</summary>
public sealed class NewModOptions
{
    public string Folder { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>RimWorld versions the mod supports ("1.6"), newest last.</summary>
    public string[] Versions { get; set; } = Array.Empty<string>();
    /// <summary>RimWorld: add a C# project that references Harmony and builds into the newest version folder.</summary>
    public bool CSharp { get; set; }
}

/// <summary>
/// Makes a ready-to-upload mod folder for the current layout, so a new mod starts with every file the checks ask for:
/// for RimWorld an About.xml with a packageId, a preview of the right size, a changelog entry and the description file,
/// and optionally a C# project with Harmony; for other layouts the description, preview and changelog the layout names.
/// </summary>
public static class NewMod
{
    /// <summary>Harmony on the Workshop, the dependency nearly every C# RimWorld mod has.</summary>
    public const string HarmonyId = "2009463077";

    /// <summary>A folder name made from a display name: letters and digits only ("Better Storage!" = "BetterStorage").</summary>
    public static string FolderFor(string name) => Regex.Replace(name, "[^A-Za-z0-9]", "");

    /// <summary>RimWorld's packageId: "Author.ModName" with letters and digits only.</summary>
    public static string PackageId(string author, string folder)
    {
        var a = Regex.Replace(author, "[^A-Za-z0-9]", "");
        return (a.Length > 0 ? a : "Me") + "." + FolderFor(folder);
    }

    /// <summary>The newest RimWorld version any mod in the folder supports ("1.6" when none says), as the new mod's default.</summary>
    public static string NewestVersion(string modsRoot)
    {
        Version? best = null;
        try
        {
            foreach (var dir in Directory.GetDirectories(modsRoot))
            {
                var about = Path.Combine(dir, "About", "About.xml");
                if (!File.Exists(about)) continue;
                try
                {
                    foreach (var li in System.Xml.Linq.XDocument.Load(about).Root?.Element("supportedVersions")?.Elements("li") ?? Enumerable.Empty<System.Xml.Linq.XElement>())
                        if (Regex.Match(li.Value.Trim(), @"^(\d+)\.(\d+)") is { Success: true } m
                            && new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) is var v && (best == null || v > best)) best = v;
                }
                catch { }
            }
        }
        catch { }
        return best == null ? "1.6" : $"{best.Major}.{best.Minor}";
    }

    /// <summary>Null when the options can be used, else what to fix.</summary>
    public static string? Validate(string modsRoot, NewModOptions o)
    {
        if (o.Name.Trim().Length == 0) return "Give the mod a name.";
        if (o.Folder.Trim().Length == 0 || o.Folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || o.Folder.Trim('.').Length == 0)
            return "Use a folder name with letters, digits, - or _.";
        if (Directory.Exists(Path.Combine(modsRoot, o.Folder.Trim()))) return $"There is already a folder named {o.Folder.Trim()}.";
        if (o.Versions.Any(v => !Regex.IsMatch(v, @"^\d+\.\d+$"))) return "Write game versions like 1.5, 1.6.";
        return null;
    }

    /// <summary>Makes the mod in <paramref name="modsRoot"/> and returns its folder.</summary>
    public static string Create(string modsRoot, ProjectConfig cfg, NewModOptions o)
    {
        if (Validate(modsRoot, o) is { } bad) throw new InvalidOperationException(bad);
        var dir = Path.Combine(modsRoot, o.Folder.Trim());
        Directory.CreateDirectory(dir);
        void Write(string rel, string text)
        {
            if (string.IsNullOrWhiteSpace(rel)) return;
            var path = Path.Combine(dir, ProjectConfig.Norm(rel));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
        }
        var description = o.Description.Trim().Length > 0 ? o.Description.Trim() : o.Name.Trim() + ".";

        if (RimWorldLint.Applies(cfg))
        {
            var versions = o.Versions.Length > 0 ? o.Versions : new[] { "1.6" };
            Write(cfg.ModMarker, AboutXml(o, versions, description));
            if (o.CSharp) WriteCSharp(dir, o, versions[^1]);
        }
        if (!string.IsNullOrWhiteSpace(cfg.Changelog))
            Write(cfg.Changelog, cfg.ChangelogByVersion || cfg.ChangelogFirstLineIsVersion ? "1.0.0\n- First release.\n" : "- First release.\n");
        Write(cfg.SteamDescriptionFile, description + "\n");
        if (!string.IsNullOrWhiteSpace(cfg.Preview) && Path.GetExtension(cfg.Preview).Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            var preview = Path.Combine(dir, ProjectConfig.Norm(cfg.Preview));
            Directory.CreateDirectory(Path.GetDirectoryName(preview)!);
            File.WriteAllBytes(preview, Png(640, 360, 35, 52, 72));
        }
        return dir;
    }

    /// <summary>The layout's mod marker when "New mod..." could not write it (the mod is not listed until it exists), else null.</summary>
    public static string? MissingMarker(string dir, ProjectConfig cfg) =>
        !string.IsNullOrWhiteSpace(cfg.ModMarker) && !File.Exists(Path.Combine(dir, ProjectConfig.Norm(cfg.ModMarker))) ? cfg.ModMarker : null;

    static string X(string s) => SecurityElement.Escape(s) ?? "";

    static string AboutXml(NewModOptions o, string[] versions, string description)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<ModMetaData>\n");
        sb.Append($"  <name>{X(o.Name.Trim())}</name>\n");
        if (o.Author.Trim().Length > 0) sb.Append($"  <author>{X(o.Author.Trim())}</author>\n");
        sb.Append($"  <packageId>{PackageId(o.Author, o.Folder)}</packageId>\n");
        sb.Append("  <modVersion>1.0.0</modVersion>\n");
        sb.Append("  <supportedVersions>\n");
        foreach (var v in versions) sb.Append($"    <li>{v}</li>\n");
        sb.Append("  </supportedVersions>\n");
        if (o.CSharp)
        {
            sb.Append("  <modDependencies>\n    <li>\n      <packageId>brrainz.harmony</packageId>\n      <displayName>Harmony</displayName>\n");
            sb.Append($"      <steamWorkshopUrl>steam://url/CommunityFilePage/{HarmonyId}</steamWorkshopUrl>\n");
            sb.Append("      <downloadUrl>https://github.com/pardeike/HarmonyRimWorld/releases/latest</downloadUrl>\n    </li>\n  </modDependencies>\n");
            sb.Append("  <loadAfter>\n    <li>brrainz.harmony</li>\n  </loadAfter>\n");
        }
        sb.Append($"  <description>{X(description)}</description>\n");
        sb.Append("</ModMetaData>\n");
        return sb.ToString();
    }

    static void WriteCSharp(string dir, NewModOptions o, string version)
    {
        var name = FolderFor(o.Folder);
        var src = Path.Combine(dir, "Source", name);
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, name + ".csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net472</TargetFramework>
                <LangVersion>latest</LangVersion>
                <RootNamespace>{name}</RootNamespace>
                <AssemblyName>{name}</AssemblyName>
                <!-- RimWorld {version} loads the DLL from the version folder. -->
                <OutputPath>../../{version}/Assemblies/</OutputPath>
                <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
                <DebugType>none</DebugType>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Krafs.Rimworld.Ref" Version="{version}.*" />
                <PackageReference Include="Lib.Harmony" Version="2.*" ExcludeAssets="runtime" />
              </ItemGroup>
            </Project>

            """.Replace("\r\n", "\n"));
        File.WriteAllText(Path.Combine(src, "ModStartup.cs"), $$"""
            using HarmonyLib;
            using Verse;

            namespace {{name}}
            {
                /// <summary>Runs once when RimWorld has loaded the game's data, and applies every [HarmonyPatch] in this mod.</summary>
                [StaticConstructorOnStartup]
                public static class ModStartup
                {
                    static ModStartup()
                    {
                        new Harmony("{{PackageId(o.Author, o.Folder).ToLowerInvariant()}}").PatchAll();
                        Log.Message("[{{X(o.Name.Trim())}}] loaded.");
                    }
                }
            }

            """.Replace("\r\n", "\n"));
        var ignore = Path.Combine(dir, ".gitignore");
        if (!File.Exists(ignore)) File.WriteAllText(ignore, "Source/**/obj/\nSource/**/bin/\n.vs/\n");
    }

    /// <summary>A solid-colour PNG, as a placeholder preview of Steam's usual 16:9 size.</summary>
    public static byte[] Png(int width, int height, byte r, byte g, byte b)
    {
        var raw = new byte[(width * 3 + 1) * height];
        for (var y = 0; y < height; y++)
        {
            var row = y * (width * 3 + 1);
            for (var x = 0; x < width; x++) { raw[row + 1 + x * 3] = r; raw[row + 2 + x * 3] = g; raw[row + 3 + x * 3] = b; }
        }
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        BigEndian(ihdr, 0, (uint)width);
        BigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
        Chunk(ms, "IHDR", ihdr);
        using (var z = new MemoryStream())
        {
            using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        BigEndian(len, 0, (uint)data.Length);
        s.Write(len);
        var td = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(td);
        var crc = new byte[4];
        BigEndian(crc, 0, Crc(td));
        s.Write(crc);
    }

    static void BigEndian(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }

    static uint Crc(byte[] data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var d in data)
        {
            c ^= d;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return c ^ 0xFFFFFFFFu;
    }
}
