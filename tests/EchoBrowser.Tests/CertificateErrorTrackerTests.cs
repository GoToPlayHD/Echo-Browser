using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>
/// Zertifikatsfehler (Audit S5): Nach »Trotzdem fortfahren« darf eine https-Seite nicht als »Sicher« erscheinen.
/// </summary>
public class CertificateErrorTrackerTests
{
    [Fact]
    public void RecordedHost_IsFoundForHttpsPagesOfThatHost()
    {
        var tracker = new CertificateErrorTracker();
        Assert.True(tracker.Record("https://expired.example/login?next=1", CoreWebView2WebErrorStatus.CertificateExpired));

        Assert.Equal(CoreWebView2WebErrorStatus.CertificateExpired, tracker.Find("https://expired.example/"));
        Assert.Equal(CoreWebView2WebErrorStatus.CertificateExpired, tracker.Find("https://EXPIRED.example:8443/other/page"));
        Assert.Null(tracker.Find("https://other.example/"));
        Assert.Null(tracker.Find("https://sub.expired.example/"));
        Assert.Null(tracker.Find("http://expired.example/"));   // unverschlüsselt: eigene Anzeige
        Assert.Null(tracker.Find(null));
    }

    [Fact]
    public void Record_ReportsOnlyChanges()
    {
        var tracker = new CertificateErrorTracker();
        Assert.True(tracker.Record("https://a.example/", CoreWebView2WebErrorStatus.CertificateIsInvalid));
        Assert.False(tracker.Record("https://a.example/x", CoreWebView2WebErrorStatus.CertificateIsInvalid));
        Assert.True(tracker.Record("https://a.example/", CoreWebView2WebErrorStatus.CertificateRevoked));
        Assert.False(tracker.Record("kein uri", CoreWebView2WebErrorStatus.CertificateIsInvalid));
        Assert.False(tracker.Record(null, CoreWebView2WebErrorStatus.CertificateIsInvalid));
    }

    [Fact]
    public void Clear_ForgetsAllHosts()
    {
        var tracker = new CertificateErrorTracker();
        tracker.Record("https://a.example/", CoreWebView2WebErrorStatus.CertificateIsInvalid);
        tracker.Clear();
        Assert.Null(tracker.Find("https://a.example/"));
    }

    [Fact]
    public void Profiles_AreSeparate()
    {
        const string url = "https://incognito-only.cert-test.example/";
        try
        {
            CertificateErrorTracker.ForProfile(incognito: true).Record(url, CoreWebView2WebErrorStatus.CertificateIsInvalid);

            Assert.NotNull(CertificateErrorTracker.ForProfile(incognito: true).Find(url));
            Assert.Null(CertificateErrorTracker.ForProfile(incognito: false).Find(url));
        }
        finally
        {
            CertificateErrorTracker.ForProfile(incognito: true).Clear();
        }
    }

    [Theory]
    [InlineData(CoreWebView2WebErrorStatus.CertificateExpired, "Security_CertExpired")]
    [InlineData(CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect, "Security_CertNameMismatch")]
    [InlineData(CoreWebView2WebErrorStatus.CertificateRevoked, "Security_CertRevoked")]
    [InlineData(CoreWebView2WebErrorStatus.CertificateIsInvalid, "Security_CertInvalid")]
    [InlineData(CoreWebView2WebErrorStatus.ClientCertificateContainsErrors, "Security_CertInvalid")]
    public void ReasonKey_HasATranslation(CoreWebView2WebErrorStatus status, string expectedKey)
    {
        string key = CertificateErrorTracker.ReasonKey(status);
        Assert.Equal(expectedKey, key);
        Assert.NotEqual(key, LocalizationService.Instance.GetString(key));   // fehlender Text gäbe den Schlüssel zurück
    }

    [Fact]
    public void Tab_WithCertificateError_IsNotSecure()
    {
        var tracker = new CertificateErrorTracker();
        tracker.Record("https://expired.example/", CoreWebView2WebErrorStatus.CertificateExpired);

        var tab = new BrowserTab { CertificateErrors = tracker, Url = "https://expired.example/page" };

        Assert.False(tab.IsSecure);
        Assert.Equal(CoreWebView2WebErrorStatus.CertificateExpired, tab.CertificateError);
        Assert.Contains(Tr.Get("Security_CertExpired"), tab.SecurityStatus);
    }

    [Fact]
    public void Tab_BecomesInsecure_WhenErrorIsReportedAfterTheAddress()
    {
        // NavigationStarting setzt die Adresse, ServerCertificateErrorDetected kommt danach
        var tracker = new CertificateErrorTracker();
        var tab = new BrowserTab { CertificateErrors = tracker, Url = "https://late.example/" };
        Assert.True(tab.IsSecure);
        Assert.Null(tab.CertificateError);

        tracker.Record("https://late.example/", CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect);
        tab.UpdateSecurityStatus();

        Assert.False(tab.IsSecure);
        Assert.Equal(CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect, tab.CertificateError);
    }

    [Fact]
    public void Tab_LeavingTheHost_ClearsTheError()
    {
        var tracker = new CertificateErrorTracker();
        tracker.Record("https://expired.example/", CoreWebView2WebErrorStatus.CertificateExpired);
        var tab = new BrowserTab { CertificateErrors = tracker, Url = "https://expired.example/" };

        tab.Url = "https://valid.example/";

        Assert.True(tab.IsSecure);
        Assert.Null(tab.CertificateError);
        Assert.Equal(Tr.Get("Security_Secure"), tab.SecurityStatus);
    }
}
