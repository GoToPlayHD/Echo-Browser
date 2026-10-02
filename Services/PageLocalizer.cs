using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Ersetzt Übersetzungs-Platzhalter in den internen HTML-Seiten (Startseite, Einstellungen):
    /// <list type="bullet">
    /// <item><c>{{t:Key}}</c> – HTML-kodierter Text (für Elementinhalt und Attribute)</item>
    /// <item><c>{{js:Key}}</c> – JavaScript-Stringliteral inkl. Anführungszeichen</item>
    /// <item><c>{{lang}}</c> – aktueller Sprachcode (für &lt;html lang&gt;)</item>
    /// </list>
    /// </summary>
    public static class PageLocalizer
    {
        private static readonly Regex TextToken = new(@"\{\{t:([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);
        private static readonly Regex JsToken = new(@"\{\{js:([A-Za-z0-9_]+)\}\}", RegexOptions.Compiled);

        public static string Apply(string html)
        {
            html = TextToken.Replace(html, m => WebUtility.HtmlEncode(Tr.Get(m.Groups[1].Value)));
            // Der Standard-Encoder von System.Text.Json maskiert <, > und &, damit ist das Literal auch in <script> sicher
            html = JsToken.Replace(html, m => JsonSerializer.Serialize(Tr.Get(m.Groups[1].Value)));
            html = html.Replace("{{lang}}", LocalizationService.Instance.CurrentLanguage);
            return html;
        }
    }
}
