using SteamCmdHelper.DropPod;
using SteamCmdHelper.DropPod.RimWorld;
using Xunit;

namespace SteamCmdHelper.Tests.DropPod;

/// <summary>Sorting the active mods: rules, ties, the game's own packages, loops and the player's rules file.</summary>
public sealed class LoadOrderTests
{
    static ModManifest Mod(string id, string? name = null, string? after = null, string? before = null, string? needs = null)
    {
        var x = "<ModMetaData>" + $"<name>{name ?? id}</name><packageId>{id}</packageId>"
            + (after != null ? "<loadAfter>" + string.Concat(after.Split(',').Select(a => $"<li>{a}</li>")) + "</loadAfter>" : "")
            + (before != null ? "<loadBefore>" + string.Concat(before.Split(',').Select(a => $"<li>{a}</li>")) + "</loadBefore>" : "")
            + (needs != null ? "<modDependencies>" + string.Concat(needs.Split(',').Select(a => $"<li><packageId>{a}</packageId></li>")) + "</modDependencies>" : "")
            + "</ModMetaData>";
        return RimWorldAbout.Parse(x, "/mods/" + id);
    }

    static string[] Ids(SortResult r) => r.Order.Select(m => m.Id).ToArray();

    readonly RimWorldModule _rw = new();

    [Fact]
    public void Dependencies_and_load_rules_are_kept_and_everything_else_keeps_its_place()
    {
        var mods = new[] { Mod("z.ui", after: "a.core"), Mod("a.core"), Mod("m.mid"), Mod("b.patch", needs: "z.ui"), Mod("c.early", before: "a.core") };
        Assert.Equal(new[] { "c.early", "a.core", "z.ui", "m.mid", "b.patch" }, Ids(LoadOrder.Sort(mods, null)));
    }

    [Fact]
    public void Alphabetical_mode_orders_ties_by_name()
    {
        var mods = new[] { Mod("c.c", "Charlie"), Mod("a.a", "alpha"), Mod("b.b", "Bravo", needs: "c.c") };
        Assert.Equal(new[] { "a.a", "c.c", "b.b" }, Ids(LoadOrder.Sort(mods, null, SortMode.Alphabetical)));
    }

    [Fact]
    public void The_base_game_and_dlc_come_first_but_harmony_still_goes_before_core()
    {
        var mods = new[]
        {
            Mod("Some.Mod"), Mod("Ludeon.RimWorld.Biotech"), Mod("brrainz.harmony", before: "Ludeon.RimWorld"),
            Mod("Ludeon.RimWorld.Royalty"), Mod("Ludeon.RimWorld"),
        };
        Assert.Equal(new[] { "brrainz.harmony", "Ludeon.RimWorld", "Ludeon.RimWorld.Royalty", "Ludeon.RimWorld.Biotech", "Some.Mod" },
            Ids(LoadOrder.Sort(mods, null, tier: _rw.SortTier)));
    }

    [Fact]
    public void Ids_match_in_any_case_and_rules_about_inactive_mods_are_ignored()
    {
        var mods = new[] { Mod("B.Second", after: "A.FIRST,not.installed"), Mod("a.first") };
        Assert.Equal(new[] { "a.first", "B.Second" }, Ids(LoadOrder.Sort(mods, null)));
    }

    [Fact]
    public void A_loop_is_reported_and_its_mods_kept_together_in_place()
    {
        var mods = new[] { Mod("x.start"), Mod("a.a", after: "b.b"), Mod("b.b", after: "c.c"), Mod("c.c", after: "a.a"), Mod("y.end", after: "c.c") };
        var r = LoadOrder.Sort(mods, null);
        Assert.Equal(5, r.Order.Count);
        var cycle = Assert.Single(r.Cycles);
        Assert.Equal(new[] { "a.a", "b.b", "c.c" }, cycle.Mods.Select(m => m.Id).OrderBy(x => x));
        Assert.Contains("a.a loads after b.b", cycle.Edges);
        Assert.Equal("x.start", r.Order[0].Id);
        Assert.Equal("y.end", r.Order[^1].Id);
    }

    [Fact]
    public void A_mod_naming_itself_is_not_a_loop()
    {
        var r = LoadOrder.Sort(new[] { Mod("a.a", after: "a.a") }, null);
        Assert.Empty(r.Cycles);
        Assert.Single(r.Order);
    }

    [Fact]
    public void Version_specific_rules_apply_for_that_version()
    {
        var b = RimWorldAbout.Parse("<ModMetaData><packageId>b.b</packageId><loadAfterByVersion><v1.5><li>a.a</li></v1.5></loadAfterByVersion></ModMetaData>");
        var mods = new[] { b, Mod("a.a") };
        Assert.Equal(new[] { "a.a", "b.b" }, Ids(LoadOrder.Sort(mods, "1.5.4104")));
        Assert.Equal(new[] { "b.b", "a.a" }, Ids(LoadOrder.Sort(mods, "1.4")));
    }

    [Fact]
    public void The_players_rules_file_adds_rules_and_first_last()
    {
        var rules = SortRules.Parse("""
            // comments are fine
            { "rules": {
                "A.A": { "loadAfter": ["c.c"] },
                "z.last": { "loadLast": true },
                "y.first": { "loadFirst": true, },
            } }
            """);
        var mods = new[] { Mod("z.last"), Mod("a.a"), Mod("c.c"), Mod("y.first") };
        Assert.Equal(new[] { "y.first", "c.c", "a.a", "z.last" }, Ids(LoadOrder.Sort(mods, null, rules: rules)));
        Assert.Throws<System.Text.Json.JsonException>(() => SortRules.Parse("{ \"rules\": "));
    }

    [Fact]
    public void A_long_chain_and_many_mods_sort_without_trouble()
    {
        var mods = Enumerable.Range(0, 3000).Select(i => Mod($"m.m{i}", after: i > 0 ? $"m.m{i - 1}" : null)).Reverse().ToArray();
        var r = LoadOrder.Sort(mods, null);
        Assert.Equal("m.m0", r.Order[0].Id);
        Assert.Equal("m.m2999", r.Order[^1].Id);
        Assert.Empty(r.Cycles);
    }
}
