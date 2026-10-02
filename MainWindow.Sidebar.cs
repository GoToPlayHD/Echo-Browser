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

        private void InitializeAddFavoritePanel()
        {
            addFavoritePanel.FavoriteCreated += (title, url, iconKey, color) =>
            {
                _sidebarService.AddFavorite(title, url, iconKey, color);
                popupAddFavorite.IsOpen = false;
            };
            addFavoritePanel.Cancelled += () => popupAddFavorite.IsOpen = false;
        }

        private void BtnAddSidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            popupAddFavorite.IsOpen = true;
            addFavoritePanel.Prepare();
        }

        #endregion
    }
}
