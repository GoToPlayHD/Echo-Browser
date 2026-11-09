using System;
using System.Net;

namespace EchoBrowser.Services
{
    /// <summary>Seite, die ein Tab zeigt, wenn sein Renderer-Prozess abgestürzt ist (echo://crashed/?url=…).</summary>
    public static class CrashPageService
    {
        public static string GetCrashPageHtml(string? originalUrl)
        {
            string url = originalUrl ?? "";
            string html = PageTemplates.Localized("crashed", () => PageLocalizer.Apply(RawHtmlTemplate));
            // Theme und Token zuerst einsetzen: danach eingefügte Nutzerdaten (Adressen, Titel) dürfen keine
            // Platzhalter mehr enthalten, die noch ersetzt werden – sonst landet z.B. das Token in einer fremden URL
            html = html.Replace("##THEME_CSS##", ThemeManager.Instance.GetCssVariables());
            html = InternalPageSecurity.InjectToken(html);
            // Als JSON-String einsetzen, damit die Adresse im Skript nichts ausbrechen kann. Webseiten können
            // echo://crashed/?url=… selbst aufrufen – "Neu laden" darf deshalb nur Web- und interne Adressen öffnen.
            string reloadJson = System.Text.Json.JsonSerializer.Serialize(IsReloadable(url) ? url : "");
            string urlText = WebUtility.HtmlEncode(url);
            html = System.Text.RegularExpressions.Regex.Replace(html, "##(URL_TEXT|URL_JSON)##",
                m => m.Groups[1].Value == "URL_TEXT" ? urlText : reloadJson);
            return html;
        }

        internal static bool IsReloadable(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == InternalPages.Scheme);

        private static string RawHtmlTemplate => PageTemplates.Raw("crashed.html");
    }
}
