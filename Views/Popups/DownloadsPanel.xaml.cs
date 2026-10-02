using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Inhalt des Downloads-Flyouts: Liste mit Fortschritt, Datei anzeigen, Ordner öffnen.</summary>
    public partial class DownloadsPanel : UserControl
    {
        /// <summary>Für eine heruntergeladene .crx-Datei wurde "Installieren" angeklickt.</summary>
        public event Action<string>? InstallExtensionRequested;

        public ObservableCollection<DownloadItem> Items { get; } = new();

        public DownloadsPanel()
        {
            InitializeComponent();
        }

        /// <summary>Neuen Download oben in die Liste einfügen.</summary>
        public void Add(DownloadItem item)
        {
            Items.Insert(0, item);
            txtEmptyDownloads.Visibility = Visibility.Collapsed;
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

        private void BtnOpenDownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string filePath || !File.Exists(filePath)) return;

            if (filePath.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
            {
                InstallExtensionRequested?.Invoke(filePath);
                return;
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
}
