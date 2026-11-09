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
using EchoBrowser.Views.Popups;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>Echo Shield: Werbe-/Tracker-Blocker, Badge und Site-Info-Popup.</summary>
    public partial class MainWindow
    {
        #region Site-Info & Brave Shield Popup

        private void BtnShield_Click(object sender, RoutedEventArgs e)
        {
            popupShield.IsOpen = true;
            UpdateShieldUi();
        }

        private void UpdateShieldBadge()
        {
            if (ActiveTab == null)
            {
                borderShieldBadge.Visibility = Visibility.Collapsed;
                return;
            }

            bool shieldActive = AppSettingsService.Instance.Settings.IsAdBlockerEnabled && ActiveTab.TrackingProtectionEnabled;
            int count = ActiveTab.BlockedTrackersCount;

            if (shieldActive && count > 0)
            {
                txtShieldBadgeCount.Text = count > 999 ? "999+" : count.ToString();
                borderShieldBadge.Visibility = Visibility.Visible;
            }
            else
            {
                borderShieldBadge.Visibility = Visibility.Collapsed;
            }

            if (shieldActive)
            {
                pathShieldIcon.Fill = FindResource("ShieldActiveBrush") as Brush ?? Brushes.CornflowerBlue;
            }
            else
            {
                pathShieldIcon.Fill = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
            }
        }

        private void InitializeShieldPanel()
        {
            shieldPanel.OptionsChanged += OnShieldOptionsChanged;
            shieldPanel.ClearSiteDataRequested += async () => await ClearActiveSiteDataAsync();
        }

        /// <summary>
        /// Zertifikatsfehler merken. Die Warnseite zeigt weiter Chromium (Action bleibt Default); geht der Nutzer
        /// trotzdem weiter, zeigen Adressleiste und Shield für diesen Host bis Sitzungsende »Nicht sicher«.
        /// </summary>
        private void AttachCertificateErrorEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.ServerCertificateErrorDetected += (s, args) =>
            {
                if (!CertificateErrorTracker.ForProfile(_isIncognito).Record(args.RequestUri, args.ErrorStatus)) return;
                if (!_isIncognito && Uri.TryCreate(args.RequestUri, UriKind.Absolute, out var uri))
                {
                    Log.Info($"Zertifikatsfehler {args.ErrorStatus} für {uri.Host}");
                }

                // Andere Tabs desselben Profils können schon auf diesem Host sein
                foreach (var window in Application.Current.Windows.OfType<MainWindow>().Where(w => w._isIncognito == _isIncognito))
                {
                    window.RefreshSecurityState();
                }
            };
        }

        private void RefreshSecurityState()
        {
            foreach (var tab in Tabs)
            {
                tab.UpdateSecurityStatus();
            }
            if (ActiveTab != null)
            {
                UpdateSecurityChip(ActiveTab);
                UpdateShieldUi();
            }
        }

        private void UpdateShieldUi()
        {
            if (ActiveTab == null) return;

            string url = ActiveTab.Url;
            string host = Tr.Get("Security_LocalPage");

            if (IsStartPage(url))
            {
                host = Tr.Get("Shield_StartPageHost");
            }
            else if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            {
                host = uri.Host;
                ActiveTab.TrackingProtectionEnabled = !AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Contains(host);
            }

            shieldPanel.ShowState(new ShieldViewState(
                Host: host,
                IsSecure: ActiveTab.IsSecure || IsStartPage(url),
                GlobalOn: AppSettingsService.Instance.Settings.IsAdBlockerEnabled,
                SiteOn: ActiveTab.TrackingProtectionEnabled,
                JavaScriptOn: ActiveTab.JavaScriptEnabled,
                PopupsBlocked: ActiveTab.PopupsBlocked,
                BlockedCount: ActiveTab.BlockedTrackersCount,
                CertificateError: ActiveTab.CertificateError));

            if (popupShield.IsOpen)
            {
                _ = LoadSitePermissionsAsync();
            }
            UpdateShieldBadge();
        }

        /// <summary>
        /// Bringt Cosmetic-Script und Netzwerkfilter eines Tabs in Einklang mit dem Shield-Status
        /// (global aktiv UND für die aktuelle Website nicht deaktiviert).
        /// </summary>
        /// <summary>
        /// Anfragearten, die Werbung und Tracker tatsächlich nutzen. Jede gefilterte Anfrage läuft über den UI-Thread –
        /// Stylesheets, Schriften, Manifeste und Untertitel bleiben deshalb außen vor (Werbenetze laden darüber nichts,
        /// was Shield blocken müsste). Document = eingebettete Frames; die Hauptseite selbst wird nie blockiert.
        /// </summary>
        private static readonly CoreWebView2WebResourceContext[] AdBlockContexts =
        {
            CoreWebView2WebResourceContext.Document,
            CoreWebView2WebResourceContext.Script,
            CoreWebView2WebResourceContext.Image,
            CoreWebView2WebResourceContext.Media,
            CoreWebView2WebResourceContext.XmlHttpRequest,
            CoreWebView2WebResourceContext.Fetch,
            CoreWebView2WebResourceContext.Ping,
            CoreWebView2WebResourceContext.EventSource,
            CoreWebView2WebResourceContext.Websocket,
            CoreWebView2WebResourceContext.Other
        };

        private async Task SyncShieldStateAsync(BrowserTab tab)
        {
            var core = tab.WebView?.CoreWebView2;
            if (core == null) return;

            bool shouldBeActive = AppSettingsService.Instance.Settings.IsAdBlockerEnabled && tab.TrackingProtectionEnabled;

            // Netzwerkfilter nur registrieren, wenn wirklich geblockt werden soll
            try
            {
                if (shouldBeActive && !tab.IsAdBlockFilterRegistered)
                {
                    foreach (var context in AdBlockContexts) core.AddWebResourceRequestedFilter("*", context);
                    tab.IsAdBlockFilterRegistered = true;
                }
                else if (!shouldBeActive && tab.IsAdBlockFilterRegistered)
                {
                    foreach (var context in AdBlockContexts) core.RemoveWebResourceRequestedFilter("*", context);
                    tab.IsAdBlockFilterRegistered = false;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shield-Netzwerkfilter konnte nicht aktualisiert werden", ex);
            }

            // Cosmetic element-hiding script
            try
            {
                if (shouldBeActive && tab.CosmeticScriptId == null)
                {
                    tab.CosmeticScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(AdBlockerService.Instance.GetCosmeticScript());
                }
                else if (!shouldBeActive && tab.CosmeticScriptId != null)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(tab.CosmeticScriptId);
                    tab.CosmeticScriptId = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Shield-Cosmetic-Script konnte nicht aktualisiert werden", ex);
            }
        }

        private async Task SyncShieldStateForAllTabsAsync()
        {
            foreach (var t in Tabs.ToList())
            {
                await SyncShieldStateAsync(t);
            }
        }

        /// <summary>
        /// Fasst Badge-Updates zusammen: bei vielen geblockten Requests wird die UI
        /// einmal pro Dispatcher-Durchlauf statt einmal pro Request aktualisiert.
        /// </summary>
        private void ScheduleShieldBadgeUpdate()
        {
            if (_shieldBadgeUpdatePending) return;
            _shieldBadgeUpdatePending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _shieldBadgeUpdatePending = false;
                UpdateShieldBadge();
                if (popupShield.IsOpen)
                {
                    UpdateShieldUi();
                }
            }));
        }

        private async void OnShieldOptionsChanged(ShieldOptions options)
        {
            if (ActiveTab == null) return;

            bool prevGlobal = AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
            bool newGlobal = options.GlobalOn;
            if (prevGlobal != newGlobal)
            {
                AppSettingsService.Instance.Settings.IsAdBlockerEnabled = newGlobal;
                AppSettingsService.Instance.Save();
            }

            bool newTabShield = options.SiteOn;
            ActiveTab.TrackingProtectionEnabled = newTabShield;
            ActiveTab.JavaScriptEnabled = options.JavaScriptOn;
            bool popupSettingChanged = ActiveTab.PopupsBlocked != options.PopupsBlocked;
            ActiveTab.PopupsBlocked = options.PopupsBlocked;

            // Ausnahmen pro Website merken: Shield aus bzw. Popups erlaubt
            if (Uri.TryCreate(ActiveTab.Url, UriKind.Absolute, out var curUri) && !string.IsNullOrEmpty(curUri.Host))
            {
                var settings = AppSettingsService.Instance.Settings;
                if (!newTabShield)
                {
                    settings.WhitelistedShieldDomains.Add(curUri.Host);
                }
                else
                {
                    settings.WhitelistedShieldDomains.Remove(curUri.Host);
                }

                // Nur wenn der Popup-Schalter selbst umgelegt wurde – nicht bei jedem anderen Schalter
                if (popupSettingChanged && !options.PopupsBlocked)
                {
                    settings.PopupAllowedDomains.Add(curUri.Host);
                }
                else if (popupSettingChanged)
                {
                    settings.PopupAllowedDomains.Remove(curUri.Host);
                }
                AppSettingsService.Instance.Save();
            }

            ActiveTab.ApplyScriptSetting();
            UpdateShieldUi();
            UpdateShieldBadge();

            // Netzwerkfilter & Cosmetic-Script synchronisieren: der globale Schalter betrifft alle Tabs
            if (prevGlobal != newGlobal)
            {
                await SyncShieldStateForAllTabsAsync();
            }
            else
            {
                await SyncShieldStateAsync(ActiveTab);
            }

            // Immediately synchronize DOM state on active tab
            bool isProtectionActive = newGlobal && newTabShield;
            if (ActiveTab?.WebView?.CoreWebView2 != null)
            {
                if (!isProtectionActive)
                {
                    try
                    {
                        await ActiveTab.WebView.CoreWebView2.ExecuteScriptAsync(
                            "window.__echoShieldDisabled = true; const s = document.getElementById('echo-shield-cosmetic'); if (s) s.remove();"
                        );
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        await ActiveTab.WebView.CoreWebView2.ExecuteScriptAsync("window.__echoShieldDisabled = false;");
                    }
                    catch { }
                }

                // If on normal web page, reload tab so that scripts and ads load cleanly without blocked state
                if (!IsStartPage(ActiveTab.Url) && !ActiveTab.Url.StartsWith("echo://", StringComparison.OrdinalIgnoreCase) && !ActiveTab.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    ActiveTab.WebView.Reload();
                }
            }
        }

        /// <summary>
        /// Cookies und gespeicherte Daten (LocalStorage, IndexedDB, Cache, Service Worker …) nur der aktuellen
        /// Website löschen. Früher wurden hier die Cookies ALLER Websites gelöscht.
        /// </summary>
        private async Task ClearActiveSiteDataAsync()
        {
            var tab = ActiveTab;
            var core = tab?.WebView?.CoreWebView2;
            if (tab == null || core == null) return;

            popupShield.IsOpen = false;

            if (!Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                ThemedDialogWindow.ShowMessage(this, "Echo Shield", Tr.Get("Shield_NoSiteData"));
                return;
            }

            try
            {
                string origin = uri.GetLeftPart(UriPartial.Authority);

                foreach (var cookie in await core.CookieManager.GetCookiesAsync(origin))
                {
                    core.CookieManager.DeleteCookie(cookie);
                }

                await core.CallDevToolsProtocolMethodAsync(
                    "Storage.clearDataForOrigin",
                    JsonSerializer.Serialize(new { origin, storageTypes = "all" }));

                ThemedDialogWindow.ShowMessage(this, "Echo Shield", Tr.Format("Shield_SiteDataClearedFor", uri.Host));
            }
            catch (Exception ex)
            {
                ThemedDialogWindow.ShowMessage(this, "Echo Shield", Tr.Format("Shield_SiteDataClearFailed", ex.Message), MessageBoxImage.Warning);
            }
        }

        #endregion
    }
}
