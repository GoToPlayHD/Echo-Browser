using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Berechtigungen vor der Installation einer Erweiterung (Audit S2): aus manifest.json lesen,
/// verständlich anzeigen, erst nach Zustimmung installieren.
/// </summary>
public class ExtensionPermissionsTests
{
    private static ExtensionPermissionSummary Describe(string manifestJson)
    {
        using var doc = JsonDocument.Parse(manifestJson);
        return ExtensionPermissions.Describe(doc.RootElement);
    }

    [Fact]
    public void ManifestV3_AllUrls_AndTabs()
    {
        var summary = Describe("""
            { "manifest_version": 3, "permissions": ["tabs", "storage", "alarms"], "host_permissions": ["<all_urls>"] }
            """);

        Assert.True(summary.AllSites);
        Assert.Empty(summary.Sites);
        Assert.Equal(new[] { "Ext_Perm_Tabs" }, summary.WarningKeys);
        Assert.Empty(summary.OtherPermissions);   // storage, alarms: keine Nutzerdaten
    }

    [Fact]
    public void ManifestV2_HostsInPermissions_AreSites()
    {
        var summary = Describe("""
            { "manifest_version": 2, "permissions": ["https://*.google.com/*", "http://example.com:8080/", "bookmarks", "somethingNew"] }
            """);

        Assert.False(summary.AllSites);
        Assert.Equal(new[] { "*.google.com", "example.com" }, summary.Sites);
        Assert.Equal(new[] { "Ext_Perm_Bookmarks" }, summary.WarningKeys);
        Assert.Equal(new[] { "somethingNew" }, summary.OtherPermissions);
    }

    [Fact]
    public void ContentScriptsOnAllSites_CountAsAllSites()
    {
        var summary = Describe("""
            { "content_scripts": [ { "matches": ["*://*/*"], "js": ["a.js"] } ] }
            """);

        Assert.True(summary.AllSites);
    }

    [Fact]
    public void SameWarning_IsListedOnce()
    {
        var summary = Describe("""{ "permissions": ["tabs", "webNavigation", "topSites"] }""");
        Assert.Equal(new[] { "Ext_Perm_Tabs" }, summary.WarningKeys);
    }

    [Fact]
    public void NoPermissions_SaysSo()
    {
        var summary = Describe("""{ "name": "Theme", "version": "1.0" }""");

        Assert.True(summary.IsEmpty);
        Assert.Equal(Tr.Get("Ext_Perm_None"), ExtensionPermissions.Format(summary));
    }

    [Fact]
    public void Format_ShortensLongSiteLists()
    {
        var summary = new ExtensionPermissionSummary(false,
            new[] { "a.example", "b.example", "c.example", "d.example", "e.example", "f.example", "g.example" },
            Array.Empty<string>(), Array.Empty<string>());

        string text = ExtensionPermissions.Format(summary);

        Assert.Contains("e.example", text);
        Assert.DoesNotContain("f.example", text);
        Assert.Contains(Tr.Format("Ext_Perm_MoreSites", 2), text);
    }

    [Fact]
    public void EveryWarning_HasATranslation()
    {
        foreach (string key in ExtensionPermissions.WarningKeyByPermission.Values.Distinct())
        {
            Assert.NotEqual(key, LocalizationService.Instance.GetString(key));
        }
    }

    [Theory]
    [InlineData("<all_urls>", true)]
    [InlineData("*://*/*", true)]
    [InlineData("https://*/*", true)]
    [InlineData("http://*/", true)]
    [InlineData("https://*.google.com/*", false)]
    [InlineData("https://example.com/*", false)]
    [InlineData("file:///*", false)]
    [InlineData("tabs", false)]
    public void AllSitesPatterns(string pattern, bool expected)
    {
        Assert.Equal(expected, ExtensionPermissions.IsAllSitesPattern(pattern));
    }

    [Fact]
    public void PrepareFromCrx_ReadsManifest_AndInstallsNothingYet()
    {
        string crx = WriteCrx(new Dictionary<string, string>
        {
            ["manifest.json"] = """
                { "manifest_version": 3, "name": "__MSG_appName__", "version": "4.2.0", "default_locale": "en",
                  "permissions": ["history"], "host_permissions": ["https://mail.example/*"] }
                """,
            ["_locales/en/messages.json"] = """{ "appName": { "message": "Mail Helper" } }""",
            ["_metadata/verified_contents.json"] = "{}"
        });

        var prepared = ExtensionService.Instance.PrepareFromCrx(crx);
        try
        {
            Assert.Equal("Mail Helper", prepared.Name);
            Assert.Equal("4.2.0", prepared.Version);
            Assert.Equal(new[] { "mail.example" }, prepared.Permissions.Sites);
            Assert.Equal(new[] { "Ext_Perm_History" }, prepared.Permissions.WarningKeys);
            Assert.False(Directory.Exists(Path.Combine(prepared.ManifestFolder, "_metadata")));   // lehnt Chromium ab
        }
        finally
        {
            ExtensionService.Instance.DiscardPrepared(prepared);
            File.Delete(crx);
        }

        Assert.False(Directory.Exists(prepared.RootFolder));   // Abbruch: nichts bleibt liegen
    }

    [Fact]
    public void PrepareFromCrx_WithoutManifest_LeavesNothingBehind()
    {
        string crx = WriteCrx(new Dictionary<string, string> { ["readme.txt"] = "keine Erweiterung" });
        string extensions = Path.Combine(AppPaths.DataFolder, "Extensions");
        string prefix = Path.GetFileNameWithoutExtension(crx) + "_";
        try
        {
            Assert.ThrowsAny<Exception>(() => ExtensionService.Instance.PrepareFromCrx(crx));
            Assert.Empty(Directory.GetDirectories(extensions, prefix + "*"));
        }
        finally
        {
            File.Delete(crx);
        }
    }

    /// <summary>CRX3 mit leerem Kopf und den Dateien als ZIP.</summary>
    private static string WriteCrx(Dictionary<string, string> files)
    {
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in files)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        string path = Path.Combine(Path.GetTempPath(), $"echo-test-{Guid.NewGuid():N}.crx");
        using var file = File.Create(path);
        file.Write("Cr24"u8);
        file.Write(BitConverter.GetBytes(3u));   // Version 3
        file.Write(BitConverter.GetBytes(0u));   // Kopflänge
        zip.Position = 0;
        zip.CopyTo(file);
        return path;
    }
}
