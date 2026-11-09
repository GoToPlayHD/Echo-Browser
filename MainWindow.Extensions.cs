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
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>Browser-Erweiterungen: Flyout, angeheftete Erweiterungen, Installation & Verwaltung.</summary>
    public partial class MainWindow
    {
        private async void BtnExtensions_Click(object sender, RoutedEventArgs e)
        {
            await RefreshExtensionsListAsync();
            popupExtensions.IsOpen = true;
        }

        private async Task RefreshExtensionsListAsync()
        {
            try
            {
                var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
                if (profile == null)
                {
                    extensionsPanel.ShowExtensions(null);
                    return;
                }

                extensionsPanel.ShowExtensions(await ExtensionService.Instance.GetInstalledExtensionsAsync(profile));
                extensionsPanel.ShowUpdateStatus(ExtensionService.Instance.UpdateStatus);

                UpdatePinnedExtensionsToolbar();
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to refresh extensions", ex);
            }
        }

        private async void UpdatePinnedExtensionsToolbar()
        {
            try
            {
                int version = ++_pinnedToolbarVersion;
                var profile = (ActiveTab?.WebView?.CoreWebView2 ?? Tabs.Select(t => t.WebView?.CoreWebView2).FirstOrDefault(c => c != null))?.Profile;
                if (profile == null)
                {
                    pnlPinnedExtensions.Children.Clear();
                    return;
                }

                var pinnedIds = AppSettingsService.Instance.Settings.PinnedExtensionIds;
                var extensions = pinnedIds == null || pinnedIds.Count == 0
                    ? Array.Empty<CoreWebView2BrowserExtension>()
                    : await ExtensionService.Instance.GetInstalledExtensionsAsync(profile);

                // Erst nach dem Warten leeren: Ein späterer Aufruf hat die Leiste inzwischen schon aufgebaut
                if (version != _pinnedToolbarVersion) return;
                pnlPinnedExtensions.Children.Clear();
                if (pinnedIds == null || pinnedIds.Count == 0) return;

                foreach (var extId in pinnedIds.ToList())
                {
                    var ext = extensions.FirstOrDefault(e => e.Id.Equals(extId, StringComparison.OrdinalIgnoreCase));
                    if (ext == null) continue;

                    var btn = new Button
                    {
                        Style = (Style)FindResource("IconButtonStyle"),
                        ToolTip = ext.Name,
                        Tag = ext,
                        Width = 28,
                        Height = 28,
                        Margin = new Thickness(0, 0, 2, 0)
                    };

                    string? iconPath = ExtensionService.Instance.GetExtensionIconPath(ext.Id, ext.Name);
                    if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                    {
                        try
                        {
                            var img = new Image
                            {
                                Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute)),
                                Width = 15,
                                Height = 15,
                                Stretch = Stretch.Uniform
                            };
                            btn.Content = img;
                        }
                        catch
                        {
                            btn.Content = CreateDefaultExtensionIcon();
                        }
                    }
                    else
                    {
                        btn.Content = CreateDefaultExtensionIcon();
                    }

                    // Left click opens extension popup GUI (or options if no popup)
                    btn.Click += (s, e) =>
                    {
                        OpenExtensionPopup(ext, btn);
                    };

                    // Context Menu for pinned icon
                    var ctx = new ContextMenu
                    {
                        Background = (Brush)FindResource("SurfaceBrush"),
                        BorderBrush = (Brush)FindResource("BorderBrush"),
                        Foreground = (Brush)FindResource("TextPrimaryBrush")
                    };

                    var miTitle = new MenuItem
                    {
                        Header = ext.Name,
                        FontWeight = FontWeights.Bold,
                        IsEnabled = false
                    };
                    ctx.Items.Add(miTitle);
                    ctx.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });

                    if (ExtensionService.Instance.HasPopup(ext.Id, ext.Name))
                    {
                        var miPopup = new MenuItem { Header = Tr.Get("Ext_MenuOpenPopup") };
                        miPopup.Click += (s, e) => OpenExtensionPopup(ext, btn);
                        ctx.Items.Add(miPopup);
                    }

                    var miOptions = new MenuItem { Header = Tr.Get("Ext_MenuOptions") };
                    miOptions.Click += (s, e) =>
                    {
                        string? optPage = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
                        if (!string.IsNullOrWhiteSpace(optPage))
                        {
                            AddNewTab($"chrome-extension://{ext.Id}/{optPage}");
                        }
                        else
                        {
                            ThemedDialogWindow.ShowMessage(this, ext.Name, Tr.Format("Ext_NoOptionsFound", ext.Name));
                        }
                    };
                    ctx.Items.Add(miOptions);

                    var miUnpin = new MenuItem { Header = Tr.Get("Ext_MenuUnpin") };
                    miUnpin.Click += (s, e) =>
                    {
                        AppSettingsService.Instance.Settings.PinnedExtensionIds.Remove(ext.Id);
                        AppSettingsService.Instance.Save();
                        UpdatePinnedExtensionsToolbar();
                    };
                    ctx.Items.Add(miUnpin);

                    ctx.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });

                    var miRemove = new MenuItem { Header = Tr.Get("Ext_MenuRemove") };
                    miRemove.Click += async (s, e) => await ConfirmAndRemoveExtensionAsync(ext);
                    ctx.Items.Add(miRemove);

                    btn.ContextMenu = ctx;
                    pnlPinnedExtensions.Children.Add(btn);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to update pinned extensions toolbar", ex);
            }
        }

        /// <summary>Erweiterung entfernen, vom Anheften lösen und die entpackte Kopie aufräumen.</summary>
        private async Task RemoveExtensionAsync(CoreWebView2BrowserExtension ext)
        {
            string id = ext.Id;
            await ext.RemoveAsync();
            AppSettingsService.Instance.Settings.PinnedExtensionIds.Remove(id);
            AppSettingsService.Instance.Save();
            ExtensionService.Instance.ForgetExtension(id);
            await RefreshExtensionsListAsync();
        }

        private FrameworkElement CreateDefaultExtensionIcon()
        {
            return new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M20.5 11H19V7c0-1.1-.9-2-2-2h-4V3.5C13 2.12 11.88 1 10.5 1S8 2.12 8 3.5V5H4c-1.1 0-1.99.9-1.99 2v3.8H3.5c1.49 0 2.7 1.21 2.7 2.7s-1.21 2.7-2.7 2.7H2V20c0 1.1.9 2 2 2h3.8v-1.5c0-1.49 1.21-2.7 2.7-2.7 1.49 0 2.7 1.21 2.7 2.7V22H17c1.1 0 2-.9 2-2v-4h1.5c1.38 0 2.5-1.12 2.5-2.5s-1.12-2.5-2.5-2.5z"),
                Fill = (Brush)FindResource("AccentSilverBrush"),
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform
            };
        }

        private void TogglePinExtension(CoreWebView2BrowserExtension ext)
        {
            var pinned = AppSettingsService.Instance.Settings.PinnedExtensionIds;
            if (!pinned.Remove(ext.Id))
            {
                pinned.Add(ext.Id);
            }
            AppSettingsService.Instance.Save();
            UpdatePinnedExtensionsToolbar();
        }

        private void OpenExtensionSettings(CoreWebView2BrowserExtension ext)
        {
            popupExtensions.IsOpen = false;
            string? optPage = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
            if (!string.IsNullOrWhiteSpace(optPage))
            {
                AddNewTab($"chrome-extension://{ext.Id}/{optPage}");
            }
            else
            {
                ThemedDialogWindow.ShowMessage(this, ext.Name, Tr.Format("Ext_NoOptionsDefined", ext.Name));
            }
        }

        private async void OpenExtensionPopup(CoreWebView2BrowserExtension ext, FrameworkElement anchor)
        {
            if (_webViewEnvironment == null) return;

            // Im InPrivate-Profil sperrt WebView2 Erweiterungen – das Popup liefe sonst im normalen Profil
            if (_isIncognito && !_incognitoWithExtensions)
            {
                ThemedDialogWindow.ShowMessage(this, ext.Name, Tr.Get("Ext_DisabledInIncognito"));
                return;
            }

            if (_activeExtensionPopup != null)
            {
                bool isSame = _activeExtensionPopup.ExtensionId.Equals(ext.Id, StringComparison.OrdinalIgnoreCase);
                try { _activeExtensionPopup.Close(); } catch { }
                _activeExtensionPopup = null;
                if (isSame) return;
            }

            // Klick auf den Knopf eines offenen Popups: das Popup hat sich beim Klick schon selbst geschlossen
            // (Deactivated) – dann nur schließen, nicht sofort wieder öffnen
            if (string.Equals(_lastClosedExtensionPopupId, ext.Id, StringComparison.OrdinalIgnoreCase) &&
                DateTime.UtcNow - _lastClosedExtensionPopupAt < TimeSpan.FromMilliseconds(400))
            {
                return;
            }

            string? popupPage = ExtensionService.Instance.GetExtensionPopupPage(ext.Id, ext.Name);
            string? optPageFallback = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
            string? iconPath = ExtensionService.Instance.GetExtensionIconPath(ext.Id, ext.Name);

            var win = new ExtensionPopupWindow
            {
                Owner = this
            };

            win.OpenOptionsRequested += (id, options) =>
            {
                AddNewTab($"chrome-extension://{id}/{options}");
            };

            var keepOpenIds = AppSettingsService.Instance.Settings.KeepOpenExtensionPopupIds;
            win.SetKeptOpen(keepOpenIds.Contains(ext.Id, StringComparer.OrdinalIgnoreCase));
            win.KeepOpenChanged += (id, keepOpen) =>
            {
                keepOpenIds.RemoveAll(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
                if (keepOpen) keepOpenIds.Add(id);
                AppSettingsService.Instance.Save();
            };

            win.Closed += (_, _) =>
            {
                if (_activeExtensionPopup != win) return;
                _activeExtensionPopup = null;
                _lastClosedExtensionPopupId = ext.Id;
                _lastClosedExtensionPopupAt = DateTime.UtcNow;
            };

            // Unsichtbar anzeigen (die WebView braucht ein Fenster), sichtbar erst, wenn es wirklich ein Popup gibt
            win.Opacity = 0;
            win.PositionUnderneath(anchor);
            win.Show();
            _activeExtensionPopup = win;

            var result = await win.InitializeAndNavigateAsync(_webViewEnvironment, ext.Id, ext.Name, popupPage, optPageFallback, iconPath,
                BrowserEnvironment.CreateControllerOptions(_webViewEnvironment, _isIncognito, _incognitoWithExtensions));

            if (result == ExtensionPopupWindow.ActionResult.PopupShown)
            {
                win.Opacity = 1;
                return;
            }

            // Kein Popup: Die Erweiterung hat den Klick bekommen (öffnet z.B. selbst einen Tab) – oder es gibt nichts zu tun
            try { win.Close(); } catch { }
            if (result == ExtensionPopupWindow.ActionResult.NoAction)
            {
                if (!string.IsNullOrWhiteSpace(optPageFallback))
                {
                    AddNewTab($"chrome-extension://{ext.Id}/{optPageFallback}");
                }
                else
                {
                    ThemedDialogWindow.ShowMessage(this, ext.Name, Tr.Format("Ext_NoPopupDefined", ext.Name));
                }
            }
        }

        private void InitializeExtensionsPanel()
        {
            extensionsPanel.PinToggleRequested += TogglePinExtension;
            extensionsPanel.OpenSettingsRequested += OpenExtensionSettings;
            extensionsPanel.OpenPopupRequested += (ext, anchor) =>
            {
                popupExtensions.IsOpen = false;
                OpenExtensionPopup(ext, anchor);
            };
            extensionsPanel.EnableRequested += async (ext, enable) => await SetExtensionEnabledAsync(ext, enable);
            extensionsPanel.RemoveRequested += async ext => await ConfirmAndRemoveExtensionAsync(ext);
            extensionsPanel.InstallFromFileRequested += async () => await InstallExtensionFromFileAsync();
            extensionsPanel.OpenWebStoreRequested += () =>
            {
                popupExtensions.IsOpen = false;
                AddNewTab("https://chromewebstore.google.com/");
            };
            extensionsPanel.CheckUpdatesRequested += async () =>
            {
                if (CanManageExtensions()) await CheckExtensionUpdatesAsync(interactive: true);
            };
        }

        // Für alle Fenster gemeinsam: Erweiterungs-Updates kurz nach dem Start, danach alle 24 Stunden
        private static DispatcherTimer? _extensionUpdateTimer;

        private void StartExtensionUpdateChecks()
        {
            if (_isIncognito || _extensionUpdateTimer != null) return;

            _extensionUpdateTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
            _extensionUpdateTimer.Tick += async (s, e) =>
            {
                _extensionUpdateTimer.Interval = TimeSpan.FromHours(24);
                var window = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => !w.IsIncognito && w.IsLoaded);
                if (window != null)
                {
                    // Automatisch nie mit Dialog: Updates mit neuen Rechten warten auf »Nach Updates suchen«
                    await window.CheckExtensionUpdatesAsync(interactive: false);
                }
            };
            _extensionUpdateTimer.Start();
        }

        /// <summary>
        /// Neue Versionen suchen und für den nächsten Start vormerken (Services/ExtensionService.Updates.cs).
        /// Interaktiv fragt bei neuen Berechtigungen nach, sonst zählen solche Updates nur als »wartet auf Zustimmung«.
        /// </summary>
        private async Task CheckExtensionUpdatesAsync(bool interactive)
        {
            var profile = (ActiveTab?.WebView?.CoreWebView2 ?? Tabs.Select(t => t.WebView?.CoreWebView2).FirstOrDefault(c => c != null))?.Profile;
            if (profile == null) return;

            var service = ExtensionService.Instance;
            var check = service.CheckForUpdatesAsync(profile,
                interactive ? update => ThemedDialogWindow.ShowExtensionUpdatePrompt(this, update) : null);
            extensionsPanel.ShowUpdateStatus(service.UpdateStatus);
            await check;
            extensionsPanel.ShowUpdateStatus(service.UpdateStatus);
        }

        private async Task InstallExtensionFromFileAsync()
        {
            if (!CanManageExtensions()) return;
            var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
            if (profile == null)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_Title"), Tr.Get("Ext_ProfileNotReady"), MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = Tr.Get("Ext_SelectCrxFile"),
                Filter = $"{Tr.Get("Ext_CrxFileFilter")} (*.crx)|*.crx|{Tr.Get("Common_AllFiles")} (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // Erst entpacken: Die Nachfrage zeigt die Berechtigungen aus dem Manifest
                    var prepared = ExtensionService.Instance.PrepareFromCrx(dlg.FileName);
                    if (!ThemedDialogWindow.ShowExtensionInstallPrompt(this, prepared, dlg.FileName))
                    {
                        ExtensionService.Instance.DiscardPrepared(prepared);
                        return;
                    }

                    var ext = await ExtensionService.Instance.InstallPreparedAsync(profile, prepared);
                    ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? prepared.Name, ext?.Id);
                    await RefreshExtensionsListAsync();
                }
                catch (Exception ex)
                {
                    ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_InstallFailedTitle"), Tr.Format("Ext_InstallError", ex.Message), MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Installieren, Entfernen und Ein-/Ausschalten nur in normalen Fenstern: Im Inkognito-Fenster würde es nur
        /// das temporäre bzw. InPrivate-Profil betreffen – und Entfernen löschte die Dateien auch für normale Fenster.
        /// </summary>
        private bool CanManageExtensions()
        {
            if (!_isIncognito) return true;
            ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_Title"), Tr.Get("Ext_ManageInNormalWindow"));
            return false;
        }

        private async Task ConfirmAndRemoveExtensionAsync(CoreWebView2BrowserExtension ext)
        {
            if (!CanManageExtensions()) return;
            if (!ThemedDialogWindow.ShowExtensionRemovePrompt(this, ext.Name)) return;

            try
            {
                await RemoveExtensionAsync(ext);
            }
            catch (Exception ex)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_Title"), Tr.Format("Ext_RemoveError", ex.Message), MessageBoxImage.Error);
            }
        }

        private async Task SetExtensionEnabledAsync(CoreWebView2BrowserExtension ext, bool enable)
        {
            if (!CanManageExtensions()) return;
            try
            {
                await ext.EnableAsync(enable);
                await RefreshExtensionsListAsync();
            }
            catch (Exception ex)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_Title"), Tr.Format("Ext_ToggleError", ex.Message), MessageBoxImage.Warning);
            }
        }

    }
}
