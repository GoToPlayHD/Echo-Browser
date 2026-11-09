using System;
using System.Collections.Generic;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Erlaubte Werte für Einstellungen, die als freier Text ankommen (Einstellungsseite, Startseite, settings.json).
    /// Unbekannte Werte werden abgewiesen statt übernommen: Manche landen später in HTML oder JavaScript
    /// einer internen Seite, die das Bridge-Token kennt.
    /// </summary>
    public static class SettingValues
    {
        public const string DefaultSearchEngine = "duckduckgo";

        public static readonly IReadOnlyList<string> SearchEngines =
            new[] { "duckduckgo", "google", "bing", "ecosia", "brave", "startpage" };

        public static readonly IReadOnlyList<string> StartupBehaviors =
            new[] { "startpage", "restore_session", "custom_url" };

        public static readonly IReadOnlyList<string> TrackingPreventionLevels =
            new[] { "none", "balanced", "strict" };

        /// <summary>
        /// Der Wert in Kleinbuchstaben und ohne Leerzeichen am Rand, wenn er erlaubt ist, sonst null.
        /// </summary>
        public static string? Match(IEnumerable<string> allowed, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;

            string normalized = value.Trim().ToLowerInvariant();
            foreach (string candidate in allowed)
            {
                if (string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase)) return normalized;
            }
            return null;
        }

        /// <summary>Bekannte Suchmaschine oder DuckDuckGo (z. B. bei einer von Hand geänderten settings.json).</summary>
        public static string NormalizeSearchEngine(string? value) => Match(SearchEngines, value) ?? DefaultSearchEngine;
    }
}
