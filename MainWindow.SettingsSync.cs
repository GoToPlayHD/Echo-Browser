using System;
using System.Windows;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Einstellungen gelten für alle Fenster: Die Einstellungsseite, das Menü oder die Symbolleiste ändern nur den
    /// Wert (AppSettingsService.Changed) – jedes Fenster wendet ihn hier auf sich an.
    /// </summary>
    public partial class MainWindow
    {
        private void AttachSettingsSync()
        {
            AppSettingsService.Instance.SettingChanged += OnSettingChanged;
            Closed += (s, e) => AppSettingsService.Instance.SettingChanged -= OnSettingChanged;
        }

        private void OnSettingChanged(string key)
        {
            var settings = AppSettingsService.Instance.Settings;
            switch (key)
            {
                case nameof(settings.IsBookmarksBarVisible):
                    ApplyBookmarksBarVisibility();
                    break;
                case nameof(settings.IsSidebarVisible):
                    ApplySidebarVisibility();
                    break;
                case nameof(settings.ShowHomeButton):
                    btnHome.Visibility = settings.ShowHomeButton ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case nameof(settings.ShowSidebarButton):
                case nameof(settings.ShowBackButton):
                case nameof(settings.ShowForwardButton):
                case nameof(settings.ShowReloadButton):
                case nameof(settings.ShowSearchEngineSelector):
                case nameof(settings.ShowExtensionsButton):
                case nameof(settings.ShowDownloadsButton):
                    ApplyToolbarButtonVisibilities();
                    break;
                case nameof(settings.SearchEngine):
                    SyncSearchEngineComboBox();
                    break;
                case nameof(settings.DefaultZoomPercent):
                    ApplyDefaultZoom();
                    break;
                case nameof(settings.TrackingPreventionLevel):
                    ApplyTrackingPrevention(settings.TrackingPreventionLevel);
                    break;
                case nameof(settings.BlockPopups):
                    foreach (var tab in Tabs)
                    {
                        string host = Uri.TryCreate(tab.Url, UriKind.Absolute, out var tabUri) ? tabUri.Host : "";
                        tab.PopupsBlocked = settings.BlockPopups && !settings.PopupAllowedDomains.Contains(host);
                    }
                    break;
                case nameof(settings.EnableJavaScript):
                    // Gilt für Webseiten ab dem nächsten Laden; interne Seiten behalten JavaScript (NavigationStarting)
                    foreach (var tab in Tabs)
                    {
                        tab.JavaScriptEnabled = settings.EnableJavaScript;
                    }
                    break;
                case nameof(settings.IsAdBlockerEnabled):
                    _ = SyncShieldStateForAllTabsAsync();
                    UpdateShieldBadge();
                    if (popupShield.IsOpen) UpdateShieldUi();
                    break;
                case nameof(settings.SendDoNotTrack):
                    _ = SyncPrivacySignalsForAllTabsAsync();
                    break;
                case nameof(settings.SavePasswords):
                case nameof(settings.AutofillForms):
                    // Profil-Einstellung: eine WebView des Fensters genügt (Inkognito hat ein eigenes Profil)
                    if (ActiveTab?.WebView?.CoreWebView2 is { } core) ApplyAutofillSettings(core);
                    break;
            }
        }

        private void ApplyBookmarksBarVisibility()
        {
            _isBookmarksBarVisible = AppSettingsService.Instance.Settings.IsBookmarksBarVisible;
            borderBookmarksBar.Visibility = _isBookmarksBarVisible ? Visibility.Visible : Visibility.Collapsed;
            menuChkBookmarksBar.IsChecked = _isBookmarksBarVisible;
        }

        private void ApplySidebarVisibility()
        {
            bool visible = AppSettingsService.Instance.Settings.IsSidebarVisible;
            borderSidebar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            menuChkSidebar.IsChecked = visible;
        }

        internal static CoreWebView2TrackingPreventionLevel ToTrackingPreventionLevel(string? level) =>
            (level ?? "balanced").ToLowerInvariant() switch
            {
                "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                "none" => CoreWebView2TrackingPreventionLevel.None,
                _ => CoreWebView2TrackingPreventionLevel.Balanced
            };
    }
}
