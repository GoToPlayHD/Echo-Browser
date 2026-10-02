using System;
using System.Security.Cryptography;
using System.Text.Json;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Schützt die Host-Bridge (WebMessageReceived) vor fremden Webseiten.
    /// Jede Webseite kann window.chrome.webview.postMessage aufrufen – deshalb
    /// werden privilegierte Nachrichten nur von den internen Seiten (Startseite,
    /// Einstellungen) akzeptiert, die ein pro Sitzung zufällig erzeugtes Token mitsenden.
    /// </summary>
    public static class InternalPageSecurity
    {
        public const string TokenPlaceholder = "##ECHO_BRIDGE_TOKEN##";
        public const string TokenPropertyName = "__echoToken";

        /// <summary>Zufälliges Token, nur in den per NavigateToString erzeugten internen Seiten enthalten.</summary>
        public static string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        /// <summary>Nachrichten, die von Chrome-Web-Store-Seiten gesendet werden dürfen.</summary>
        private static readonly string[] WebStoreMessageTypes = { "installExtensionFromWebStore" };

        /// <summary>
        /// Prüft, ob eine Nachricht vom internen Echo-Dokument stammt: Quelle muss die
        /// lokal erzeugte Seite sein (data:/about:blank) und das Token muss passen.
        /// </summary>
        public static bool IsTrustedInternalMessage(string? source, JsonElement root)
        {
            if (!IsInternalPageSource(source)) return false;
            if (!root.TryGetProperty(TokenPropertyName, out var tokenEl) || tokenEl.ValueKind != JsonValueKind.String) return false;

            string? token = tokenEl.GetString();
            if (token == null) return false;

            return CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(token),
                System.Text.Encoding.UTF8.GetBytes(Token));
        }

        /// <summary>Darf die Nachricht dieses Typs von der angegebenen (externen) Quelle kommen?</summary>
        public static bool IsAllowedFromWebStore(string? source, string type)
        {
            if (Array.IndexOf(WebStoreMessageTypes, type) < 0) return false;
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)) return false;
            if (!uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;

            if (uri.Host.Equals("chromewebstore.google.com", StringComparison.OrdinalIgnoreCase))
                return true;

            // Alter Store unter chrome.google.com/webstore
            return uri.Host.Equals("chrome.google.com", StringComparison.OrdinalIgnoreCase) &&
                   uri.AbsolutePath.StartsWith("/webstore", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInternalPageSource(string? source)
        {
            // NavigateToString-Seiten melden je nach Runtime "about:blank" oder eine data:-URL.
            // Echte Webseiten, lokale Dateien und Erweiterungsseiten sind nie intern –
            // das eigentliche Vertrauen kommt aber erst durch das Token zustande.
            if (string.IsNullOrEmpty(source)) return true;
            return !(source.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                     source.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
                     source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ||
                     source.StartsWith("chrome-extension:", StringComparison.OrdinalIgnoreCase) ||
                     source.StartsWith("blob:", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Setzt das Token in das HTML-Template einer internen Seite ein.</summary>
        public static string InjectToken(string html) => html.Replace(TokenPlaceholder, Token);
    }
}
