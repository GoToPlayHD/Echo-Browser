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

        /// <summary>
        /// Eine einzelne Einstellung hat sich geändert (Schlüssel = Eigenschaftsname in AppSettings).
        /// Jedes Fenster wendet sie auf sich an – so gilt eine Änderung sofort in allen Fenstern.
        /// </summary>
        public event Action<string>? SettingChanged;

        /// <summary>Einstellung geändert: speichern und alle Fenster benachrichtigen.</summary>
        public void Changed(string key)
        {
            Save();
            SettingChanged?.Invoke(key);
        }

        private AppSettingsService()
        {
            _filePath = AppPaths.File("settings.json");
            Load();
        }

        public void Load()
        {
            // Standard-Verknüpfungen nur beim ersten Start (bzw. wenn die Datei sie nie enthielt) –
            // hat der Nutzer alle gelöscht, bleibt die Liste leer
            bool hasStoredShortcuts = false;
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    hasStoredShortcuts = json.Contains("\"StartpageShortcuts\"", StringComparison.Ordinal);
                    if (settings != null)
                    {
                        Settings = settings;
                    }
                }
            }
            catch (Exception ex)
            {
                AtomicFile.Quarantine(_filePath, ex);
                Settings = new AppSettings();
            }

            if (Settings.StartpageShortcuts == null || (!hasStoredShortcuts && Settings.StartpageShortcuts.Count == 0))
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

        private static readonly JsonSerializerOptions SaveOptions = new() { WriteIndented = true };

        /// <summary>
        /// Speichern: Inhalt jetzt erzeugen (aufrufender Thread), schreiben im Hintergrund – Schieberegler der
        /// Einstellungsseite rufen das sehr oft auf. Beim Beenden wartet App auf BackgroundFileWriter.Flush.
        /// </summary>
        public void Save()
        {
            try
            {
                BackgroundFileWriter.Enqueue(_filePath, JsonSerializer.Serialize(Settings, SaveOptions));
                SettingsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                Log.Warn("Einstellungen konnten nicht gespeichert werden", ex);
            }
        }

        public void SetSearchEngine(string engineKey)
        {
            string? engine = SettingValues.Match(SettingValues.SearchEngines, engineKey);
            if (engine == null)
            {
                Log.Warn($"Unbekannte Suchmaschine '{engineKey}' ignoriert");
                return;
            }
            Settings.SearchEngine = engine;
            Changed(nameof(AppSettings.SearchEngine));
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
