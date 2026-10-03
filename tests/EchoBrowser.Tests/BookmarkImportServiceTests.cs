using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Lesezeichen aus Chrome/Edge (JSON) und HTML-Dateien importieren bzw. als HTML exportieren.</summary>
public class BookmarkImportServiceTests
{
    private const string ChromeJson = """
    {
      "checksum": "x",
      "roots": {
        "bookmark_bar": {
          "type": "folder", "name": "Lesezeichenleiste",
          "children": [
            { "type": "url", "name": "GitHub", "url": "https://github.com/" },
            { "type": "folder", "name": "Arbeit", "children": [
                { "type": "url", "name": "Docs", "url": "https://learn.microsoft.com/" },
                { "type": "url", "name": "Bookmarklet", "url": "javascript:alert(1)" }
            ] }
          ]
        },
        "other": { "type": "folder", "children": [ { "type": "url", "name": "", "url": "https://example.com/" } ] },
        "synced": { "type": "folder", "children": [ { "type": "url", "name": "GitHub doppelt", "url": "https://www.github.com" } ] }
      },
      "version": 1
    }
    """;

    [Fact]
    public void Chromium_FlattensFoldersAndSkipsScripts()
    {
        var result = BookmarkImportService.ParseChromium(ChromeJson);

        Assert.Equal(new[] { "https://github.com/", "https://learn.microsoft.com/", "https://example.com/" }, result.Select(b => b.Url));
        Assert.Equal("Docs", result[1].Title);
        Assert.Equal("https://example.com/", result[2].Title); // ohne Namen: Adresse als Titel
    }

    [Fact]
    public void NetscapeHtml_ReadsLinksWithEntitiesAndFolders()
    {
        const string html = """
            <!DOCTYPE NETSCAPE-Bookmark-file-1>
            <DL><p>
                <DT><H3>Ordner</H3>
                <DL><p>
                    <DT><A HREF="https://example.com/?a=1&amp;b=2" ADD_DATE="1">Beispiel &amp; Co</A>
                    <DT><A HREF="place:sort=8">Firefox-intern</A>
                </DL><p>
                <DT><a href="http://heise.de" ICON="data:image/png;base64,AAA">heise <b>online</b></a>
            </DL><p>
            """;

        var result = BookmarkImportService.ParseNetscapeHtml(html);

        Assert.Equal(2, result.Count);
        Assert.Equal(new ImportedBookmark("Beispiel & Co", "https://example.com/?a=1&b=2"), result[0]);
        Assert.Equal(new ImportedBookmark("heise online", "http://heise.de"), result[1]);
    }

    [Fact]
    public void Export_ThenImport_RoundTrips()
    {
        var group = Bookmark.CreateGroup("Arbeit <intern>");
        group.Children.Add(new Bookmark("Docs & Wiki", "https://learn.microsoft.com/?x=1&y=2"));
        var bookmarks = new[] { new Bookmark("GitHub", "https://github.com/"), group };

        string html = BookmarkImportService.ToNetscapeHtml(bookmarks);
        var reimported = BookmarkImportService.ParseNetscapeHtml(html);

        Assert.Contains("<H3>Arbeit &lt;intern&gt;</H3>", html);
        Assert.Equal(new[]
        {
            new ImportedBookmark("GitHub", "https://github.com/"),
            new ImportedBookmark("Docs & Wiki", "https://learn.microsoft.com/?x=1&y=2")
        }, reimported);
    }

    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("chrome://settings", false)]
    [InlineData("file:///C:/x.html", false)]
    [InlineData("kein link", false)]
    public void OnlyWebAddresses_AreImported(string url, bool expected)
    {
        Assert.Equal(expected, BookmarkImportService.IsImportableUrl(url));
    }
}
