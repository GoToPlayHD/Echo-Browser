using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Kacheln "Meistbesucht" auf der Startseite.</summary>
public class MostVisitedTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0);

    private static SuggestionSource Visit(string title, string url, int daysAgo = 1) => new(title, url, Now.AddDays(-daysAgo));

    [Fact]
    public void OneTilePerSite_OrderedByVisits()
    {
        var history = new[]
        {
            Visit("Artikel 1", "https://www.heise.de/news/1"),
            Visit("heise online", "https://www.heise.de/"),
            Visit("Artikel 2", "https://heise.de/news/2"),
            Visit("GitHub", "https://github.com/"),
            Visit("Repo", "https://github.com/a/b")
        };

        var result = MostVisited.Calculate(history, Array.Empty<string>(), Now);

        Assert.Equal(new[] { "heise.de", "github.com" }, result.Select(r => r.Host));
        Assert.Equal("https://www.heise.de/", result[0].Url);   // kürzeste Adresse = Startseite
        Assert.Equal("heise.de", result[0].Title);               // Domain statt (oft unbrauchbarer) Seitentitel
    }

    [Fact]
    public void SitesAlreadyInShortcuts_AreSkipped()
    {
        var history = new[] { Visit("GitHub", "https://github.com/"), Visit("Wiki", "https://de.wikipedia.org/") };

        var result = MostVisited.Calculate(history, new[] { "https://www.github.com" }, Now);

        Assert.Equal("de.wikipedia.org", Assert.Single(result).Host);
    }

    [Fact]
    public void OldVisits_AndNonWebAddresses_AreIgnored()
    {
        var history = new[]
        {
            Visit("Alt", "https://old.example/", daysAgo: 90),
            Visit("Datei", "file:///C:/x.html"),
            Visit("Einstellungen", "echo://settings")
        };

        Assert.Empty(MostVisited.Calculate(history, Array.Empty<string>(), Now));
    }

    [Fact]
    public void InterimTitles_AreNotShown()
    {
        var result = MostVisited.Calculate(new[] { Visit("..Loading..", "https://www.example.com/") }, Array.Empty<string>(), Now);

        Assert.Equal("example.com", Assert.Single(result).Title);
    }

    [Fact]
    public void ResultCount_IsLimited()
    {
        var history = Enumerable.Range(0, 20).Select(i => Visit($"Seite {i}", $"https://site{i}.example/"));

        Assert.Equal(6, MostVisited.Calculate(history, Array.Empty<string>(), Now).Count);
    }
}
