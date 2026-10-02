using System.Windows;

namespace EchoBrowser
{
    /// <summary>Browserverlauf-Popup. Inhalt und Logik: Views/Popups/HistoryPanel.</summary>
    public partial class MainWindow
    {
        private void InitializeHistoryPanel()
        {
            historyPanel.Initialize(_historyService, _isIncognito);
            historyPanel.NavigateRequested += url =>
            {
                popupHistory.IsOpen = false;
                NavigateToInput(url);
            };
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            historyPanel.Prepare();
            popupHistory.IsOpen = true;
            historyPanel.FocusSearch();
        }

        /// <summary>Verlaufsliste aktualisieren, z.B. nachdem der Verlauf über die Einstellungen gelöscht wurde.</summary>
        public void UpdateFilteredHistory() => historyPanel.Refresh();
    }
}
