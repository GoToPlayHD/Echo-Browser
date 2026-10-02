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
    /// <summary>Tabs: Anlegen, Auswählen, Schließen, Drag & Drop sowie die WebView-Ereignisse pro Tab.</summary>
    public partial class MainWindow
    {
        #region Multi-Tab Management & Drag-Drop Reordering

        public void AddNewTab(string? targetUrl = null, bool activateTab = true)
        {
            if (_webViewEnvironment == null) return;

            string initialUrl = string.IsNullOrWhiteSpace(targetUrl) ? StartPageService.StartPageUrl : targetUrl;
            var tab = new BrowserTab
            {
                Title = (initialUrl == SettingsPageService.SettingsPageUrl) ? "Einstellungen" : "Neuer Tab",
                Url = initialUrl
            };

            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };

            tab.WebView = webView;
            WebViewContainer.Children.Add(webView);

            // Register WebView events
            AttachWebViewEvents(tab, webView, initialUrl);

            Tabs.Add(tab);
            if (activateTab)
            {
                SelectTab(tab);
            }
        }

        /// <summary>
        /// Initialisiert die WebView eines Tabs und hängt alle Ereignisse an.
        /// WebView2-Ereignisse feuern auf dem UI-Thread – ein Dispatcher.Invoke ist nicht nötig.
        /// </summary>
        private async void AttachWebViewEvents(BrowserTab tab, WebView2 webView, string initialUrl)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(_webViewEnvironment);

                var core = webView.CoreWebView2;
                if (core != null)
                {
                    await ConfigureCoreWebViewAsync(tab, core);
                    AttachAdBlocker(tab, core);

                    // Nachrichten der internen Seiten & des Web Store -> MainWindow.WebMessages.cs
                    core.WebMessageReceived += async (s, args) => await _webMessageRouter.HandleAsync(tab, core, args);

                    // Neue Fenster / Popups als Tab öffnen
                    core.NewWindowRequested += (s, args) =>
                    {
                        args.Handled = true;
                        if (!string.IsNullOrWhiteSpace(args.Uri))
                        {
                            AddNewTab(args.Uri, activateTab: !AppSettingsService.Instance.Settings.OpenNewTabInBackground);
                        }
                    };

                    // Downloads -> MainWindow.Downloads.cs
                    core.DownloadStarting += (s, args) => HandleDownloadStarting(core, args);

                    AttachPageStateEvents(tab, webView, core);
                }

                AttachNavigationEvents(tab, webView);
                NavigateInitially(tab, webView, initialUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing tab webview: {ex.Message}");
            }
        }

        private async Task ConfigureCoreWebViewAsync(BrowserTab tab, CoreWebView2 core)
        {
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsScriptEnabled = tab.JavaScriptEnabled;
            core.Settings.AreDevToolsEnabled = true;

            // Cosmetic element-hiding + Netzwerkfilter (nur wenn Shield aktiv)
            await SyncShieldStateAsync(tab);

            // Chrome Web Store Extension Helper Script
            try
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionService.GetWebStoreHelperScript());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Web-Store-Hilfsskript konnte nicht registriert werden: {ex.Message}");
            }

            // Allow extension downloads without prompt interruptions.
            // Nur für .crx – alle anderen Dateitypen (z.B. .exe/.msi) durchlaufen weiterhin die Chromium-Sicherheitsprüfung.
            core.SaveFileSecurityCheckStarting += (s, args) =>
            {
                string ext = (args.FileExtension ?? "").TrimStart('.');
                if (ext.Equals("crx", StringComparison.OrdinalIgnoreCase))
                {
                    args.CancelSave = false;
                    args.SuppressDefaultPolicy = true;
                }
            };

            try
            {
                string trackingLevel = AppSettingsService.Instance.Settings.TrackingPreventionLevel ?? "balanced";
                core.Profile.PreferredTrackingPreventionLevel = trackingLevel.ToLowerInvariant() switch
                {
                    "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                    "none" => CoreWebView2TrackingPreventionLevel.None,
                    _ => CoreWebView2TrackingPreventionLevel.Balanced
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Tracking-Prävention konnte nicht gesetzt werden: {ex.Message}");
            }
        }

        /// <summary>
        /// Network-level Ad and Tracker blocking.
        /// Der Filter selbst wird in SyncShieldStateAsync nur bei aktivem Shield registriert,
        /// sonst würde jede Anfrage (Bilder, Fonts, ...) über den UI-Thread laufen.
        /// </summary>
        private void AttachAdBlocker(BrowserTab tab, CoreWebView2 core)
        {
            core.WebResourceRequested += (s, args) =>
            {
                if (!AppSettingsService.Instance.Settings.IsAdBlockerEnabled) return;
                if (!tab.TrackingProtectionEnabled) return;

                try
                {
                    if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var reqUri)) return;
                    if (!reqUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
                        !reqUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)) return;

                    if (!AdBlockerService.Instance.IsBlocked(reqUri.Host) && !IsAdScriptPath(reqUri.AbsolutePath)) return;

                    if (_webViewEnvironment != null)
                    {
                        args.Response = _webViewEnvironment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "");
                    }
                    tab.BlockedTrackersCount++;
                    if (tab == ActiveTab)
                    {
                        ScheduleShieldBadgeUpdate();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Echo] Fehler im Shield-Netzwerkfilter: {ex.Message}");
                }
            };
        }

        private static bool IsAdScriptPath(string path) =>
            path.EndsWith("/ads.js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith("/pagead.js", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("/widget/ads.js", StringComparison.OrdinalIgnoreCase);

        /// <summary>Titel und Zurück/Vor-Status eines Tabs aktuell halten.</summary>
        private void AttachPageStateEvents(BrowserTab tab, WebView2 webView, CoreWebView2 core)
        {
            core.DocumentTitleChanged += (s, args) =>
            {
                if (tab.Url == StartPageService.StartPageUrl || tab.Url == SettingsPageService.SettingsPageUrl) return;

                tab.Title = core.DocumentTitle;
                RecordHistory(tab);
            };

            core.HistoryChanged += (s, args) =>
            {
                tab.CanGoBack = webView.CanGoBack;
                tab.CanGoForward = webView.CanGoForward;
                if (tab == ActiveTab)
                {
                    UpdateNavigationControls();
                }
            };
        }

        private void AttachNavigationEvents(BrowserTab tab, WebView2 webView)
        {
            webView.NavigationStarting += async (s, args) =>
            {
                // Check domain whitelist
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var navUri) && !string.IsNullOrEmpty(navUri.Host))
                {
                    tab.TrackingProtectionEnabled = !AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Contains(navUri.Host);
                }

                await SyncShieldStateAsync(tab);

                tab.BlockedTrackersCount = 0;
                tab.IsLoading = true;
                if (!args.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                {
                    tab.Url = args.Uri;
                }
                if (tab == ActiveTab)
                {
                    txtUrl.Text = IsStartPage(tab.Url) ? "" : tab.Url;
                    UpdateNavigationControls();
                    UpdateShieldBadge();
                    if (popupShield.IsOpen)
                    {
                        UpdateShieldUi();
                    }
                }
            };

            webView.NavigationCompleted += (s, args) =>
            {
                tab.IsLoading = false;
                if (tab == ActiveTab)
                {
                    UpdateNavigationControls();
                    CheckBookmarkStatus();
                }
                RecordHistory(tab);
            };

            webView.SourceChanged += (s, args) =>
            {
                string currentSrc = webView.Source.ToString();
                if (!currentSrc.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                {
                    tab.Url = currentSrc;
                }
                if (tab == ActiveTab)
                {
                    txtUrl.Text = IsStartPage(tab.Url) ? "" : tab.Url;
                    CheckBookmarkStatus();
                    UpdateShieldUi();
                }
            };
        }

        /// <summary>Verlaufseintrag anlegen – nicht im Inkognito-Modus und nicht für interne Seiten.</summary>
        private void RecordHistory(BrowserTab tab)
        {
            if (_isIncognito || string.IsNullOrWhiteSpace(tab.Url)) return;
            if (tab.Url.StartsWith("echo://", StringComparison.OrdinalIgnoreCase) ||
                tab.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase)) return;

            _historyService.AddEntry(tab.Title, tab.Url);
        }

        /// <summary>Erste Navigation: Startseite, Einstellungen oder die angeforderte URL.</summary>
        private void NavigateInitially(BrowserTab tab, WebView2 webView, string initialUrl)
        {
            if (initialUrl == SettingsPageService.SettingsPageUrl)
            {
                NavigateToSettingsPage(tab);
            }
            else if (initialUrl == StartPageService.StartPageUrl || initialUrl == "about:blank")
            {
                tab.Url = StartPageService.StartPageUrl;
                tab.Title = _isIncognito ? "Neuer Tab (Inkognito)" : "Neuer Tab";
                webView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
            }
            else
            {
                webView.CoreWebView2?.Navigate(initialUrl);
            }
        }

        public void SelectTab(BrowserTab tab)
        {
            if (tab == null) return;

            foreach (var t in Tabs)
            {
                bool isActive = (t == tab);
                t.IsActive = isActive;
                if (t.WebView != null)
                {
                    t.WebView.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            ActiveTab = tab;
        }

        public void CloseTab(BrowserTab tab)
        {
            if (tab == null) return;

            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            // Remove WebView control from container
            if (tab.WebView != null)
            {
                WebViewContainer.Children.Remove(tab.WebView);
            }

            // Dispose tab and webview resources explicitly
            tab.Dispose();
            Tabs.Remove(tab);

            // If we closed the active tab, select an adjacent one
            if (ActiveTab == tab)
            {
                if (Tabs.Count > 0)
                {
                    int newIndex = Math.Clamp(index, 0, Tabs.Count - 1);
                    SelectTab(Tabs[newIndex]);
                }
                else
                {
                    // No tabs left: Open custom startpage
                    AddNewTab(StartPageService.StartPageUrl);
                }
            }
        }

        private void OnActiveTabChanged()
        {
            if (ActiveTab == null) return;

            txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
            UpdateNavigationControls();
            CheckBookmarkStatus();
            UpdateShieldBadge();
            UpdateShieldUi();
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab(StartPageService.StartPageUrl);
        }

        private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BrowserTab tab)
            {
                CloseTab(tab);
            }
        }

        // Tab Drag-and-Drop Reordering with Live Feedback
        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BrowserTab tab)
            {
                // If close button was clicked, don't start dragging
                if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
                {
                    return;
                }
                _tabDragStartPoint = e.GetPosition(null);
                _draggedTab = tab;
                SelectTab(tab);
            }
        }

        private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedTab != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _tabDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe)
                    {
                        var data = new DataObject("EchoBrowserTab", _draggedTab);
                        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        _draggedTab = null;
                    }
                }
            }
        }

        private void Tab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedTab = null;
        }

        private void Tab_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBrowserTab"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                // Live reorder while dragging
                if (e.Data.GetData("EchoBrowserTab") is BrowserTab sourceTab &&
                    sender is FrameworkElement fe && fe.DataContext is BrowserTab targetTab)
                {
                    if (sourceTab != targetTab)
                    {
                        int oldIndex = Tabs.IndexOf(sourceTab);
                        int newIndex = Tabs.IndexOf(targetTab);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            Tabs.Move(oldIndex, newIndex);
                        }
                    }
                }
            }
        }

        private void Tab_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        #endregion
    }
}
