using System.Text.Json;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Werte von der Einstellungs- und Startseite: nur erlaubte Werte werden übernommen,
/// die Suchmaschine landet als sicheres JSON-Literal in der Startseite.
/// </summary>
public class SettingValidationTests
{
    [Theory]
    [InlineData("google", "google")]
    [InlineData("Google", "google")]
    [InlineData("  brave ", "brave")]
    [InlineData("STARTPAGE", "startpage")]
    public void Match_AcceptsKnownValues_Normalized(string input, string expected)
    {
        Assert.Equal(expected, SettingValues.Match(SettingValues.SearchEngines, input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("yahoo")]
    [InlineData("duckduckgo\"")]
    [InlineData("\";alert(1)//")]
    [InlineData("</script><script>alert(1)</script>")]
    public void Match_RejectsUnknownValues(string? input)
    {
        Assert.Null(SettingValues.Match(SettingValues.SearchEngines, input));
        Assert.Equal("duckduckgo", SettingValues.NormalizeSearchEngine(input));
    }

    [Fact]
    public void SetSettingFromPage_RejectsUpdateUrl()
    {
        Assert.False(MainWindow.SetSettingFromPage("UpdateUrl", JsonSerializer.SerializeToElement("http://evil.example/")));
    }

    [Theory]
    [InlineData("SearchEngine", "\";alert(1)//")]
    [InlineData("SearchEngine", "yahoo")]
    [InlineData("StartupBehavior", "launch_rockets")]
    [InlineData("TrackingPreventionLevel", "</script>")]
    [InlineData("Language", "xx")]
    [InlineData("Language", "de\"")]
    public void SetSettingFromPage_RejectsInvalidValues_AndKeepsSetting(string key, string value)
    {
        var settings = AppSettingsService.Instance.Settings;
        var before = (settings.SearchEngine, settings.StartupBehavior, settings.TrackingPreventionLevel, settings.Language);

        Assert.False(MainWindow.SetSettingFromPage(key, JsonSerializer.SerializeToElement(value)));

        Assert.Equal(before, (settings.SearchEngine, settings.StartupBehavior, settings.TrackingPreventionLevel, settings.Language));
    }

    [Fact]
    public void SetSettingFromPage_AcceptsKnownSearchEngine_Normalized()
    {
        // Nur DuckDuckGo (Standard): andere Tests laufen parallel und erwarten dessen Such-URLs
        var settings = AppSettingsService.Instance.Settings;
        string original = settings.SearchEngine;
        try
        {
            Assert.True(MainWindow.SetSettingFromPage("SearchEngine", JsonSerializer.SerializeToElement(" DuckDuckGo ")));
            Assert.Equal("duckduckgo", settings.SearchEngine);
        }
        finally
        {
            settings.SearchEngine = original;
        }
    }

    [Fact]
    public void StartPage_EmbedsSearchEngineAsJsonLiteral_EvenWithTamperedSettings()
    {
        // Eine von Hand geänderte settings.json darf kein Skript in die Startseite (mit Bridge-Token) bringen
        var settings = AppSettingsService.Instance.Settings;
        string original = settings.SearchEngine;
        try
        {
            settings.SearchEngine = "\";alert(1);//";
            string html = StartPageService.GetStartPageHtml();

            Assert.Contains("engineSelect.value = \"duckduckgo\";", html);
            Assert.DoesNotContain("alert(1)", html);
            Assert.DoesNotContain("##CURRENT_ENGINE##", html);
        }
        finally
        {
            settings.SearchEngine = original;
        }
    }
}
