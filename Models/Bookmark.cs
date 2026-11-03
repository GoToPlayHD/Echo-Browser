using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace EchoBrowser.Models
{
    public class Bookmark : INotifyPropertyChanged
    {
        private string _title = string.Empty;
        private string _url = string.Empty;
        private bool _isGroup = false;
        private string _groupColor = "#8A8F99";
        private ObservableCollection<Bookmark> _children = new();

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

        public bool IsGroup
        {
            get => _isGroup;
            set
            {
                if (_isGroup != value)
                {
                    _isGroup = value;
                    OnPropertyChanged();
                }
            }
        }

        public string GroupColor
        {
            get => _groupColor;
            set
            {
                if (_groupColor != value)
                {
                    _groupColor = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<Bookmark> Children
        {
            get => _children;
            set
            {
                if (_children != value)
                {
                    if (_children != null) _children.CollectionChanged -= Children_CollectionChanged;
                    _children = value ?? new();
                    if (_children != null) _children.CollectionChanged += Children_CollectionChanged;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ChildCount));
                }
            }
        }

        public DateTime DateAdded { get; set; } = DateTime.Now;

        private string? _iconKey;
        private string? _iconColor;

        /// <summary>
        /// Eigenes Symbol in der Seitenleiste ("youtube", "github" …). null = Website-Symbol (Favicon) wie in der
        /// Lesezeichenleiste. Seitenleiste und Lesezeichenleiste zeigen dieselben Lesezeichen.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IconKey
        {
            get => _iconKey;
            set
            {
                if (_iconKey != value)
                {
                    _iconKey = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasCustomIcon));
                    OnPropertyChanged(nameof(IconPathData));
                }
            }
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? IconColor
        {
            get => _iconColor;
            set
            {
                if (_iconColor != value)
                {
                    _iconColor = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayIconColor));
                }
            }
        }

        [JsonIgnore]
        public bool HasCustomIcon => !string.IsNullOrEmpty(_iconKey);

        [JsonIgnore]
        public string IconPathData => SidebarFavorite.GetSvgPathForIconKey(_iconKey ?? "globe");

        /// <summary>Symbolfarbe, die auf dem aktuellen Hintergrund lesbar ist.</summary>
        [JsonIgnore]
        public string DisplayIconColor => EchoBrowser.Services.ThemeManager.Instance.ReadableOnBackground(_iconColor ?? "#C4C7CC");

        /// <summary>Nach einem Theme-Wechsel neu berechnen lassen.</summary>
        public void RefreshThemeColors() => OnPropertyChanged(nameof(DisplayIconColor));

        [JsonIgnore]
        public string DisplayInitial => string.IsNullOrWhiteSpace(Title) 
            ? "?" 
            : Title.Trim().Substring(0, 1).ToUpperInvariant();

        [JsonIgnore]
        public int ChildCount => Children?.Count ?? 0;

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(ChildCount));
        }

        public Bookmark()
        {
            _children.CollectionChanged += Children_CollectionChanged;
        }

        public Bookmark(string title, string url) : this()
        {
            Title = string.IsNullOrWhiteSpace(title) ? url : title;
            Url = url;
            DateAdded = DateTime.Now;
            IsGroup = false;
        }

        public static Bookmark CreateGroup(string groupName, string color = "#8A8F99")
        {
            return new Bookmark
            {
                Title = groupName,
                IsGroup = true,
                GroupColor = color,
                DateAdded = DateTime.Now
            };
        }
    }
}
