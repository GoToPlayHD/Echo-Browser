using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Adressen der internen Seiten unter echo://.</summary>
public class InternalPagesTests
{
    [Theory]
    [InlineData("echo://start/", "echo://start")]
    [InlineData("echo://start", "echo://start")]
    [InlineData("ECHO://Settings/", "echo://settings")]
    [InlineData("echo://crashed/?url=x", "echo://crashed/?url=x")]
    [InlineData("https://example.com/", "https://example.com/")]
    public void Normalize_DropsTrailingSlashOnlyForPlainInternalPages(string input, string expected)
    {
        Assert.Equal(expected, InternalPages.Normalize(input));
    }

    [Theory]
    [InlineData("echo://start", "start")]
    [InlineData("echo://start/", "start")]
    [InlineData("echo://Settings/", "settings")]
    [InlineData("echo://crashed/?url=https%3A%2F%2Fexample.com", "crashed")]
    [InlineData("https://start/", null)]
    [InlineData("about:blank", null)]
    [InlineData(null, null)]
    public void GetPageName_ReturnsHostOfInternalUrls(string? url, string? expected)
    {
        Assert.Equal(expected, InternalPages.GetPageName(url));
    }

    [Fact]
    public void Is_ComparesPageName()
    {
        Assert.True(InternalPages.Is("echo://settings/", InternalPages.Settings));
        Assert.False(InternalPages.Is("echo://start/", InternalPages.Settings));
        Assert.False(InternalPages.Is("https://settings.example.com/", InternalPages.Settings));
    }

    [Fact]
    public void CrashedPage_KeepsOriginalUrlEscaped()
    {
        string crashed = InternalPages.CrashedPageFor("https://example.com/a?b=c&d=e");

        Assert.Equal(InternalPages.Crashed, InternalPages.GetPageName(crashed));
        Assert.Contains("url=https%3A%2F%2Fexample.com%2Fa%3Fb%3Dc%26d%3De", crashed);
    }

    [Fact]
    public void StartAndSettingsUrls_AreInternal()
    {
        Assert.True(InternalPages.IsInternalUrl(StartPageService.StartPageUrl));
        Assert.True(InternalPages.IsInternalUrl(SettingsPageService.SettingsPageUrl));
        Assert.Equal(StartPageService.StartPageUrl, InternalPages.Normalize(StartPageService.StartPageUrl + "/"));
    }
}
