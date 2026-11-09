using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Bedeutung einer Eingabe in Adressleiste, Startseite und "In neuem Tab öffnen" (Alt+Enter).</summary>
public class AddressInputTests
{
    private static AddressTarget Resolve(string input) => AddressInput.Resolve(input, q => "https://search.example/?q=" + q);

    [Theory]
    [InlineData("")]
    [InlineData("about:blank")]
    [InlineData("echo://start")]
    public void StartPage(string input)
    {
        Assert.Equal(new AddressTarget(AddressTargetKind.StartPage, StartPageService.StartPageUrl), Resolve(input));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("  Settings ")]
    [InlineData("about:preferences")]
    [InlineData("chrome://settings")]
    [InlineData("edge://settings")]
    public void SettingsAliases(string input)
    {
        Assert.Equal(new AddressTarget(AddressTargetKind.Settings, SettingsPageService.SettingsPageUrl), Resolve(input));
    }

    [Fact]
    public void Address_IsOpened()
    {
        var target = Resolve("example.com");
        Assert.Equal(AddressTargetKind.Url, target.Kind);
        Assert.StartsWith("https://example.com", target.Url);
    }

    [Fact]
    public void Words_AreSearched()
    {
        Assert.Equal(new AddressTarget(AddressTargetKind.Search, "https://search.example/?q=echo browser"), Resolve("echo browser"));
    }
}
