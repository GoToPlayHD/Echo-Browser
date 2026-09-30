using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public class AppSettingsService
    {
        private static AppSettingsService? _instance;
        public static AppSettingsService Instance => _instance ??= new AppSettingsService();

        private readonly string _filePath;
        public AppSettings Settings { get; private set; } = new();

        public event Action? SettingsChanged;

        private AppSettingsService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "settings.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        Settings = settings;
                    }
                }
            }
            catch
            {
                Settings = new AppSettings();
            }

            // Defaults if shortcuts empty
            if (Settings.StartpageShortcuts == null || Settings.StartpageShortcuts.Count == 0)
            {
                Settings.StartpageShortcuts = new List<StartpageShortcut>
                {
                    new StartpageShortcut("Google", "https://www.google.com", "google"),
                    new StartpageShortcut("YouTube", "https://www.youtube.com", "youtube"),
                    new StartpageShortcut("GitHub", "https://github.com", "github"),
                    new StartpageShortcut("ChatGPT", "https://chatgpt.com", "chatgpt"),
                    new StartpageShortcut("Wikipedia", "https://de.wikipedia.org", "wikipedia"),
                    new StartpageShortcut("Reddit", "https://reddit.com", "reddit")
                };
                Save();
            }

            if (string.IsNullOrWhiteSpace(Settings.DownloadPath))
            {
                Settings.DownloadPath = AppSettings.GetDefaultDownloadPath();
                Save();
            }
        }

        public void ResetToDefaults()
        {
            Settings = new AppSettings();
            Settings.StartpageShortcuts = new List<StartpageShortcut>
            {
                new StartpageShortcut("Google", "https://www.google.com", "google"),
                new StartpageShortcut("YouTube", "https://www.youtube.com", "youtube"),
                new StartpageShortcut("GitHub", "https://github.com", "github"),
                new StartpageShortcut("ChatGPT", "https://chatgpt.com", "chatgpt"),
                new StartpageShortcut("Wikipedia", "https://de.wikipedia.org", "wikipedia"),
                new StartpageShortcut("Reddit", "https://reddit.com", "reddit")
            };
            Save();
        }

        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Settings, options);
                File.WriteAllText(_filePath, json);
                SettingsChanged?.Invoke();
            }
            catch
            {
                // Ignore file write errors
            }
        }

        public void SetSearchEngine(string engineKey)
        {
            if (string.IsNullOrWhiteSpace(engineKey)) return;
            Settings.SearchEngine = engineKey.ToLowerInvariant().Trim();
            Save();
        }

        public string GetSearchUrl(string query)
        {
            string encoded = Uri.EscapeDataString(query);
            return Settings.SearchEngine switch
            {
                "google" => $"https://www.google.com/search?q={encoded}",
                "bing" => $"https://www.bing.com/search?q={encoded}",
                "ecosia" => $"https://www.ecosia.org/search?q={encoded}",
                "brave" => $"https://search.brave.com/search?q={encoded}",
                "startpage" => $"https://www.startpage.com/do/dsearch?query={encoded}",
                _ => $"https://duckduckgo.com/?q={encoded}" // Default: DuckDuckGo
            };
        }

        public string GetSearchEngineDisplayName()
        {
            return Settings.SearchEngine switch
            {
                "google" => "Google",
                "bing" => "Bing",
                "ecosia" => "Ecosia",
                "brave" => "Brave",
                "startpage" => "Startpage",
                _ => "DuckDuckGo"
            };
        }
    }
}
