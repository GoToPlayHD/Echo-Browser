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
    /// <summary>Navigation und Omnibox (Adressleiste, Zurück/Vor/Neu laden/Home).</summary>
    public partial class MainWindow
    {
        #region Navigation & Omnibox

        private void UpdateNavigationControls()
        {
            if (ActiveTab == null) return;

            btnBack.IsEnabled = ActiveTab.CanGoBack;
            btnForward.IsEnabled = ActiveTab.CanGoForward;

            if (ActiveTab.IsLoading)
            {
                pathReload.Visibility = Visibility.Collapsed;
                pathStop.Visibility = Visibility.Visible;
                btnReload.ToolTip = Tr.Get("Nav_StopLoading");
                progressLoading.Visibility = Visibility.Visible;
            }
            else
            {
                pathReload.Visibility = Visibility.Visible;
                pathStop.Visibility = Visibility.Collapsed;
                btnReload.ToolTip = Tr.Get("Nav_Reload");
                progressLoading.Visibility = Visibility.Collapsed;
            }
        }

        public static bool IsStartPage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            return InternalPages.Is(url, InternalPages.Start) ||
                   InternalPages.Is(url, InternalPages.NewTab) ||
                   url.Equals("about:blank", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Eingabe aus Adressleiste oder Startseite: Adresse öffnen oder mit der gewählten Suchmaschine suchen.</summary>
        private void NavigateToInput(string input, BrowserTab? tab = null)
        {
            tab ??= ActiveTab;
            var core = tab?.WebView?.CoreWebView2;
            if (string.IsNullOrWhiteSpace(input) || tab == null || core == null) return;

            string target = input.Trim();

            // Startseite
            if (IsStartPage(target))
            {
                tab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
                core.Navigate(StartPageService.StartPageUrl);
                return;
            }

            // Einstellungsseite (auch unter den von Chrome/Edge/Firefox gewohnten Adressen)
            if (InternalPages.Is(target, InternalPages.Settings) ||
                target.Equals("about:settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:preferences", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("chrome://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("edge://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToSettingsPage(tab);
                return;
            }

            // Adresse öffnen oder mit der eingestellten Suchmaschine suchen (Erkennung: UrlHelper.ToNavigableUrl)
            target = UrlHelper.ToNavigableUrl(target) ?? AppSettingsService.Instance.GetSearchUrl(target);

            try
            {
                core.Navigate(target);
            }
            catch (Exception ex)
            {
                Log.Warn($"Navigation zu '{target}' fehlgeschlagen", ex);
            }
        }

        // Enter, Esc und Pfeiltasten der Adressleiste: TxtUrl_PreviewKeyDown in MainWindow.Omnibox.cs

        private void TxtUrl_GotFocus(object sender, RoutedEventArgs e)
        {
            _typedAddressText = txtUrl.Text;
            UpdateAddressDisplay();
            txtUrl.SelectAll();
        }

        /// <summary>Erster Klick in die Adressleiste markiert alles (wie in Chrome), weitere Klicks setzen den Cursor.</summary>
        private void TxtUrl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!txtUrl.IsKeyboardFocusWithin)
            {
                txtUrl.Focus();
                e.Handled = true;
            }
        }

        private void TxtUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            CloseOmniboxPopup();
            if (ActiveTab != null && string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                _isEditingAddress = false;
                ShowAddress(ActiveTab);
            }
            UpdateAddressDisplay();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoBack)
            {
                ActiveTab.WebView.GoBack();
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoForward)
            {
                ActiveTab.WebView.GoForward();
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView == null) return;

            if (ActiveTab.IsLoading)
            {
                ActiveTab.WebView.Stop();
            }
            else if (InternalPages.Is(ActiveTab.WebView.CoreWebView2?.Source, InternalPages.Crashed))
            {
                // Auf der Absturzseite heißt "Neu laden": die ursprüngliche Seite erneut öffnen
                NavigateToInput(ActiveTab.Url);
            }
            else
            {
                ActiveTab.WebView.Reload();
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            NavigateToInput(StartPageService.StartPageUrl);
        }

        #endregion
    }
}
