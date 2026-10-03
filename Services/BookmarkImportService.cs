using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>Ein Lesezeichen aus einem anderen Browser oder einer HTML-Datei.</summary>
    public sealed record ImportedBookmark(string Title, string Url);

    /// <summary>
    /// Lesezeichen-Import aus Chrome/Edge/Brave (Datei "Bookmarks" im JSON-Format) und aus HTML-Dateien
    /// (Netscape-Format, das alle Browser exportieren) sowie Export in dieses HTML-Format.
    /// </summary>
    public static class BookmarkImportService
    {
        /// <summary>Speicherort der Lesezeichen-Datei des Standardprofils von Chrome, Edge oder Brave.</summary>
        public static string? ChromiumBookmarksPath(string browser)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string? userData = browser switch
            {
                "chrome" => Path.Combine(local, "Google", "Chrome", "User Data"),
                "edge" => Path.Combine(local, "Microsoft", "Edge", "User Data"),
                "brave" => Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
                _ => null
            };
            if (userData == null) return null;

            string file = Path.Combine(userData, "Default", "Bookmarks");
            return File.Exists(file) ? file : null;
        }

        /// <summary>Alle Lesezeichen (Lesezeichenleiste, Weitere, Mobil) einer Chromium-"Bookmarks"-Datei, Ordner aufgelöst.</summary>
        public static List<ImportedBookmark> ParseChromium(string json)
        {
            var result = new List<ImportedBookmark>();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("roots", out var roots)) return result;

            foreach (var root in roots.EnumerateObject())
            {
                if (root.Value.ValueKind == JsonValueKind.Object) Collect(root.Value, result);
            }
            return Deduplicate(result);
        }

        private static void Collect(JsonElement node, List<ImportedBookmark> result)
        {
            string type = node.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            if (type == "url" && node.TryGetProperty("url", out var url) && IsImportableUrl(url.GetString()))
            {
                string name = node.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                result.Add(new ImportedBookmark(string.IsNullOrWhiteSpace(name) ? url.GetString()! : name.Trim(), url.GetString()!));
            }

            if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in children.EnumerateArray()) Collect(child, result);
            }
        }

        private static readonly Regex AnchorRegex = new(
            @"<A\s[^>]*HREF\s*=\s*""(?<url>[^""]*)""[^>]*>(?<title>.*?)</A>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

        private static readonly Regex TagRegex = new("<[^>]+>", RegexOptions.Compiled);

        /// <summary>Lesezeichen aus einer HTML-Exportdatei (Netscape-Format) – Ordner werden aufgelöst.</summary>
        public static List<ImportedBookmark> ParseNetscapeHtml(string html)
        {
            var result = new List<ImportedBookmark>();
            foreach (Match match in AnchorRegex.Matches(html))
            {
                string url = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
                if (!IsImportableUrl(url)) continue;

                string title = WebUtility.HtmlDecode(TagRegex.Replace(match.Groups["title"].Value, "")).Trim();
                result.Add(new ImportedBookmark(title.Length > 0 ? title : url, url));
            }
            return Deduplicate(result);
        }

        /// <summary>Echo-Lesezeichen als HTML-Datei (Netscape-Format) – Gruppen werden zu Ordnern.</summary>
        public static string ToNetscapeHtml(IEnumerable<Bookmark> bookmarks)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE NETSCAPE-Bookmark-file-1>");
            sb.AppendLine("<!-- Exportiert aus Echo-Browser -->");
            sb.AppendLine("<META HTTP-EQUIV=\"Content-Type\" CONTENT=\"text/html; charset=UTF-8\">");
            sb.AppendLine("<TITLE>Bookmarks</TITLE>");
            sb.AppendLine("<H1>Bookmarks</H1>");
            sb.AppendLine("<DL><p>");
            sb.AppendLine("    <DT><H3 PERSONAL_TOOLBAR_FOLDER=\"true\">Echo-Browser</H3>");
            sb.AppendLine("    <DL><p>");

            foreach (var bookmark in bookmarks)
            {
                if (bookmark.IsGroup)
                {
                    sb.AppendLine($"        <DT><H3>{WebUtility.HtmlEncode(bookmark.Title)}</H3>");
                    sb.AppendLine("        <DL><p>");
                    foreach (var child in bookmark.Children) AppendLink(sb, child, "            ");
                    sb.AppendLine("        </DL><p>");
                }
                else
                {
                    AppendLink(sb, bookmark, "        ");
                }
            }

            sb.AppendLine("    </DL><p>");
            sb.AppendLine("</DL><p>");
            return sb.ToString();
        }

        private static void AppendLink(StringBuilder sb, Bookmark bookmark, string indent)
        {
            long added = new DateTimeOffset(bookmark.DateAdded).ToUnixTimeSeconds();
            sb.AppendLine($"{indent}<DT><A HREF=\"{WebUtility.HtmlEncode(bookmark.Url)}\" ADD_DATE=\"{added}\">{WebUtility.HtmlEncode(bookmark.Title)}</A>");
        }

        /// <summary>Nur echte Webadressen übernehmen (keine javascript:-Bookmarklets, keine chrome://-Seiten).</summary>
        public static bool IsImportableUrl(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFtp);

        private static List<ImportedBookmark> Deduplicate(IEnumerable<ImportedBookmark> bookmarks)
        {
            var seen = new HashSet<string>();
            return bookmarks.Where(b => seen.Add(UrlHelper.NormalizeForComparison(b.Url))).ToList();
        }
    }
}
