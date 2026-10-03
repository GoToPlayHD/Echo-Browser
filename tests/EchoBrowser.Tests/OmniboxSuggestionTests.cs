using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Vorschläge der Adressleiste: Reihenfolge, Duplikate, Inline-Vervollständigung, Suchvorschläge.</summary>
public class OmniboxSuggestionTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0);

    private static SuggestionSource Visit(string title, string url, int daysAgo) => new(title, url, Now.AddDays(-daysAgo));

    [Fact]
    public void HostPrefix_BeatsTitleMatch()
    {
        var result = OmniboxRanker.Rank("git",
            openTabs: [],
            bookmarks: [new("Mein Gitarrenkurs", "https://musik.example/kurs")],
            history: [Visit("GitHub", "https://github.com/", 1)],
            Now);

        Assert.Equal("https://github.com/", result[0].Url);
    }

    [Fact]
    public void FrequentAndRecentSites_RankHigher()
    {
        var history = new List<SuggestionSource>
        {
            Visit("Wikipedia (alt)", "https://wiki.example/alt", 200),
            Visit("Wikipedia", "https://wikipedia.org/", 1),
            Visit("Wikipedia", "https://wikipedia.org/", 2),
            Visit("Wikipedia", "https://wikipedia.org/", 3)
        };

        var result = OmniboxRanker.Rank("wiki", [], [], history, Now);

        Assert.Equal("https://wikipedia.org/", result[0].Url);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SameAddress_FromSeveralSources_AppearsOnce_AsOpenTab()
    {
        var result = OmniboxRanker.Rank("github",
            openTabs: [new("GitHub", "https://github.com", TabId: "tab-1")],
            bookmarks: [new("GitHub", "https://www.github.com/")],
            history: [Visit("GitHub", "http://github.com/", 1)],
            Now);

        var single = Assert.Single(result);
        Assert.Equal(SuggestionKind.SwitchToTab, single.Kind);
        Assert.Equal("tab-1", single.TabId);
    }

    [Fact]
    public void AllWords_MustMatch()
    {
        var result = OmniboxRanker.Rank("echo browser",
            [],
            [new("Echo Browser auf GitHub", "https://github.com/GoToPlayHD/Echo-Browser"), new("Echo der Zeit", "https://radio.example/echo")],
            [],
            Now);

        Assert.Equal("https://github.com/GoToPlayHD/Echo-Browser", Assert.Single(result).Url);
    }

    [Fact]
    public void ResultCount_IsLimited()
    {
        var history = Enumerable.Range(0, 20).Select(i => Visit($"Seite {i}", $"https://seite{i}.example/", i)).ToList();

        Assert.Equal(OmniboxRanker.MaxLocalResults, OmniboxRanker.Rank("seite", [], [], history, Now).Count);
    }

    [Fact]
    public void EmptyInput_GivesNothing()
    {
        Assert.Empty(OmniboxRanker.Rank("  ", [], [], [Visit("A", "https://a.example/", 0)], Now));
    }

    [Theory]
    [InlineData("git", "github.com")]
    [InlineData("GIT", "GIThub.com")]       // Getipptes bleibt, wie es ist
    [InlineData("github.com/Go", "github.com/GoToPlayHD/Echo-Browser")]
    public void InlineCompletion_CompletesHostThenPath(string typed, string expected)
    {
        var ranked = new List<OmniboxSuggestion>
        {
            new(SuggestionKind.History, "Echo", "https://github.com/GoToPlayHD/Echo-Browser", 100)
        };

        Assert.Equal(expected, OmniboxRanker.InlineCompletion(typed, ranked));
    }

    [Theory]
    [InlineData("wetter ")]
    [InlineData("wetter berlin")]
    [InlineData("")]
    public void InlineCompletion_NotForSearchTerms(string typed)
    {
        var ranked = new List<OmniboxSuggestion> { new(SuggestionKind.History, "Wetter", "https://wetter.example/", 100) };

        Assert.Null(OmniboxRanker.InlineCompletion(typed, ranked));
    }

    [Fact]
    public void InlineCompletion_SkipsSearchSuggestions()
    {
        var ranked = new List<OmniboxSuggestion> { new(SuggestionKind.Search, "github copilot", "github copilot", 100) };

        Assert.Null(OmniboxRanker.InlineCompletion("git", ranked));
    }

    [Theory]
    [InlineData("[\"wet\",[\"wetter\",\"wetter berlin\",\"Wetter\"]]", new[] { "wetter", "wetter berlin" })]
    [InlineData("{\"query\":\"wet\",\"suggestions\":[\"wetter\",\"wetteronline\"]}", new[] { "wetter", "wetteronline" })]
    [InlineData("[{\"phrase\":\"wetter\"},{\"phrase\":\"wetter heute\"}]", new[] { "wetter", "wetter heute" })]
    [InlineData("kein json", new string[0])]
    [InlineData("{}", new string[0])]
    public void SearchSuggestions_AreParsedFromCommonFormats(string json, string[] expected)
    {
        Assert.Equal(expected, SearchSuggestionClient.Parse(json));
    }

    [Theory]
    [InlineData("google", "suggestqueries.google.com")]
    [InlineData("duckduckgo", "duckduckgo.com/ac")]
    [InlineData("unbekannt", "duckduckgo.com/ac")]
    public void Endpoints_MatchEngine(string engine, string expectedPart)
    {
        Assert.Contains(expectedPart, SearchSuggestionClient.EndpointFor(engine, "a b"));
        Assert.Contains("a%20b", SearchSuggestionClient.EndpointFor(engine, "a b"));
    }
}
