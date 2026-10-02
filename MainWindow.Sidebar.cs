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
    /// <summary>Seitenleiste mit eigenen Favoriten.</summary>
    public partial class MainWindow
    {
        #region Favoriten SideBar & Custom Favorites

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = borderSidebar.Visibility == Visibility.Visible;
            borderSidebar.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            menuChkSidebar.IsChecked = !isVisible;

            // Persist setting
            AppSettingsService.Instance.Settings.IsSidebarVisible = !isVisible;
            AppSettingsService.Instance.Save();
        }

        private void SidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is SidebarFavorite fav)
            {
                NavigateToInput(fav.Url);
            }
        }

        private void MenuSidebarOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SidebarFavorite fav)
            {
                AddNewTab(fav.Url);
            }
        }

        private void MenuSidebarDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SidebarFavorite fav)
            {
                _sidebarService.RemoveFavorite(fav);
            }
        }

        private void BtnAddSidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            txtAddFavTitle.Text = "";
            txtAddFavUrl.Text = "https://";
            _selectedFavIconKey = "globe";
            _selectedFavColor = "#C4C7CC";
            popupAddFavorite.IsOpen = true;
            txtAddFavTitle.Focus();
        }

        private void SelectFavIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string iconKey)
            {
                _selectedFavIconKey = iconKey;
                _selectedFavColor = iconKey switch
                {
                    "youtube" => "#FF4444",
                    "reddit" => "#FF6633",
                    "chatgpt" => "#34D399",
                    "google" => "#4285F4",
                    _ => "#C4C7CC"
                };
            }
        }

        private void BtnSaveAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            string title = txtAddFavTitle.Text.Trim();
            string url = txtAddFavUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                title = url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
            }

            if (string.IsNullOrWhiteSpace(url) || url == "https://" || url == "http://")
            {
                MessageBox.Show("Bitte geben Sie eine gültige Web-Adresse (URL) ein.", "Favorit hinzufügen", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            _sidebarService.AddFavorite(title, url, _selectedFavIconKey, _selectedFavColor);
            popupAddFavorite.IsOpen = false;
        }

        private void BtnCancelAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            popupAddFavorite.IsOpen = false;
        }

        #endregion
    }
}
