using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Eigene Fehlerseite für nicht erreichbare Websites und Verlauf bei Titeländerungen.</summary>
public class NavigationPolishTests
{
    [Theory]
    [InlineData(CoreWebView2WebErrorStatus.HostNameNotResolved, NetErrorPageService.ErrorKind.Dns)]
    [InlineData(CoreWebView2WebErrorStatus.Disconnected, NetErrorPageService.ErrorKind.Offline)]
    [InlineData(CoreWebView2WebErrorStatus.Timeout, NetErrorPageService.ErrorKind.Timeout)]
    [InlineData(CoreWebView2WebErrorStatus.ConnectionReset, NetErrorPageService.ErrorKind.Connection)]
    public void NetworkErrors_GetEchoErrorPage(CoreWebView2WebErrorStatus status, NetErrorPageService.ErrorKind expected)
    {
        Assert.Equal(expected, NetErrorPageService.Classify(status));
    }

    [Theory]
    [InlineData(CoreWebView2WebErrorStatus.OperationCanceled)]
    [InlineData(CoreWebView2WebErrorStatus.CertificateIsInvalid)]
    [InlineData(CoreWebView2WebErrorStatus.CertificateExpired)]
    [InlineData(CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired)]
    public void CanceledCertificateAndAuthErrors_KeepChromiumPage(CoreWebView2WebErrorStatus status)
    {
        Assert.Null(NetErrorPageService.Classify(status));
    }

    [Fact]
    public void ErrorCode_LooksLikeChrome()
    {
        Assert.Equal("HOST_NAME_NOT_RESOLVED", NetErrorPageService.ErrorCode(CoreWebView2WebErrorStatus.HostNameNotResolved));
    }

    [Fact]
    public void ErrorPage_EncodesAddress()
    {
        string html = NetErrorPageService.BuildHtml("evil.example", "https://evil.example/<script>alert(1)</script>",
            NetErrorPageService.ErrorKind.Dns, CoreWebView2WebErrorStatus.HostNameNotResolved);

        Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void InjectionScript_OnlyReplacesChromiumErrorPage_AndRetriesOnlyWebAddresses()
    {
        string web = NetErrorPageService.BuildInjectionScript("https://example.invalid/", NetErrorPageService.ErrorKind.Dns,
            CoreWebView2WebErrorStatus.HostNameNotResolved);
        Assert.Contains("location.href.startsWith('chrome-error://')", web);
        Assert.Contains("const target = \"https://example.invalid/\"", web);

        string script = NetErrorPageService.BuildInjectionScript("javascript:alert(1)", NetErrorPageService.ErrorKind.Connection,
            CoreWebView2WebErrorStatus.CannotConnect);
        Assert.Contains("const target = \"\"", script);
    }

    [Fact]
    public void TitleChange_UpdatesLastEntryInsteadOfAddingVisits()
    {
        var history = HistoryService.Instance;
        history.Entries.Clear();

        history.AddEntry("Posteingang", "https://mail.example/inbox");
        // Ältere Einträge (> 15 s) würden ohne titleChangeOnly als neuer Besuch zählen
        history.Entries[0].VisitedAt = DateTime.Now.AddMinutes(-5);
        history.AddEntry("(3) Posteingang", "https://mail.example/inbox", titleChangeOnly: true);

        Assert.Single(history.Entries);
        Assert.Equal("(3) Posteingang", history.Entries[0].Title);

        history.AddEntry("Andere Seite", "https://mail.example/other", titleChangeOnly: true);
        Assert.Equal(2, history.Entries.Count);
        history.Entries.Clear();
    }
}
