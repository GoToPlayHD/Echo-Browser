using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>
    /// Downloads-Flyout: gemeinsame Liste aller Fenster (DownloadService) mit Fortschritt, Pause/Fortsetzen,
    /// Abbrechen, Öffnen, "Im Ordner anzeigen", Erneut versuchen und Entfernen.
    /// </summary>
    public partial class DownloadsPanel : UserControl
    {
        /// <summary>Für eine heruntergeladene .crx-Datei wurde "Installieren" angeklickt.</summary>
        public event Action<string>? InstallExtensionRequested;

        /// <summary>Abgebrochener Download soll neu gestartet werden (Adresse in neuem Tab laden).</summary>
        public event Action<string>? RetryRequested;

        public DownloadsPanel()
        {
            InitializeComponent();
            listDownloads.ItemsSource = DownloadService.Instance.Items;
            DownloadService.Instance.Items.CollectionChanged += (s, e) => UpdateEmptyState();
            UpdateEmptyState();
        }

        /// <summary>Vor dem Öffnen: prüfen, ob fertige Dateien inzwischen gelöscht wurden.</summary>
        public void Refresh()
        {
            foreach (var item in DownloadService.Instance.Items)
            {
                item.RefreshFileState();
            }
            UpdateEmptyState();
        }

        private void UpdateEmptyState()
        {
            bool empty = DownloadService.Instance.Items.Count == 0;
            txtEmptyDownloads.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            btnClearFinished.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        }

        private static DownloadItem? ItemOf(object sender) => (sender as FrameworkElement)?.Tag as DownloadItem;

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
                Log.Warn("Downloads-Ordner konnte nicht geöffnet werden", ex);
            }
        }

        private void BtnClearFinished_Click(object sender, RoutedEventArgs e) => DownloadService.Instance.ClearFinished();

        private void FileName_Click(object sender, MouseButtonEventArgs e)
        {
            if (ItemOf(sender) is not { IsCompleted: true } item || !File.Exists(item.FilePath)) return;

            if (item.IsCrx)
            {
                InstallExtensionRequested?.Invoke(item.FilePath);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo { FileName = item.FilePath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn($"Datei {item.FilePath} konnte nicht geöffnet werden", ex);
            }
        }

        private void BtnShowInFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { } item || !File.Exists(item.FilePath)) return;
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
            }
            catch (Exception ex)
            {
                Log.Warn("Datei konnte im Explorer nicht angezeigt werden", ex);
            }
        }

        private void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is { } item && File.Exists(item.FilePath))
            {
                InstallExtensionRequested?.Invoke(item.FilePath);
            }
        }

        private void BtnPauseResume_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { Operation: { } operation } item) return;
            try
            {
                if (item.IsPaused)
                {
                    operation.Resume();
                    item.IsPaused = false;
                }
                else
                {
                    operation.Pause();
                    item.IsPaused = true;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Download konnte nicht pausiert/fortgesetzt werden", ex);
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { } item) return;
            try
            {
                item.Operation?.Cancel();
            }
            catch (Exception ex)
            {
                Log.Warn("Download konnte nicht abgebrochen werden", ex);
            }
            item.IsPaused = false;
            item.IsCancelled = true;
        }

        private void BtnRetry_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is not { } item || string.IsNullOrWhiteSpace(item.SourceUrl)) return;
            DownloadService.Instance.Remove(item);
            RetryRequested?.Invoke(item.SourceUrl);
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (ItemOf(sender) is { } item)
            {
                DownloadService.Instance.Remove(item);
            }
        }
    }
}
