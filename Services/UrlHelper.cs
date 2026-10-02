using System;

namespace EchoBrowser.Services
{
    /// <summary>Kleine Helfer für vom Nutzer eingegebene Adressen (Lesezeichen, Favoriten).</summary>
    public static class UrlHelper
    {
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
