using System.IO;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Datenordner: Standard unter %LOCALAPPDATA%, eigener Ordner über ECHO_USER_DATA_DIR.</summary>
public class AppPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutCustomFolder_UsesLocalAppData(string? custom)
    {
        // Nicht der Installationsordner "EchoBrowser" – den räumt Velopack bei jedem Update auf
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoBrowserData");
        Assert.Equal(expected, AppPaths.ResolveDataFolder(custom));
    }

    [Fact]
    public void LegacyData_IsMovedOut_ProgramFilesStay()
    {
        string root = Path.Combine(Path.GetTempPath(), "echo-migrate-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "EchoBrowser");
        string target = Path.Combine(root, "EchoBrowserData");
        try
        {
            // Velopack-Installation mit alten Daten darin
            Directory.CreateDirectory(Path.Combine(legacy, "current"));
            Directory.CreateDirectory(Path.Combine(legacy, "packages"));
            File.WriteAllText(Path.Combine(legacy, "Update.exe"), "");
            File.WriteAllText(Path.Combine(legacy, "bookmarks.json"), "[1]");
            File.WriteAllText(Path.Combine(legacy, "settings.json"), "{}");
            File.WriteAllText(Path.Combine(legacy, "blocklist.txt"), "x");
            Directory.CreateDirectory(Path.Combine(legacy, "WebView2Data", "EBWebView"));
            string extFolder = Path.Combine(legacy, "Extensions", "ublock_1", "src");
            Directory.CreateDirectory(extFolder);
            File.WriteAllText(Path.Combine(legacy, "Extensions", "extensions-index.json"),
                System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["abc"] = extFolder }));

            int moved = AppPaths.MigrateLegacyDataFolder(legacy, target);

            Assert.Equal(5, moved);
            Assert.Equal("[1]", File.ReadAllText(Path.Combine(target, "bookmarks.json")));
            Assert.True(Directory.Exists(Path.Combine(target, "WebView2Data", "EBWebView")));
            Assert.True(Directory.Exists(Path.Combine(legacy, "current")));
            Assert.True(File.Exists(Path.Combine(legacy, "Update.exe")));
            Assert.False(File.Exists(Path.Combine(legacy, "bookmarks.json")));

            var index = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(target, "Extensions", "extensions-index.json")))!;
            Assert.Equal(Path.Combine(target, "Extensions", "ublock_1", "src"), index["abc"]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LegacyData_NeverOverwritesNewerData()
    {
        string root = Path.Combine(Path.GetTempPath(), "echo-migrate-" + Guid.NewGuid().ToString("N"));
        string legacy = Path.Combine(root, "EchoBrowser");
        string target = Path.Combine(root, "EchoBrowserData");
        try
        {
            Directory.CreateDirectory(legacy);
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(legacy, "bookmarks.json"), "alt");
            File.WriteAllText(Path.Combine(target, "bookmarks.json"), "neu");

            AppPaths.MigrateLegacyDataFolder(legacy, target);

            Assert.Equal("neu", File.ReadAllText(Path.Combine(target, "bookmarks.json")));
            Assert.True(File.Exists(Path.Combine(legacy, "bookmarks.json")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CustomFolder_IsMadeAbsolute()
    {
        string resolved = AppPaths.ResolveDataFolder(" echo-test-profile ");
        Assert.True(Path.IsPathFullyQualified(resolved));
        Assert.Equal("echo-test-profile", Path.GetFileName(resolved));
    }

    [Fact]
    public void CustomFolder_ExpandsEnvironmentVariables()
    {
        string resolved = AppPaths.ResolveDataFolder(@"%TEMP%\echo-profile");
        Assert.Equal(Path.Combine(Path.GetFullPath(Environment.GetEnvironmentVariable("TEMP")!), "echo-profile"), resolved);
    }
}
