using System.Text;
using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Reading Player.log: entries, levels, repeats grouped, and the mods at fault.</summary>
public sealed class LogAnalysisTests : IDisposable
{
    readonly FakeRimWorld _rw = new();
    readonly RimWorldModule _game = new();

    public void Dispose() => _rw.Dispose();

    /// <summary>A mod whose Assemblies folder holds a real .NET DLL (this test project's), so its namespaces are known.</summary>
    ModIndex Index()
    {
        var coder = _rw.FromWorkshop("111", FakeRimWorld.About("Coder Mod", "test.coder", "1.5"));
        Directory.CreateDirectory(Path.Combine(coder, "1.5", "Assemblies"));
        File.Copy(typeof(LogAnalysisTests).Assembly.Location, Path.Combine(coder, "1.5", "Assemblies", "Coder.dll"));
        File.WriteAllText(Path.Combine(coder, "1.5", "Assemblies", "notcode.dll"), "MZ not really");
        _rw.Local("Patcher", FakeRimWorld.About("Patcher", "test.patcher", "1.5"));
        _rw.Local("Xml Mod", FakeRimWorld.About("Xml Mod", "test.xml", "1.5"));
        _rw.Local("Loud", FakeRimWorld.About("Loud Mod", "test.loud", "1.5"));
        var scan = ModLibrary.Scan(_game, _rw.Install());
        return new ModIndex(scan.Mods, ModAssemblies.ForMods(scan.Mods));
    }

    [Fact]
    public void Mod_dlls_give_their_namespaces_and_shared_libraries_are_skipped()
    {
        var a = ModAssemblies.Read(typeof(LogAnalysisTests).Assembly.Location)!;
        Assert.Contains("SteamCmdHelper.Tests.DropPod", a.Namespaces);
        Assert.Null(ModAssemblies.Read(typeof(ModManifest).Assembly.Location.Replace("SteamCmdHelper.Tests", "nothing")));
    }

    string Log() => string.Join("\n",
        "Mono path[0] = 'C:/Steam/steamapps/common/RimWorld/RimWorldWin64_Data/Managed'",
        "RimWorld 1.5.4104 rev435",
        "",
        "Loading game from file Colony with mods test.coder, test.patcher",
        "",
        "Exception ticking Pawn_Bob: System.NullReferenceException: Object reference not set to an instance of an object",
        "[Ref 1A2B3C4D]",
        "  at SteamCmdHelper.Tests.DropPod.LogAnalysisTests.Boom () [0x00012] in <abc>:0",
        "  at (wrapper dynamic-method) Verse.Pawn.Verse.Pawn.Tick_Patch2(Verse.Pawn)",
        "  at Verse.TickList.Tick () [0x00000] in <def>:0",
        " - PREFIX test.patcher: Boolean Patcher.Patches:Prefix(Verse.Pawn __instance)",
        "UnityEngine.StackTraceUtility:ExtractStackTrace ()",
        "Verse.Log:Error (string)",
        "",
        "(Filename: C:\\buildslave\\unity\\build\\Runtime/Export/Debug/Debug.bindings.h Line: 39)",
        "",
        "Exception ticking Pawn_Alice: System.NullReferenceException: Object reference not set to an instance of an object",
        "[Ref 99999999] Duplicate stacktrace, see ref for original",
        "  at SteamCmdHelper.Tests.DropPod.LogAnalysisTests.Boom () [0x00034] in <abc>:0",
        "  at (wrapper dynamic-method) Verse.Pawn.Verse.Pawn.Tick_Patch2(Verse.Pawn)",
        "  at Verse.TickList.Tick () [0x00000] in <def>:0",
        "Verse.Log:Error (string)",
        "",
        "XML error: <labell>oops</labell> doesn't correspond to any field in type ThingDef. Context: <ThingDef> in file C:\\Steam\\steamapps\\common\\RimWorld\\Mods\\Xml Mod\\Defs\\Things.xml",
        "Verse.Log:Error (string)",
        "",
        "[Loud Mod] the config is old; using defaults",
        "Verse.Log:Warning (string)",
        "",
        "Could not find a type named test.loud.Missing",
        "",
        "Just a normal message from test.coder",
        "Verse.Log:Message (string)",
        "");

    [Fact]
    public void Errors_are_grouped_and_put_on_the_right_mods()
    {
        var log = Log().Replace("C:\\Steam\\steamapps\\common\\RimWorld", _rw.Game);
        var r = LogAnalysis.Read(new StringReader(log), Index());

        Assert.Equal("1.5.4104 rev435", r.GameVersion);
        Assert.Equal(4, r.Errors);
        Assert.Equal(1, r.Warnings);

        var tick = r.Issues[0];
        Assert.Equal(2, tick.Count); // Bob and Alice: same error, numbers and refs ignored
        Assert.StartsWith("Exception ticking Pawn_Bob", tick.Title);
        Assert.Equal(new[] { new ModBlame("test.coder", Blame.Code), new ModBlame("test.patcher", Blame.Patch) }, tick.Mods);
        Assert.Equal(6, tick.Line);

        Assert.Contains(r.Issues, i => i.Title.StartsWith("XML error") && i.Mods.SequenceEqual(new[] { new ModBlame("test.xml", Blame.File) }));
        Assert.Contains(r.Issues, i => i.Level == LogLevel.Warning && i.Mods.Single().Mod == "test.loud");
        Assert.Contains(r.Issues, i => i.Title.StartsWith("Could not find") && i.Mods.Count == 0);
        Assert.DoesNotContain(r.Issues, i => i.Title.StartsWith("Just a normal"));
        Assert.Equal(2, r.ErrorsByMod["test.coder"]);
    }

    [Fact]
    public void A_big_log_streams_and_caps_what_it_keeps()
    {
        var sb = new StringBuilder("RimWorld 1.5.4104 rev435\n\n");
        for (var i = 0; i < 3000; i++) sb.Append($"Error in thing{(char)('a' + i % 26)}{(char)('a' + i / 26 % 26)}{(char)('a' + i / 676)}: broke\nVerse.Log:Error (string)\n\n");
        for (var i = 0; i < 1000; i++) sb.Append("Exception: same\n").Append(string.Concat(Enumerable.Repeat("  at A.B.C () [0x0]\n", 400))).Append('\n');
        var r = LogAnalysis.Read(new StringReader(sb.ToString()), new ModIndex(Array.Empty<ModManifest>(), new Dictionary<string, List<ModAssembly>>()));
        Assert.True(r.Truncated);
        Assert.Equal(2000, r.Issues.Count);
        Assert.True(r.Lines > 400_000);
    }

    [Fact]
    public void An_empty_or_odd_log_gives_an_empty_report()
    {
        var empty = new ModIndex(Array.Empty<ModManifest>(), new Dictionary<string, List<ModAssembly>>());
        Assert.Empty(LogAnalysis.Read(new StringReader(""), empty).Issues);
        Assert.Null(LogAnalysis.Read(new StringReader("\0\0\0 binary junk"), empty).GameVersion);
    }
}
