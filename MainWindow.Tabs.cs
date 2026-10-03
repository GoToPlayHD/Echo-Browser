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

        /// <param name="deferLoad">
        /// Nur für Tabs im Hintergrund: noch keine WebView anlegen, die Seite lädt erst beim Aktivieren
        /// (schnellere Wiederherstellung der Sitzung).
        /// </param>
        public BrowserTab? AddNewTab(string? targetUrl = null, bool activateTab = true, string? title = null, bool deferLoad = false)
        {
            if (_webViewEnvironment == null) return null;

            string initialUrl = string.IsNullOrWhiteSpace(targetUrl) ? StartPageService.StartPageUrl : targetUrl;
            var settings = AppSettingsService.Instance.Settings;
            var tab = new BrowserTab
            {
                Title = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : Tr.Get(initialUrl == SettingsPageService.SettingsPageUrl ? "Tab_Settings" : "Tab_NewTab"),
                Url = initialUrl,
                JavaScriptEnabled = settings.EnableJavaScript,
                PopupsBlocked = settings.BlockPopups
            };
            tab.Favicon = InternalPages.IsInternalUrl(initialUrl) || initialUrl == "about:blank"
                ? AppIcon
                : _isIncognito ? null : FaviconCache.Instance.Get(initialUrl);

            // Adresse oder Titel geändert -> Sitzung (verzögert) sichern
            tab.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(BrowserTab.Url) or nameof(BrowserTab.Title) or nameof(BrowserTab.IsActive))
                {
                    ScheduleSessionSave();
                }
            };

            if (deferLoad && !activateTab)
            {
                tab.IsDiscarded = true;
            }
            else
            {
                CreateTabWebView(tab, initialUrl);
            }

            Tabs.Add(tab);
            if (activateTab)
            {
                SelectTab(tab);
            }
            return tab;
        }

        /// <summary>Legt das WebView2-Steuerelement eines Tabs an (auch zum Neuaufbau nach einem Browser-Absturz).</summary>
        private void CreateTabWebView(BrowserTab tab, string url)
        {
            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = tab.IsActive ? Visibility.Visible : Visibility.Collapsed
            };

            tab.WebView = webView;
            Grid.SetColumnSpan(webView, 3);
            WebViewContainer.Children.Add(webView);
            AttachSplitFocusEvents(tab, webView);

            AttachWebViewEvents(tab, webView, url);
        }

        /// <summary>
        /// Initialisiert die WebView eines Tabs und hängt alle Ereignisse an.
        /// WebView2-Ereignisse feuern auf dem UI-Thread – ein Dispatcher.Invoke ist nicht nötig.
        /// </summary>
        private async void AttachWebViewEvents(BrowserTab tab, WebView2 webView, string initialUrl)
        {
            try
            {
                // Nach einem Absturz des Browser-Prozesses liefert das eine neue Umgebung
                var env = _webViewEnvironment = await BrowserEnvironment.GetAsync();
                await webView.EnsureCoreWebView2Async(env, BrowserEnvironment.CreateControllerOptions(env, _isIncognito));

                var core = webView.CoreWebView2;
                if (core != null)
                {
                    // Interne Seiten (echo://start, echo://settings …) -> MainWindow.InternalPages.cs
                    AttachInternalPages(env, core);

                    await ConfigureCoreWebViewAsync(tab, webView, core);
                    AttachAdBlocker(tab, core);

                    // Nachrichten der internen Seiten & des Web Store -> MainWindow.WebMessages.cs
                    core.WebMessageReceived += async (s, args) => await _webMessageRouter.HandleAsync(tab, core, args);

                    // Neue Fenster / Popups als Tab öffnen – ungefragte Popups blockieren
                    core.NewWindowRequested += (s, args) => HandleNewWindowRequested(tab, args);

                    // Downloads -> MainWindow.Downloads.cs
                    core.DownloadStarting += (s, args) => HandleDownloadStarting(core, args);

                    // Abstürze des Renderers oder Browsers -> MainWindow.InternalPages.cs
                    core.ProcessFailed += (s, args) => HandleProcessFailed(tab, webView, args);

                    // Website-Symbol und Ton-Anzeige im Tab -> MainWindow.TabActions.cs
                    AttachTabIndicatorEvents(tab, core);

                    // Zoom pro Website & Vollbild-Videos -> MainWindow.Zoom.cs / MainWindow.Fullscreen.cs
                    AttachZoomEvents(tab, webView);
                    AttachFullscreenEvents(tab, core);

                    // Kamera, Mikrofon, Standort … -> MainWindow.Permissions.cs
                    AttachPermissionEvents(tab, core);

                    AttachPageStateEvents(tab, webView, core);

                    // Link-Kontextmenü "In geteilter Ansicht öffnen" -> MainWindow.SplitView.cs
                    AttachSplitLinkMenu(tab, core);
                }

                AttachNavigationEvents(tab, webView);
                NavigateInitially(tab, webView, initialUrl);
            }
            catch (Exception ex)
            {
                Log.Error($"WebView eines Tabs konnte nicht initialisiert werden ({initialUrl})", ex);
            }
        }

        /// <summary>
        /// Links mit target=_blank und window.open: als Tab öffnen. Popups, die eine Seite ohne Klick des Nutzers
        /// öffnen will, werden blockiert und in der Adressleiste angeboten (MainWindow.Popups.cs).
        /// </summary>
        private void HandleNewWindowRequested(BrowserTab tab, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;
            if (string.IsNullOrWhiteSpace(args.Uri)) return;

            if (!args.IsUserInitiated && tab.PopupsBlocked)
            {
                tab.BlockedPopupUrls.Add(args.Uri);
                if (tab == ActiveTab)
                {
                    UpdatePopupBlockedIndicator();
                }
                return;
            }

            AddNewTab(args.Uri, activateTab: !AppSettingsService.Instance.Settings.OpenNewTabInBackground);
        }

        private async Task ConfigureCoreWebViewAsync(BrowserTab tab, WebView2 webView, CoreWebView2 core)
        {
            var settings = AppSettingsService.Instance.Settings;

            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsScriptEnabled = tab.JavaScriptEnabled;
            core.Settings.AreDevToolsEnabled = true;

            // Zoom der ersten Seite; danach stellt ContentLoading den Zoom jeder Website her (MainWindow.Zoom.cs)
            double startZoom = ZoomService.Instance.Get(tab.Url) ?? ZoomService.DefaultFactor;
            if (!ZoomLevels.AreEqual(startZoom, 1.0))
            {
                SetZoom(webView, startZoom, remember: false);
            }

            // "Do Not Track" & Global Privacy Control -> MainWindow.Privacy.cs
            await AttachPrivacySignalsAsync(tab, core);

            // Cosmetic element-hiding + Netzwerkfilter (nur wenn Shield aktiv)
            await SyncShieldStateAsync(tab);

            // Chrome Web Store Extension Helper Script
            try
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionService.GetWebStoreHelperScript());
            }
            catch (Exception ex)
            {
                Log.Warn("Web-Store-Hilfsskript konnte nicht registriert werden", ex);
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

            ApplyAutofillSettings(core);
            ApplyColorScheme(core);

            try
            {
                string trackingLevel = settings.TrackingPreventionLevel ?? "balanced";
                core.Profile.PreferredTrackingPreventionLevel = trackingLevel.ToLowerInvariant() switch
                {
                    "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                    "none" => CoreWebView2TrackingPreventionLevel.None,
                    _ => CoreWebView2TrackingPreventionLevel.Balanced
                };
            }
            catch (Exception ex)
            {
                Log.Warn("Tracking-Prävention konnte nicht gesetzt werden", ex);
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
                    Log.Warn("Fehler im Shield-Netzwerkfilter", ex);
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
                // Interne Seiten behalten ihren übersetzten Tab-Titel ("Neuer Tab", "Einstellungen")
                if (InternalPages.IsInternalUrl(tab.Url)) return;

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
                var settings = AppSettingsService.Instance.Settings;
                bool isInternal = InternalPages.IsInternalUrl(args.Uri);

                // Website-Ausnahmen für Shield und Popup-Blocker
                if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var navUri) && !string.IsNullOrEmpty(navUri.Host))
                {
                    tab.TrackingProtectionEnabled = !settings.WhitelistedShieldDomains.Contains(navUri.Host);
                    tab.PopupsBlocked = settings.BlockPopups && !settings.PopupAllowedDomains.Contains(navUri.Host);
                }

                // Interne Seiten brauchen immer JavaScript – auch wenn es für Webseiten abgeschaltet ist
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.IsScriptEnabled = isInternal || tab.JavaScriptEnabled;
                }

                // Die Absturzseite behält die ursprüngliche Adresse (für Adressleiste, Sitzung und "Neu laden")
                if (!InternalPages.Is(args.Uri, InternalPages.Crashed))
                {
                    PrepareFaviconForNavigation(tab, args.Uri);
                    tab.Url = InternalPages.Normalize(args.Uri);
                }

                tab.BlockedTrackersCount = 0;
                tab.BlockedPopupUrls.Clear();
                DiscardPermissionRequests(tab);
                tab.IsLoading = true;
                if (tab == ActiveTab)
                {
                    ShowAddress(tab);
                    UpdateNavigationControls();
                    UpdateShieldBadge();
                    UpdatePopupBlockedIndicator();
                    if (popupShield.IsOpen)
                    {
                        UpdateShieldUi();
                    }
                }

                await SyncShieldStateAsync(tab);
            };

            webView.NavigationCompleted += (s, args) =>
            {
                tab.IsLoading = false;

                // Wurde die Navigation nicht übernommen (Download, abgebrochen, 204), gilt weiter die alte Adresse
                string currentSource = webView.CoreWebView2?.Source ?? "";
                if (currentSource.Length > 0 && !InternalPages.Is(currentSource, InternalPages.Crashed))
                {
                    tab.Url = InternalPages.Normalize(currentSource);
                }

                if (tab == ActiveTab)
                {
                    ShowAddress(tab);
                    UpdateNavigationControls();
                    CheckBookmarkStatus();
                    UpdateZoomIndicator();

                    // Offene Suchleiste: auf der neuen Seite weitersuchen
                    if (IsFindBarOpen && !string.IsNullOrEmpty(findBar.SearchText))
                    {
                        _ = StartFindAsync(findBar.SearchText, findBar.MatchCase);
                    }
                }
                RecordHistory(tab);
            };

            webView.SourceChanged += (s, args) =>
            {
                string currentSrc = webView.Source?.ToString() ?? "";
                if (currentSrc.Length > 0 && !InternalPages.Is(currentSrc, InternalPages.Crashed))
                {
                    tab.Url = InternalPages.Normalize(currentSrc);
                }
                if (tab == ActiveTab)
                {
                    ShowAddress(tab);
                    CheckBookmarkStatus();
                    UpdateShieldUi();
                }
            };
        }

        /// <summary>Verlaufseintrag anlegen – nicht im Inkognito-Modus und nicht für interne Seiten.</summary>
        private void RecordHistory(BrowserTab tab)
        {
            if (_isIncognito || string.IsNullOrWhiteSpace(tab.Url)) return;
            if (InternalPages.IsInternalUrl(tab.Url) ||
                tab.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            // Die Absturzseite zeigt die alte Adresse – kein Verlaufseintrag "Seite abgestürzt"
            if (InternalPages.Is(tab.WebView?.CoreWebView2?.Source, InternalPages.Crashed)) return;

            _historyService.AddEntry(tab.Title, tab.Url);
        }

        /// <summary>Erste Navigation: Startseite, Einstellungen oder die angeforderte URL.</summary>
        private void NavigateInitially(BrowserTab tab, WebView2 webView, string initialUrl)
        {
            if (initialUrl == StartPageService.StartPageUrl || initialUrl == "about:blank")
            {
                tab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
                initialUrl = StartPageService.StartPageUrl;
            }
            else if (initialUrl == SettingsPageService.SettingsPageUrl)
            {
                tab.Title = Tr.Get("Tab_Settings");
            }

            try
            {
                webView.CoreWebView2?.Navigate(initialUrl);
            }
            catch (ArgumentException ex)
            {
                // Ungültige Adresse (z.B. aus einer alten Sitzung) – lieber die Startseite als ein leerer Tab
                Log.Warn($"Ungültige Adresse beim Öffnen eines Tabs: {initialUrl}", ex);
                webView.CoreWebView2?.Navigate(StartPageService.StartPageUrl);
            }
        }

        public void SelectTab(BrowserTab tab)
        {
            if (tab == null) return;

            ExpandGroupOf(tab);

            // Der bisherige Tab ist ab jetzt inaktiv – ab hier läuft seine Zeit bis zum Tab-Schlaf
            if (ActiveTab != null && ActiveTab != tab) ActiveTab.LastActiveAt = DateTime.Now;
            tab.LastActiveAt = DateTime.Now;

            foreach (var t in Tabs)
            {
                bool isActive = (t == tab);
                t.IsActive = isActive;
                if (t.WebView != null)
                {
                    t.WebView.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            WakeTab(tab);
            ActiveTab = tab;
            ApplySplitLayout();
        }

        public void CloseTab(BrowserTab tab, bool rememberForReopen = true)
        {
            if (tab == null) return;

            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            OnTabClosingForSplit(tab);

            // Für Strg+Umschalt+T merken
            if (rememberForReopen)
            {
                _closedTabs.Push(tab.Url, tab.Title, index);
            }

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

            ShowAddress(ActiveTab);
            UpdateNavigationControls();
            CheckBookmarkStatus();
            UpdateShieldBadge();
            UpdateShieldUi();
            UpdatePopupBlockedIndicator();
            UpdateZoomIndicator();
            CloseFindBar();
            OnActiveTabChangedForPermissions();

            // Bei vielen Tabs zum aktiven Tab scrollen
            FindVisualChild<EchoBrowser.Views.Controls.TabStripPanel>(itemsTabs)?.InvalidateArrange();
            UpdateVerticalTabsForActiveTab();
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
                        var dragged = _draggedTab;
                        var data = new DataObject("EchoBrowserTab", dragged);
                        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        _draggedTab = null;

                        // Gruppe nach der neuen Position anpassen (MainWindow.TabGroups.cs)
                        FinishTabDrag(dragged);
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
                    // Angeheftete und übrige Tabs bleiben getrennt
                    if (sourceTab != targetTab && sourceTab.IsPinned == targetTab.IsPinned)
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
