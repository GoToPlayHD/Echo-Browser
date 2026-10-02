using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Inhalt des Verlauf-Popups: Suche, Liste, einzelne Einträge löschen, Verlauf leeren.</summary>
    public partial class HistoryPanel : UserControl
    {
        private HistoryService? _historyService;

        /// <summary>Ein Verlaufseintrag wurde angeklickt und soll geöffnet werden.</summary>
        public event Action<string>? NavigateRequested;

        public ObservableCollection<HistoryItem> FilteredEntries { get; } = new();

        public HistoryPanel()
        {
            InitializeComponent();
        }

        public void Initialize(HistoryService historyService, bool isIncognito)
        {
            _historyService = historyService;
            bannerIncognitoHistory.Visibility = isIncognito ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Vor dem Öffnen: Suche leeren und Liste neu aufbauen.</summary>
        public void Prepare()
        {
            txtSearchHistory.Text = "";
            Refresh();
        }

        public void FocusSearch() => txtSearchHistory.Focus();

        /// <summary>Liste anhand des aktuellen Suchtexts neu aufbauen.</summary>
        public void Refresh()
        {
            FilteredEntries.Clear();
            if (_historyService == null) return;

            string query = txtSearchHistory.Text?.Trim() ?? "";
            foreach (var item in _historyService.Entries)
            {
                if (query.Length == 0 ||
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredEntries.Add(item);
                }
            }

            txtEmptyHistory.Visibility = FilteredEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void TxtSearchHistory_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                NavigateRequested?.Invoke(url);
            }
        }

        private void BtnDeleteHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HistoryItem item)
            {
                _historyService?.RemoveEntry(item);
                Refresh();
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            bool confirmed = ThemedDialogWindow.ShowConfirm(
                Window.GetWindow(this),
                Tr.Get("History_Clear"),
                Tr.Get("History_ClearConfirm"),
                Tr.Get("History_Clear"),
                Tr.Get("Dialog_Cancel"));

            if (confirmed)
            {
                _historyService?.ClearHistory();
                Refresh();
            }
        }
    }
}
