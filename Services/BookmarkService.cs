using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public class BookmarkService
    {
        private readonly string _filePath;

        public ObservableCollection<Bookmark> Bookmarks { get; } = new();

        public BookmarkService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "bookmarks.json");
            LoadBookmarks();
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
            catch
            {
                // Fallback on read error
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
            catch
            {
                // Suppress file write issues
            }
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

        public Bookmark AddBookmark(string title, string url, Bookmark? targetGroup = null)
        {
            var bm = new Bookmark(title, url);
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
