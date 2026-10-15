using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The mod list's status badges, filter, sort order and the recent mods folders.</summary>
public class ModRowsTests
{
    static ModInfo Mod(string name = "Better Storage", string version = "1.4.0", string? id = "2000000001", string? problem = null)
    {
        var m = new ModInfo { Name = name, Folder = name.Replace(" ", ""), Version = version, WorkshopId = id };
        if (problem != null) m.Problems.Add(problem);
        return m;
    }

    [Theory]
    [InlineData("1.4.0", RowState.UpToDate)]
    [InlineData("v1.4.0", RowState.UpToDate)]
    [InlineData("1.3.9", RowState.Changed)]
    [InlineData("...", RowState.Ready)]
    [InlineData("?", RowState.Ready)]
    [InlineData(null, RowState.Ready)]
    public void A_mod_on_Steam_compares_the_version_there_with_the_one_to_upload(string? onSteam, RowState expected)
    {
        Assert.Equal(expected, ModRows.State(Mod(), onSteam, failedLastRun: false));
    }

    [Fact]
    public void Fix_beats_a_failure_and_a_failure_beats_everything_else()
    {
        Assert.Equal(RowState.Fix, ModRows.State(Mod(problem: "no changelog"), "1.4.0", failedLastRun: true));
        Assert.Equal(RowState.Failed, ModRows.State(Mod(), "1.4.0", failedLastRun: true));
        Assert.Equal(RowState.Failed, ModRows.State(Mod(id: null), "-", failedLastRun: true));
    }

    [Fact]
    public void A_mod_not_on_Steam_is_new()
    {
        Assert.Equal(RowState.New, ModRows.State(Mod(id: null), "-", failedLastRun: false));
        Assert.Equal("New", ModRows.Label(RowState.New));
        Assert.Equal("Up to date", ModRows.Label(RowState.UpToDate));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("storage", true)]
    [InlineData("BetterSt", true)]
    [InlineData("2000000001", true)]
    [InlineData("hauling", false)]
    public void The_filter_matches_name_folder_or_Workshop_ID(string filter, bool shown)
    {
        Assert.Equal(shown, ModRows.Show(Mod(), RowState.Changed, filter, hideUpToDate: false));
    }

    [Fact]
    public void Hide_up_to_date_hides_only_up_to_date_rows()
    {
        Assert.False(ModRows.Show(Mod(), RowState.UpToDate, "", hideUpToDate: true));
        Assert.True(ModRows.Show(Mod(), RowState.UpToDate, "", hideUpToDate: false));
        foreach (var s in new[] { RowState.Fix, RowState.Failed, RowState.Changed, RowState.New, RowState.Ready })
            Assert.True(ModRows.Show(Mod(), s, "", hideUpToDate: true));
    }

    [Fact]
    public void Natural_sort_orders_numbers_by_value()
    {
        var names = new List<string> { "Mod 10", "mod 2", "Mod 1", "1.10.0", "1.9.2", "1.9.10" };
        names.Sort(ModRows.CompareNatural);
        Assert.Equal(new[] { "1.9.2", "1.9.10", "1.10.0", "Mod 1", "mod 2", "Mod 10" }, names);
    }

    [Fact]
    public void Sorting_by_status_puts_what_needs_attention_first()
    {
        var states = new List<RowState> { RowState.UpToDate, RowState.New, RowState.Fix, RowState.Changed, RowState.Failed, RowState.Ready };
        states.Sort(ModRows.CompareState);
        Assert.Equal(new[] { RowState.Fix, RowState.Failed, RowState.Changed, RowState.New, RowState.Ready, RowState.UpToDate }, states);
    }

    [Fact]
    public void Recent_folders_keep_the_newest_first_without_duplicates()
    {
        var s = new AppSettings { DoNotSave = true };
        s.Remember("/mods/a");
        s.Remember("/mods/b");
        s.Remember("/mods/A/");
        Assert.Equal("/mods/A", s.RepoPath);
        Assert.Equal(new[] { "/mods/A", "/mods/b" }, s.RecentRepos);
        for (var i = 0; i < 20; i++) s.Remember("/mods/x" + i);
        Assert.Equal(AppSettings.MaxRecent, s.RecentRepos.Count);
        Assert.Equal("/mods/x19", s.RecentRepos[0]);
    }

    [Fact]
    public void Recent_folders_that_are_gone_are_not_offered()
    {
        using var repo = new TempRepo();
        var s = new AppSettings { DoNotSave = true };
        s.Remember(Path.Combine(repo.Root, "missing"));
        s.Remember(repo.Root);
        Assert.Equal(new[] { repo.Root }, s.RecentThatExist());
    }
}
