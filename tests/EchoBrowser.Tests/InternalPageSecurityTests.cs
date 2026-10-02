using System.Text.Json;
using EchoBrowser.Services;
using Xunit;

namespace EchoBrowser.Tests;

/// <summary>Die Host-Bridge darf nur von internen Seiten (mit Token) und dem Web Store benutzt werden.</summary>
public class InternalPageSecurityTests
{
    private static JsonElement Message(string? token)
    {
        var message = new Dictionary<string, string> { ["type"] = "updateSetting" };
        if (token != null)
        {
            message[InternalPageSecurity.TokenPropertyName] = token;
        }
        return JsonDocument.Parse(JsonSerializer.Serialize(message)).RootElement;
    }

    [Theory]
    [InlineData("about:blank")]
    [InlineData("data:text/html;charset=utf-8;base64,PGh0bWw+")]
    [InlineData(null)]
    public void InternalPage_WithValidToken_IsTrusted(string? source)
    {
        Assert.True(InternalPageSecurity.IsTrustedInternalMessage(source, Message(InternalPageSecurity.Token)));
    }

    [Fact]
    public void InternalPage_WithoutToken_IsRejected()
    {
        Assert.False(InternalPageSecurity.IsTrustedInternalMessage("about:blank", Message(null)));
    }

    [Fact]
    public void InternalPage_WithWrongToken_IsRejected()
    {
        Assert.False(InternalPageSecurity.IsTrustedInternalMessage("about:blank", Message("falsches-token")));
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("http://localhost:8080/")]
    [InlineData("file:///C:/temp/page.html")]
    [InlineData("chrome-extension://abcdefghijklmnop/popup.html")]
    [InlineData("blob:https://example.com/123")]
    public void WebPage_IsRejected_EvenWithCorrectToken(string source)
    {
        // Selbst wenn eine Webseite das Token kennen würde, kommt sie nicht durch
        Assert.False(InternalPageSecurity.IsTrustedInternalMessage(source, Message(InternalPageSecurity.Token)));
    }

    [Fact]
    public void Token_IsLongAndRandomLooking()
    {
        Assert.Equal(64, InternalPageSecurity.Token.Length); // 32 Bytes als Hex
        Assert.Matches("^[0-9A-F]+$", InternalPageSecurity.Token);
    }

    [Fact]
    public void InjectToken_ReplacesPlaceholder()
    {
        string html = InternalPageSecurity.InjectToken($"token='{InternalPageSecurity.TokenPlaceholder}'");
        Assert.Equal($"token='{InternalPageSecurity.Token}'", html);
    }

    [Theory]
    [InlineData("https://chromewebstore.google.com/detail/ublock-origin/cjpalhdlnbpafiamejdnhcphjbkeiagm")]
    [InlineData("https://chrome.google.com/webstore/detail/abc")]
    public void WebStore_MayRequestInstall(string source)
    {
        Assert.True(InternalPageSecurity.IsAllowedFromWebStore(source, "installExtensionFromWebStore"));
    }

    [Theory]
    [InlineData("https://chromewebstore.google.com/", "updateSetting")]
    [InlineData("https://chromewebstore.google.com/", "clearBrowsingData")]
    [InlineData("http://chromewebstore.google.com/", "installExtensionFromWebStore")]
    [InlineData("https://chromewebstore.google.com.evil.com/", "installExtensionFromWebStore")]
    [InlineData("https://chrome.google.com/search", "installExtensionFromWebStore")]
    [InlineData("https://evil.example.com/", "installExtensionFromWebStore")]
    [InlineData(null, "installExtensionFromWebStore")]
    public void WebStore_OtherMessagesOrHosts_AreRejected(string? source, string type)
    {
        Assert.False(InternalPageSecurity.IsAllowedFromWebStore(source, type));
    }
}
