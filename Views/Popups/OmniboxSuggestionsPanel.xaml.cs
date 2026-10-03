using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>
    /// Vorschlagsliste unter der Adressleiste. Die Auswahl steuert das Hauptfenster per Pfeiltasten;
    /// ein Klick wählt direkt (schon beim Drücken, bevor die Adressleiste den Fokus verliert).
    /// </summary>
    public partial class OmniboxSuggestionsPanel : UserControl
    {
        /// <summary>Vorschlag per Maus gewählt.</summary>
        public event Action<OmniboxSuggestion>? SuggestionChosen;

        public OmniboxSuggestionsPanel()
        {
            InitializeComponent();
        }

        public int Count => list.Items.Count;

        public OmniboxSuggestion? Selected => list.SelectedItem as OmniboxSuggestion;

        public IReadOnlyList<OmniboxSuggestion> Items
        {
            get
            {
                var items = new List<OmniboxSuggestion>();
                foreach (var item in list.Items) items.Add((OmniboxSuggestion)item);
                return items;
            }
        }

        public void SetItems(IReadOnlyList<OmniboxSuggestion> suggestions, int selectedIndex = 0)
        {
            list.ItemsSource = suggestions;
            list.SelectedIndex = suggestions.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, suggestions.Count - 1);
        }

        /// <summary>Auswahl um einen Eintrag verschieben (am Ende wieder oben).</summary>
        public OmniboxSuggestion? MoveSelection(int delta)
        {
            if (list.Items.Count == 0) return null;
            int index = (list.SelectedIndex + delta + list.Items.Count) % list.Items.Count;
            list.SelectedIndex = index;
            return Selected;
        }

        private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ItemAt(e.OriginalSource) is { } suggestion)
            {
                SuggestionChosen?.Invoke(suggestion);
                e.Handled = true;
            }
        }

        /// <summary>Wie in Chrome: die Maus markiert den Eintrag unter dem Zeiger.</summary>
        private void List_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (ItemAt(e.OriginalSource) is { } suggestion && !ReferenceEquals(list.SelectedItem, suggestion))
            {
                list.SelectedItem = suggestion;
            }
        }

        private static OmniboxSuggestion? ItemAt(object source)
        {
            var element = source as DependencyObject;
            while (element != null && element is not ListBoxItem)
            {
                element = element is Visual or System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(element)
                    : LogicalTreeHelper.GetParent(element);
            }
            return (element as ListBoxItem)?.DataContext as OmniboxSuggestion;
        }
    }
}
