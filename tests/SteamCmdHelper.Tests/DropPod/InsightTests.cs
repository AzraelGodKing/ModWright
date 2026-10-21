using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

// Stand-ins with the names of Harmony's attributes, so this test DLL declares patches the reader can find.
namespace HarmonyLib
{
    public enum MethodType { Normal, Getter, Setter, Constructor, StaticConstructor }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch() { }
        public HarmonyPatch(Type type) { }
        public HarmonyPatch(string methodName) { }
        public HarmonyPatch(Type type, string methodName) { }
        public HarmonyPatch(Type type, string methodName, Type[] argumentTypes) { }
        public HarmonyPatch(string typeName, string methodName) { }
        public HarmonyPatch(Type type, MethodType methodType) { }
        public HarmonyPatch(string propertyName, MethodType methodType) { }
    }

    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyPrefix : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyTranspiler : Attribute { }
}

namespace SteamCmdHelper.Tests.DropPod
{
    using HarmonyLib;

    public sealed class FakePawn { public int Health { get; set; } public void Tick() { } public void Kill() { } }

    [HarmonyPatch(typeof(FakePawn), nameof(FakePawn.Tick))]
    static class PatchTick
    {
        static bool Prefix() => true;
        static void Postfix() { }
    }

    [HarmonyPatch(typeof(FakePawn))]
    [HarmonyPatch("Kill")]
    static class PatchKill
    {
        [HarmonyTranspiler] static IEnumerable<object> Rewrite(IEnumerable<object> code) => code;
    }

    static class PatchByMethod
    {
        [HarmonyPatch("Verse.Thing, Assembly-CSharp", "SpawnSetup"), HarmonyPrefix] static void Before() { }
        [HarmonyPatch(typeof(FakePawn), MethodType.Constructor)] static void Postfix() { }
    }

    [HarmonyPatch(typeof(FakePawn))]
    static class PatchGetter
    {
        [HarmonyPatch("Health", MethodType.Getter)] static void Postfix() { }
    }

    static class NotAPatch { static void Prefix() { } }

    /// <summary>Harmony patches read from DLLs, and the clash, per-tick and weight hints built on them.</summary>
    public sealed class InsightTests : IDisposable
    {
        readonly FakeRimWorld _rw = new();

        public void Dispose() => _rw.Dispose();

        const string Pawn = "SteamCmdHelper.Tests.DropPod.FakePawn";

        [Fact]
        public void Declared_patches_are_read_from_the_dll()
        {
            var p = HarmonyPatches.Read(typeof(InsightTests).Assembly.Location);
            Assert.Contains(new HarmonyPatchInfo(Pawn + ".Tick", PatchKind.Prefix, true), p);
            Assert.Contains(new HarmonyPatchInfo(Pawn + ".Tick", PatchKind.Postfix, false), p);
            Assert.Contains(new HarmonyPatchInfo(Pawn + ".Kill", PatchKind.Transpiler, false), p);
            Assert.Contains(new HarmonyPatchInfo("Verse.Thing.SpawnSetup", PatchKind.Prefix, false), p);
            Assert.Contains(new HarmonyPatchInfo(Pawn + "..ctor", PatchKind.Postfix, false), p);
            Assert.Contains(new HarmonyPatchInfo(Pawn + ".get_Health", PatchKind.Postfix, false), p);
            Assert.DoesNotContain(p, x => x.Target.Contains("NotAPatch"));
            Assert.Empty(HarmonyPatches.Read(Path.Combine(_rw.Root, "missing.dll")));
        }

