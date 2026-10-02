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
    /// <summary>Downloads-Flyout und Download-Aktionen.</summary>
    public partial class MainWindow
    {
        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
        {
            popupDownloads.IsOpen = true;
        }

        private void BtnOpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string downloadsPath = AppSettingsService.Instance.Settings.DownloadPath;
                if (string.IsNullOrWhiteSpace(downloadsPath) || !Directory.Exists(downloadsPath))
                {
                    downloadsPath = AppSettings.GetDefaultDownloadPath();
                }
                Process.Start(new ProcessStartInfo { FileName = downloadsPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open downloads: {ex.Message}");
            }
        }

        private async void BtnOpenDownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath && File.Exists(filePath))
            {
                if (filePath.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
                {
                    var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
                    if (profile != null)
                    {
                        try
                        {
                            string extName = Path.GetFileNameWithoutExtension(filePath);
                            if (ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, null, filePath))
                            {
                                var ext = await ExtensionService.Instance.InstallExtensionFromCrxAsync(profile, filePath);
                                ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? extName, ext?.Id);
                                await RefreshExtensionsListAsync();
                            }
                            return;
                        }
                        catch (Exception ex)
                        {
                            ThemedDialogWindow.ShowMessage(this, "Echo-Browser Erweiterungen", $"Fehler beim Installieren der Erweiterung:\n{ex.Message}", MessageBoxImage.Error);
                            return;
                        }
                    }
                }

                try
                {
                    Process.Start("explorer.exe", $"/select,\"{filePath}\"");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Could not open file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Eigener Download-Ablauf: Zielordner bestimmen, optional nachfragen, im Flyout anzeigen
        /// und heruntergeladene .crx-Erweiterungen zur Installation anbieten.
        /// </summary>
        private void HandleDownloadStarting(CoreWebView2 core, CoreWebView2DownloadStartingEventArgs args)
        {
            string fileName = Path.GetFileName(args.ResultFilePath);
            string customFolder = AppSettingsService.Instance.Settings.DownloadPath;
            bool hasCustomFolder = !string.IsNullOrWhiteSpace(customFolder) && Directory.Exists(customFolder);
            if (hasCustomFolder)
            {
                args.ResultFilePath = Path.Combine(customFolder, fileName);
            }

            if (AppSettingsService.Instance.Settings.AskDownloadLocation)
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = fileName,
                    InitialDirectory = hasCustomFolder
                        ? customFolder
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
                };
                if (saveDialog.ShowDialog() != true)
                {
                    args.Cancel = true;
                    return;
                }
                args.ResultFilePath = saveDialog.FileName;
            }
            args.Handled = true;

            var operation = args.DownloadOperation;
            var download = new DownloadItem
            {
                FileName = fileName,
                FilePath = args.ResultFilePath,
                TotalBytes = operation.TotalBytesToReceive.HasValue ? (long)operation.TotalBytesToReceive.Value : 0
            };

            Downloads.Insert(0, download);
            txtEmptyDownloads.Visibility = Visibility.Collapsed;
            downloadBadge.Visibility = Visibility.Visible;

            operation.BytesReceivedChanged += (s, e) => download.BytesReceived = (long)operation.BytesReceived;

            operation.StateChanged += (s, e) =>
            {
                if (operation.State == CoreWebView2DownloadState.Completed)
                {
                    download.IsCompleted = true;
                    download.State = "Abgeschlossen";
                    downloadBadge.Visibility = Visibility.Collapsed;

                    // Auto-install CRX if it is a downloaded extension
                    if (download.FilePath.EndsWith(".crx", StringComparison.OrdinalIgnoreCase) && File.Exists(download.FilePath))
                    {
                        _ = OfferCrxInstallAsync(core, download.FilePath);
                    }
                }
                else if (operation.State == CoreWebView2DownloadState.Interrupted)
                {
                    download.IsCancelled = true;
                    download.State = "Unterbrochen";
                    downloadBadge.Visibility = Visibility.Collapsed;
                }
            };
        }

        private async Task OfferCrxInstallAsync(CoreWebView2 core, string crxPath)
        {
            try
            {
                await Task.Delay(250);
                var profile = core.Profile;
                if (profile == null) return;

                string extName = Path.GetFileNameWithoutExtension(crxPath);
                if (!ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, null, crxPath)) return;

                var ext = await ExtensionService.Instance.InstallExtensionFromCrxAsync(profile, crxPath);
                ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? extName, ext?.Id);
                await RefreshExtensionsListAsync();
            }
            catch (Exception ex)
            {
                ThemedDialogWindow.ShowMessage(this, "Echo-Browser Erweiterungen", $"Automatische Installation der Erweiterung fehlgeschlagen:\n{ex.Message}", MessageBoxImage.Warning);
            }
        }
    }
}
