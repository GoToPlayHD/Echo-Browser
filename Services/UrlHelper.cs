using System;
using System.Text.RegularExpressions;

namespace EchoBrowser.Services
{
    /// <summary>Kleine Helfer für vom Nutzer eingegebene Adressen (Adressleiste, Lesezeichen, Favoriten).</summary>
    public static class UrlHelper
    {
        /// <summary>Eingaben mit diesen Schemata werden unverändert geöffnet.</summary>
        private static readonly string[] DirectSchemes =
            { "http:", "https:", "file:", "about:", "view-source:", "chrome-extension:", "edge:", "chrome:", InternalPages.Scheme + ":" };

        // Hostname mit Top-Level-Domain aus Buchstaben (auch Umlaute/IDN), optional Port und Pfad/Abfrage
        private static readonly Regex HostWithTld = new(
            @"^[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?(?:\.[\p{L}\p{N}](?:[\p{L}\p{N}\-]*[\p{L}\p{N}])?)*\.(?:\p{L}{2,}|xn--[a-z0-9\-]+)(?::\d{1,5})?(?:[/?#].*)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex LocalHost = new(@"^localhost(?::\d{1,5})?(?:[/?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex Ipv4 = new(@"^(?:\d{1,3}\.){3}\d{1,3}(?::\d{1,5})?(?:[/?#].*)?$", RegexOptions.Compiled);

        /// <summary>
        /// Adressleisten-Eingabe als Adresse deuten. Gibt die zu öffnende URL zurück oder null,
        /// wenn die Eingabe ein Suchbegriff ist (z.B. "wetter berlin", "3.14", "c#").
        /// </summary>
        public static string? ToNavigableUrl(string? input)
        {
            string text = input?.Trim() ?? "";
            if (text.Length == 0) return null;

            foreach (string scheme in DirectSchemes)
            {
                if (text.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return text;
            }

            if (text.Contains(' ')) return null;
            if (LocalHost.IsMatch(text) || Ipv4.IsMatch(text)) return "http://" + text;
            if (HostWithTld.IsMatch(text)) return "https://" + text;
            return null;
        }

        /// <summary>Strg+Enter in der Adressleiste: "example" wird zu "https://www.example.com".</summary>
        public static string CompleteWithWwwAndCom(string input)
        {
            string text = input.Trim();
            if (text.Length == 0 || text.Contains(' ') || text.Contains('.') || text.Contains(':')) return text;
            return $"https://www.{text}.com";
        }

        /// <summary>
        /// Vergleichsschlüssel für Adressen (Duplikate in Vorschlägen erkennen): ohne http/https, ohne "www.",
        /// ohne Standard-Port und ohne abschließenden Schrägstrich. Pfad und Abfrage bleiben, wie sie sind.
        /// </summary>
        public static string NormalizeForComparison(string url)
        {
            string text = url.Trim();
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
            {
                return text.TrimEnd('/').ToLowerInvariant();
            }

            string host = uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
            string port = uri.IsDefaultPort ? "" : ":" + uri.Port;
            string prefix = uri.Scheme is "http" or "https" ? "" : uri.Scheme + "://";
            return (prefix + host + port + uri.PathAndQuery + uri.Fragment).TrimEnd('/');
        }

        /// <summary>Host ohne "www." für die Anzeige und die Inline-Vervollständigung ("github.com").</summary>
        public static string? ShortHost(string? url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)) return null;
            return uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host;
        }
        /// <summary>True, wenn die Eingabe leer ist oder nur aus einem Schema-Präfix wie "https://" besteht.</summary>
        public static bool IsEmptyInput(string? url)
        {
            string trimmed = url?.Trim() ?? "";
            return trimmed.Length == 0 ||
                   trimmed.Equals("https://", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("http://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ergänzt "https://", wenn kein http(s)-Schema angegeben ist.</summary>
        public static string EnsureScheme(string url)
        {
            url = url.Trim();
            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? url
                : "https://" + url;
        }

        /// <summary>Anzeigename aus der Adresse, wenn kein Titel eingegeben wurde (z.B. "github.com").</summary>
        public static string HostForDisplay(string url)
        {
            if (Uri.TryCreate(EnsureScheme(url), UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                return uri.Host;
            }
            return url.Trim();
        }
    }
}
