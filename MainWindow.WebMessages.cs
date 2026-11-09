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
            _webMessageRouter.Register("checkForUpdates", OnCheckForUpdatesMessage);
            _webMessageRouter.Register("makeDefaultBrowser", OnMakeDefaultBrowserMessage);
            _webMessageRouter.Register("importBookmarks", OnImportBookmarksMessage);
            _webMessageRouter.Register("exportBookmarks", OnExportBookmarksMessage);
            _webMessageRouter.Register("chooseStartpageBackground", OnChooseStartpageBackgroundMessage);
            _webMessageRouter.Register("clearStartpageBackground", OnClearStartpageBackgroundMessage);
            _webMessageRouter.Register("restartToApplyUpdate", OnRestartToApplyUpdateMessage);
        }

        #region Startseite

        private void OnNavigateMessage(WebMessageContext ctx)
        {
            string? url = ctx.GetString("url");
            if (!string.IsNullOrWhiteSpace(url))
            {
                NavigateToInput(url, ctx.Tab);
            }
        }

        private void OnSetSearchEngineMessage(WebMessageContext ctx)
        {
            // Alle Fenster gleichen ihre Auswahlliste an (OnSettingChanged)
            AppSettingsService.Instance.SetSearchEngine(ctx.GetString("engine") ?? "");
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

            if (!CanManageExtensions())
            {
                await ctx.CallPageAsync("onEchoExtensionInstallResult", false, Tr.Get("Ext_ManageInNormalWindow"));
                return;
            }

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

            if (SetSettingFromPage(key, value))
            {
                // Speichern und alle Fenster die Änderung übernehmen lassen (MainWindow.SettingsSync.cs)
                AppSettingsService.Instance.Changed(key);
            }
            else
            {
                Log.Warn($"Unbekannte Einstellung oder ungültiger Wert von der Einstellungsseite: '{key}'");
            }
        }

        /// <summary>
        /// Wert der Einstellungsseite übernehmen. Was ein Fenster daraus macht, steht in OnSettingChanged;
        /// hier bleiben nur Wirkungen, die es pro App genau einmal gibt (Sprache, Updates, Fensterstil aller Fenster).
        /// Textwerte mit fester Auswahl müssen in <see cref="SettingValues"/> stehen, sonst bleibt die Einstellung unverändert.
        /// </summary>
        /// <returns>false bei unbekanntem Schlüssel oder nicht erlaubtem Wert.</returns>
        internal static bool SetSettingFromPage(string key, JsonElement value)
        {
            var settings = AppSettingsService.Instance.Settings;
            switch (key)
            {
                // Allgemein
                case "StartupBehavior":
                    if (SettingValues.Match(SettingValues.StartupBehaviors, value.GetString()) is not { } startup) return false;
                    settings.StartupBehavior = startup;
                    break;
                case "CustomStartupUrl": settings.CustomStartupUrl = value.GetString() ?? ""; break;
                case "ShowHomeButton": settings.ShowHomeButton = value.GetBoolean(); break;
                case "IsStartpageFavoritesVisible": settings.IsStartpageFavoritesVisible = value.GetBoolean(); break;
                case "Language":
                    if (SettingValues.Match(LocalizationService.Instance.AvailableLanguages, value.GetString()) is not { } language) return false;
                    settings.Language = language;
                    // LanguageChanged aktualisiert alle Fenster und die internen Seiten
                    LocalizationService.Instance.SetLanguage(settings.Language);
                    break;

                // Suche
                case "SearchEngine":
                    if (SettingValues.Match(SettingValues.SearchEngines, value.GetString()) is not { } engine) return false;
                    settings.SearchEngine = engine;
                    break;
                case "EnableSearchSuggestions": settings.EnableSearchSuggestions = value.GetBoolean(); break;

                // Erscheinungsbild
                case "IsBookmarksBarVisible": settings.IsBookmarksBarVisible = value.GetBoolean(); break;
                case "IsSidebarVisible": settings.IsSidebarVisible = value.GetBoolean(); break;
                case "UseMica":
                    settings.UseMica = value.GetBoolean();
                    foreach (var window in Application.Current.Windows.OfType<MainWindow>())
                    {
                        window.ApplyWindowEffects();
                    }
                    break;
                case "DefaultZoomPercent": settings.DefaultZoomPercent = value.GetInt32(); break;

                // Symbolleiste
                case "ShowSidebarButton": settings.ShowSidebarButton = value.GetBoolean(); break;
                case "ShowBackButton": settings.ShowBackButton = value.GetBoolean(); break;
                case "ShowForwardButton": settings.ShowForwardButton = value.GetBoolean(); break;
                case "ShowReloadButton": settings.ShowReloadButton = value.GetBoolean(); break;
                case "ShowSearchEngineSelector": settings.ShowSearchEngineSelector = value.GetBoolean(); break;
                case "ShowExtensionsButton": settings.ShowExtensionsButton = value.GetBoolean(); break;
                case "ShowDownloadsButton": settings.ShowDownloadsButton = value.GetBoolean(); break;

                // Datenschutz
                case "TrackingPreventionLevel":
                    if (SettingValues.Match(SettingValues.TrackingPreventionLevels, value.GetString()) is not { } level) return false;
                    settings.TrackingPreventionLevel = level;
                    break;
                case "BlockPopups": settings.BlockPopups = value.GetBoolean(); break;
                case "EnableJavaScript": settings.EnableJavaScript = value.GetBoolean(); break;
                case "IsAdBlockerEnabled": settings.IsAdBlockerEnabled = value.GetBoolean(); break;
                // Gilt für neu geöffnete Inkognito-Fenster
                case "AllowExtensionsInIncognito": settings.AllowExtensionsInIncognito = value.GetBoolean(); break;
                case "SendDoNotTrack": settings.SendDoNotTrack = value.GetBoolean(); break;
                case "SavePasswords": settings.SavePasswords = value.GetBoolean(); break;
                case "AutofillForms": settings.AutofillForms = value.GetBoolean(); break;

                // Downloads & Tabs
                case "AskDownloadLocation": settings.AskDownloadLocation = value.GetBoolean(); break;
                case "OpenNewTabInBackground": settings.OpenNewTabInBackground = value.GetBoolean(); break;
                case "VerticalTabs":
                    settings.VerticalTabs = value.GetBoolean();
                    ApplyTabLayoutToAllWindows();
                    break;
                case "TabSleepMinutes": settings.TabSleepMinutes = Math.Max(0, value.GetInt32()); break;
                case "WarnOnClosingMultipleTabs": settings.WarnOnClosingMultipleTabs = value.GetBoolean(); break;

                // Updates (Velopack)
                case "AutoCheckForUpdates":
                    settings.AutoCheckForUpdates = value.GetBoolean();
                    if (settings.AutoCheckForUpdates) UpdateService.Instance.StartAutoCheckTimer();
                    else UpdateService.Instance.StopAutoCheckTimer();
                    break;
                case "CheckPrereleaseUpdates": settings.CheckPrereleaseUpdates = value.GetBoolean(); break;
                // Keine "UpdateUrl": Die Update-Quelle ist fest (UpdateService.DefaultUpdateUrl)

                default:
                    return false;
            }
            return true;
        }

        private void OnSetThemePresetMessage(WebMessageContext ctx)
        {
            ThemeManager.Instance.ApplyAndSavePreset(ctx.GetString("preset") ?? "");
        }

        private void OnSetAccentColorMessage(WebMessageContext ctx)
        {
            string hex = ctx.GetString("hex") ?? "";
            if (!ThemeManager.Instance.ApplyAndSaveAccentColor(hex))
            {
                Log.Warn($"Ungültige Akzentfarbe '{hex}'");
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
                    Log.Warn("Browserdaten konnten nicht gelöscht werden", ex);
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
            try
            {
                await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(AppSettingsService.Instance.Settings.AdBlockerFilterUrl, force: true);
            }
            catch (Exception ex)
            {
                // Sonst bliebe der Knopf auf der Einstellungsseite dauerhaft deaktiviert
                Log.Warn("Filterliste konnte nicht aktualisiert werden", ex);
                await ctx.CallPageAsync("onAdBlockFilterUpdateFailed", ex.Message);
                return;
            }
            await ctx.CallPageAsync("onAdBlockFilterUpdated", AdBlockerService.Instance.BlockedDomainsCount);
        }

        private async Task OnCheckForUpdatesMessage(WebMessageContext ctx)
        {
            await UpdateService.Instance.CheckForUpdatesAsync(isManualCheck: true);
        }

        private void OnRestartToApplyUpdateMessage(WebMessageContext ctx)
        {
            SaveSessionForRestart();
            UpdateService.Instance.RestartAndApplyUpdate();
        }

        #endregion
    }
}
