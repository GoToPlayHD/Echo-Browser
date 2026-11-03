using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>Lesezeichen – eine Instanz für alle Fenster, damit sich Fenster nicht gegenseitig überschreiben.</summary>
    public class BookmarkService
    {
        private static BookmarkService? _instance;
        public static BookmarkService Instance => _instance ??= new BookmarkService();

        private readonly string _filePath;

        public ObservableCollection<Bookmark> Bookmarks { get; } = new();

        /// <summary>
        /// Nach jeder Änderung (auch in Gruppen): Lesezeichenleiste, Seitenleiste und Stern aller Fenster
        /// zeigen dieselben Daten und aktualisieren sich darüber.
        /// </summary>
        public event Action? Changed;

        private BookmarkService()
        {
            _filePath = AppPaths.File("bookmarks.json");
            LoadBookmarks();
            // Frühere Favoriten der Seitenleiste einmalig übernehmen (seitdem eine gemeinsame Liste)
            SidebarService.MigrateInto(this);
        }

        public void LoadBookmarks()
        {
            Bookmarks.Clear();
            bool fileExisted = File.Exists(_filePath);
            try
            {
                if (fileExisted)
                {
                    string json = File.ReadAllText(_filePath);
                    var items = JsonSerializer.Deserialize<ObservableCollection<Bookmark>>(json);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            Bookmarks.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AtomicFile.Quarantine(_filePath, ex);
                fileExisted = false;
            }

            // Only add initial defaults if the file was never created before
            if (!fileExisted)
            {
                Bookmarks.Add(new Bookmark("DuckDuckGo", "https://duckduckgo.com"));
                Bookmarks.Add(new Bookmark("GitHub", "https://github.com"));
                Bookmarks.Add(new Bookmark("Wikipedia", "https://de.wikipedia.org"));
                Bookmarks.Add(new Bookmark("Hacker News", "https://news.ycombinator.com"));
                SaveBookmarks();
            }
        }

        public void SaveBookmarks()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Bookmarks, options);
                AtomicFile.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Log.Warn("bookmarks.json konnte nicht gespeichert werden", ex);
            }
            Changed?.Invoke();
        }

        public bool IsBookmarked(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string clean = url.TrimEnd('/');
            return IsUrlInList(Bookmarks, clean);
        }

        private bool IsUrlInList(System.Collections.Generic.IEnumerable<Bookmark> list, string cleanUrl)
        {
            foreach (var b in list)
            {
                if (!b.IsGroup && b.Url.TrimEnd('/').Equals(cleanUrl, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (b.IsGroup && b.Children != null && IsUrlInList(b.Children, cleanUrl))
                    return true;
            }
            return false;
        }

        public bool ToggleBookmark(string title, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string clean = url.TrimEnd('/');
            
            // Check top level
            var existing = Bookmarks.FirstOrDefault(b => !b.IsGroup && b.Url.TrimEnd('/').Equals(clean, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                Bookmarks.Remove(existing);
                SaveBookmarks();
                return false;
            }

            // Check children
            foreach (var grp in Bookmarks.Where(b => b.IsGroup))
            {
                var child = grp.Children.FirstOrDefault(c => c.Url.TrimEnd('/').Equals(clean, StringComparison.OrdinalIgnoreCase));
                if (child != null)
                {
                    grp.Children.Remove(child);
                    SaveBookmarks();
                    return false;
                }
            }

            // Add new bookmark to root
            Bookmarks.Add(new Bookmark(title, url));
            SaveBookmarks();
            return true;
        }

        public Bookmark AddBookmark(string title, string url, Bookmark? targetGroup = null, string? iconKey = null, string? iconColor = null)
        {
            var bm = new Bookmark(title, url) { IconKey = iconKey, IconColor = iconColor };
            if (targetGroup != null && targetGroup.IsGroup)
            {
                targetGroup.Children.Add(bm);
            }
            else
            {
                Bookmarks.Add(bm);
            }
            SaveBookmarks();
            return bm;
        }

        /// <summary>
        /// Importierte Lesezeichen als neue Gruppe anlegen. Bereits vorhandene Adressen werden übersprungen.
        /// Gibt die Anzahl der neu angelegten Lesezeichen zurück (0 = keine Gruppe angelegt).
        /// </summary>
        public int ImportAsGroup(string groupName, IEnumerable<ImportedBookmark> items)
        {
            var known = new HashSet<string>(
                Bookmarks.SelectMany(b => b.IsGroup ? b.Children : new ObservableCollection<Bookmark> { b })
                         .Select(b => UrlHelper.NormalizeForComparison(b.Url)));

            var group = Bookmark.CreateGroup(groupName);
            foreach (var item in items)
            {
                if (known.Add(UrlHelper.NormalizeForComparison(item.Url)))
                {
                    group.Children.Add(new Bookmark(item.Title, item.Url));
                }
            }

            if (group.Children.Count == 0) return 0;
            Bookmarks.Add(group);
            SaveBookmarks();
            return group.Children.Count;
        }

        /// <summary>
        /// Favoriten der früheren, eigenen Seitenleisten-Liste übernehmen: vorhandene Adressen bekommen nur das
        /// Symbol, neue werden hinten angehängt. Gibt die Anzahl neu angelegter Lesezeichen zurück.
        /// </summary>
        public int MergeSidebarFavorites(IEnumerable<SidebarFavorite> favorites)
        {
            int added = MergeSidebarFavorites(Bookmarks, favorites);
            SaveBookmarks();
            return added;
        }

        internal static int MergeSidebarFavorites(ObservableCollection<Bookmark> bookmarks, IEnumerable<SidebarFavorite> favorites)
        {
            var all = bookmarks.SelectMany(b => b.IsGroup ? b.Children.AsEnumerable() : new[] { b }).ToList();
            int added = 0;
            foreach (var fav in favorites)
            {
                if (string.IsNullOrWhiteSpace(fav.Url)) continue;
                string key = UrlHelper.NormalizeForComparison(fav.Url);
                string? iconKey = string.IsNullOrEmpty(fav.IconKey) || fav.IconKey == "globe" ? null : fav.IconKey;

                var existing = all.FirstOrDefault(b => UrlHelper.NormalizeForComparison(b.Url) == key);
                if (existing != null)
                {
                    if (existing.IconKey == null && iconKey != null)
                    {
                        existing.IconKey = iconKey;
                        existing.IconColor = fav.IconColor;
                    }
                    continue;
                }

                var bm = new Bookmark(fav.Title, fav.Url) { IconKey = iconKey, IconColor = iconKey != null ? fav.IconColor : null };
                bookmarks.Add(bm);
                all.Add(bm);
                added++;
            }
            return added;
        }

        public Bookmark AddGroup(string groupName, string color = "#8A8F99")
        {
            var group = Bookmark.CreateGroup(groupName, color);
            Bookmarks.Add(group);
            SaveBookmarks();
            return group;
        }

        public void RemoveBookmark(Bookmark bookmark, Bookmark? parentGroup = null)
        {
            if (parentGroup != null && parentGroup.Children.Contains(bookmark))
            {
                parentGroup.Children.Remove(bookmark);
                SaveBookmarks();
            }
            else if (Bookmarks.Contains(bookmark))
            {
                Bookmarks.Remove(bookmark);
                SaveBookmarks();
            }
            else
            {
                foreach (var g in Bookmarks.Where(b => b.IsGroup))
                {
                    if (g.Children.Contains(bookmark))
                    {
                        g.Children.Remove(bookmark);
                        SaveBookmarks();
                        break;
                    }
                }
            }
        }
    }
}
