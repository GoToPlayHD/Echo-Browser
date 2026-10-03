using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using EchoBrowser.Services;

namespace EchoBrowser.Models
{
    public class BrowserTab : INotifyPropertyChanged, IDisposable
    {
        private string _title = Tr.Get("Tab_NewTab");
        private string _url = "about:blank";
        private bool _isLoading;
        private bool _canGoBack;
        private bool _canGoForward;
        private bool _isSecure;
        private string _securityStatus = Tr.Get("Security_Unchecked");
        private bool _trackingProtectionEnabled = true;
        private bool _javaScriptEnabled = true;
        private bool _popupsBlocked = true;
        private int _blockedTrackersCount;
        private bool _isActive;
        private bool _isDisposed;
        private ImageSource? _favicon;
        private bool _isAudible;
        private bool _isMuted;
        private bool _isSleeping;
        private bool _isDiscarded;
        private long? _memoryBytes;
        private bool _isPinned;
        private TabGroup? _group;
        private bool _isHiddenByGroup;
        private bool _isSplitVisible;

        public string Id { get; } = Guid.NewGuid().ToString();

        public WebView2? WebView { get; set; }
        public string? CosmeticScriptId { get; set; }
        public bool IsAdBlockFilterRegistered { get; set; }
        public string? PrivacySignalsScriptId { get; set; }

        /// <summary>Popups, die die aktuelle Seite ohne Klick öffnen wollte (werden in der Adressleiste angeboten).</summary>
        public List<string> BlockedPopupUrls { get; } = new();

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = string.IsNullOrWhiteSpace(value) ? Tr.Get("Tab_NewTab") : value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayTitle));
                }
            }
        }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Tr.Get("Tab_NewTab") : Title;

        /// <summary>Website für die Infokarte des Tabs ("github.com"), bei internen Seiten die Adresse.</summary>
        public string DisplayHost => Uri.TryCreate(_url, UriKind.Absolute, out var uri) && uri.Host.Length > 0
            ? (uri.Host.StartsWith("www.", StringComparison.Ordinal) ? uri.Host[4..] : uri.Host)
            : _url;

        public string Url
        {
            get => _url;
            set
            {
                if (_url != value)
                {
                    _url = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DisplayHost));
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

        /// <summary>Symbol der Website (null = Standard-Globus).</summary>
        public ImageSource? Favicon
        {
            get => _favicon;
            set
            {
                if (_favicon != value)
                {
                    _favicon = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasFavicon));
                }
            }
        }

        public bool HasFavicon => _favicon != null;

        /// <summary>Die Seite spielt gerade Ton ab.</summary>
        public bool IsAudible
        {
            get => _isAudible;
            set
            {
                if (_isAudible != value)
                {
                    _isAudible = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowAudioIndicator));
                }
            }
        }

        public bool IsMuted
        {
            get => _isMuted;
            set
            {
                if (_isMuted != value)
                {
                    _isMuted = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ShowAudioIndicator));
                }
            }
        }

        /// <summary>Lautsprecher-Symbol im Tab: wenn Ton läuft oder der Tab stummgeschaltet ist.</summary>
        public bool ShowAudioIndicator => _isAudible || _isMuted;

        /// <summary>Angeheftet: steht vorne, nur als Symbol, ohne Schließen-Kreuz.</summary>
        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned != value)
                {
                    _isPinned = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Tab-Gruppe oder null. Angeheftete Tabs gehören nie zu einer Gruppe.</summary>
        public TabGroup? Group
        {
            get => _group;
            set
            {
                if (_group != value)
                {
                    _group = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsGrouped));
                }
            }
        }

        public bool IsGrouped => _group != null;

        /// <summary>Gehört zu einer eingeklappten Gruppe und ist deshalb in der Tab-Leiste ausgeblendet.</summary>
        public bool IsHiddenByGroup
        {
            get => _isHiddenByGroup;
            set
            {
                if (_isHiddenByGroup != value)
                {
                    _isHiddenByGroup = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Gerade in der geteilten Ansicht zu sehen – die Tab-Leiste hebt beide Tabs hervor.</summary>
        public bool IsSplitVisible
        {
            get => _isSplitVisible;
            set
            {
                if (_isSplitVisible != value)
                {
                    _isSplitVisible = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Tab schläft (eingefroren oder verworfen) – wird in der Tab-Leiste abgeblendet.</summary>
        public bool IsSleeping
        {
            get => _isSleeping;
            set
            {
                if (_isSleeping != value)
                {
                    _isSleeping = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CardStatus));
                }
            }
        }

        /// <summary>
        /// Der Tab hat (noch) keine WebView: verworfen oder bei der Wiederherstellung der Sitzung nicht geladen.
        /// Adresse, Titel und Symbol bleiben, die Seite lädt beim Aktivieren.
        /// </summary>
        public bool IsDiscarded
        {
            get => _isDiscarded;
            set
            {
                if (_isDiscarded != value)
                {
                    _isDiscarded = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>Zuletzt sichtbar – Grundlage für den Tab-Schlaf.</summary>
        public DateTime LastActiveAt { get; set; } = DateTime.Now;

        /// <summary>Die Seite hat das Einfrieren abgelehnt (z.B. laufende Kamera); bis zur nächsten Aktivierung nicht erneut versuchen.</summary>
        public bool SleepRefused { get; set; }

        /// <summary>Arbeitsspeicher der Renderer-Prozesse dieses Tabs (anteilig), null = unbekannt.</summary>
        public long? MemoryBytes
        {
            get => _memoryBytes;
            set
            {
                if (_memoryBytes != value)
                {
                    _memoryBytes = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(MemoryDisplay));
                    OnPropertyChanged(nameof(CardStatus));
                }
            }
        }

        public string MemoryDisplay => _memoryBytes is long bytes && !_isDiscarded ? MemoryFormat.Format(bytes) : "";

        /// <summary>Zusatzzeile der Infokarte: "Schläft – spart Speicher" bzw. "Speicher: 85 MB".</summary>
        public string CardStatus =>
            _isSleeping ? Tr.Get("Tab_Sleeping")
            : MemoryDisplay.Length > 0 ? Tr.Format("Tab_MemoryUsage", MemoryDisplay)
            : "";

        /// <summary>
        /// WebView freigeben (Tab verwerfen oder nach einem Absturz neu aufbauen). Der Zustand, der an der
        /// alten CoreWebView2 hing, wird zurückgesetzt.
        /// </summary>
        public void ReleaseWebView()
        {
            var webView = WebView;
            WebView = null;
            try
            {
                webView?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warn("WebView eines Tabs ließ sich nicht freigeben", ex);
            }

            CosmeticScriptId = null;
            PrivacySignalsScriptId = null;
            IsAdBlockFilterRegistered = false;
            IsLoading = false;
            IsAudible = false;
            MemoryBytes = null;
        }

        public void UpdateSecurityStatus()
        {
            if (string.IsNullOrWhiteSpace(_url) || _url.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = false;
                SecurityStatus = Tr.Get("Security_LocalPage");
                return;
            }

            if (_url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = true;
                SecurityStatus = Tr.Get("Security_Secure");
            }
            else if (_url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                IsSecure = false;
                SecurityStatus = Tr.Get("Security_InsecureHttp");
            }
            else
            {
                IsSecure = false;
                SecurityStatus = Tr.Get("Security_SpecialUrl");
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
