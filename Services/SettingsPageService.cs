using System;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public static class SettingsPageService
    {
        public const string SettingsPageUrl = "echo://settings";

        public static string GetSettingsPageHtml(AppSettings settings, string webViewVersion = "120.0", string appVersion = "1.2", object? initialUpdateStatus = null)
        {
            string settingsJson = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = false });
            string updateStatusJson = initialUpdateStatus != null
                ? JsonSerializer.Serialize(initialUpdateStatus, new JsonSerializerOptions { WriteIndented = false })
                : "null";

            // Erst übersetzen, dann Nutzerdaten (Einstellungen) einsetzen – so werden darin keine Platzhalter ersetzt
            string html = PageTemplates.Localized("settings", () => PageLocalizer.Apply(RawHtmlTemplate));
            // Theme und Token zuerst einsetzen: danach eingefügte Nutzerdaten (Adressen, Titel) dürfen keine
            // Platzhalter mehr enthalten, die noch ersetzt werden – sonst landet z.B. das Token in einer fremden URL
            html = html.Replace("##THEME_CSS##", ThemeManager.Instance.GetCssVariables());
            html = InternalPageSecurity.InjectToken(html);
            html = html.Replace("##UPDATE_STATUS_JSON##", updateStatusJson);
            html = html.Replace("##WEBVIEW_VERSION##", webViewVersion);
            html = html.Replace("##APP_VERSION##", appVersion);
            html = html.Replace("##START_BACKGROUND_STATUS##", System.Net.WebUtility.HtmlEncode(StartBackgroundStatus(settings)));
            bool isDefault = DefaultBrowserService.IsDefault();
            html = html.Replace("##DEFAULT_BROWSER_STATUS##", System.Net.WebUtility.HtmlEncode(
                Tr.Get(isDefault ? "Settings_DefaultBrowserIs" : "Settings_DefaultBrowserIsNot")));
            html = html.Replace("##DEFAULT_BROWSER_BUTTON_STYLE##", isDefault ? "display: none;" : "");
            // Einstellungen (mit Nutzertexten) zuletzt, damit darin keine Platzhalter mehr ersetzt werden
            html = html.Replace("##SETTINGS_JSON##", settingsJson);

            return html;
        }

        /// <summary>Statuszeile zum Hintergrundbild: Dateiname oder Hinweis auf den Farbverlauf des Themes.</summary>
        public static string StartBackgroundStatus(AppSettings settings)
        {
            string path = settings.StartpageBackgroundPath ?? "";
            return path.Length > 0 && System.IO.File.Exists(path)
                ? Tr.Format("Settings_StartBackgroundSet", System.IO.Path.GetFileName(path))
                : Tr.Get("Settings_StartBackgroundNone");
        }

        private static string RawHtmlTemplate => PageTemplates.Raw("settings.html");
    }
}
