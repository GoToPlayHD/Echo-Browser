using System.IO;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Regressionstests für die Fehlerbehebungen aus dem Code-Review (November 2026).</summary>
public class BugfixTests
{
    [Theory]
    [InlineData("cjpalhdlnbpafiamejdnhcphjbkeiagm", true)]
    [InlineData("CJPALHDLNBPAFIAMEJDNHCPHJBKEIAGM", false)]
    [InlineData(@"x\..\..\..\..\foo", false)]
    [InlineData("abc%26prod%3D1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ExtensionId_OnlyRealStoreIdsAreAccepted(string? id, bool expected)
    {
        Assert.Equal(expected, ExtensionService.IsValidExtensionId(id));
    }

    [Fact]
    public void Sanitize_RemovesOnlyTopLevelReservedEntries()
    {
        string dir = Path.Combine(Path.GetTempPath(), "echo-sanitize-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "_metadata"));
            Directory.CreateDirectory(Path.Combine(dir, "_locales", "de"));
            Directory.CreateDirectory(Path.Combine(dir, "js"));
            File.WriteAllText(Path.Combine(dir, "js", "_helpers.js"), "");
            File.WriteAllText(Path.Combine(dir, "manifest.json"), "{}");

            Assert.True(ExtensionService.HasReservedEntries(dir));
            ExtensionService.SanitizeUnpackedExtension(dir);

            Assert.False(Directory.Exists(Path.Combine(dir, "_metadata")));
            Assert.True(Directory.Exists(Path.Combine(dir, "_locales")));
            Assert.True(File.Exists(Path.Combine(dir, "js", "_helpers.js")));
            Assert.False(ExtensionService.HasReservedEntries(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CosmeticScript_DoesNotBlockWholeSites()
    {
        string script = AdBlockerService.Instance.GetCosmeticScript();
        Assert.DoesNotContain("##BLOCKED_PATTERNS##", script);
        Assert.Contains("\"events.reddit.com\"", script);
        Assert.DoesNotContain("\"reddit.com\"", script);
        Assert.DoesNotContain("'apple.com'", script);
    }

    [Theory]
    [InlineData("https://example.com/page", true)]
    [InlineData("http://example.com", true)]
    [InlineData("echo://settings", true)]
    [InlineData("file:///C:/Windows/win.ini", false)]
    [InlineData("chrome://flags", false)]
    [InlineData("javascript:alert(1)", false)]
    public void CrashPage_ReloadsOnlyWebAndInternalPages(string url, bool expected)
    {
        Assert.Equal(expected, CrashPageService.IsReloadable(url));
    }

    [Fact]
    public void CrashPage_DoesNotLeakTokenIntoUserUrl()
    {
        string html = CrashPageService.GetCrashPageHtml("https://evil.example/###ECHO_BRIDGE_TOKEN##");
        Assert.Contains("https://evil.example/###ECHO_BRIDGE_TOKEN##", html);
    }

    [Theory]
    [InlineData("https://chromewebstore.google.com/detail/x/abc", true)]
    [InlineData("https://clients2.googleusercontent.com/crx/blobs/abc", true)]
    [InlineData("https://r1---sn-abc.gvt1.com/edgedl/x.crx", true)]
    [InlineData("https://evil.example/fake.crx", false)]
    [InlineData("http://clients2.google.com/x.crx", false)]
    [InlineData("https://clients2.google.com.evil.example/x.crx", false)]
    public void CrxAutoInstall_OnlyFromGoogle(string url, bool expected)
    {
        Assert.Equal(expected, DownloadService.IsTrustedCrxSource(url));
    }

    [Fact]
    public void IncognitoDownloads_AreNotPersisted()
    {
        string file = Path.Combine(Path.GetTempPath(), "echo-downloads-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var service = new DownloadService(file);
            service.Add(new DownloadItem { FileName = "normal.zip", IsCompleted = true });
            service.Add(new DownloadItem { FileName = "geheim.zip", IsCompleted = true, IsIncognito = true });

            string json = File.ReadAllText(file);
            Assert.Contains("normal.zip", json);
            Assert.DoesNotContain("geheim.zip", json);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Quarantine_KeepsCorruptFileForRecovery()
    {
        string dir = Path.Combine(Path.GetTempPath(), "echo-quarantine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, "{ kaputt");
            AtomicFile.Quarantine(file, new InvalidDataException());

            Assert.False(File.Exists(file));
            Assert.Single(Directory.GetFiles(dir, "settings.json.corrupt-*"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
