using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public class SidebarService
    {
        private readonly string _filePath;

        public ObservableCollection<SidebarFavorite> Favorites { get; } = new();

        public SidebarService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "sidebar_favorites.json");
            LoadFavorites();
        }

        public void LoadFavorites()
        {
            Favorites.Clear();
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var items = JsonSerializer.Deserialize<ObservableCollection<SidebarFavorite>>(json);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            item.UpdateIconData();
                            Favorites.Add(item);
                        }
                    }
                }
            }
            catch
            {
                // Fallback on corrupt file
            }

            if (Favorites.Count == 0)
            {
                // Sensible default favorites
                Favorites.Add(new SidebarFavorite("Google", "https://www.google.com", "google", "#D0D3D9"));
                Favorites.Add(new SidebarFavorite("YouTube", "https://www.youtube.com", "youtube", "#FF4A4A"));
                Favorites.Add(new SidebarFavorite("GitHub", "https://github.com", "github", "#F0F2F5"));
                Favorites.Add(new SidebarFavorite("ChatGPT", "https://chatgpt.com", "chatgpt", "#34D399"));
                Favorites.Add(new SidebarFavorite("Wikipedia", "https://de.wikipedia.org", "wikipedia", "#C4C7CC"));
                Favorites.Add(new SidebarFavorite("Reddit", "https://reddit.com", "reddit", "#FF6633"));
                SaveFavorites();
            }
        }

        public void SaveFavorites()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Favorites, options);
                AtomicFile.WriteAllText(_filePath, json);
            }
            catch
            {
                // Suppress save issues
            }
        }

        public void AddFavorite(string title, string url, string iconKey, string color = "#C4C7CC")
        {
            var fav = new SidebarFavorite(title, url, iconKey, color);
            Favorites.Add(fav);
            SaveFavorites();
        }

        public void RemoveFavorite(SidebarFavorite fav)
        {
            if (Favorites.Contains(fav))
            {
                Favorites.Remove(fav);
                SaveFavorites();
            }
        }
    }
}
