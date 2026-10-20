using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace SteamCmdHelper.Tests;

/// <summary>The app in other languages: lookups by English text, user files over built-in ones, and the built-in files themselves.</summary>
public sealed class LocalizerTests : IDisposable
{
    readonly TempRepo _dir = new();

    public void Dispose()
    {
        Localizer.Use("", Array.Empty<Translation>());
        _dir.Dispose();
    }

    static Translation Lang(string code, params (string En, string To)[] strings) =>
        new() { Code = code, Language = code.ToUpperInvariant(), Strings = strings.ToDictionary(s => s.En, s => s.To) };

    [Fact]
    public void Text_is_looked_up_by_its_English_and_left_alone_when_missing()
    {
        Localizer.Use("de", new[] { Lang("de", ("&Refresh", "&Neu laden"), ("Close", "")) });
        Assert.Equal("de", Localizer.Current);
        Assert.Equal("&Neu laden", Localizer.T("&Refresh"));
        Assert.Equal("Close", Localizer.T("Close")); // empty = not translated yet
        Assert.Equal("Something else", Localizer.T("Something else"));
        Assert.True(Localizer.Translates("&Refresh"));
        Assert.False(Localizer.Translates("&Neu laden"));
    }

    [Fact]
    public void English_and_unknown_codes_translate_nothing()
    {
        var langs = new[] { Lang("de", ("Close", "Schließen")) };
        Localizer.Use("fr", langs);
        Assert.Equal("", Localizer.Current);
        Assert.Equal("Close", Localizer.T("Close"));
        Localizer.Use("de", langs);
        Localizer.Use("en", langs);
        Assert.Equal("Close", Localizer.T("Close"));
    }

    [Fact]
    public void A_file_in_the_languages_folder_adds_a_language_or_replaces_a_built_in_one()
    {
        _dir.Write("de.json", """{ "language": "Deutsch (mine)", "strings": { "Close": "Zu" } }""");
        _dir.Write("pt-BR.json", """{ "language": "Português", "code": "pt-BR", "strings": { "Close": "Fechar" }, }""");
        _dir.Write("broken.json", "{ not json");
        var all = Localizer.Available(_dir.Root);
        Assert.Equal("Deutsch (mine)", Assert.Single(all, t => t.Code == "de").Language);
        Assert.Equal("Fechar", Assert.Single(all, t => t.Code == "pt-BR").Strings["Close"]);
        Assert.DoesNotContain(all, t => t.Code == "broken");
        Localizer.Use("de", all);
        Assert.Equal("Zu", Localizer.T("Close"));
    }

    [Fact]
    public void The_template_has_every_known_text_and_keeps_existing_translations()
    {
        var start = Lang("de", ("Close", "Schließen"));
        var json = Localizer.Template(start, new[] { "Cancel", "Close" }, new[] { Lang("ru", ("&Refresh", "&Обновить")) });
        var t = Localizer.Parse(json, "x")!;
        Assert.Equal("de", t.Code);
        Assert.Equal(new[] { "&Refresh", "Cancel", "Close" }, t.Strings.Keys);
        Assert.Equal("Schließen", t.Strings["Close"]);
        Assert.Equal("", t.Strings["Cancel"]);
        Assert.Contains("Schließen", json); // written readably, not as \u escapes
    }

    public static IEnumerable<object[]> BuiltIn() => Localizer.Available(null).Select(t => new object[] { t.Code });

    [Theory]
    [MemberData(nameof(BuiltIn))]
    public void Built_in_translations_keep_mnemonics_and_placeholders(string code)
    {
        var t = Localizer.Available(null).Single(x => x.Code == code);
        Assert.NotEmpty(t.Strings);
        foreach (var (en, to) in t.Strings)
        {
            if (to.Length == 0) continue;
            static int Marks(string s) => Regex.Matches(s, @"&(?=[^\s&])").Count;
            Assert.True(Marks(en) == Marks(to), $"{code}: \"{to}\" should mark one Alt letter like \"{en}\"");
            foreach (Match m in Regex.Matches(en, @"\{\w+\}|Ctrl\+\w+|\bF\d\b"))
                Assert.True(to.Contains(m.Value), $"{code}: \"{to}\" lost {m.Value}");
        }
    }

    [Fact]
    public void The_three_built_in_languages_are_there()
    {
        var codes = Localizer.Available(null).Select(t => t.Code).ToList();
        Assert.Contains("de", codes);
        Assert.Contains("ru", codes);
        Assert.Contains("zh-Hans", codes);
    }
}
