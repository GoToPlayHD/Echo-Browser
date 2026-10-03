using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace EchoBrowser.Models
{
    /// <summary>Farben der Tab-Gruppen (wie in Chrome).</summary>
    public enum TabGroupColor { Grey, Blue, Red, Yellow, Green, Pink, Purple, Cyan, Orange }

    /// <summary>Eine Tab-Gruppe: Name, Farbe, eingeklappt. Die Tabs verweisen über <see cref="BrowserTab.Group"/> darauf.</summary>
    public sealed class TabGroup : INotifyPropertyChanged
    {
        private string _name = "";
        private TabGroupColor _color;
        private bool _isCollapsed;
        private int _tabCount;

        public string Id { get; init; } = Guid.NewGuid().ToString("N");

        public string Name
        {
            get => _name;
            set
            {
                value = value?.Trim() ?? "";
                if (_name == value) return;
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasName));
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        public bool HasName => _name.Length > 0;

        public TabGroupColor Color
        {
            get => _color;
            set
            {
                if (_color == value) return;
                _color = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ColorBrush));
            }
        }

        public Brush ColorBrush => TabGroupPalette.BrushFor(_color);

        public bool IsCollapsed
        {
            get => _isCollapsed;
            set
            {
                if (_isCollapsed == value) return;
                _isCollapsed = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        /// <summary>Anzahl der Tabs – eingeklappte Gruppen zeigen sie im Kopf an.</summary>
        public int TabCount
        {
            get => _tabCount;
            set
            {
                if (_tabCount == value) return;
                _tabCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HeaderText));
            }
        }

        /// <summary>Text im Gruppenkopf: Name, eingeklappt mit Anzahl ("Arbeit · 4"), ohne Namen nur die Anzahl.</summary>
        public string HeaderText =>
            _isCollapsed ? (HasName ? $"{_name} · {_tabCount}" : _tabCount.ToString()) : _name;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public static class TabGroupPalette
    {
        /// <summary>Helle Töne mit dunkler Schrift – gut lesbar in hellen und dunklen Designs.</summary>
        private static readonly Dictionary<TabGroupColor, string> Hex = new()
        {
            [TabGroupColor.Grey] = "#BDC1C6",
            [TabGroupColor.Blue] = "#8AB4F8",
            [TabGroupColor.Red] = "#F28B82",
            [TabGroupColor.Yellow] = "#FDD663",
            [TabGroupColor.Green] = "#81C995",
            [TabGroupColor.Pink] = "#FF8BCB",
            [TabGroupColor.Purple] = "#C58AF9",
            [TabGroupColor.Cyan] = "#78D9EC",
            [TabGroupColor.Orange] = "#FCAD70",
        };

        private static readonly Dictionary<TabGroupColor, Brush> Brushes = new();

        public static IReadOnlyList<TabGroupColor> All { get; } = Enum.GetValues<TabGroupColor>();

        public static Brush BrushFor(TabGroupColor color)
        {
            if (!Brushes.TryGetValue(color, out var brush))
            {
                brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex[color]));
                brush.Freeze();
                Brushes[color] = brush;
            }
            return brush;
        }

        /// <summary>Farbe für eine neue Gruppe: die erste, die noch keine Gruppe hat (wie Chrome).</summary>
        public static TabGroupColor NextColor(IEnumerable<TabGroupColor> used)
        {
            var taken = new HashSet<TabGroupColor>(used);
            foreach (var color in All)
            {
                if (color != TabGroupColor.Grey && !taken.Contains(color)) return color;
            }
            return TabGroupColor.Grey;
        }
    }
}
