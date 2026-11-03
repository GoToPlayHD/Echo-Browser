using System.Collections.ObjectModel;
using System.IO;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Gemeinsame Lesezeichen für Seiten- und Lesezeichenleiste, Shield-Hauptseiten, Inkognito-Profile.</summary>
public class SyncAndPrivacyTests
{
    [Fact]
    public void SidebarMigration_AddsNewFavorites_AndKeepsTheirIcon()
    {
        var bookmarks = new ObservableCollection<Bookmark> { new("GitHub", "https://github.com") };
        var favorites = new[]
        {
            new SidebarFavorite("GitHub", "https://github.com/", "github", "#F0F2F5"),   // schon als Lesezeichen da
            new SidebarFavorite("YouTube", "https://www.youtube.com", "youtube", "#FF4A4A"),
            new SidebarFavorite("Notizen", @"file:///C:/Notizen/index.html", "globe")       // ohne eigenes Symbol
        };

        int added = BookmarkService.MergeSidebarFavorites(bookmarks, favorites);

        Assert.Equal(2, added);
        Assert.Equal(3, bookmarks.Count);
        Assert.Equal("github", bookmarks[0].IconKey);                  // vorhandenes Lesezeichen bekommt das Symbol
        Assert.Equal("youtube", bookmarks[1].IconKey);
        Assert.True(bookmarks[1].HasCustomIcon);
        Assert.Null(bookmarks[2].IconKey);                             // "globe" = Website-Symbol verwenden
        Assert.False(bookmarks[2].HasCustomIcon);
    }

    [Fact]
    public void SidebarMigration_FindsBookmarksInsideGroups()
    {
        var group = Bookmark.CreateGroup("Arbeit");
        group.Children.Add(new Bookmark("Wiki", "https://de.wikipedia.org"));
        var bookmarks = new ObservableCollection<Bookmark> { group };

        int added = BookmarkService.MergeSidebarFavorites(bookmarks,
            new[] { new SidebarFavorite("Wikipedia", "https://de.wikipedia.org", "wikipedia", "#C4C7CC") });

        Assert.Equal(0, added);
        Assert.Single(bookmarks);
        Assert.Equal("wikipedia", group.Children[0].IconKey);
    }

    [Fact]
    public void SidebarMigration_MovesLegacyFileAway()
    {
        string dir = Path.Combine(Path.GetTempPath(), "echo-sidebar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string legacy = Path.Combine(dir, "sidebar_favorites.json");
            var merged = new List<SidebarFavorite>();
            File.WriteAllText(legacy, """[{"Title":"A","Url":"https://a.example","IconKey":"globe"}]""");
            SidebarService.MigrateInto(favorites => { merged.AddRange(favorites); return merged.Count; }, legacy);

            Assert.Single(merged);
            Assert.False(File.Exists(legacy));
            Assert.True(File.Exists(legacy + ".migrated"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Bookmark_WithoutCustomIcon_DoesNotWriteIconFields()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new Bookmark("A", "https://a.example"));
        Assert.DoesNotContain("IconKey", json);
        Assert.DoesNotContain("IconColor", json);
    }

    [Theory]
    [InlineData("https://mixpanel.com/", "https://mixpanel.com/", true)]
    [InlineData("https://mixpanel.com/pricing", "https://mixpanel.com/pricing#plans", true)]
    [InlineData("https://ads.example/frame", "https://news.example/", false)]
    [InlineData("https://ads.example/frame", null, false)]
    public void Shield_RecognizesTheMainDocumentOfANavigation(string request, string? navigation, bool expected)
    {
        Assert.Equal(expected, AdBlockerService.IsSameDocumentUrl(request, navigation));
    }

    [Fact]
    public void CosmeticScript_EmulatesXhrNetworkErrorOnlyOnce()
    {
        string script = AdBlockerService.Instance.GetCosmeticScript();
        Assert.DoesNotContain("this.onerror(", script);
        Assert.Contains("'loadend'", script);
    }

    [Fact]
    public void LeftoverIncognitoProfiles_AreDeleted_OtherProfilesStay()
    {
        string data = Path.Combine(Path.GetTempPath(), "echo-wv-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(data, "EBWebView");
        Directory.CreateDirectory(Path.Combine(root, "WV2Profile_incognito-abc123"));
        Directory.CreateDirectory(Path.Combine(root, "Default"));
        try
        {
            BrowserEnvironment.DeleteLeftoverIncognitoProfiles(data);

            Assert.False(Directory.Exists(Path.Combine(root, "WV2Profile_incognito-abc123")));
            Assert.True(Directory.Exists(Path.Combine(root, "Default")));
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public void IncognitoProfileName_IsAValidWebView2ProfileName()
    {
        // Erlaubt sind Buchstaben, Ziffern und #@$()+-_~. (höchstens 64 Zeichen)
        Assert.Matches(@"^[A-Za-z0-9#@$()+\-_~.]{1,64}$", BrowserEnvironment.IncognitoProfileName);
    }
}
