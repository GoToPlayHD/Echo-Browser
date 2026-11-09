using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public static class StartPageService
    {
        public const string StartPageUrl = "echo://start";

        public static string GetStartPageHtml(bool isIncognito = false)
        {
            var settings = AppSettingsService.Instance.Settings;
            string currentEngine = settings.SearchEngine ?? "duckduckgo";
            bool showFavorites = !isIncognito && settings.IsStartpageFavoritesVisible;
            string shortcutsJson = JsonSerializer.Serialize(settings.StartpageShortcuts ?? new());

            string incognitoExtra = isIncognito 
                ? @"<div class=""incognito-tag"">
                        <svg width=""16"" height=""16"" viewBox=""0 0 24 24"" fill=""none"" stroke=""#9CA3AF"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                            <path d=""M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z""/>
                            <circle cx=""12"" cy=""12"" r=""3""/>
                        </svg>
                        <span>{{t:Start_Incognito}}</span>
                    </div>
                    <p class=""incognito-notice"">{{t:Start_IncognitoNotice}}</p>"
                : "";

            // Erst übersetzen, dann Nutzerdaten (Verknüpfungen) einsetzen – so werden darin keine Platzhalter ersetzt
            string html = PageTemplates.Localized(isIncognito ? "start-incognito" : "start",
                () => PageLocalizer.Apply(RawHtmlTemplate.Replace("##INCOGNITO_EXTRA##", incognitoExtra)));
            // Theme und Token zuerst einsetzen: danach eingefügte Nutzerdaten (Adressen, Titel) dürfen keine
            // Platzhalter mehr enthalten, die noch ersetzt werden – sonst landet z.B. das Token in einer fremden URL
            html = html.Replace("##THEME_CSS##", ThemeManager.Instance.GetCssVariables());
            html = InternalPageSecurity.InjectToken(html);
            html = html.Replace("##TITLE##", System.Net.WebUtility.HtmlEncode(Tr.Get(isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab")));
            html = html.Replace("##CURRENT_ENGINE##", currentEngine);
            html = html.Replace("##SHOW_FAVORITES##", showFavorites ? "true" : "false");
            html = html.Replace("##SHORTCUTS_SECTION_CLASS##", showFavorites ? "" : "hidden");
            html = html.Replace("##TOGGLE_TEXT##", System.Net.WebUtility.HtmlEncode(Tr.Get(showFavorites ? "Start_HideShortcuts" : "Start_ShowShortcuts")));

            // "Meistbesucht" aus dem Verlauf (nicht im Inkognito-Modus)
            var mostVisited = isIncognito
                ? new List<MostVisitedSite>()
                : MostVisited.Calculate(
                    HistoryService.Instance.Entries.Select(h => new SuggestionSource(h.Title, h.Url, h.VisitedAt)),
                    (settings.StartpageShortcuts ?? new()).Select(s => s.Url),
                    DateTime.Now);
            string mostVisitedJson = JsonSerializer.Serialize(mostVisited);

            // Eigenes Hintergrundbild
            string wallpaper = settings.StartpageBackgroundPath ?? "";
            bool hasWallpaper = wallpaper.Length > 0 && System.IO.File.Exists(wallpaper);
            html = html.Replace("##WALLPAPER_CLASS##", hasWallpaper ? "has-wallpaper" : "");
            html = html.Replace("##WALLPAPER_VERSION##", hasWallpaper ? System.IO.File.GetLastWriteTimeUtc(wallpaper).Ticks.ToString() : "0");

            // Nutzerdaten zuletzt und in einem Durchgang: ein Titel wie "##MOST_VISITED_JSON##" wird so nicht ersetzt
            html = System.Text.RegularExpressions.Regex.Replace(html, "##(SHORTCUTS_JSON|MOST_VISITED_JSON)##",
                m => m.Groups[1].Value == "SHORTCUTS_JSON" ? shortcutsJson : mostVisitedJson);

            return html;
        }

        private static string RawHtmlTemplate => PageTemplates.Raw("start.html");
    }
}
