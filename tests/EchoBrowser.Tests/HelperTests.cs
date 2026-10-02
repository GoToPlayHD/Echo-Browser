using System.Windows.Media;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

public class UrlHelperTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("https://", true)]
    [InlineData("HTTP://", true)]
    [InlineData("github.com", false)]
    [InlineData("https://github.com", false)]
    public void IsEmptyInput(string? input, bool expected)
    {
        Assert.Equal(expected, UrlHelper.IsEmptyInput(input));
    }

    [Theory]
    [InlineData("github.com", "https://github.com")]
    [InlineData("  github.com/path  ", "https://github.com/path")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("HTTPS://example.com", "HTTPS://example.com")]
    public void EnsureScheme(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.EnsureScheme(input));
    }

    [Theory]
    [InlineData("https://github.com/GoToPlayHD/Echo-Browser", "github.com")]
    [InlineData("www.wikipedia.org", "www.wikipedia.org")]
    [InlineData("http://localhost:8080/test", "localhost")]
    public void HostForDisplay(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.HostForDisplay(input));
    }
}

public class ThemeManagerTests
{
    [Theory]
    [InlineData("#38BDF8", 0xFF, 0x38, 0xBD, 0xF8)]
    [InlineData("38bdf8", 0xFF, 0x38, 0xBD, 0xF8)]
    [InlineData("#8038BDF8", 0x80, 0x38, 0xBD, 0xF8)]
    public void ColorFromHex_ParsesRgbAndArgb(string hex, byte a, byte r, byte g, byte b)
    {
        Assert.Equal(Color.FromArgb(a, r, g, b), ThemeManager.ColorFromHex(hex));
    }

    [Fact]
    public void ColorFromHex_WrongLength_FallsBackToGray()
    {
        Assert.Equal(Colors.Gray, ThemeManager.ColorFromHex("#123"));
    }

    [Fact]
    public void ColorFromHex_InvalidCharacters_Throws()
    {
        Assert.Throws<FormatException>(() => ThemeManager.ColorFromHex("#GGGGGG"));
    }
}
