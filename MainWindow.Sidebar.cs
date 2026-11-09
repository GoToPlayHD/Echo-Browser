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
            // Gilt für alle Fenster (MainWindow.SettingsSync.cs)
            bool isVisible = borderSidebar.Visibility == Visibility.Visible;
            AppSettingsService.Instance.Settings.IsSidebarVisible = !isVisible;
            AppSettingsService.Instance.Changed(nameof(AppSettings.IsSidebarVisible));
        }

        private void SidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Bookmark bookmark)
            {
                NavigateToInput(bookmark.Url);
            }
        }

        private void SidebarGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Bookmark { IsGroup: true } group)
            {
                ShowGroupPopup(group, fe, System.Windows.Controls.Primitives.PlacementMode.Right);
            }
        }

        private void MenuSidebarOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bookmark)
            {
                AddNewTab(bookmark.Url);
            }
        }

        private void MenuSidebarDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bookmark)
            {
                _bookmarkService.RemoveBookmark(bookmark);
            }
        }

        private void InitializeAddFavoritePanel()
        {
            addFavoritePanel.FavoriteCreated += (title, url, iconKey, color) =>
            {
                // Gemeinsame Liste: erscheint auch in der Lesezeichenleiste. Ohne gewähltes Symbol das Website-Symbol.
                bool customIcon = !string.IsNullOrEmpty(iconKey) && iconKey != "globe";
                _bookmarkService.AddBookmark(title, url, iconKey: customIcon ? iconKey : null, iconColor: customIcon ? color : null);
                popupAddFavorite.IsOpen = false;
            };
            addFavoritePanel.Cancelled += () => popupAddFavorite.IsOpen = false;
        }

        private void BtnAddSidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            popupAddFavorite.IsOpen = true;
            addFavoritePanel.Prepare();
            FocusWpfInput(addFavoritePanel.InitialFocusTarget);
        }

        #endregion
    }
}
