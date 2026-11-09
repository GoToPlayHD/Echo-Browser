using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>Downloads-Flyout, Fortschrittsring am Download-Knopf und der eigene Download-Ablauf.</summary>
    public partial class MainWindow
    {
        /// <summary>Umfang des Fortschrittsrings in Einheiten der Strichstärke (π · (22 − 2) / 2).</summary>
        private const double ProgressRingLength = 31.4;

        private bool _isDownloadRingSpinning;

        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
        {
            downloadsPanel.Refresh();
            popupDownloads.IsOpen = true;
        }

        private void InitializeDownloadsPanel()
        {
            downloadsPanel.InstallExtensionRequested += crxPath =>
            {
                var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
                if (profile != null)
                {
                    _ = InstallCrxWithPromptAsync(profile, crxPath);
                }
            };
            downloadsPanel.RetryRequested += url =>
            {
                popupDownloads.IsOpen = false;
                AddNewTab(url, activateTab: false);
            };

            DownloadService.Instance.ProgressChanged += UpdateDownloadProgressRing;
            Closed += (s, e) => DownloadService.Instance.ProgressChanged -= UpdateDownloadProgressRing;
            UpdateDownloadProgressRing();
        }

        /// <summary>Ring um den Download-Knopf: Fortschritt aller laufenden Downloads (drehend bei unbekannter Größe).</summary>
        private void UpdateDownloadProgressRing()
        {
            double? progress = DownloadService.Instance.OverallProgress();
            var rotation = (RotateTransform)ellipseDownloadProgress.RenderTransform;

            if (progress == null)
            {
                gridDownloadRing.Visibility = Visibility.Collapsed;
                rotation.BeginAnimation(RotateTransform.AngleProperty, null);
                _isDownloadRingSpinning = false;
                return;
            }

            gridDownloadRing.Visibility = Visibility.Visible;
            if (progress < 0)
            {
                ellipseDownloadProgress.StrokeDashArray = new DoubleCollection { ProgressRingLength * 0.25, 100 };
                if (!_isDownloadRingSpinning)
                {
                    rotation.BeginAnimation(RotateTransform.AngleProperty,
                        new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
                    _isDownloadRingSpinning = true;
                }
            }
            else
            {
                if (_isDownloadRingSpinning)
                {
                    rotation.BeginAnimation(RotateTransform.AngleProperty, null);
                    _isDownloadRingSpinning = false;
                }
                rotation.Angle = -90; // Fortschritt beginnt oben
                ellipseDownloadProgress.StrokeDashArray = new DoubleCollection { Math.Max(0.01, progress.Value * ProgressRingLength), 100 };
            }
        }

        /// <summary>
        /// Eigener Download-Ablauf: Zielordner bestimmen, optional nachfragen, in der gemeinsamen Liste anzeigen
        /// und heruntergeladene .crx-Erweiterungen zur Installation anbieten.
        /// </summary>
        /// <summary>Geschlossene Tabs, deren WebView für laufende Downloads weiterlebt (MainWindow.Tabs.cs CloseTab).</summary>
        private readonly List<BrowserTab> _tabsKeptForDownloads = new();

        /// <summary>Downloads in Tabs dieses Fensters, auch in geschlossenen, die nur noch für den Download weiterleben.</summary>
        private int ActiveDownloadCount => Tabs.Concat(_tabsKeptForDownloads).Sum(t => t.ActiveDownloads);

        private void OnTabDownloadFinished(BrowserTab tab)
        {
            tab.ActiveDownloads = Math.Max(0, tab.ActiveDownloads - 1);
            if (tab.ActiveDownloads > 0 || !_tabsKeptForDownloads.Remove(tab)) return;

            if (tab.WebView != null)
            {
                WebViewContainer.Children.Remove(tab.WebView);
            }
            tab.Dispose();
        }

        private void HandleDownloadStarting(BrowserTab tab, CoreWebView2 core, CoreWebView2DownloadStartingEventArgs args)
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
                FileName = Path.GetFileName(args.ResultFilePath),
                FilePath = args.ResultFilePath,
                SourceUrl = operation.Uri,
                IsIncognito = _isIncognito,
                TotalBytes = operation.TotalBytesToReceive.HasValue ? (long)operation.TotalBytesToReceive.Value : 0,
                Operation = operation
            };

            DownloadService.Instance.Add(download);

            // Der Download braucht die WebView des Tabs bis zum Ende
            tab.ActiveDownloads++;
            bool finished = false;
            void Finished()
            {
                if (finished) return;
                finished = true;
                OnTabDownloadFinished(tab);
            }

            // Tab nur für den Download geöffnet ("Erneut versuchen", Link mit target=_blank): kein leerer Tab zurück
            if ((string.IsNullOrEmpty(core.Source) || core.Source == "about:blank") && !core.CanGoBack && Tabs.Contains(tab) && Tabs.Count > 1)
            {
                Dispatcher.BeginInvoke(() => CloseTab(tab, rememberForReopen: false));
            }

            // Wie in Chrome: das Flyout zeigt den neuen Download sofort
            if (IsActive)
            {
                downloadsPanel.Refresh();
                popupDownloads.IsOpen = true;
            }

            operation.BytesReceivedChanged += (s, e) =>
            {
                download.BytesReceived = (long)operation.BytesReceived;
                if (download.TotalBytes == 0 && operation.TotalBytesToReceive is ulong total)
                {
                    download.TotalBytes = (long)total;
                }
            };

            operation.StateChanged += (s, e) =>
            {
                switch (operation.State)
                {
                    case CoreWebView2DownloadState.Completed:
                        download.IsPaused = false;
                        download.BytesReceived = (long)operation.BytesReceived;
                        download.IsCompleted = true;
                        download.Operation = null;
                        Finished();

                        // Auto-install CRX if it is a downloaded extension – nur aus dem Chrome Web Store bzw. von Googles
                        // Update-Server; .crx von anderen Seiten lassen sich über "Installieren" im Download-Flyout installieren
                        if (download.IsCrx && File.Exists(download.FilePath) && DownloadService.IsTrustedCrxSource(download.SourceUrl))
                        {
                            _ = OfferCrxInstallAsync(core, download.FilePath);
                        }
                        break;

                    case CoreWebView2DownloadState.Interrupted:
                        // Pausieren meldet WebView2 ebenfalls als "unterbrochen" – das merken wir uns selbst
                        // Netzabbruch o.ä., den WebView2 fortsetzen kann: als pausiert zeigen ("Fortsetzen") statt abgebrochen
                        if (download.IsPaused || operation.InterruptReason == CoreWebView2DownloadInterruptReason.UserPaused ||
                            (operation.CanResume && operation.InterruptReason != CoreWebView2DownloadInterruptReason.UserCanceled))
                        {
                            download.IsPaused = true;
                        }
                        else
                        {
                            download.IsCancelled = true;
                            download.Operation = null;
                            Finished();
                        }
                        break;

                    case CoreWebView2DownloadState.InProgress:
                        download.IsPaused = false;
                        break;
                }
            };
        }

        private async Task OfferCrxInstallAsync(CoreWebView2 core, string crxPath)
        {
            await Task.Delay(250);
            try
            {
                var profile = core.Profile;
                if (profile != null)
                {
                    await InstallCrxWithPromptAsync(profile, crxPath);
                }
            }
            catch (Exception ex)
            {
                // z.B. wenn der Tab inzwischen geschlossen wurde
                Log.Warn("Erweiterungsinstallation nach Download nicht möglich", ex);
            }
        }

        /// <summary>Nachfrage anzeigen und eine .crx-Erweiterung installieren (nach Download oder per Klick im Flyout).</summary>
        private async Task InstallCrxWithPromptAsync(CoreWebView2Profile profile, string crxPath)
        {
            if (!CanManageExtensions()) return;
            try
            {
                // Erst entpacken: Die Nachfrage zeigt die Berechtigungen aus dem Manifest
                var prepared = ExtensionService.Instance.PrepareFromCrx(crxPath);
                if (!ThemedDialogWindow.ShowExtensionInstallPrompt(this, prepared, crxPath))
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
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Ext_DialogTitle"), Tr.Format("Ext_InstallError", ex.Message), MessageBoxImage.Error);
            }
        }
    }
}
