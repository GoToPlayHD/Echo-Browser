using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EchoBrowser.Services
{
    public enum SuggestionKind
    {
        /// <summary>Eingabe direkt als Adresse öffnen.</summary>
        Url,
        /// <summary>Mit der gewählten Suchmaschine suchen.</summary>
        Search,
        /// <summary>Bereits offener Tab – zu ihm wechseln statt neu zu laden.</summary>
        SwitchToTab,
        Bookmark,
        History
    }

    /// <summary>Ein Eintrag in der Vorschlagsliste der Adressleiste.</summary>
    public sealed record OmniboxSuggestion(SuggestionKind Kind, string Title, string Url, double Score = 0)
    {
        /// <summary>Für Tabs: Kennung des Tabs (BrowserTab.Id), damit das Fenster zu ihm wechseln kann.</summary>
        public string? TabId { get; init; }

        public bool IsSearch => Kind == SuggestionKind.Search;
        public bool IsTab => Kind == SuggestionKind.SwitchToTab;
        public bool IsHistory => Kind == SuggestionKind.History;
        public bool IsBookmark => Kind == SuggestionKind.Bookmark;

        /// <summary>Zweite Zeile/Spalte: Adresse ohne "https://".</summary>
        public string DisplayUrl => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? Url[8..].TrimEnd('/')
            : Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? Url[7..].TrimEnd('/')
            : Url;
    }

    /// <summary>Quelle für Vorschläge: offener Tab, Lesezeichen oder Verlaufseintrag.</summary>
    public sealed record SuggestionSource(string Title, string Url, DateTime? VisitedAt = null, string? TabId = null);

    /// <summary>
    /// Bewertet Tabs, Lesezeichen und Verlauf für eine Eingabe. Treffer am Anfang des Hosts zählen am meisten
    /// ("git" → github.com), dann Wortanfänge im Titel, dann beliebige Teile. Der Verlauf gewichtet
    /// Häufigkeit und Aktualität (wie die "frecency" von Firefox/Chrome).
    /// </summary>
    public static class OmniboxRanker
    {
        public const int MaxLocalResults = 6;

        public static List<OmniboxSuggestion> Rank(
            string input,
            IEnumerable<SuggestionSource> openTabs,
            IEnumerable<SuggestionSource> bookmarks,
            IEnumerable<SuggestionSource> history,
            DateTime now,
            int max = MaxLocalResults)
        {
            string query = input.Trim();
            if (query.Length == 0) return new List<OmniboxSuggestion>();

            string[] tokens = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var byUrl = new Dictionary<string, OmniboxSuggestion>();

            void Offer(OmniboxSuggestion candidate)
            {
                string key = UrlHelper.NormalizeForComparison(candidate.Url);
                if (!byUrl.TryGetValue(key, out var existing))
                {
                    byUrl[key] = candidate;
                    return;
                }

                // Gleiche Adresse aus mehreren Quellen: höchste Punktzahl, ein offener Tab gewinnt immer
                double score = Math.Max(existing.Score, candidate.Score) + 5;
                var winner = existing.IsTab ? existing : candidate.IsTab ? candidate
                    : candidate.Score > existing.Score ? candidate : existing;
                byUrl[key] = winner with { Score = score };
            }

            foreach (var tab in openTabs)
            {
                double match = MatchScore(tokens, tab.Title, tab.Url);
                if (match > 0) Offer(new OmniboxSuggestion(SuggestionKind.SwitchToTab, tab.Title, tab.Url, match + 15) { TabId = tab.TabId });
            }

            foreach (var bookmark in bookmarks)
            {
                double match = MatchScore(tokens, bookmark.Title, bookmark.Url);
                if (match > 0) Offer(new OmniboxSuggestion(SuggestionKind.Bookmark, bookmark.Title, bookmark.Url, match + 25));
            }

            // Verlauf: pro Adresse Besuche zählen und den letzten Besuch merken
            foreach (var group in history.GroupBy(h => UrlHelper.NormalizeForComparison(h.Url)))
            {
                var latest = group.OrderByDescending(h => h.VisitedAt ?? DateTime.MinValue).First();
                double match = MatchScore(tokens, latest.Title, latest.Url);
                if (match <= 0) continue;

                int visits = group.Count();
                double days = latest.VisitedAt.HasValue ? Math.Max(0, (now - latest.VisitedAt.Value).TotalDays) : 365;
                double frecency = Math.Min(30, visits * 5) + 30 * Math.Exp(-days / 14);
                Offer(new OmniboxSuggestion(SuggestionKind.History, latest.Title, latest.Url, match + frecency));
            }

            return byUrl.Values
                .OrderByDescending(s => s.Score)
                .ThenBy(s => s.Url.Length)
                .Take(max)
                .ToList();
        }

        /// <summary>Jedes Wort der Eingabe muss in Titel oder Adresse vorkommen; sonst 0.</summary>
        public static double MatchScore(string[] tokens, string? title, string url)
        {
            string host = UrlHelper.ShortHost(url)?.ToLowerInvariant() ?? "";
            string address = UrlHelper.NormalizeForComparison(url).ToLowerInvariant();
            string name = (title ?? "").ToLowerInvariant();
            double total = 0;

            foreach (string token in tokens)
            {
                double best = 0;
                if (host.StartsWith(token, StringComparison.Ordinal)) best = 100;
                else if (address.StartsWith(token, StringComparison.Ordinal)) best = 90;
                else if (StartsAnyWord(name, token)) best = 60;
                else if (host.Contains(token, StringComparison.Ordinal)) best = 45;
                else if (address.Contains(token, StringComparison.Ordinal)) best = 30;
                else if (name.Contains(token, StringComparison.Ordinal)) best = 25;

                if (best == 0) return 0;
                total += best;
            }
            return total / tokens.Length;
        }

        private static bool StartsAnyWord(string text, string token)
        {
            int index = 0;
            while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                if (index == 0 || !char.IsLetterOrDigit(text[index - 1])) return true;
                index++;
            }
            return false;
        }

        /// <summary>
        /// Inline-Vervollständigung wie in Chrome: tippt man "git" und ist github.com der beste Treffer,
        /// wird "hub.com" markiert angehängt. Gibt den vollständigen Text zurück oder null.
        /// </summary>
        public static string? InlineCompletion(string input, IEnumerable<OmniboxSuggestion> ranked) =>
            InlineCompletion(input, ranked, out _);

        /// <summary>Wie oben, liefert zusätzlich den Vorschlag, aus dem die Vervollständigung stammt.</summary>
        public static string? InlineCompletion(string input, IEnumerable<OmniboxSuggestion> ranked, out OmniboxSuggestion? source)
        {
            source = null;
            string typed = input.Trim();
            if (typed.Length == 0 || typed.Contains(' ') || input.EndsWith(' ')) return null;

            foreach (var suggestion in ranked)
            {
                if (suggestion.IsSearch || suggestion.Kind == SuggestionKind.Url) continue;

                string candidate = UrlHelper.NormalizeForComparison(suggestion.Url);
                if (candidate.Length > typed.Length && candidate.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                {
                    // Nur bis zum Ende des Hosts vervollständigen, wenn nur ein Teil des Hosts getippt wurde
                    string host = UrlHelper.ShortHost(suggestion.Url) ?? candidate;
                    string completion = typed.Length < host.Length && host.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                        ? host
                        : candidate;
                    source = suggestion;
                    return typed + completion[typed.Length..];
                }
            }
            return null;
        }
    }

    /// <summary>Suchvorschläge der eingestellten Suchmaschine (Format "OpenSearch Suggestions").</summary>
    public static class SearchSuggestionClient
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) EchoBrowser");
            return client;
        }

        public static string? EndpointFor(string engine, string query)
        {
            string q = Uri.EscapeDataString(query);
            return engine switch
            {
                "google" => $"https://suggestqueries.google.com/complete/search?client=firefox&q={q}",
                "bing" => $"https://api.bing.com/osjson.aspx?query={q}",
                "ecosia" => $"https://ac.ecosia.org/autocomplete?q={q}&type=list",
                "brave" => $"https://search.brave.com/api/suggest?q={q}",
                "startpage" => $"https://www.startpage.com/suggestions?q={q}&format=opensearch",
                _ => $"https://duckduckgo.com/ac/?q={q}&type=list"
            };
        }

        public static async Task<List<string>> GetAsync(string engine, string query, CancellationToken token)
        {
            string? url = EndpointFor(engine, query);
            if (url == null) return new List<string>();
            string json = await Http.GetStringAsync(url, token);
            return Parse(json);
        }

        /// <summary>
        /// Versteht das übliche Format ["eingabe", ["vorschlag 1", "vorschlag 2"]] sowie Varianten
        /// mit {"suggestions": [...]} oder einer Liste von {"phrase": "..."}.
        /// </summary>
        public static List<string> Parse(string json)
        {
            var result = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                JsonElement list = default;

                if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() >= 2 && root[1].ValueKind == JsonValueKind.Array)
                {
                    list = root[1];
                }
                else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("suggestions", out var suggestions))
                {
                    list = suggestions;
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    list = root;
                }

                if (list.ValueKind != JsonValueKind.Array) return result;

                foreach (var item in list.EnumerateArray())
                {
                    string? text = item.ValueKind switch
                    {
                        JsonValueKind.String => item.GetString(),
                        JsonValueKind.Object when item.TryGetProperty("phrase", out var p) => p.GetString(),
                        JsonValueKind.Object when item.TryGetProperty("text", out var t) => t.GetString(),
                        _ => null
                    };
                    if (!string.IsNullOrWhiteSpace(text) && !result.Contains(text, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(text.Trim());
                    }
                }
            }
            catch (JsonException)
            {
                // Unerwartetes Format – dann eben keine Vorschläge
            }
            return result;
        }
    }
}
