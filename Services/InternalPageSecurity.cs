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

        /// <summary>Zufälliges Token, nur in den unter echo:// ausgelieferten internen Seiten enthalten.</summary>
        public static string Token { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

        /// <summary>Nachrichten, die von Chrome-Web-Store-Seiten gesendet werden dürfen.</summary>
        private static readonly string[] WebStoreMessageTypes = { "installExtensionFromWebStore" };

        /// <summary>
        /// Prüft, ob eine Nachricht vom internen Echo-Dokument stammt: Quelle muss eine
        /// echo://-Seite sein und das Token muss passen.
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
            // Interne Seiten werden ausschließlich unter echo:// ausgeliefert (MainWindow.InternalPages.cs).
            // Alles andere – Webseiten, Dateien, Erweiterungen, about:blank, data: – ist nie intern.
            // Das eigentliche Vertrauen kommt zusätzlich erst durch das Token zustande.
            return InternalPages.IsInternalUrl(source);
        }

        /// <summary>Setzt das Token in das HTML-Template einer internen Seite ein.</summary>
        public static string InjectToken(string html) => html.Replace(TokenPlaceholder, Token);
    }
}
