using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Prüft die Sprachdateien in Localization/ auf Vollständigkeit und passende Platzhalter.</summary>
public class LocalizationFileTests
{
    private const string ReferenceLanguage = "de";

    private static string LocalizationDirectory()
    {
        // Vom Testausgabeordner nach oben bis zum Projektordner mit EchoBrowser.csproj
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "EchoBrowser.csproj")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Localization");
    }

    private static Dictionary<string, string> Load(string lang)
    {
        string path = Path.Combine(LocalizationDirectory(), $"{lang}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    public static IEnumerable<object[]> OtherLanguages() =>
        Directory.GetFiles(LocalizationDirectory(), "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(lang => lang != ReferenceLanguage)
            .Select(lang => new object[] { lang! });

    private static string[] Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+(:[^}]*)?\}").Select(m => m.Value).OrderBy(p => p).ToArray();

    [Fact]
    public void AtLeastGermanAndEnglishExist()
    {
        Assert.True(File.Exists(Path.Combine(LocalizationDirectory(), "de.json")));
        Assert.True(File.Exists(Path.Combine(LocalizationDirectory(), "en.json")));
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Language_HasExactlyTheSameKeysAsGerman(string lang)
    {
        var reference = Load(ReferenceLanguage).Keys.ToHashSet();
        var keys = Load(lang).Keys.ToHashSet();

        Assert.Empty(reference.Except(keys));   // fehlende Übersetzungen
        Assert.Empty(keys.Except(reference));   // Übersetzungen ohne deutschen Text
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Language_KeepsAllPlaceholders(string lang)
    {
        var reference = Load(ReferenceLanguage);
        var translations = Load(lang);

        var wrong = translations
            .Where(t => reference.ContainsKey(t.Key) && !Placeholders(t.Value).SequenceEqual(Placeholders(reference[t.Key])))
            .Select(t => t.Key)
            .ToList();

        Assert.Empty(wrong);
    }

    [Theory]
    [MemberData(nameof(OtherLanguages))]
    public void Language_HasNoEmptyTexts(string lang)
    {
        Assert.Empty(Load(lang).Where(t => string.IsNullOrWhiteSpace(t.Value)).Select(t => t.Key));
    }
}
