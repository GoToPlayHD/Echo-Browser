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
    /// <summary>Einstellungsseite öffnen, Sitzung speichern/wiederherstellen, Zoom & Tracking-Prävention, Schließen-Logik.</summary>
    public partial class MainWindow
    {
        #region Settings & Session Management

        /// <summary>Startseite bzw. Einstellungsseite eines Tabs neu erzeugen (z.B. nach Sprachwechsel).</summary>
        private void RefreshInternalPage(BrowserTab tab)
        {
            if (tab.WebView == null) return;

            if (tab.Url == SettingsPageService.SettingsPageUrl)
            {
                NavigateToSettingsPage(tab);
            }
            else if (tab.Url == StartPageService.StartPageUrl)
            {
                tab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
                tab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
            }
        }

        public void NavigateToSettingsPage(BrowserTab? tab)
        {
            if (tab?.WebView == null) return;
            tab.Url = SettingsPageService.SettingsPageUrl;
            tab.Title = Tr.Get("Tab_Settings");
            string webViewVer = _webViewEnvironment?.BrowserVersionString ?? "120.0";
            string html = SettingsPageService.GetSettingsPageHtml(AppSettingsService.Instance.Settings, webViewVer, "1.2");
            tab.WebView.NavigateToString(html);
            if (tab == ActiveTab)
            {
                txtUrl.Text = SettingsPageService.SettingsPageUrl;
                UpdateNavigationControls();
            }
        }

        public void OpenSettingsTab()
        {
            // If already open, switch to that tab
            foreach (var tab in Tabs)
            {
                if (tab.Url.Equals(SettingsPageService.SettingsPageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    SelectTab(tab);
                    return;
                }
            }

            // If current tab is clean start page, navigate within it
            if (ActiveTab != null && (IsStartPage(ActiveTab.Url) || string.IsNullOrWhiteSpace(ActiveTab.Url)))
            {
                NavigateToSettingsPage(ActiveTab);
            }
            else
            {
                AddNewTab(SettingsPageService.SettingsPageUrl);
            }
        }

        private void SaveCurrentSession()
        {
            if (_isIncognito) return;
            try
            {
                var urls = Tabs.Select(t => t.Url)
                               .Where(u => !string.IsNullOrWhiteSpace(u) && 
                                           u != StartPageService.StartPageUrl && 
                                           u != SettingsPageService.SettingsPageUrl &&
                                           !u.StartsWith("echo://", StringComparison.OrdinalIgnoreCase))
                               .ToList();
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string file = Path.Combine(appData, "EchoBrowser", "session.json");
                AtomicFile.WriteAllText(file, JsonSerializer.Serialize(urls));
            }
            catch { }
        }

        private void RestorePreviousSession()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string file = Path.Combine(appData, "EchoBrowser", "session.json");
                if (File.Exists(file))
                {
                    var urls = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file));
                    if (urls != null && urls.Count > 0)
                    {
                        foreach (var url in urls)
                        {
                            AddNewTab(url);
                        }
                        return;
                    }
                }
            }
            catch { }

            AddNewTab(StartPageService.StartPageUrl);
        }

        private void ApplyTrackingPrevention(string level)
        {
            var preventionLevel = level.ToLowerInvariant() switch
            {
                "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                "none" => CoreWebView2TrackingPreventionLevel.None,
                _ => CoreWebView2TrackingPreventionLevel.Balanced
            };

            foreach (var tab in Tabs)
            {
                try
                {
                    if (tab.WebView?.CoreWebView2?.Profile != null)
                    {
                        tab.WebView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = preventionLevel;
                    }
                }
                catch { }
            }
        }

        private void ApplyDefaultZoom(int percent)
        {
            double zoomFactor = Math.Clamp(percent / 100.0, 0.5, 3.0);
            foreach (var tab in Tabs)
            {
                try
                {
                    if (tab.WebView?.CoreWebView2 != null && 
                        tab.Url != StartPageService.StartPageUrl && 
                        tab.Url != SettingsPageService.SettingsPageUrl)
                    {
                        tab.WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                        tab.WebView.ZoomFactor = zoomFactor;
                    }
                }
                catch { }
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+, -> Settings
            if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OpenSettingsTab();
                e.Handled = true;
            }
            // Ctrl+T -> New Tab
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                AddNewTab(StartPageService.StartPageUrl);
                e.Handled = true;
            }
            // Ctrl+W -> Close Tab
            else if (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ActiveTab != null) CloseTab(ActiveTab);
                e.Handled = true;
            }
            // Ctrl+Shift+N -> New Incognito Window
            else if (e.Key == Key.N && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OpenNewIncognitoWindow();
                e.Handled = true;
            }
            // Ctrl+N -> New Window
            else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var win = new MainWindow();
                win.Show();
                e.Handled = true;
            }
            // Ctrl+Shift+B -> Toggle Bookmarks Bar
            else if (e.Key == Key.B && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                MenuToggleBookmarksBar_Click(sender, e);
                e.Handled = true;
            }
            // Ctrl+H -> History
            else if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (popupHistory.IsOpen)
                {
                    popupHistory.IsOpen = false;
                }
                else
                {
                    MenuHistory_Click(sender, e);
                }
                e.Handled = true;
            }
            // Ctrl+J -> Downloads
            else if (e.Key == Key.J && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                popupDownloads.IsOpen = !popupDownloads.IsOpen;
                e.Handled = true;
            }
            // Ctrl+L or Alt+D -> Focus Omnibox
            else if ((e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                     (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                txtUrl.Focus();
                txtUrl.SelectAll();
                e.Handled = true;
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isIncognito && Tabs.Count > 1 && AppSettingsService.Instance.Settings.WarnOnClosingMultipleTabs)
            {
                string title = LocalizationService.Instance.GetString("Dialog_CloseBrowserTitle", "Echo-Browser beenden");
                string msg = string.Format(LocalizationService.Instance.GetString("Dialog_CloseMultipleTabsMessage", "Möchtest du wirklich alle {0} geöffneten Tabs schließen?"), Tabs.Count);
                string confirm = LocalizationService.Instance.GetString("Dialog_CloseAllTabs", "Alle Tabs schließen");
                string cancel = LocalizationService.Instance.GetString("Dialog_Cancel", "Abbrechen");

                bool confirmed = ThemedDialogWindow.ShowConfirm(this, title, msg, confirm, cancel);
                if (!confirmed)
                {
                    e.Cancel = true;
                    return;
                }
            }

            SaveCurrentSession();

            if (!_isIncognito && AppSettingsService.Instance.Settings.ClearDataOnExit)
            {
                try
                {
                    _historyService.ClearHistory();
                }
                catch { }
            }

            if (_isIncognito && !string.IsNullOrEmpty(_incognitoFolder))
            {
                try
                {
                    if (Directory.Exists(_incognitoFolder))
                    {
                        Directory.Delete(_incognitoFolder, true);
                    }
                }
                catch { }
            }
        }

        #endregion
    }
}
