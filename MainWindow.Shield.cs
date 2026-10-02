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
    /// <summary>Echo Shield: Werbe-/Tracker-Blocker, Badge und Site-Info-Popup.</summary>
    public partial class MainWindow
    {
        #region Site-Info & Brave Shield Popup

        private void BtnShield_Click(object sender, RoutedEventArgs e)
        {
            UpdateShieldUi();
            popupShield.IsOpen = true;
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

        private void UpdateShieldUi()
        {
            if (ActiveTab == null) return;

            string url = ActiveTab.Url;
            string host = "Lokale Seite";

            if (IsStartPage(url))
            {
                host = "Echo Startseite";
            }
            else
            {
                try
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                    {
                        host = uri.Host;
                        bool isWhitelisted = AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Contains(host);
                        ActiveTab.TrackingProtectionEnabled = !isWhitelisted;
                    }
                }
                catch { }
            }

            txtShieldHost.Text = string.IsNullOrWhiteSpace(host) ? "Echo-Browser" : host;

            if (ActiveTab.IsSecure || IsStartPage(url))
            {
                txtShieldStatus.Text = "Sichere Verbindung (TLS/HTTPS)";
                txtShieldStatus.Foreground = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
            }
            else
            {
                txtShieldStatus.Text = "Verbindung nicht verschlüsselt (HTTP)";
                txtShieldStatus.Foreground = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
            }

            _isUpdatingShieldUi = true;
            try
            {
                bool isGlobalOn = AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
                bool isTabOn = ActiveTab.TrackingProtectionEnabled;
                bool isProtectionActive = isGlobalOn && isTabOn;

                chkGlobalShield.IsChecked = isGlobalOn;
                chkTrackingProtection.IsChecked = isTabOn;
                chkJavaScript.IsChecked = ActiveTab.JavaScriptEnabled;
                chkPopups.IsChecked = ActiveTab.PopupsBlocked;

                if (isProtectionActive)
                {
                    txtShieldActiveState.Text = LocalizationService.Instance.GetString("Shield_ActiveStateOn", "Echo Shield: Aktiviert");
                    txtShieldActiveState.Foreground = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
                    pathShieldPopupIcon.Fill = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
                }
                else
                {
                    txtShieldActiveState.Text = LocalizationService.Instance.GetString("Shield_ActiveStateOff", "Echo Shield: Deaktiviert");
                    txtShieldActiveState.Foreground = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
                    pathShieldPopupIcon.Fill = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
                }

                txtTrackersBlocked.Text = string.Format(LocalizationService.Instance.GetString("Shield_TrackersBlockedFormat", "{0} Tracker und Werbeanzeigen blockiert"), ActiveTab.BlockedTrackersCount);
                txtFilterRuleCount.Text = string.Format(LocalizationService.Instance.GetString("Shield_FilterRuleCountFormat", "{0:N0} Filterregeln geladen"), AdBlockerService.Instance.BlockedDomainsCount);

                UpdateShieldBadge();
            }
            finally
            {
                _isUpdatingShieldUi = false;
            }
        }

        /// <summary>
        /// Bringt Cosmetic-Script und Netzwerkfilter eines Tabs in Einklang mit dem Shield-Status
        /// (global aktiv UND für die aktuelle Website nicht deaktiviert).
        /// </summary>
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
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    tab.IsAdBlockFilterRegistered = true;
                }
                else if (!shouldBeActive && tab.IsAdBlockFilterRegistered)
                {
                    core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    tab.IsAdBlockFilterRegistered = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Shield-Netzwerkfilter konnte nicht aktualisiert werden: {ex.Message}");
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
                Debug.WriteLine($"[Echo] Shield-Cosmetic-Script konnte nicht aktualisiert werden: {ex.Message}");
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

        private async void ShieldOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingShieldUi || ActiveTab == null) return;

            bool prevGlobal = AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
            bool newGlobal = chkGlobalShield.IsChecked ?? true;
            if (prevGlobal != newGlobal)
            {
                AppSettingsService.Instance.Settings.IsAdBlockerEnabled = newGlobal;
                AppSettingsService.Instance.Save();
            }

            bool newTabShield = chkTrackingProtection.IsChecked ?? true;
            ActiveTab.TrackingProtectionEnabled = newTabShield;
            ActiveTab.JavaScriptEnabled = chkJavaScript.IsChecked ?? true;
            ActiveTab.PopupsBlocked = chkPopups.IsChecked ?? true;

            // Remember domain in whitelist if tracking protection is turned off
            if (Uri.TryCreate(ActiveTab.Url, UriKind.Absolute, out var curUri) && !string.IsNullOrEmpty(curUri.Host))
            {
                if (!newTabShield)
                {
                    AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Add(curUri.Host);
                }
                else
                {
                    AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Remove(curUri.Host);
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

        private async void BtnUpdateFilterList_Click(object sender, RoutedEventArgs e)
        {
            btnUpdateFilterList.IsEnabled = false;
            btnUpdateFilterList.Content = "Lade Filter...";

            try
            {
                string url = AppSettingsService.Instance.Settings.AdBlockerFilterUrl;
                await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(url, force: true);
                txtFilterRuleCount.Text = $"{AdBlockerService.Instance.BlockedDomainsCount:N0} Filterregeln geladen";
                btnUpdateFilterList.Content = "Aktualisiert!";
                await Task.Delay(1800);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Filteraktualisierung fehlgeschlagen: {ex.Message}", "Echo Shield", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnUpdateFilterList.Content = "Filter aktualisieren";
                btnUpdateFilterList.IsEnabled = true;
            }
        }

        private async void BtnClearSiteData_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView?.CoreWebView2 != null)
            {
                try
                {
                    await ActiveTab.WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.Cookies | 
                        CoreWebView2BrowsingDataKinds.CacheStorage | 
                        CoreWebView2BrowsingDataKinds.IndexedDb);

                    popupShield.IsOpen = false;
                    MessageBox.Show(
                        "Cookies und Website-Cache wurden für diese Sitzung erfolgreich geleert.",
                        "Echo Shield",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to clear browsing data: {ex.Message}");
                }
            }
        }

        #endregion
    }
}
