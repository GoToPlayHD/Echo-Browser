using System.IO;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Speichern im Hintergrund, Tab-Schlaf mit Downloads und die eingebetteten internen Seiten.</summary>
public class ArchitectureRefactorTests
{
    [Fact]
    public void BackgroundWriter_WritesLatestContentOnFlush()
    {
        string file = Path.Combine(Path.GetTempPath(), "echo-bgwrite-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            BackgroundFileWriter.Enqueue(file, "erste Fassung");
            BackgroundFileWriter.Enqueue(file, "zweite Fassung");
            BackgroundFileWriter.Flush();

            Assert.Equal("zweite Fassung", File.ReadAllText(file));
            Assert.False(File.Exists(file + ".tmp"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void TabWithRunningDownload_IsNeverDiscarded()
    {
        var now = DateTime.Now;
        var idleForDays = new TabSleepState(IsVisible: false, HasWebView: true, IsSleeping: true, IsDiscarded: false,
            IsAudible: false, IsLoading: false, SleepRefused: false, LastActiveAt: now.AddDays(-2));

        Assert.Equal(TabSleepAction.Discard, TabSleepPolicy.Decide(idleForDays, now, 30));
        Assert.NotEqual(TabSleepAction.Discard, TabSleepPolicy.Decide(idleForDays with { HasActiveDownload = true }, now, 30));
    }

    [Theory]
    [InlineData("start.html", "##SHORTCUTS_JSON##")]
    [InlineData("settings.html", "##SETTINGS_JSON##")]
    [InlineData("crashed.html", "##URL_JSON##")]
    public void InternalPages_AreEmbeddedWithTheirPlaceholders(string name, string placeholder)
    {
        string html = PageTemplates.Raw(name);
        Assert.StartsWith("<!DOCTYPE html>", html);
        Assert.Contains(placeholder, html);
        Assert.Contains("##THEME_CSS##", html);
        // Aus dem C#-Verbatim-String übernommen: keine verdoppelten Anführungszeichen in Attributen
        Assert.DoesNotContain("=\"\"", html.Replace("content: \"\";", ""));
    }

    [Fact]
    public void LocalizedTemplate_IsBuiltOncePerLanguage()
    {
        int builds = 0;
        string Build() { builds++; return "x"; }

        PageTemplates.Localized("test-variant", Build);
        PageTemplates.Localized("test-variant", Build);
        Assert.Equal(1, builds);
    }
}
