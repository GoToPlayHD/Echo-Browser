using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser.Models
{
    public class BrowserTab : INotifyPropertyChanged, IDisposable
    {
        private string _title = "Neuer Tab";
        private string _url = "about:blank";
        private bool _isLoading;
        private bool _canGoBack;
        private bool _canGoForward;
        private bool _isSecure;
        private string _securityStatus = "Nicht überprüft";
        private bool _trackingProtectionEnabled = true;
        private bool _javaScriptEnabled = true;
        private bool _popupsBlocked = true;
        private int _blockedTrackersCount;
        private bool _isActive;
        private bool _isDisposed;

        public string Id { get; } = Guid.NewGuid().ToString();

        public WebView2? WebView { get; set; }

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = string.IsNullOrWhiteSpace(value) ? "Neuer Tab" : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Neuer Tab" : Title;

        public string Url
        {
            get => _url;
            set
            {
                if (_url != value)
                {
                    _url = value;
                    OnPropertyChanged();
                    UpdateSecurityStatus();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsLoaded));
                }
            }
        }

        public bool IsLoaded => !_isLoading;

        public bool CanGoBack
        {
            get => _canGoBack;
            set
            {
                if (_canGoBack != value)
                {
                    _canGoBack = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanGoForward
        {
            get => _canGoForward;
            set
            {
                if (_canGoForward != value)
                {
                    _canGoForward = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSecure
        {
            get => _isSecure;
            set
            {
                if (_isSecure != value)
                {
                    _isSecure = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SecurityStatus
        {
            get => _securityStatus;
            set
            {
                if (_securityStatus != value)
                {
                    _securityStatus = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool TrackingProtectionEnabled
        {
            get => _trackingProtectionEnabled;
            set
            {
                if (_trackingProtectionEnabled != value)
                {
                    _trackingProtectionEnabled = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool JavaScriptEnabled
        {
            get => _javaScriptEnabled;
            set
            {
                if (_javaScriptEnabled != value)
                {
                    _javaScriptEnabled = value;
                    OnPropertyChanged();
                    ApplyScriptSetting();
                }
            }
        }

        public bool PopupsBlocked
        {
            get => _popupsBlocked;
            set
            {
                if (_popupsBlocked != value)
                {
                    _popupsBlocked = value;
                    OnPropertyChanged();
                }
            }
        }

        public int BlockedTrackersCount
        {
            get => _blockedTrackersCount;
            set
            {
                if (_blockedTrackersCount != value)
                {
                    _blockedTrackersCount = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive != value)
                {
                    _isActive = value;
                    OnPropertyChanged();
                }
            }
        }

        public void UpdateSecurityStatus()
        {
            if (string.IsNullOrWhiteSpace(_url) || _url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = false;
                SecurityStatus = "Lokale Seite";
                return;
            }

            if (_url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = true;
                SecurityStatus = "Sichere Verbindung (TLS/HTTPS)";
            }
            else if (_url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = false;
                SecurityStatus = "Nicht sicher (Unverschlüsseltes HTTP)";
            }
            else
            {
                IsSecure = false;
                SecurityStatus = "Spezielle URL";
            }
        }

        public void ApplyScriptSetting()
        {
            if (WebView?.CoreWebView2?.Settings != null)
            {
                try
                {
                    WebView.CoreWebView2.Settings.IsScriptEnabled = JavaScriptEnabled;
                }
                catch
                {
                    // Ignore if WebView is not ready or disposed
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try
            {
                if (WebView != null)
                {
                    WebView.Stop();
                    WebView.Dispose();
                    WebView = null;
                }
            }
            catch
            {
                // Suppress disposal errors on teardown
            }
        }
    }
}
