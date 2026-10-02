using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using EchoBrowser.Services;

namespace EchoBrowser.Models
{
    public class DownloadItem : INotifyPropertyChanged
    {
        private long _bytesReceived;
        private long _totalBytes;
        private string _state = "Wird heruntergeladen...";
        private bool _isCompleted;
        private bool _isCancelled;

        public string Id { get; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime StartTime { get; set; } = DateTime.Now;
        public bool IsCrx => FileName.EndsWith(".crx", StringComparison.OrdinalIgnoreCase);

        public long BytesReceived
        {
            get => _bytesReceived;
            set
            {
                if (_bytesReceived != value)
                {
                    _bytesReceived = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ProgressPercentage));
                    OnPropertyChanged(nameof(ProgressText));
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
                    OnPropertyChanged(nameof(ProgressPercentage));
                    OnPropertyChanged(nameof(ProgressText));
                }
            }
        }

        public double ProgressPercentage => TotalBytes > 0 
            ? Math.Clamp((double)BytesReceived / TotalBytes * 100.0, 0, 100) 
            : 0;

        public string ProgressText
        {
            get
            {
                if (IsCompleted) return Tr.Get("Downloads_Completed");
                if (IsCancelled) return Tr.Get("Downloads_Cancelled");
                if (TotalBytes > 0)
                {
                    return $"{FormatBytes(BytesReceived)} / {FormatBytes(TotalBytes)} ({ProgressPercentage:F0}%)";
                }
                return Tr.Format("Downloads_BytesDownloaded", FormatBytes(BytesReceived));
            }
        }

        public string State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged(nameof(ProgressText));
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
                    OnPropertyChanged(nameof(ProgressText));
                }
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            while (Math.Round(number / 1024) >= 1)
            {
                number /= 1024;
                counter++;
            }
            return $"{number:n1} {suffixes[counter]}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
