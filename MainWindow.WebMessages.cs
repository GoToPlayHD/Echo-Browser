using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Handler für Nachrichten der internen Seiten (Startseite, Einstellungen) und des Chrome Web Store.
    /// Parsing und Herkunftsprüfung übernimmt der <see cref="WebMessageRouter"/>.
    /// WebMessageReceived feuert auf dem UI-Thread – die Handler dürfen die UI direkt anfassen.
    /// </summary>
    public partial class MainWindow
    {
        private readonly WebMessageRouter _webMessageRouter = new();

        private void RegisterWebMessageHandlers()
        {
            // Startseite
            _webMessageRouter.Register("navigate", OnNavigateMessage);
            _webMessageRouter.Register("setSearchEngine", OnSetSearchEngineMessage);
            _webMessageRouter.Register("toggleStartpageFavorites", OnToggleStartpageFavoritesMessage);
            _webMessageRouter.Register("saveStartpageShortcuts", OnSaveStartpageShortcutsMessage);

            // Chrome Web Store
            _webMessageRouter.Register("installExtensionFromWebStore", OnInstallExtensionFromWebStoreMessage);

            // Einstellungsseite
            _webMessageRouter.Register("updateSetting", OnUpdateSettingMessage);
            _webMessageRouter.Register("setThemePreset", OnSetThemePresetMessage);
            _webMessageRouter.Register("setAccentColor", OnSetAccentColorMessage);
            _webMessageRouter.Register("browseDownloadFolder", OnBrowseDownloadFolderMessage);
            _webMessageRouter.Register("openDownloadFolder", OnOpenDownloadFolderMessage);
            _webMessageRouter.Register("clearBrowsingData", OnClearBrowsingDataMessage);
            _webMessageRouter.Register("resetSettings", OnResetSettingsMessage);
            _webMessageRouter.Register("updateAdBlockFilter", OnUpdateAdBlockFilterMessage);
        }

        #region Startseite

        private void OnNavigateMessage(WebMessageContext ctx)
        {
            string? url = ctx.GetString("url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                NavigateToInput(url);
            }
        }

        private void OnSetSearchEngineMessage(WebMessageContext ctx)
        {
            AppSettingsService.Instance.SetSearchEngine(ctx.GetString("engine") ?? "");
            SyncSearchEngineComboBox();
        }

        private void OnToggleStartpageFavoritesMessage(WebMessageContext ctx)
        {
            if (!ctx.TryGet("visible", out var visEl)) return;
            AppSettingsService.Instance.Settings.IsStartpageFavoritesVisible = visEl.GetBoolean();
            AppSettingsService.Instance.Save();
        }

        private void OnSaveStartpageShortcutsMessage(WebMessageContext ctx)
        {
            if (!ctx.TryGet("shortcuts", out var scEl)) return;
            var shortcuts = JsonSerializer.Deserialize<List<StartpageShortcut>>(scEl.GetRawText());
            if (shortcuts == null) return;

            AppSettingsService.Instance.Settings.StartpageShortcuts = shortcuts;
            AppSettingsService.Instance.Save();
        }

        #endregion

        #region Chrome Web Store

        private async Task OnInstallExtensionFromWebStoreMessage(WebMessageContext ctx)
        {
            string extId = ctx.GetString("extensionId") ?? "";
            string extName = ctx.GetString("extensionName") ?? Tr.Get("Ext_Generic");
            if (string.IsNullOrWhiteSpace(extId)) return;

            var profile = ctx.Core.Profile;
            if (profile == null) return;

            try
            {
                if (!ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, extId))
                {
                    await ctx.CallPageAsync("onEchoExtensionInstallResult", false, "Vom Benutzer abgebrochen");
                    return;
                }

                var ext = await ExtensionService.Instance.DownloadAndInstallExtensionAsync(profile, extId, extName);
                await ctx.CallPageAsync("onEchoExtensionInstallResult", true, "Installiert");
                ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? extName, ext?.Id ?? extId);
                await RefreshExtensionsListAsync();
            }
            catch (Exception ex)
            {
                await ctx.CallPageAsync("onEchoExtensionInstallResult", false, ex.Message);
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_DialogTitle"), Tr.Format("Ext_DownloadInstallError", ex.Message), MessageBoxImage.Error);
            }
        }

        #endregion

        #region Einstellungsseite

        private void OnUpdateSettingMessage(WebMessageContext ctx)
        {
            string? key = ctx.GetString("key");
            if (key == null || !ctx.TryGet("value", out var value)) return;

            ApplySettingFromPage(ctx, key, value);
            AppSettingsService.Instance.Save();
        }

        private void ApplySettingFromPage(WebMessageContext ctx, string key, JsonElement value)
        {
            var settings = AppSettingsService.Instance.Settings;
            switch (key)
            {
                // Allgemein
                case "StartupBehavior":
                    settings.StartupBehavior = value.GetString() ?? "startpage";
                    break;
                case "CustomStartupUrl":
                    settings.CustomStartupUrl = value.GetString() ?? "";
                    break;
                case "ShowHomeButton":
                    settings.ShowHomeButton = value.GetBoolean();
                    btnHome.Visibility = settings.ShowHomeButton ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "IsStartpageFavoritesVisible":
                    settings.IsStartpageFavoritesVisible = value.GetBoolean();
                    break;
                case "Language":
                    settings.Language = value.GetString() ?? "de";
                    // LanguageChanged aktualisiert alle Fenster und die internen Seiten
                    LocalizationService.Instance.SetLanguage(settings.Language);
                    break;

                // Suche
                case "SearchEngine":
                    AppSettingsService.Instance.SetSearchEngine(value.GetString() ?? "duckduckgo");
                    SyncSearchEngineComboBox();
                    break;
                case "EnableSearchSuggestions":
                    settings.EnableSearchSuggestions = value.GetBoolean();
                    break;

                // Erscheinungsbild
                case "IsBookmarksBarVisible":
                    bool showBm = value.GetBoolean();
                    _isBookmarksBarVisible = showBm;
                    borderBookmarksBar.Visibility = showBm ? Visibility.Visible : Visibility.Collapsed;
                    menuChkBookmarksBar.IsChecked = showBm;
                    settings.IsBookmarksBarVisible = showBm;
                    break;
                case "IsSidebarVisible":
                    bool showSb = value.GetBoolean();
                    borderSidebar.Visibility = showSb ? Visibility.Visible : Visibility.Collapsed;
                    menuChkSidebar.IsChecked = showSb;
                    settings.IsSidebarVisible = showSb;
                    break;
                case "DefaultZoomPercent":
                    settings.DefaultZoomPercent = value.GetInt32();
                    ApplyDefaultZoom(settings.DefaultZoomPercent);
                    break;

                // Symbolleiste
                case "ShowSidebarButton":
                    settings.ShowSidebarButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowBackButton":
                    settings.ShowBackButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowForwardButton":
                    settings.ShowForwardButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowReloadButton":
                    settings.ShowReloadButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowSearchEngineSelector":
                    settings.ShowSearchEngineSelector = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowExtensionsButton":
                    settings.ShowExtensionsButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;
                case "ShowDownloadsButton":
                    settings.ShowDownloadsButton = value.GetBoolean();
                    ApplyToolbarButtonVisibilities();
                    break;

                // Datenschutz
                case "TrackingPreventionLevel":
                    settings.TrackingPreventionLevel = value.GetString() ?? "balanced";
                    ApplyTrackingPrevention(settings.TrackingPreventionLevel);
                    break;
                case "BlockPopups":
                    settings.BlockPopups = value.GetBoolean();
                    break;
                case "EnableJavaScript":
                    bool js = value.GetBoolean();
                    settings.EnableJavaScript = js;
                    ctx.Tab.JavaScriptEnabled = js;
                    ctx.Core.Settings.IsScriptEnabled = js;
                    break;
                case "IsAdBlockerEnabled":
                    settings.IsAdBlockerEnabled = value.GetBoolean();
                    _ = SyncShieldStateForAllTabsAsync();
                    UpdateShieldBadge();
                    if (popupShield.IsOpen) UpdateShieldUi();
                    break;
                case "SendDoNotTrack":
                    settings.SendDoNotTrack = value.GetBoolean();
                    break;

                // Downloads & Tabs
                case "AskDownloadLocation":
                    settings.AskDownloadLocation = value.GetBoolean();
                    break;
                case "OpenNewTabInBackground":
                    settings.OpenNewTabInBackground = value.GetBoolean();
                    break;
                case "WarnOnClosingMultipleTabs":
                    settings.WarnOnClosingMultipleTabs = value.GetBoolean();
                    break;

                default:
                    Debug.WriteLine($"[Echo] Unbekannte Einstellung von der Einstellungsseite: '{key}'");
                    break;
            }
        }

        private void OnSetThemePresetMessage(WebMessageContext ctx)
        {
            string presetStr = ctx.GetString("preset") ?? "";
            if (!Enum.TryParse<ThemePreset>(presetStr, out var preset)) return;

            ThemeManager.Instance.ApplyPreset(preset);
            AppSettingsService.Instance.Settings.ThemePreset = presetStr;
            AppSettingsService.Instance.Save();
        }

        private void OnSetAccentColorMessage(WebMessageContext ctx)
        {
            string hex = ctx.GetString("hex") ?? "";
            try
            {
                Color color = ThemeManager.ColorFromHex(hex);
                ThemeManager.Instance.SetAccentColor(color);
                AppSettingsService.Instance.Settings.AccentColor = hex;
                AppSettingsService.Instance.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Ungültige Akzentfarbe '{hex}': {ex.Message}");
            }
        }

        private async Task OnBrowseDownloadFolderMessage(WebMessageContext ctx)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Tr.Get("Downloads_SelectFolder"),
                InitialDirectory = AppSettingsService.Instance.Settings.DownloadPath
            };
            if (dlg.ShowDialog() != true) return;

            AppSettingsService.Instance.Settings.DownloadPath = dlg.FolderName;
            AppSettingsService.Instance.Save();
            await ctx.CallPageAsync("onDownloadPathChanged", dlg.FolderName);
        }

        private void OnOpenDownloadFolderMessage(WebMessageContext ctx)
        {
            string path = AppSettingsService.Instance.Settings.DownloadPath;
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }

        private async Task OnClearBrowsingDataMessage(WebMessageContext ctx)
        {
            if (ctx.GetBool("clearHistory"))
            {
                _historyService.ClearHistory();
                UpdateFilteredHistory();
            }

            CoreWebView2BrowsingDataKinds kinds = 0;
            if (ctx.GetBool("clearCookies")) kinds |= CoreWebView2BrowsingDataKinds.Cookies;
            if (ctx.GetBool("clearCache")) kinds |= CoreWebView2BrowsingDataKinds.DiskCache;
            if (kinds != 0)
            {
                try
                {
                    await ctx.Core.Profile.ClearBrowsingDataAsync(kinds);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Echo] Browserdaten konnten nicht gelöscht werden: {ex.Message}");
                }
            }

            await ctx.CallPageAsync("onBrowsingDataCleared");
        }

        private async Task OnResetSettingsMessage(WebMessageContext ctx)
        {
            AppSettingsService.Instance.ResetToDefaults();
            RestoreSavedSettings();
            await ctx.CallPageAsync("onSettingUpdatedFromHost", AppSettingsService.Instance.Settings);
        }

        private async Task OnUpdateAdBlockFilterMessage(WebMessageContext ctx)
        {
            await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(AppSettingsService.Instance.Settings.AdBlockerFilterUrl, force: true);
            await ctx.CallPageAsync("onAdBlockFilterUpdated", AdBlockerService.Instance.BlockedDomainsCount);
        }

        #endregion
    }
}
