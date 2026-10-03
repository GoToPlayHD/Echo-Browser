using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Models
{
    /// <summary>Ein Download – laufend (mit WebView2-Operation) oder aus der gespeicherten Liste.</summary>
    public class DownloadItem : INotifyPropertyChanged
    {
        private long _bytesReceived;
        private long _totalBytes;
        private bool _isCompleted;
        private bool _isCancelled;
        private bool _isPaused;

        public string Id { get; init; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string SourceUrl { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.Now;

        /// <summary>Laufende WebView2-Operation (nur während der Sitzung, nicht gespeichert).</summary>
        [JsonIgnore]
        public CoreWebView2DownloadOperation? Operation { get; set; }

        [JsonIgnore]
        public bool IsCrx => FileName.EndsWith(".crx", StringComparison.OrdinalIgnoreCase);

        /// <summary>Dateityp für das Symbol, z.B. "PDF" oder "ZIP".</summary>
        [JsonIgnore]
        public string Extension
        {
            get
            {
                string ext = Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
                return ext.Length == 0 ? "?" : ext.Length > 4 ? ext[..4] : ext;
            }
        }

        public long BytesReceived
        {
            get => _bytesReceived;
            set
            {
                if (_bytesReceived != value)
                {
                    _bytesReceived = value;
                    OnPropertyChanged();
                    OnStatusChanged();
                }
            }
        }

        public long TotalBytes
        {
            get => _totalBytes;
            set
            {
                if (_totalBytes != value)
                {
                    _totalBytes = value;
                    OnPropertyChanged();
                    OnStatusChanged();
                }
            }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    OnPropertyChanged();
                    OnStatusChanged();
                }
            }
        }

        public bool IsCancelled
        {
            get => _isCancelled;
            set
            {
                if (_isCancelled != value)
                {
                    _isCancelled = value;
                    OnPropertyChanged();
                    OnStatusChanged();
                }
            }
        }

        [JsonIgnore]
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    OnPropertyChanged();
                    OnStatusChanged();
                }
            }
        }

        [JsonIgnore]
        public bool IsInProgress => !_isCompleted && !_isCancelled;

        [JsonIgnore]
        public bool HasKnownSize => _totalBytes > 0;

        /// <summary>Fertig, aber die Datei wurde inzwischen gelöscht oder verschoben.</summary>
        [JsonIgnore]
        public bool IsFileMissing => _isCompleted && !File.Exists(FilePath);

        [JsonIgnore]
        public double ProgressPercentage => _totalBytes > 0
            ? Math.Clamp((double)_bytesReceived / _totalBytes * 100.0, 0, 100)
            : 0;

        /// <summary>Statuszeile im Downloads-Flyout.</summary>
        [JsonIgnore]
        public string ProgressText
        {
            get
            {
                if (IsCompleted) return IsFileMissing ? Tr.Get("Downloads_FileMissing") : $"{FormatBytes(Math.Max(_bytesReceived, _totalBytes))} · {Tr.Get("Downloads_Completed")}";
                if (IsCancelled) return Tr.Get("Downloads_Cancelled");

                string progress = _totalBytes > 0
                    ? $"{FormatBytes(_bytesReceived)} / {FormatBytes(_totalBytes)}"
                    : Tr.Format("Downloads_BytesDownloaded", FormatBytes(_bytesReceived));
                return IsPaused ? $"{Tr.Get("Downloads_Paused")} · {progress}" : progress;
            }
        }

        /// <summary>Die Datei liegt wieder vor (z.B. nach dem Laden der Liste) – Anzeige aktualisieren.</summary>
        public void RefreshFileState() => OnStatusChanged();

        private void OnStatusChanged()
        {
            OnPropertyChanged(nameof(ProgressPercentage));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(IsInProgress));
            OnPropertyChanged(nameof(HasKnownSize));
            OnPropertyChanged(nameof(IsFileMissing));
        }

        public static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1 && counter < suffixes.Length - 1)
            {
                number /= 1024;
                counter++;
            }
            return counter == 0 ? $"{number:n0} {suffixes[counter]}" : $"{number:n1} {suffixes[counter]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