        static Dictionary<string, List<HarmonyPatchInfo>> Patches() => new()
        {
            ["a"] = new() { new("Verse.Pawn.Kill", PatchKind.Transpiler, false), new("Verse.Pawn.Tick", PatchKind.Postfix, false) },
            ["b"] = new() { new("Verse.Pawn.Kill", PatchKind.Transpiler, false), new("Verse.Pawn.Tick", PatchKind.Prefix, true) },
            ["c"] = new() { new("Verse.Pawn.Kill", PatchKind.Transpiler, false), new("Verse.Pawn.Draw", PatchKind.Postfix, false) },
            ["d"] = new() { new("Verse.Pawn.Draw", PatchKind.Postfix, false) },
            ["busy"] = Enumerable.Range(0, 6).Select(i => new HarmonyPatchInfo($"RimWorld.Need{i}.NeedInterval{(i % 2 == 0 ? "Tick" : "")}", PatchKind.Postfix, false))
                .Append(new HarmonyPatchInfo("RimWorld.StatExtension.GetStatValue", PatchKind.Prefix, false)).ToList(),
        };

        [Fact]
        public void Clashing_patches_among_active_mods_are_found()
        {
            var c = Insight.Conflicts(Patches(), new[] { "a", "b", "d", "zzz" }, k => k.ToUpperInvariant());
            Assert.Equal(2, c.Count);
            Assert.Contains(c, i => i.Level == ProblemLevel.Warning && i.Mods.SequenceEqual(new[] { "a", "b" }) && i.Message.StartsWith("A and B all rewrite Pawn.Kill"));
            Assert.Contains(c, i => i.Level == ProblemLevel.Info && i.Message == "B can stop Pawn.Tick from running, which also stops what A adds to it.");
            Assert.DoesNotContain(c, i => i.Message.Contains("Draw")); // two postfixes are fine
            Assert.Empty(Insight.Conflicts(Patches(), new[] { "a" }, k => k));
        }

        [Fact]
        public void Per_tick_patches_are_counted()
        {
            var t = Insight.TickPatches(Patches(), new[] { "a", "busy", "d" }, k => k);
            Assert.Equal(new[] { "busy", "a" }, t.Select(i => i.Mods[0]));
            Assert.Equal(ProblemLevel.Info, t[0].Level); // 3 tick methods and GetStatValue: 4, under 5
            Assert.StartsWith("busy changes 4 methods", t[0].Message);
            Assert.True(Insight.IsHot("Verse.TickManager.DoSingleTick"));
            Assert.False(Insight.IsHot("Verse.Pawn.Kill"));
        }

        [Fact]
        public void Heavy_mods_are_weighed_from_their_folders()
        {
            var dir = _rw.Local("Big", FakeRimWorld.About("Big", "x.big", "1.5"));
            Directory.CreateDirectory(Path.Combine(dir, "1.5", "Defs"));
            for (var i = 0; i < 301; i++) File.WriteAllText(Path.Combine(dir, "1.5", "Defs", $"d{i}.xml"), "<Defs/>");
            Directory.CreateDirectory(Path.Combine(dir, "Textures", "Things"));
            File.WriteAllBytes(Path.Combine(dir, "Textures", "Things", "a.png"), new byte[1024]);
            var mod = new RimWorldModule().ReadMod(dir, ModSource.Local)!;

            var w = Insight.Weigh(mod);
            Assert.Equal(new ModWeight("x.big", 1024, 1, 301, 0), w);
            var h = Insight.Heavy(new[] { w, new ModWeight("small", 10, 1, 1, 0) }, new[] { "x.big", "small" }, k => k);
            Assert.Equal("x.big", Assert.Single(h).Mods[0]);
            Assert.Empty(Insight.Heavy(new[] { w }, new[] { "small" }, k => k));
        }

        [Fact]
        public void Rules_notes_are_listed_for_active_mods()
        {
            var rules = SortRules.Parse("{ \"rules\": { \"a\": { \"note\": \"Slow with big colonies\" }, \"b\": { \"note\": \" \" } } }");
            var n = Insight.Notes(rules, new[] { "a", "b", "c" }, k => k.ToUpperInvariant());
            Assert.Equal("A: Slow with big colonies", Assert.Single(n).Message);
            Assert.Empty(Insight.Notes(null, new[] { "a" }, k => k));
        }
    }
}
