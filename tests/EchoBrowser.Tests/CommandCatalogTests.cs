using System.IO;
using System.Text.Json;
using System.Windows.Input;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Befehlskatalog (Tastenkürzel + Befehlspalette) und unscharfe Suche.</summary>
public class CommandCatalogTests
{
    [Fact]
    public void Ids_AreUnique()
    {
        var duplicates = CommandCatalog.All.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Gestures_AreUnique()
    {
        var duplicates = CommandCatalog.All
            .Where(c => c.HasGesture)
            .GroupBy(c => (c.Key, c.Modifiers))
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(", ", g.Select(c => c.Id)));
        Assert.Empty(duplicates);
    }

    [Fact]
    public void AllTitles_AreTranslated()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EchoBrowser.csproj"))) dir = dir.Parent;
        var german = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(dir!.FullName, "Localization", "de.json")))!;

        var missing = CommandCatalog.All.Select(c => c.TitleKey)
            .Concat(CommandCatalog.SettingsSections.Select(s => s.TitleKey))
            .Where(key => !german.ContainsKey(key));
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryCommand_HasAnAction_InMainWindow()
    {
        // Die Zuordnung Id -> Aktion steht in MainWindow.Commands.cs; jede Id muss dort vorkommen
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EchoBrowser.csproj"))) dir = dir.Parent;
        string source = File.ReadAllText(Path.Combine(dir!.FullName, "MainWindow.Commands.cs"));

        var missing = CommandCatalog.All.Where(c => !source.Contains($"[\"{c.Id}\"]")).Select(c => c.Id);
        Assert.Empty(missing);
    }

    [Theory]
    [InlineData(Key.T, ModifierKeys.Control | ModifierKeys.Shift, "Ctrl+Shift+T")]
    [InlineData(Key.OemPlus, ModifierKeys.Control, "Ctrl++")]
    [InlineData(Key.D0, ModifierKeys.Control, "Ctrl+0")]
    [InlineData(Key.F11, ModifierKeys.None, "F11")]
    [InlineData(Key.Left, ModifierKeys.Alt, "Alt+←")]
    public void GestureText_IsReadable(Key key, ModifierKeys modifiers, string expected)
    {
        Assert.Equal(expected, CommandCatalog.FormatGesture(key, modifiers));
    }

    [Theory]
    [InlineData("nt", "Neuer Tab", true)]
    [InlineData("neu tab", "Neuer Tab", true)]
    [InlineData("inkog", "Neues Inkognito-Fenster", true)]
    [InlineData("xyz", "Neuer Tab", false)]
    [InlineData("bat", "Tab", false)]          // Reihenfolge zählt
    public void Fuzzy_MatchesCharactersInOrder(string query, string text, bool matches)
    {
        Assert.Equal(matches, FuzzyMatcher.Score(query, text) > 0);
    }

    [Fact]
    public void Fuzzy_PrefersWordStartsAndPrefixes()
    {
        Assert.True(FuzzyMatcher.Score("tab", "Tab schließen") > FuzzyMatcher.Score("tab", "Lesezeichen-Datenbank"));
        Assert.True(FuzzyMatcher.Score("zoom", "Zoom zurücksetzen") > FuzzyMatcher.Score("zoom", "Vergrößern (Zoom)"));
    }

    [Theory]
    [InlineData("tab", "Tab schließen", true)]
    [InlineData("nt", "Neuer Tab", true)]               // Wortanfänge
    [InlineData("vollb", "Vollbild", true)]
    [InlineData("tab", "Einstellungen › Tabs & Verhalten", true)]
    [InlineData("tab", "Lesezeichenleiste ein/aus", false)] // verstreut
    [InlineData("xyz", "Neuer Tab", false)]
    public void StrongMatches_AreWordStartsOrConsecutive(string query, string text, bool strong)
    {
        Assert.Equal(strong, FuzzyMatcher.IsStrong(query, FuzzyMatcher.Score(query, text)));
    }
}
