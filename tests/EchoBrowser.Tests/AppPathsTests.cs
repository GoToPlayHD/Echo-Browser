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
        string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoBrowser");
        Assert.Equal(expected, AppPaths.ResolveDataFolder(custom));
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
