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
            return url.Equals(StartPageService.StartPageUrl, StringComparison.OrdinalIgnoreCase) ||
                   url.Equals("echo://newtab", StringComparison.OrdinalIgnoreCase) ||
                   url.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
        }

        private void NavigateToInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || ActiveTab?.WebView == null) return;

            string target = input.Trim();

            // Check if navigating to custom startpage
            if (target.Equals("echo://start", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("echo://newtab", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                ActiveTab.Url = StartPageService.StartPageUrl;
                ActiveTab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
                ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                txtUrl.Text = "";
                return;
            }

            // Check if navigating to custom settings page
            if (target.Equals("echo://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:preferences", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("chrome://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("edge://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToSettingsPage(ActiveTab);
                return;
            }

            // Direct URL vs Search detection
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                // Direct complete URL
            }
            else if (target.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                     target.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                target = "http://" + target;
            }
            else if (Regex.IsMatch(target, @"^[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(/.*)?$") && !target.Contains(' '))
            {
                // Domain format
                target = "https://" + target;
            }
            else
            {
                // Query configured search engine from AppSettingsService!
                target = AppSettingsService.Instance.GetSearchUrl(target);
            }

            try
            {
                ActiveTab.WebView.CoreWebView2?.Navigate(target);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToInput(txtUrl.Text);
                WebViewContainer.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                if (ActiveTab != null)
                {
                    txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
                }
                WebViewContainer.Focus();
            }
        }

        private void TxtUrl_GotFocus(object sender, RoutedEventArgs e)
        {
            txtUrl.SelectAll();
        }

        private void TxtUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ActiveTab != null && string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
            }
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
            else
            {
                if (IsStartPage(ActiveTab.Url))
                {
                    ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                    txtUrl.Text = "";
                }
                else
                {
                    ActiveTab.WebView.Reload();
                }
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView == null) return;

            if (IsStartPage(ActiveTab.Url))
            {
                ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                txtUrl.Text = "";
            }
            else
            {
                NavigateToInput(StartPageService.StartPageUrl);
            }
        }

        #endregion
    }
}
