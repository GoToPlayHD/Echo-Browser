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
