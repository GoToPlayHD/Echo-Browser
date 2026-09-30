using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EchoBrowser.Models
{
    public class SidebarFavorite : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _url = string.Empty;
        private string _iconKey = "globe";
        private string _iconPathData = string.Empty;
        private string _iconColor = "#C4C7CC";

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayInitial));
                }
            }
        }

        public string Url
        {
            get => _url;
            set
            {
                if (_url != value)
                {
                    _url = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IconKey
        {
            get => _iconKey;
            set
            {
                if (_iconKey != value)
                {
                    _iconKey = value;
                    OnPropertyChanged();
                    UpdateIconData();
                }
            }
        }

        public string IconPathData
        {
            get => _iconPathData;
            set
            {
                if (_iconPathData != value)
                {
                    _iconPathData = value;
                    OnPropertyChanged();
                }
            }
        }

        public string IconColor
        {
            get => _iconColor;
            set
            {
                if (_iconColor != value)
                {
                    _iconColor = value;
                    OnPropertyChanged();
                }
            }
        }

        [JsonIgnore]
        public string DisplayInitial => string.IsNullOrWhiteSpace(Title)
            ? "?"
            : Title.Trim().Substring(0, 1).ToUpperInvariant();

        public SidebarFavorite()
        {
            UpdateIconData();
        }

        public SidebarFavorite(string title, string url, string iconKey = "globe", string iconColor = "#C4C7CC")
        {
            Title = title;
            Url = url;
            IconKey = iconKey;
            IconColor = iconColor;
            UpdateIconData();
        }

        public void UpdateIconData()
        {
            IconPathData = GetSvgPathForIconKey(IconKey);
        }

        public static string GetSvgPathForIconKey(string key)
        {
            return key?.ToLowerInvariant() switch
            {
                "google" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm5.46 8.18H12v2.73h3.14c-.28 1.48-1.55 2.59-3.14 2.59-1.89 0-3.42-1.54-3.42-3.42s1.53-3.42 3.42-3.42c.86 0 1.63.32 2.23.85l1.96-1.96C17.06 6.8 14.7 6 12 6 8.69 6 6 8.69 6 12s2.69 6 6 6c3.47 0 5.76-2.44 5.76-5.87 0-.41-.04-.8-.11-1.13h-.19z",
                "youtube" => "M21.58 7.19a2.76 2.76 0 0 0-1.94-1.96C17.93 4.75 12 4.75 12 4.75s-5.93 0-7.64.48a2.76 2.76 0 0 0-1.94 1.96C1.94 8.9 1.94 12 1.94 12s0 3.1.48 4.81a2.76 2.76 0 0 0 1.94 1.96c1.71.48 7.64.48 7.64.48s5.93 0 7.64-.48a2.76 2.76 0 0 0 1.94-1.96c.48-1.71.48-4.81.48-4.81s0-3.1-.48-4.81zM10 15V9l5.2 3L10 15z",
                "github" => "M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.53 1.032 1.53 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z",
                "chatgpt" => "M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2zm1 14.93V17a1 1 0 0 1-2 0v-.07A6 6 0 0 1 7.07 13H7a1 1 0 0 1 0-2h.07A6 6 0 0 1 11 7.07V7a1 1 0 0 1 2 0v.07A6 6 0 0 1 16.93 11H17a1 1 0 0 1 0 2h-.07A6 6 0 0 1 13 16.93z",
                "reddit" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm6.2 11.23c.04.26.06.52.06.77 0 2.46-2.86 4.45-6.26 4.45s-6.26-1.99-6.26-4.45c0-.25.02-.51.06-.77A1.737 1.737 0 0 1 5 11.5c0-.96.78-1.74 1.74-1.74.52 0 .98.23 1.3.6 1.07-.73 2.52-1.2 4.13-1.26l.84-3.95 2.75.59c.1.58.61 1.02 1.22 1.02.69 0 1.25-.56 1.25-1.25S16.67 4.26 15.98 4.26c-.46 0-.86.25-1.07.62l-3.08-.66a.434.434 0 0 0-.51.34l-.94 4.44c-1.65.05-3.13.52-4.22 1.27.32-.38.79-.62 1.32-.62.96 0 1.74.78 1.74 1.74 0 .5-.21.95-.55 1.26z",
                "wikipedia" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 14.5h-2L9.2 9H7V7.5h4.5V9h-1.3l1.3 5.3 1.7-6.8H12V7.5h3.5V9h-1.2l-1.3 7.5z",
                "twitter" => "M18.244 2.25h3.308l-7.227 8.26 8.502 11.24H16.17l-5.214-6.817L4.99 21.75H1.68l7.73-8.835L1.254 2.25H8.08l4.713 6.231zm-1.161 17.52h1.833L7.084 4.126H5.117z",
                "duckduckgo" => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-13h2v6h-2zm0 8h2v2h-2z",
                _ => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z"
            };
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
