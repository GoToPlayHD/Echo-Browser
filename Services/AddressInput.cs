using System;

namespace EchoBrowser.Services
{
    public enum AddressTargetKind { StartPage, Settings, Url, Search }

    /// <summary>Ergebnis einer Eingabe in Adressleiste, Startseite oder "In neuem Tab öffnen".</summary>
    public readonly record struct AddressTarget(AddressTargetKind Kind, string Url);

    /// <summary>
    /// Was bedeutet eine Eingabe? Startseite, Einstellungen (auch unter den von Chrome/Edge/Firefox gewohnten
    /// Adressen), eine Webadresse oder eine Suche mit der eingestellten Suchmaschine.
    /// Eine Stelle für Adressleiste, Alt+Enter und Startseite – ohne UI, daher direkt testbar.
    /// </summary>
    public static class AddressInput
    {
        public static bool IsStartPage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            return InternalPages.Is(url, InternalPages.Start) ||
                   InternalPages.Is(url, InternalPages.NewTab) ||
                   url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSettingsAlias(string input) =>
            InternalPages.Is(input, InternalPages.Settings) ||
            input.Equals("about:settings", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("about:preferences", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("chrome://settings", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("edge://settings", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("settings", StringComparison.OrdinalIgnoreCase);

        /// <summary>Eingabe auflösen; die Suche nutzt die eingestellte Suchmaschine.</summary>
        public static AddressTarget Resolve(string input) =>
            Resolve(input, AppSettingsService.Instance.GetSearchUrl);

        /// <param name="searchUrl">Baut die Such-URL für einen Suchbegriff (Tests übergeben eine feste Suchmaschine).</param>
        public static AddressTarget Resolve(string input, Func<string, string> searchUrl)
        {
            string target = (input ?? "").Trim();
            if (IsStartPage(target)) return new(AddressTargetKind.StartPage, StartPageService.StartPageUrl);
            if (IsSettingsAlias(target)) return new(AddressTargetKind.Settings, SettingsPageService.SettingsPageUrl);

            // Erkennung von Adressen: UrlHelper.ToNavigableUrl
            return UrlHelper.ToNavigableUrl(target) is { } url
                ? new(AddressTargetKind.Url, url)
                : new(AddressTargetKind.Search, searchUrl(target));
        }
    }
}
