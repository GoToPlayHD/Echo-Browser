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
        /// <param name="openerRequest">
        /// window.open/target=_blank: Die neue WebView wird der Seite als Fenster übergeben (args.NewWindow),
        /// damit window.opener und postMessage funktionieren (OAuth-/SSO- und Zahlungs-Popups).
        /// </param>
        public BrowserTab? AddNewTab(string? targetUrl = null, bool activateTab = true, string? title = null, bool deferLoad = false,
            CoreWebView2NewWindowRequestedEventArgs? openerRequest = null)
        {
            if (_webViewEnvironment == null) return null;

            string initialUrl = string.IsNullOrWhiteSpace(targetUrl) ? StartPageService.StartPageUrl : targetUrl;
            var settings = AppSettingsService.Instance.Settings;
            var tab = new BrowserTab
            {
                // Vor Url setzen: Url bewertet die Sicherheit der Adresse sofort
                CertificateErrors = CertificateErrorTracker.ForProfile(_isIncognito),
                Title = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : Tr.Get(initialUrl == SettingsPageService.SettingsPageUrl ? "Tab_Settings" : "Tab_NewTab"),
                Url = initialUrl,
                JavaScriptEnabled = settings.EnableJavaScript,
                PopupsBlocked = settings.BlockPopups
            };
            if (InternalPages.IsInternalUrl(initialUrl) || initialUrl == "about:blank")
            {
                tab.Favicon = AppIcon;
            }
            else if (!_isIncognito)
            {
                // Gespeichertes Symbol im Hintergrund laden (wichtig beim Wiederherstellen vieler Tabs)
                _ = LoadCachedFaviconAsync(tab, initialUrl);
            }

            // Adresse oder Titel geändert -> Sitzung (verzögert) sichern
            tab.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(BrowserTab.Url) or nameof(BrowserTab.Title) or nameof(BrowserTab.IsActive))
                {
                    ScheduleSessionSave();
                }
            };

            if (deferLoad && !activateTab && openerRequest == null)
            {
                tab.IsDiscarded = true;
            }
            else
            {
                CreateTabWebView(tab, initialUrl, openerRequest);
            }

            Tabs.Add(tab);
            if (activateTab)
            {
                SelectTab(tab);
            }
            return tab;
        }

        /// <summary>Legt das WebView2-Steuerelement eines Tabs an (auch zum Neuaufbau nach einem Browser-Absturz).</summary>
        private void CreateTabWebView(BrowserTab tab, string url, CoreWebView2NewWindowRequestedEventArgs? openerRequest = null)
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

            AttachWebViewEvents(tab, webView, url, openerRequest);
        }

        /// <summary>
        /// Initialisiert die WebView eines Tabs und hängt alle Ereignisse an.
        /// WebView2-Ereignisse feuern auf dem UI-Thread – ein Dispatcher.Invoke ist nicht nötig.
        /// </summary>
        private async void AttachWebViewEvents(BrowserTab tab, WebView2 webView, string initialUrl,
            CoreWebView2NewWindowRequestedEventArgs? openerRequest = null)
        {
            // Die öffnende Seite wartet, bis die neue WebView bereit ist – auch im Fehlerfall freigeben
            var openerDeferral = openerRequest?.GetDeferral();
            try
            {
                // Nach einem Absturz des Browser-Prozesses liefert das eine neue Umgebung
                var env = _webViewEnvironment = await BrowserEnvironment.GetAsync();
                await webView.EnsureCoreWebView2Async(env, BrowserEnvironment.CreateControllerOptions(env, _isIncognito, _incognitoWithExtensions));

                var core = webView.CoreWebView2;
                if (core != null && (!_isIncognito || _incognitoWithExtensions))
                {
                    // Temporäres Inkognito-Profil: startet leer, die installierten Erweiterungen hineinladen – vor der
                    // ersten Seite, sonst fehlen dort ihre Inhaltsskripte.
                    // Normales Profil: kennt seine Erweiterungen schon; nach einem Umzug des Datenordners vergessene neu
                    // laden und geänderte Hintergrund-Skripte neu starten – das läuft parallel zum Laden der Seite.
                    var extensionSetup = ExtensionService.Instance.EnsureProfileExtensionsAsync(core.Profile, isTemporaryProfile: _isIncognito);
                    if (_isIncognito) await extensionSetup;
                    if (!_pinnedExtensionsShown)
                    {
                        // Erst danach kennt das Profil alle Erweiterungen – vorher bliebe die Leiste leer
                        _pinnedExtensionsShown = true;
                        _ = ShowPinnedExtensionsWhenReadyAsync(extensionSetup);
                    }
                }
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
                    core.DownloadStarting += (s, args) => HandleDownloadStarting(tab, core, args);

                    // Abstürze des Renderers oder Browsers -> MainWindow.InternalPages.cs
                    core.ProcessFailed += (s, args) => HandleProcessFailed(tab, webView, args);

                    // Website-Symbol und Ton-Anzeige im Tab -> MainWindow.TabActions.cs
                    AttachTabIndicatorEvents(tab, core);

                    // Zoom pro Website & Vollbild-Videos -> MainWindow.Zoom.cs / MainWindow.Fullscreen.cs
                    AttachZoomEvents(tab, webView);
                    AttachFullscreenEvents(tab, core);

                    // Kamera, Mikrofon, Standort … -> MainWindow.Permissions.cs
                    AttachPermissionEvents(tab, core);

                    // Zertifikatsfehler: kein »Sicher« nach »Trotzdem fortfahren« -> MainWindow.Shield.cs
                    AttachCertificateErrorEvents(tab, core);

                    AttachPageStateEvents(tab, webView, core);

                    // Link-Kontextmenü "In geteilter Ansicht öffnen" -> MainWindow.SplitView.cs
                    AttachSplitLinkMenu(tab, core);

                    // window.close() aus einem Popup (z.B. nach der OAuth-Anmeldung) schließt den Tab.
                    // Chromium erlaubt das nur Fenstern, die per Skript geöffnet wurden.
                    core.WindowCloseRequested += (s, args) => CloseTab(tab, rememberForReopen: false);
                }

                AttachNavigationEvents(tab, webView);

                if (openerRequest != null && core != null)
                {
                    // WebView2 lädt die Adresse selbst und verbindet window.opener mit der öffnenden Seite
                    openerRequest.NewWindow = core;
                }
                else
                {
                    NavigateInitially(tab, webView, initialUrl);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"WebView eines Tabs konnte nicht initialisiert werden ({initialUrl})", ex);
            }
            finally
            {
                openerDeferral?.Complete();
            }
        }

        /// <summary>
        /// Links mit target=_blank und window.open: als Tab öffnen. Popups, die eine Seite ohne Klick des Nutzers
        /// öffnen will, werden blockiert und in der Adressleiste angeboten (MainWindow.Popups.cs).
        /// </summary>
        private void HandleNewWindowRequested(BrowserTab tab, CoreWebView2NewWindowRequestedEventArgs args)
        {
            args.Handled = true;

            if (!args.IsUserInitiated && tab.PopupsBlocked)
            {
                // Ohne Adresse (window.open() ohne URL) gibt es nichts anzubieten
                if (string.IsNullOrWhiteSpace(args.Uri)) return;
                tab.BlockedPopupUrls.Add(args.Uri);
                if (tab == ActiveTab)
                {
                    UpdatePopupBlockedIndicator();
                }
                return;
            }

            // Per Skript geöffnete Fenster (OAuth, Zahlung) immer im Vordergrund, Links mit target=_blank
            // wie eingestellt; die Seite behält über window.opener die Verbindung zum neuen Tab
            bool isScriptPopup = args.WindowFeatures?.HasPosition == true || args.WindowFeatures?.HasSize == true;
            bool activate = isScriptPopup || !AppSettingsService.Instance.Settings.OpenNewTabInBackground;
            string url = string.IsNullOrWhiteSpace(args.Uri) ? "about:blank" : args.Uri;
            AddNewTab(url, activateTab: activate, title: url == "about:blank" ? "about:blank" : null, openerRequest: args);
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

            // Die Skripte der Seite unabhängig voneinander gleichzeitig registrieren (spart Wartezeit vor dem ersten Laden):
            // "Do Not Track" & Global Privacy Control (MainWindow.Privacy.cs), Shield (nur wenn aktiv), Web-Store-Helfer
            await Task.WhenAll(
                AttachPrivacySignalsAsync(tab, core),
                SyncShieldStateAsync(tab),
                RegisterWebStoreHelperAsync(core));

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
                core.Profile.PreferredTrackingPreventionLevel = ToTrackingPreventionLevel(settings.TrackingPreventionLevel);
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

                    // Wie uBlock Origin/Brave im Standardmodus: Ruft der Nutzer eine gelistete Website selbst auf
                    // (Adressleiste, Link), wird die Seite geladen – blockiert werden nur ihre Unterressourcen
                    // und eingebettete Frames. Sonst bleibt der Tab ohne Erklärung leer.
                    if (args.ResourceContext == CoreWebView2WebResourceContext.Document &&
                        AdBlockerService.IsSameDocumentUrl(args.Request.Uri, tab.MainFrameNavigationUri)) return;

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
                // Während des Ladens trägt NavigationCompleted den Besuch ein (und weiß, ob er gelungen ist);
                // danach halten Titeländerungen den Eintrag aktuell (auch Seitenwechsel per pushState)
                if (!tab.IsLoading) RecordHistory(tab, titleChangeOnly: true);
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
                tab.MainFrameNavigationUri = args.Uri;
                tab.CurrentNavigationId = args.NavigationId;
                tab.LoadFailed = false;

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

                // Von Echo gestartet (oder erste Seite eines Tabs)? Nur dann das Ziel schon jetzt anzeigen
                if (tab.ExpectBrowserNavigation)
                {
                    tab.ExpectBrowserNavigation = false;
                    tab.BrowserNavigationId = args.NavigationId;
                }
                string committed = webView.CoreWebView2?.Source ?? "";
                bool showTarget = args.NavigationId == tab.BrowserNavigationId ||
                                  committed.Length == 0 || committed == "about:blank";

                // Die Absturzseite behält die ursprüngliche Adresse (für Adressleiste, Sitzung und "Neu laden").
                // Von der Seite gestartete Navigationen: Adresse erst nach dem Laden (SourceChanged)
                if (showTarget && !InternalPages.Is(args.Uri, InternalPages.Crashed))
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
                // Abgelöste Navigation (schnell hintereinander geklickt): die neue lädt noch
                if (args.NavigationId != tab.CurrentNavigationId) return;

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

                // Nicht erreichbare Seiten (DNS, offline, abgebrochen) gehören nicht in den Verlauf,
                // stattdessen zeigt Echo eine eigene Fehlerseite
                tab.LoadFailed = !args.IsSuccess;
                if (args.IsSuccess)
                {
                    RecordHistory(tab);
                }
                else
                {
                    _ = ShowNetErrorPageAsync(tab, webView, args.WebErrorStatus);
                }
            };

            webView.SourceChanged += (s, args) =>
            {
                // CoreWebView2.Source statt webView.Source: Uri.ToString() würde %20 & Co. entschlüsseln
                string currentSrc = webView.CoreWebView2?.Source ?? "";
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
        private void RecordHistory(BrowserTab tab, bool titleChangeOnly = false)
        {
            // Die tatsächlich geladene Seite – tab.Url zeigt während einer Navigation schon auf das neue Ziel,
            // der Titel gehört dann aber noch zur alten Seite
            string url = tab.WebView?.CoreWebView2?.Source ?? "";
            if (_isIncognito || tab.LoadFailed || string.IsNullOrWhiteSpace(url)) return;
            if (InternalPages.IsInternalUrl(url) ||
                url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return;
            // Absturzseite (zeigt die alte Adresse) – kein Verlaufseintrag "Seite abgestürzt"; interne Seiten: s.o.

            _historyService.AddEntry(tab.Title, url, titleChangeOnly);
        }

        private static async Task RegisterWebStoreHelperAsync(CoreWebView2 core)
        {
            try
            {
                await core.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionService.GetWebStoreHelperScript());
            }
            catch (Exception ex)
            {
                Log.Warn("Web-Store-Hilfsskript konnte nicht registriert werden", ex);
            }
        }

        private async Task ShowPinnedExtensionsWhenReadyAsync(Task extensionSetup)
        {
            try
            {
                await extensionSetup;
            }
            catch (Exception ex)
            {
                Log.Warn("Erweiterungen konnten nicht vorbereitet werden", ex);
            }
            UpdatePinnedExtensionsToolbar();
            StartExtensionUpdateChecks();
        }

        /// <summary>Eigene Fehlerseite statt Chromiums Standardseite (Services/NetErrorPageService.cs).</summary>
        private async Task ShowNetErrorPageAsync(BrowserTab tab, WebView2 webView, CoreWebView2WebErrorStatus status)
        {
            var core = webView.CoreWebView2;
            if (core == null) return;

            string url = core.Source;
            var kind = NetErrorPageService.Classify(status);

            // Chromium lädt seine Fehlerseite kurz darauf selbst neu und meldet dann "ConnectionAborted":
            // die ursprüngliche Fehlerart (z.B. Adresse nicht gefunden) behalten
            if (status == CoreWebView2WebErrorStatus.ConnectionAborted && tab.LastNetError is { } last && last.Url == url)
            {
                kind = last.Kind;
                status = last.Status;
            }
            if (kind == null) return;
            tab.LastNetError = (url, kind.Value, status);

            try
            {
                await core.ExecuteScriptAsync(NetErrorPageService.BuildInjectionScript(url, kind.Value, status));
            }
            catch (Exception ex)
            {
                // Tab inzwischen geschlossen oder weiter navigiert
                Log.Warn($"Fehlerseite für {url} konnte nicht angezeigt werden", ex);
            }
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

            // Wie in Chrome/Edge: Mit dem letzten Tab schließt sich das Fenster (Sitzung wie beim Schließen per X).
            // Verzögert, weil der Aufruf auch aus einem WebView2-Ereignis des Tabs kommen kann (window.close()).
            if (Tabs.Count == 1)
            {
                Dispatcher.BeginInvoke(Close);
                return;
            }

            OnTabClosingForSplit(tab);

            // Für Strg+Umschalt+T merken
            if (rememberForReopen)
            {
                _closedTabs.Push(tab.Url, tab.Title, index);
            }

            if (tab.ActiveDownloads > 0 && tab.WebView != null)
            {
                // Laufende Downloads hängen an der WebView: unsichtbar weiterleben lassen, bis sie fertig sind
                tab.WebView.Visibility = Visibility.Collapsed;
                _tabsKeptForDownloads.Add(tab);
            }
            else
            {
                if (tab.WebView != null)
                {
                    WebViewContainer.Children.Remove(tab.WebView);
                }
                tab.Dispose();
            }
            Tabs.Remove(tab);

            // If we closed the active tab, select an adjacent one (der letzte Tab schließt oben das Fenster)
            if (ActiveTab == tab)
            {
                int newIndex = Math.Clamp(index, 0, Tabs.Count - 1);
                SelectTab(Tabs[newIndex]);
            }
        }

        private void OnActiveTabChanged(BrowserTab? previous = null)
        {
            if (ActiveTab == null) return;

            // Video-Vollbild gehört zum bisherigen Tab: nach Tabwechsel oder Schließen nicht im Vollbild hängen bleiben.
            // Das Video selbst im bisherigen Tab beenden – sonst steckt es beim Zurückwechseln noch im Vollbild.
            if (_isFullscreen && _fullscreenFromPage && ActiveTab.WebView?.CoreWebView2?.ContainsFullScreenElement != true)
            {
                if (previous?.WebView?.CoreWebView2 is { ContainsFullScreenElement: true } previousCore)
                {
                    _ = previousCore.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
                }
                SetFullscreen(false, fromPage: true);
            }

            // Getippter, nicht abgeschickter Text gehört zum vorherigen Tab – nicht in den neuen übernehmen
            CloseOmniboxPopup();
            FinishAddressEditing();
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
                        // Gezogenen Tab sichtbar "anheben", solange er verschoben wird
                        double opacityBefore = fe.Opacity;
                        fe.Opacity = 0.6;
                        try
                        {
                            DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        }
                        finally
                        {
                            fe.Opacity = opacityBefore;
                        }
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
