using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Adressleiste: Ist die Eingabe eine Adresse oder ein Suchbegriff?</summary>
public class UrlDetectionTests
{
    [Theory]
    [InlineData("github.com", "https://github.com")]
    [InlineData("  GitHub.com/GoToPlayHD  ", "https://GitHub.com/GoToPlayHD")]
    [InlineData("example.com:8080/x?y=1", "https://example.com:8080/x?y=1")]
    [InlineData("sub.domain.co.uk", "https://sub.domain.co.uk")]
    [InlineData("münchen.de", "https://münchen.de")]
    [InlineData("example.com#top", "https://example.com#top")]
    [InlineData("localhost", "http://localhost")]
    [InlineData("localhost:3000/api", "http://localhost:3000/api")]
    [InlineData("192.168.0.1", "http://192.168.0.1")]
    [InlineData("127.0.0.1:8080/test", "http://127.0.0.1:8080/test")]
    [InlineData("https://example.com/a b", "https://example.com/a b")]
    [InlineData("http://example.com", "http://example.com")]
    [InlineData("file:///C:/seite.html", "file:///C:/seite.html")]
    [InlineData("echo://settings", "echo://settings")]
    [InlineData("about:blank", "about:blank")]
    [InlineData("view-source:https://example.com", "view-source:https://example.com")]
    public void Addresses_AreOpened(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.ToNavigableUrl(input));
    }

    [Theory]
    [InlineData("wetter berlin")]
    [InlineData("3.14")]
    [InlineData("c#")]
    [InlineData("example")]
    [InlineData("example.c")]
    [InlineData("user@example.com")]
    [InlineData("was ist github.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchTerms_AreNotAddresses(string? input)
    {
        Assert.Null(UrlHelper.ToNavigableUrl(input));
    }

    [Theory]
    [InlineData("example", "https://www.example.com")]
    [InlineData(" heise ", "https://www.heise.com")]
    [InlineData("example.org", "example.org")]
    [InlineData("zwei worte", "zwei worte")]
    public void CtrlEnter_AddsWwwAndCom(string input, string expected)
    {
        Assert.Equal(expected, UrlHelper.CompleteWithWwwAndCom(input));
    }

    [Theory]
    [InlineData("https://www.github.com/", "github.com")]
    [InlineData("http://github.com", "github.com")]
    [InlineData("HTTPS://GitHub.com:443/Path/", "github.com/Path")]
    [InlineData("https://example.com:8080/a?b=1", "example.com:8080/a?b=1")]
    [InlineData("echo://settings", "echo://settings")]
    public void NormalizeForComparison_IgnoresSchemeWwwAndSlash(string url, string expected)
    {
        Assert.Equal(expected, UrlHelper.NormalizeForComparison(url));
    }

    [Theory]
    [InlineData("https://www.github.com/x", "github.com")]
    [InlineData("https://docs.github.com/", "docs.github.com")]
    [InlineData("kein link", null)]
    public void ShortHost_DropsWww(string url, string? expected)
    {
        Assert.Equal(expected, UrlHelper.ShortHost(url));
    }
}
