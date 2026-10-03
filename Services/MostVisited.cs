using System;
using System.Collections.Generic;
using System.Linq;

namespace EchoBrowser.Services
{
    /// <summary>Eine Kachel "Meistbesucht" auf der Startseite.</summary>
    public sealed record MostVisitedSite(string Title, string Url, string Host);

    /// <summary>
    /// Meistbesuchte Websites der letzten Wochen für die Startseite – eine Kachel pro Website (Host),
    /// ohne Websites, die schon als Schnellzugriff angelegt sind.
    /// </summary>
    public static class MostVisited
    {
        public static List<MostVisitedSite> Calculate(
            IEnumerable<SuggestionSource> history,
            IEnumerable<string> excludedUrls,
            DateTime now,
            int max = 6,
            int days = 30)
        {
            var excludedHosts = new HashSet<string>(
                excludedUrls.Select(UrlHelper.ShortHost).Where(h => h != null)!,
                StringComparer.OrdinalIgnoreCase);

            return history
                .Where(h => h.VisitedAt == null || (now - h.VisitedAt.Value).TotalDays <= days)
                .Select(h => (Entry: h, Host: UrlHelper.ShortHost(h.Url)))
                .Where(x => x.Host != null && !excludedHosts.Contains(x.Host) &&
                            (x.Entry.Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             x.Entry.Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                .GroupBy(x => x.Host!, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Max(x => x.Entry.VisitedAt ?? DateTime.MinValue))
                .Take(max)
                .Select(g =>
                {
                    // Die kürzeste Adresse ist meist die Startseite der Website. Beschriftet wird mit der Domain:
                    // gespeicherte Seitentitel sind oft Zwischenstände ("Loading…", Cookie-Abfragen).
                    var entry = g.OrderBy(x => x.Entry.Url.Length).First().Entry;
                    return new MostVisitedSite(g.Key, entry.Url, g.Key);
                })
                .ToList();
        }
    }
}
