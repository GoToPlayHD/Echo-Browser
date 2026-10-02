using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>Browserverlauf-Popup.</summary>
    public partial class MainWindow
    {
        #region Browser History Operations

        public void UpdateFilteredHistory(string filter = "")
        {
            FilteredHistory.Clear();
            string query = filter?.Trim() ?? "";

            var source = _historyService.Entries;
            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(query) ||
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredHistory.Add(item);
                }
            }

            txtEmptyHistory.Visibility = FilteredHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            bannerIncognitoHistory.Visibility = _isIncognito ? Visibility.Visible : Visibility.Collapsed;
            txtSearchHistory.Text = "";
            UpdateFilteredHistory();
            popupHistory.IsOpen = true;
            txtSearchHistory.Focus();
        }

        private void TxtSearchHistory_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilteredHistory(txtSearchHistory.Text);
        }

        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                popupHistory.IsOpen = false;
                NavigateToInput(url);
            }
        }

        private void BtnDeleteHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HistoryItem item)
            {
                _historyService.RemoveEntry(item);
                UpdateFilteredHistory(txtSearchHistory.Text);
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Möchtest du den gesamten Browser-Verlauf wirklich löschen?",
                "Verlauf leeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _historyService.ClearHistory();
                UpdateFilteredHistory();
            }
        }

        #endregion
    }
}
