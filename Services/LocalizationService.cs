using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Lädt die Übersetzungen aus den eingebetteten Dateien Localization/&lt;sprache&gt;.json.
    /// Fehlt ein Text in der aktuellen Sprache, wird auf Englisch und dann auf Deutsch zurückgegriffen.
    /// Neue Sprache: einfach eine weitere JSON-Datei in den Ordner Localization legen.
    /// </summary>
    public class LocalizationService : INotifyPropertyChanged
    {
        private static LocalizationService? _instance;
        public static LocalizationService Instance => _instance ??= new LocalizationService();

        public const string DefaultLanguage = "de";
        private static readonly string[] FallbackLanguages = { "en", "de" };

        public event Action? LanguageChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly Dictionary<string, Dictionary<string, string>> _languages = new(StringComparer.OrdinalIgnoreCase);
        private string _currentLanguage = DefaultLanguage;

        private LocalizationService()
        {
            LoadEmbeddedLanguages();
        }

        /// <summary>Alle verfügbaren Sprachcodes (aus den eingebetteten JSON-Dateien).</summary>
        public IReadOnlyCollection<string> AvailableLanguages => _languages.Keys;

        public string CurrentLanguage
        {
            get => _currentLanguage;
            set
            {
                string norm = (value ?? DefaultLanguage).ToLowerInvariant().Trim();
                if (!_languages.ContainsKey(norm))
                {
                    norm = DefaultLanguage;
                }

                if (_currentLanguage == norm) return;

                _currentLanguage = norm;
                // "Item[]" aktualisiert alle XAML-Bindings auf den Indexer ({loc:Loc ...})
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                LanguageChanged?.Invoke();
            }
        }

        public void SetLanguage(string lang) => CurrentLanguage = lang;

        /// <summary>Indexer für XAML-Bindings: LocalizationService.Instance["Key"].</summary>
        public string this[string key] => GetString(key);

        public string GetString(string key, string fallback = "")
        {
            if (string.IsNullOrWhiteSpace(key)) return fallback;

            if (TryGet(_currentLanguage, key, out var value)) return value;
            foreach (var lang in FallbackLanguages)
            {
                if (TryGet(lang, key, out value)) return value;
            }

            Debug.WriteLine($"[Echo] Fehlende Übersetzung: '{key}'");
            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }

        /// <summary>Übersetzter Text mit Platzhaltern ({0}, {1}, ...).</summary>
        public string Format(string key, params object?[] args)
        {
            try
            {
                return string.Format(GetString(key), args);
            }
            catch (FormatException)
            {
                return GetString(key);
            }
        }

        /// <summary>Alle Texte mit dem angegebenen Präfix in der aktuellen Sprache (inkl. Fallbacks), z.B. für die HTML-Seiten.</summary>
        public Dictionary<string, string> GetAllStrings(string prefix)
        {
            var keys = _languages.Values
                .SelectMany(d => d.Keys)
                .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal);

            return keys.ToDictionary(k => k, k => GetString(k), StringComparer.Ordinal);
        }

        private bool TryGet(string lang, string key, out string value)
        {
            value = "";
            if (_languages.TryGetValue(lang, out var dict) && dict.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v))
            {
                value = v;
                return true;
            }
            return false;
        }

        private void LoadEmbeddedLanguages()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string marker = ".Localization.";

            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                int idx = resourceName.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0 || !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                string lang = resourceName.Substring(idx + marker.Length, resourceName.Length - idx - marker.Length - ".json".Length);
                try
                {
                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) continue;
                    var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                    if (dict != null)
                    {
                        _languages[lang.ToLowerInvariant()] = new Dictionary<string, string>(dict, StringComparer.Ordinal);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn($"Sprachdatei '{resourceName}' konnte nicht geladen werden", ex);
                }
            }
        }
    }
}
