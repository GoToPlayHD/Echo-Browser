using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace EchoBrowser.Views.Popups
{
    public enum PaletteItemKind { Command, Tab, Bookmark, History, Url, Search }

    /// <summary>Ein Treffer der Befehlspalette.</summary>
    public sealed record PaletteItem(PaletteItemKind Kind, string Title, string Url = "", string Chip = "", string InlineDetail = "")
    {
        public string? CommandId { get; init; }
        public string? TabId { get; init; }
        public Visibility ChipVisibility => string.IsNullOrEmpty(Chip) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Befehlspalette (Strg+K). Suche und Ausführung steuert MainWindow.Commands.cs.</summary>
    public partial class CommandPalette : UserControl
    {
        public event Action<string>? QueryChanged;
        public event Action<PaletteItem>? ItemChosen;
        public event Action? CloseRequested;

        public CommandPalette()
        {
            InitializeComponent();
        }

        public TextBox InputBox => txtQuery;

        public string Query => txtQuery.Text;

        public void Reset()
        {
            txtQuery.Text = "";
            _lastMousePosition = null;
        }

        public void SetItems(IReadOnlyList<PaletteItem> items)
        {
            list.ItemsSource = items;
            list.SelectedIndex = items.Count > 0 ? 0 : -1;
            txtEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtQuery_TextChanged(object sender, TextChangedEventArgs e) => QueryChanged?.Invoke(txtQuery.Text);

        private void TxtQuery_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Down:
                case Key.Up:
                    if (list.Items.Count > 0)
                    {
                        int delta = e.Key == Key.Down ? 1 : -1;
                        list.SelectedIndex = (list.SelectedIndex + delta + list.Items.Count) % list.Items.Count;
                        list.ScrollIntoView(list.SelectedItem);
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    if (list.SelectedItem is PaletteItem item) ItemChosen?.Invoke(item);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    CloseRequested?.Invoke();
                    e.Handled = true;
                    break;
            }
        }

        private void List_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (ItemAt(e.OriginalSource) is PaletteItem item)
            {
                ItemChosen?.Invoke(item);
                e.Handled = true;
            }
        }

        private Point? _lastMousePosition;

        /// <summary>
        /// Nur echte Mausbewegungen wählen aus. WPF meldet auch eine Bewegung, wenn die Palette unter dem
        /// stillstehenden Zeiger aufgeht – sonst wäre gleich die Zeile unter dem Zeiger statt der ersten gewählt.
        /// </summary>
        private void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            Point position = e.GetPosition(this);
            bool moved = _lastMousePosition is Point last && (Math.Abs(last.X - position.X) > 1 || Math.Abs(last.Y - position.Y) > 1);
            _lastMousePosition = position;
            if (!moved) return;

            if (ItemAt(e.OriginalSource) is PaletteItem item && !ReferenceEquals(list.SelectedItem, item))
            {
                list.SelectedItem = item;
            }
        }

        private static PaletteItem? ItemAt(object source)
        {
            var element = source as DependencyObject;
            while (element != null && element is not ListBoxItem)
            {
                element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
            }
            return (element as ListBoxItem)?.DataContext as PaletteItem;
        }
    }
}
