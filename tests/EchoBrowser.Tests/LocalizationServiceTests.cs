using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Tests, die die aktuelle Sprache des (globalen) LocalizationService umstellen.
/// Gleiche Collection = werden nacheinander statt parallel ausgeführt.
/// </summary>
[CollectionDefinition("Localization", DisableParallelization = true)]
public class LocalizationCollection { }

[Collection("Localization")]
public class LocalizationServiceTests : IDisposable
{
    private readonly string _previousLanguage = LocalizationService.Instance.CurrentLanguage;

    public void Dispose() => LocalizationService.Instance.SetLanguage(_previousLanguage);

    [Fact]
    public void EmbeddedLanguages_AreLoaded()
    {
        Assert.Contains("de", LocalizationService.Instance.AvailableLanguages);
        Assert.Contains("en", LocalizationService.Instance.AvailableLanguages);
    }

    [Fact]
    public void GetString_ReturnsTextOfCurrentLanguage()
    {
        LocalizationService.Instance.SetLanguage("de");
        Assert.Equal("Abbrechen", Tr.Get("Dialog_Cancel"));

        LocalizationService.Instance.SetLanguage("en");
        Assert.Equal("Cancel", Tr.Get("Dialog_Cancel"));
    }

    [Fact]
    public void UnknownLanguage_FallsBackToGerman()
    {
        LocalizationService.Instance.SetLanguage("xx");
        Assert.Equal("de", LocalizationService.Instance.CurrentLanguage);
    }

    [Fact]
    public void UnknownKey_ReturnsKeyItself()
    {
        Assert.Equal("Gibt_Es_Nicht", Tr.Get("Gibt_Es_Nicht"));
    }

    [Fact]
    public void Format_InsertsArguments()
    {
        LocalizationService.Instance.SetLanguage("en");
        Assert.Equal("Add 'uBlock Origin'?", Tr.Format("Ext_InstallHeadline", "uBlock Origin"));
    }

    [Fact]
    public void LanguageChange_RaisesEvents()
    {
        LocalizationService.Instance.SetLanguage("de");
        bool raised = false;
        var changedProperties = new List<string?>();
        void OnChanged() => raised = true;
        void OnProperty(object? s, System.ComponentModel.PropertyChangedEventArgs e) => changedProperties.Add(e.PropertyName);

        LocalizationService.Instance.LanguageChanged += OnChanged;
        LocalizationService.Instance.PropertyChanged += OnProperty;
        try
        {
            LocalizationService.Instance.SetLanguage("en");
        }
        finally
        {
            LocalizationService.Instance.LanguageChanged -= OnChanged;
            LocalizationService.Instance.PropertyChanged -= OnProperty;
        }

        Assert.True(raised);
        Assert.Contains("Item[]", changedProperties); // aktualisiert alle {loc:Loc}-Bindings
    }

    [Fact]
    public void PageLocalizer_EncodesTextForHtmlAndJavaScript()
    {
        LocalizationService.Instance.SetLanguage("en");

        // "Privacy & security" enthält ein & – muss im HTML als &amp; landen
        Assert.Equal("<span>Privacy &amp; security</span>", PageLocalizer.Apply("<span>{{t:Settings_NavPrivacy}}</span>"));

        // Als JS-Literal inkl. Anführungszeichen; < > & werden von System.Text.Json maskiert
        string js = PageLocalizer.Apply("var s = {{js:Settings_NavPrivacy}};");
        Assert.Equal("var s = \"Privacy \\u0026 security\";", js);

        Assert.Equal("<html lang=\"en\">", PageLocalizer.Apply("<html lang=\"{{lang}}\">"));
    }

    [Fact]
    public void PageLocalizer_LeavesUnrelatedBracesAlone()
    {
        const string script = "if (a) { b = `${c}`; } {{ not a token }}";
        Assert.Equal(script, PageLocalizer.Apply(script));
    }
}
