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
    public partial class MainWindow : Window
    {
        private readonly BookmarkService _bookmarkService = new();
        private readonly SidebarService _sidebarService = new();
        private readonly HistoryService _historyService = new();
        private CoreWebView2Environment? _webViewEnvironment;
        private BrowserTab? _activeTab;
        private bool _isBookmarksBarVisible = true;
        private bool _isSyncingSearchEngine;
        private bool _isUpdatingShieldUi;
        private bool _shieldBadgeUpdatePending;
        private ExtensionPopupWindow? _activeExtensionPopup;

        // Drag & Drop State for Tabs and Bookmarks
        private Point _tabDragStartPoint;
        private BrowserTab? _draggedTab;
        private Point _bmDragStartPoint;
        private Bookmark? _draggedBookmark;
        private Bookmark? _activeGroupForPopup;

        // Add Favorite State
        private string _selectedFavIconKey = "globe";
        private string _selectedFavColor = "#C4C7CC";

        public ObservableCollection<BrowserTab> Tabs { get; } = new();
        public ObservableCollection<Bookmark> Bookmarks => _bookmarkService.Bookmarks;
        public ObservableCollection<SidebarFavorite> SidebarFavorites => _sidebarService.Favorites;
        public ObservableCollection<DownloadItem> Downloads { get; } = new();
        public ObservableCollection<HistoryItem> FilteredHistory { get; } = new();

        public BrowserTab? ActiveTab
        {
            get => _activeTab;
            set
            {
                if (_activeTab != value)
                {
                    _activeTab = value;
                    OnActiveTabChanged();
                }
            }
        }

        private readonly bool _isIncognito;
        private string? _incognitoFolder;

        public bool IsIncognito => _isIncognito;

        public MainWindow() : this(false)
        {
        }

        public MainWindow(bool isIncognito)
        {
            _isIncognito = isIncognito;
            InitializeComponent();
            DataContext = this;
            StateChanged += MainWindow_StateChanged;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;

            if (_isIncognito)
            {
                Title = "Echo-Browser (Inkognito)";
                badgeIncognito.Visibility = Visibility.Visible;
            }

            // Restore saved settings
            RestoreSavedSettings();
        }

        private void RestoreSavedSettings()
        {
            var settings = AppSettingsService.Instance.Settings;

            // Restore Bookmarks bar visibility
            _isBookmarksBarVisible = settings.IsBookmarksBarVisible;
            borderBookmarksBar.Visibility = _isBookmarksBarVisible ? Visibility.Visible : Visibility.Collapsed;
            menuChkBookmarksBar.IsChecked = _isBookmarksBarVisible;

            // Restore Sidebar visibility
            borderSidebar.Visibility = settings.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
            menuChkSidebar.IsChecked = settings.IsSidebarVisible;

            // Restore Toolbar button visibilities
            ApplyToolbarButtonVisibilities();

            // Restore Search Engine selector
            SyncSearchEngineComboBox();

            // Restore Theme Preset
            if (Enum.TryParse<ThemePreset>(settings.ThemePreset, out var preset))
            {
                ThemeManager.Instance.ApplyPreset(preset);
            }

            // Restore Accent Color
            if (!string.IsNullOrWhiteSpace(settings.AccentColor) && settings.AccentColor != "#C4C7CC")
            {
                try
                {
                    Color color = ThemeManager.ColorFromHex(settings.AccentColor);
                    ThemeManager.Instance.SetAccentColor(color);
                }
                catch { }
            }
        }

        public void ApplyToolbarButtonVisibilities()
        {
            var s = AppSettingsService.Instance.Settings;
            btnToggleSidebar.Visibility = s.ShowSidebarButton ? Visibility.Visible : Visibility.Collapsed;
            btnBack.Visibility = s.ShowBackButton ? Visibility.Visible : Visibility.Collapsed;
            btnForward.Visibility = s.ShowForwardButton ? Visibility.Visible : Visibility.Collapsed;
            btnReload.Visibility = s.ShowReloadButton ? Visibility.Visible : Visibility.Collapsed;
            btnHome.Visibility = s.ShowHomeButton ? Visibility.Visible : Visibility.Collapsed;
            cmbSearchEngine.Visibility = s.ShowSearchEngineSelector ? Visibility.Visible : Visibility.Collapsed;
            btnExtensions.Visibility = s.ShowExtensionsButton ? Visibility.Visible : Visibility.Collapsed;
            btnDownloads.Visibility = s.ShowDownloadsButton ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuHideToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tag)
            {
                var s = AppSettingsService.Instance.Settings;
                switch (tag)
                {
                    case "Sidebar": s.ShowSidebarButton = false; break;
                    case "Back": s.ShowBackButton = false; break;
                    case "Forward": s.ShowForwardButton = false; break;
                    case "Reload": s.ShowReloadButton = false; break;
                    case "Home": s.ShowHomeButton = false; break;
                    case "SearchEngine": s.ShowSearchEngineSelector = false; break;
                    case "Extensions": s.ShowExtensionsButton = false; break;
                    case "Downloads": s.ShowDownloadsButton = false; break;
                }
                AppSettingsService.Instance.Save();
                ApplyToolbarButtonVisibilities();
            }
        }

        private void MenuToolbarSubmenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem parent)
            {
                var s = AppSettingsService.Instance.Settings;
                foreach (var child in parent.Items)
                {
                    if (child is MenuItem mi && mi.Tag is string tag)
                    {
                        mi.IsChecked = tag switch
                        {
                            "Sidebar" => s.ShowSidebarButton,
                            "Back" => s.ShowBackButton,
                            "Forward" => s.ShowForwardButton,
                            "Reload" => s.ShowReloadButton,
                            "Home" => s.ShowHomeButton,
                            "SearchEngine" => s.ShowSearchEngineSelector,
                            "Extensions" => s.ShowExtensionsButton,
                            "Downloads" => s.ShowDownloadsButton,
                            _ => mi.IsChecked
                        };
                    }
                }
            }
        }

        private void MenuToolbarButton_Toggle(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag)
            {
                var s = AppSettingsService.Instance.Settings;
                bool isChecked = mi.IsChecked;
                switch (tag)
                {
                    case "Sidebar": s.ShowSidebarButton = isChecked; break;
                    case "Back": s.ShowBackButton = isChecked; break;
                    case "Forward": s.ShowForwardButton = isChecked; break;
                    case "Reload": s.ShowReloadButton = isChecked; break;
                    case "Home": s.ShowHomeButton = isChecked; break;
                    case "SearchEngine": s.ShowSearchEngineSelector = isChecked; break;
                    case "Extensions": s.ShowExtensionsButton = isChecked; break;
                    case "Downloads": s.ShowDownloadsButton = isChecked; break;
                }
                AppSettingsService.Instance.Save();
                ApplyToolbarButtonVisibilities();
            }
        }

        private void MenuShowAllToolbarButtons_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Instance.Settings;
            s.ShowSidebarButton = true;
            s.ShowBackButton = true;
            s.ShowForwardButton = true;
            s.ShowReloadButton = true;
            s.ShowHomeButton = true;
            s.ShowSearchEngineSelector = true;
            s.ShowExtensionsButton = true;
            s.ShowDownloadsButton = true;
            AppSettingsService.Instance.Save();
            ApplyToolbarButtonVisibilities();
        }

        private void MenuOpenToolbarSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsTab();
        }

        private void SyncSearchEngineComboBox()
        {
            _isSyncingSearchEngine = true;
            try
            {
                string engine = AppSettingsService.Instance.Settings.SearchEngine ?? "duckduckgo";
                foreach (ComboBoxItem item in cmbSearchEngine.Items)
                {
                    if (item.Tag is string tag && tag.Equals(engine, StringComparison.OrdinalIgnoreCase))
                    {
                        cmbSearchEngine.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _isSyncingSearchEngine = false;
            }
        }

        private void CmbSearchEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isSyncingSearchEngine) return;

            if (cmbSearchEngine.SelectedItem is ComboBoxItem item && item.Tag is string engineKey)
            {
                AppSettingsService.Instance.SetSearchEngine(engineKey);

                // If active tab is on the startpage, reload it to reflect the new default engine
                if (ActiveTab?.Url == StartPageService.StartPageUrl)
                {
                    ActiveTab.WebView?.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeBrowserEnvironmentAsync();

            // 1. Initialize Localization from AppSettings
            LocalizationService.Instance.SetLanguage(AppSettingsService.Instance.Settings.Language);

            // 2. Apple-Style Language Selection Onboarding on First Launch
            if (!_isIncognito && !AppSettingsService.Instance.Settings.HasCompletedFirstRunLanguageSetup)
            {
                var setupWin = new LanguageSetupWindow { Owner = this };
                setupWin.ShowDialog();
            }

            ApplyLocalizationToUi();

            var settings = AppSettingsService.Instance.Settings;
            if (!_isIncognito && settings.StartupBehavior == "restore_session")
            {
                RestorePreviousSession();
            }
            else if (!_isIncognito && settings.StartupBehavior == "custom_url" && !string.IsNullOrWhiteSpace(settings.CustomStartupUrl))
            {
                AddNewTab(settings.CustomStartupUrl);
            }
            else
            {
                AddNewTab(StartPageService.StartPageUrl);
            }

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(600);
                UpdatePinnedExtensionsToolbar();
            });
        }

        private void ApplyLocalizationToUi()
        {
            try
            {
                btnBack.ToolTip = LocalizationService.Instance.GetString("Nav_Back", "Zurück (Alt+Links)");
                btnForward.ToolTip = LocalizationService.Instance.GetString("Nav_Forward", "Vorwärts (Alt+Rechts)");
                btnReload.ToolTip = LocalizationService.Instance.GetString("Nav_Reload", "Neu laden (F5)");
                btnHome.ToolTip = LocalizationService.Instance.GetString("Nav_Home", "Startseite");
                btnNewTab.ToolTip = LocalizationService.Instance.GetString("Nav_NewTab", "Neuer Tab (Strg+T)");
                btnShield.ToolTip = LocalizationService.Instance.GetString("Nav_EchoShield", "Echo Shield Schutz");
                btnExtensions.ToolTip = LocalizationService.Instance.GetString("Nav_Extensions", "Erweiterungen");
                btnDownloads.ToolTip = LocalizationService.Instance.GetString("Nav_Downloads", "Downloads");
                btnMenu.ToolTip = LocalizationService.Instance.GetString("Nav_Settings", "Einstellungen");
                txtUrlPlaceholder.Text = LocalizationService.Instance.GetString("Nav_AddressPlaceholder", "Suchen oder Webadresse eingeben...");

                UpdateShieldUi();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to apply localization: {ex.Message}");
            }
        }

        private async Task InitializeBrowserEnvironmentAsync()
        {
            try
            {
                string userDataFolder;
                if (_isIncognito)
                {
                    _incognitoFolder = Path.Combine(Path.GetTempPath(), "EchoBrowser_Incognito_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(_incognitoFolder);
                    userDataFolder = _incognitoFolder;
                }
                else
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    userDataFolder = Path.Combine(appData, "EchoBrowser", "WebView2Data");
                    Directory.CreateDirectory(userDataFolder);
                }

                var options = new CoreWebView2EnvironmentOptions
                {
                    AreBrowserExtensionsEnabled = true
                };

                _webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                _ = AdBlockerService.Instance.InitializeAsync();
                borderSplash.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Initialisieren der WebView2-Chromium-Engine:\n{ex.Message}",
                    "Echo-Browser Initialisierungsfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #region TitleBar Window Movement & System Buttons

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // If user clicked inside a Tab, Button, ComboBox, or ScrollBar, do NOT move the window!
            if (e.OriginalSource is DependencyObject dep)
            {
                if (FindVisualParent<Button>(dep) != null) return;
                if (FindVisualParent<ComboBox>(dep) != null) return;
                if (FindVisualParent<ScrollBar>(dep) != null) return;
                if (FindVisualParent<FrameworkElement>(dep, f => f.DataContext is BrowserTab) != null) return;
            }

            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                pathMaximizeIcon.Data = Geometry.Parse("M4 8H2V2h6v2H4v4zm10-6h6v6h-2V4h-4V2zm6 14v4h-4v2h6v-6h-2zM4 16v4h4v2H2v-6h2z");
            }
            else
            {
                pathMaximizeIcon.Data = Geometry.Parse("M3 3h18v18H3V3zm16 16V5H5v14h14z");
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = (WindowState == WindowState.Maximized)
                ? WindowState.Normal
                : WindowState.Maximized;
        }

        private void BtnCloseWindow_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Multi-Tab Management & Drag-Drop Reordering

        public void AddNewTab(string? targetUrl = null, bool activateTab = true)
        {
            if (_webViewEnvironment == null) return;

            string initialUrl = string.IsNullOrWhiteSpace(targetUrl) ? StartPageService.StartPageUrl : targetUrl;
            var tab = new BrowserTab
            {
                Title = (initialUrl == SettingsPageService.SettingsPageUrl) ? "Einstellungen" : "Neuer Tab",
                Url = initialUrl
            };

            var webView = new WebView2
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed
            };

            tab.WebView = webView;
            WebViewContainer.Children.Add(webView);

            // Register WebView events
            AttachWebViewEvents(tab, webView, initialUrl);

            Tabs.Add(tab);
            if (activateTab)
            {
                SelectTab(tab);
            }
        }

        private async void AttachWebViewEvents(BrowserTab tab, WebView2 webView, string initialUrl)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(_webViewEnvironment);

                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.IsScriptEnabled = tab.JavaScriptEnabled;
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = true;

                    // Cosmetic element-hiding + Netzwerkfilter (nur wenn Shield aktiv)
                    await SyncShieldStateAsync(tab);

                    // Chrome Web Store Extension Helper Script
                    try
                    {
                        await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ExtensionService.GetWebStoreHelperScript());
                    }
                    catch { }

                    // Allow extension downloads without prompt interruptions.
                    // Nur für .crx – alle anderen Dateitypen (z.B. .exe/.msi) durchlaufen weiterhin die Chromium-Sicherheitsprüfung.
                    webView.CoreWebView2.SaveFileSecurityCheckStarting += (s, args) =>
                    {
                        string ext = (args.FileExtension ?? "").TrimStart('.');
                        if (ext.Equals("crx", StringComparison.OrdinalIgnoreCase))
                        {
                            args.CancelSave = false;
                            args.SuppressDefaultPolicy = true;
                        }
                    };

                    // Network-level Ad and Tracker blocking.
                    // Der Filter selbst wird in SyncShieldStateAsync nur bei aktivem Shield registriert,
                    // sonst würde jede Anfrage (Bilder, Fonts, ...) über den UI-Thread laufen.
                    try
                    {
                        webView.CoreWebView2.WebResourceRequested += (s, args) =>
                        {
                            if (!AppSettingsService.Instance.Settings.IsAdBlockerEnabled) return;
                            if (!tab.TrackingProtectionEnabled) return;

                            try
                            {
                                if (Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var reqUri) &&
                                    (reqUri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || 
                                     reqUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
                                {
                                    bool shouldBlock = AdBlockerService.Instance.IsBlocked(reqUri.Host);
                                    if (!shouldBlock)
                                    {
                                        string path = reqUri.AbsolutePath;
                                        if (path.EndsWith("/ads.js", StringComparison.OrdinalIgnoreCase) ||
                                            path.EndsWith("/pagead.js", StringComparison.OrdinalIgnoreCase) ||
                                            path.Contains("/widget/ads.js", StringComparison.OrdinalIgnoreCase))
                                        {
                                            shouldBlock = true;
                                        }
                                    }

                                    if (shouldBlock)
                                    {
                                        if (_webViewEnvironment != null)
                                        {
                                            args.Response = _webViewEnvironment.CreateWebResourceResponse(Stream.Null, 403, "Forbidden", "");
                                        }
                                        tab.BlockedTrackersCount++;
                                        if (tab == ActiveTab)
                                        {
                                            ScheduleShieldBadgeUpdate();
                                        }
                                    }
                                }
                            }
                            catch { }
                        };
                    }
                    catch { }

                    try
                    {
                        string trackingLevel = AppSettingsService.Instance.Settings.TrackingPreventionLevel ?? "balanced";
                        webView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = trackingLevel.ToLowerInvariant() switch
                        {
                            "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                            "none" => CoreWebView2TrackingPreventionLevel.None,
                            _ => CoreWebView2TrackingPreventionLevel.Balanced
                        };
                    }
                    catch { }

                    // Intercept WebMessages from custom startpage and settings page
                    webView.CoreWebView2.WebMessageReceived += (s, args) =>
                    {
                        try
                        {
                            string json = args.WebMessageAsJson;
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;
                            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("type", out var typeProp))
                            {
                                string type = typeProp.GetString() ?? "";

                                // Jede Webseite kann postMessage aufrufen: nur interne Seiten (mit Token)
                                // und der Chrome Web Store (nur Installationsanfragen) werden akzeptiert.
                                if (!InternalPageSecurity.IsTrustedInternalMessage(args.Source, root) &&
                                    !InternalPageSecurity.IsAllowedFromWebStore(args.Source, type))
                                {
                                    Debug.WriteLine($"[Echo] WebMessage '{type}' von nicht vertrauenswürdiger Quelle verworfen: {args.Source}");
                                    return;
                                }

                                if (type == "navigate" && root.TryGetProperty("url", out var urlEl))
                                {
                                    string url = urlEl.GetString() ?? "";
                                    if (!string.IsNullOrWhiteSpace(url))
                                    {
                                        Dispatcher.Invoke(() => NavigateToInput(url));
                                    }
                                }
                                else if (type == "setSearchEngine" && root.TryGetProperty("engine", out var engEl))
                                {
                                    string engine = engEl.GetString() ?? "";
                                    Dispatcher.Invoke(() =>
                                    {
                                        AppSettingsService.Instance.SetSearchEngine(engine);
                                        SyncSearchEngineComboBox();
                                    });
                                }
                                else if (type == "toggleStartpageFavorites" && root.TryGetProperty("visible", out var visEl))
                                {
                                    bool visible = visEl.GetBoolean();
                                    Dispatcher.Invoke(() =>
                                    {
                                        AppSettingsService.Instance.Settings.IsStartpageFavoritesVisible = visible;
                                        AppSettingsService.Instance.Save();
                                    });
                                }
                                else if (type == "saveStartpageShortcuts" && root.TryGetProperty("shortcuts", out var scEl))
                                {
                                    var shortcuts = JsonSerializer.Deserialize<List<StartpageShortcut>>(scEl.GetRawText());
                                    if (shortcuts != null)
                                    {
                                        Dispatcher.Invoke(() =>
                                        {
                                            AppSettingsService.Instance.Settings.StartpageShortcuts = shortcuts;
                                            AppSettingsService.Instance.Save();
                                        });
                                    }
                                }
                                else if (type == "installExtensionFromWebStore")
                                {
                                    string extId = root.TryGetProperty("extensionId", out var idEl) ? (idEl.GetString() ?? "") : "";
                                    string extName = root.TryGetProperty("extensionName", out var nameEl) ? (nameEl.GetString() ?? "Erweiterung") : "Erweiterung";

                                    if (!string.IsNullOrWhiteSpace(extId))
                                    {
                                        Dispatcher.Invoke(async () =>
                                        {
                                            try
                                             {
                                                if (webView.CoreWebView2?.Profile != null)
                                                {
                                                    if (!ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, extId))
                                                    {
                                                        await webView.CoreWebView2.ExecuteScriptAsync("window.onEchoExtensionInstallResult && window.onEchoExtensionInstallResult(false, 'Vom Benutzer abgebrochen');");
                                                        return;
                                                    }

                                                    var ext = await ExtensionService.Instance.DownloadAndInstallExtensionAsync(webView.CoreWebView2.Profile, extId, extName);
                                                    string finalName = ext?.Name ?? extName;
                                                    await webView.CoreWebView2.ExecuteScriptAsync("window.onEchoExtensionInstallResult && window.onEchoExtensionInstallResult(true, 'Installiert');");
                                                    ThemedDialogWindow.ShowExtensionInstalledSuccess(this, finalName, ext?.Id ?? extId);
                                                    await RefreshExtensionsListAsync();
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                string safeMsg = ex.Message.Replace("'", "\\'").Replace("\r", "").Replace("\n", " ");
                                                await webView.CoreWebView2.ExecuteScriptAsync($"window.onEchoExtensionInstallResult && window.onEchoExtensionInstallResult(false, '{safeMsg}');");
                                                ThemedDialogWindow.ShowMessage(this, "Echo-Browser Erweiterungen", $"Fehler beim Herunterladen und Installieren der Erweiterung:\n{ex.Message}", MessageBoxImage.Error);
                                            }
                                        });
                                    }
                                }
                                else if (type == "updateSetting" && root.TryGetProperty("key", out var keyEl) && root.TryGetProperty("value", out var valEl))
                                {
                                    string key = keyEl.GetString() ?? "";
                                    Dispatcher.Invoke(() =>
                                    {
                                        var settings = AppSettingsService.Instance.Settings;
                                        switch (key)
                                        {
                                            case "StartupBehavior":
                                                settings.StartupBehavior = valEl.GetString() ?? "startpage";
                                                break;
                                            case "CustomStartupUrl":
                                                settings.CustomStartupUrl = valEl.GetString() ?? "";
                                                break;
                                            case "ShowHomeButton":
                                                bool showHome = valEl.GetBoolean();
                                                settings.ShowHomeButton = showHome;
                                                btnHome.Visibility = showHome ? Visibility.Visible : Visibility.Collapsed;
                                                break;
                                            case "IsStartpageFavoritesVisible":
                                                settings.IsStartpageFavoritesVisible = valEl.GetBoolean();
                                                break;
                                            case "SearchEngine":
                                                string engine = valEl.GetString() ?? "duckduckgo";
                                                AppSettingsService.Instance.SetSearchEngine(engine);
                                                SyncSearchEngineComboBox();
                                                break;
                                            case "EnableSearchSuggestions":
                                                settings.EnableSearchSuggestions = valEl.GetBoolean();
                                                break;
                                            case "IsBookmarksBarVisible":
                                                bool showBm = valEl.GetBoolean();
                                                _isBookmarksBarVisible = showBm;
                                                borderBookmarksBar.Visibility = showBm ? Visibility.Visible : Visibility.Collapsed;
                                                menuChkBookmarksBar.IsChecked = showBm;
                                                settings.IsBookmarksBarVisible = showBm;
                                                break;
                                            case "IsSidebarVisible":
                                                bool showSb = valEl.GetBoolean();
                                                borderSidebar.Visibility = showSb ? Visibility.Visible : Visibility.Collapsed;
                                                menuChkSidebar.IsChecked = showSb;
                                                settings.IsSidebarVisible = showSb;
                                                break;
                                            case "DefaultZoomPercent":
                                                int zoom = valEl.GetInt32();
                                                settings.DefaultZoomPercent = zoom;
                                                ApplyDefaultZoom(zoom);
                                                break;
                                            case "TrackingPreventionLevel":
                                                string level = valEl.GetString() ?? "balanced";
                                                settings.TrackingPreventionLevel = level;
                                                ApplyTrackingPrevention(level);
                                                break;
                                            case "BlockPopups":
                                                settings.BlockPopups = valEl.GetBoolean();
                                                break;
                                            case "EnableJavaScript":
                                                bool js = valEl.GetBoolean();
                                                settings.EnableJavaScript = js;
                                                tab.JavaScriptEnabled = js;
                                                if (webView.CoreWebView2 != null) webView.CoreWebView2.Settings.IsScriptEnabled = js;
                                                break;
                                            case "ShowSidebarButton":
                                                settings.ShowSidebarButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowBackButton":
                                                settings.ShowBackButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowForwardButton":
                                                settings.ShowForwardButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowReloadButton":
                                                settings.ShowReloadButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowSearchEngineSelector":
                                                settings.ShowSearchEngineSelector = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowExtensionsButton":
                                                settings.ShowExtensionsButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "ShowDownloadsButton":
                                                settings.ShowDownloadsButton = valEl.GetBoolean();
                                                ApplyToolbarButtonVisibilities();
                                                break;
                                            case "IsAdBlockerEnabled":
                                                settings.IsAdBlockerEnabled = valEl.GetBoolean();
                                                _ = SyncShieldStateForAllTabsAsync();
                                                UpdateShieldBadge();
                                                if (popupShield.IsOpen) UpdateShieldUi();
                                                break;
                                            case "SendDoNotTrack":
                                                settings.SendDoNotTrack = valEl.GetBoolean();
                                                break;
                                            case "AskDownloadLocation":
                                                settings.AskDownloadLocation = valEl.GetBoolean();
                                                break;
                                            case "OpenNewTabInBackground":
                                                settings.OpenNewTabInBackground = valEl.GetBoolean();
                                                break;
                                            case "WarnOnClosingMultipleTabs":
                                                settings.WarnOnClosingMultipleTabs = valEl.GetBoolean();
                                                break;
                                            case "Language":
                                                string newLang = valEl.GetString() ?? "de";
                                                settings.Language = newLang;
                                                LocalizationService.Instance.SetLanguage(newLang);
                                                ApplyLocalizationToUi();
                                                break;
                                        }
                                        AppSettingsService.Instance.Save();
                                    });
                                }
                                else if (type == "setThemePreset" && root.TryGetProperty("preset", out var presetEl))
                                {
                                    string presetStr = presetEl.GetString() ?? "";
                                    Dispatcher.Invoke(() =>
                                    {
                                        if (Enum.TryParse<ThemePreset>(presetStr, out var preset))
                                        {
                                            ThemeManager.Instance.ApplyPreset(preset);
                                            AppSettingsService.Instance.Settings.ThemePreset = presetStr;
                                            AppSettingsService.Instance.Save();
                                        }
                                    });
                                }
                                else if (type == "setAccentColor" && root.TryGetProperty("hex", out var hexEl))
                                {
                                    string hex = hexEl.GetString() ?? "";
                                    Dispatcher.Invoke(() =>
                                    {
                                        try
                                        {
                                            Color color = ThemeManager.ColorFromHex(hex);
                                            ThemeManager.Instance.SetAccentColor(color);
                                            AppSettingsService.Instance.Settings.AccentColor = hex;
                                            AppSettingsService.Instance.Save();
                                        }
                                        catch { }
                                    });
                                }
                                else if (type == "browseDownloadFolder")
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        var dlg = new Microsoft.Win32.OpenFolderDialog
                                        {
                                            Title = "Download-Ordner auswählen",
                                            InitialDirectory = AppSettingsService.Instance.Settings.DownloadPath
                                        };
                                        if (dlg.ShowDialog() == true)
                                        {
                                            string newPath = dlg.FolderName;
                                            AppSettingsService.Instance.Settings.DownloadPath = newPath;
                                            AppSettingsService.Instance.Save();
                                            string escaped = JsonEncodedText.Encode(newPath).ToString();
                                            webView.CoreWebView2?.ExecuteScriptAsync($"window.onDownloadPathChanged && window.onDownloadPathChanged('{escaped}');");
                                        }
                                    });
                                }
                                else if (type == "openDownloadFolder")
                                {
                                    Dispatcher.Invoke(() =>
                                    {
                                        string p = AppSettingsService.Instance.Settings.DownloadPath;
                                        if (Directory.Exists(p))
                                        {
                                            Process.Start(new ProcessStartInfo { FileName = p, UseShellExecute = true });
                                        }
                                    });
                                }
                                else if (type == "clearBrowsingData")
                                {
                                    bool clearHist = root.TryGetProperty("clearHistory", out var ch) && ch.GetBoolean();
                                    bool clearCook = root.TryGetProperty("clearCookies", out var cc) && cc.GetBoolean();
                                    bool clearCache = root.TryGetProperty("clearCache", out var cca) && cca.GetBoolean();

                                    Dispatcher.Invoke(async () =>
                                    {
                                        if (clearHist)
                                        {
                                            _historyService.ClearHistory();
                                            UpdateFilteredHistory();
                                        }

                                        if (clearCook || clearCache)
                                        {
                                            CoreWebView2BrowsingDataKinds kinds = 0;
                                            if (clearCook) kinds |= CoreWebView2BrowsingDataKinds.Cookies;
                                            if (clearCache) kinds |= CoreWebView2BrowsingDataKinds.DiskCache;
                                            if (kinds != 0)
                                            {
                                                try
                                                {
                                                    await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(kinds);
                                                }
                                                catch { }
                                            }
                                        }

                                        await webView.CoreWebView2.ExecuteScriptAsync("window.onBrowsingDataCleared && window.onBrowsingDataCleared();");
                                    });
                                }
                                else if (type == "resetSettings")
                                {
                                    Dispatcher.Invoke(async () =>
                                    {
                                        AppSettingsService.Instance.ResetToDefaults();
                                        RestoreSavedSettings();
                                        string newSettingsJson = JsonSerializer.Serialize(AppSettingsService.Instance.Settings);
                                        await webView.CoreWebView2.ExecuteScriptAsync($"window.onSettingUpdatedFromHost && window.onSettingUpdatedFromHost({newSettingsJson});");
                                    });
                                }
                                else if (type == "updateAdBlockFilter")
                                {
                                    Dispatcher.Invoke(async () =>
                                    {
                                        await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(AppSettingsService.Instance.Settings.AdBlockerFilterUrl, force: true);
                                        int count = AdBlockerService.Instance.BlockedDomainsCount;
                                        await webView.CoreWebView2.ExecuteScriptAsync($"window.onAdBlockFilterUpdated && window.onAdBlockFilterUpdated({count});");
                                    });
                                }
                            }
                        }
                        catch { }
                    };

                    // Handle new windows / popups
                    webView.CoreWebView2.NewWindowRequested += (s, args) =>
                    {
                        args.Handled = true;
                        if (!string.IsNullOrWhiteSpace(args.Uri))
                        {
                            Dispatcher.Invoke(() =>
                            {
                                bool inBackground = AppSettingsService.Instance.Settings.OpenNewTabInBackground;
                                AddNewTab(args.Uri, activateTab: !inBackground);
                            });
                        }
                    };
                    // Handle downloads
                    webView.CoreWebView2.DownloadStarting += (s, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string fileName = Path.GetFileName(args.ResultFilePath);
                            string customFolder = AppSettingsService.Instance.Settings.DownloadPath;
                            if (!string.IsNullOrWhiteSpace(customFolder) && Directory.Exists(customFolder))
                            {
                                args.ResultFilePath = Path.Combine(customFolder, fileName);
                            }

                            if (AppSettingsService.Instance.Settings.AskDownloadLocation)
                            {
                                var saveDialog = new Microsoft.Win32.SaveFileDialog
                                {
                                    FileName = fileName,
                                    InitialDirectory = !string.IsNullOrWhiteSpace(customFolder) && Directory.Exists(customFolder)
                                        ? customFolder
                                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads"
                                };
                                if (saveDialog.ShowDialog() == true)
                                {
                                    args.ResultFilePath = saveDialog.FileName;
                                    args.Handled = true;
                                }
                                else
                                {
                                    args.Cancel = true;
                                    return;
                                }
                            }
                            else
                            {
                                args.Handled = true;
                            }

                            var download = new DownloadItem
                            {
                                FileName = fileName,
                                FilePath = args.ResultFilePath,
                                TotalBytes = args.DownloadOperation.TotalBytesToReceive.HasValue 
                                    ? (long)args.DownloadOperation.TotalBytesToReceive.Value 
                                    : 0
                            };

                            Downloads.Insert(0, download);
                            txtEmptyDownloads.Visibility = Visibility.Collapsed;
                            downloadBadge.Visibility = Visibility.Visible;

                            args.DownloadOperation.BytesReceivedChanged += (dSender, dArgs) =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    download.BytesReceived = (long)args.DownloadOperation.BytesReceived;
                                });
                            };

                            args.DownloadOperation.StateChanged += (dSender, dArgs) =>
                            {
                                Dispatcher.Invoke(() =>
                                {
                                    if (args.DownloadOperation.State == CoreWebView2DownloadState.Completed)
                                    {
                                        download.IsCompleted = true;
                                        download.State = "Abgeschlossen";
                                        downloadBadge.Visibility = Visibility.Collapsed;

                                        // Auto-install CRX if it is a downloaded extension
                                        if (download.FilePath.EndsWith(".crx", StringComparison.OrdinalIgnoreCase) && File.Exists(download.FilePath))
                                        {
                                            _ = Dispatcher.InvokeAsync(async () =>
                                            {
                                                try
                                                {
                                                    await Task.Delay(250);
                                                    if (webView.CoreWebView2?.Profile != null)
                                                    {
                                                        string extName = Path.GetFileNameWithoutExtension(download.FilePath);
                                                        if (ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, null, download.FilePath))
                                                        {
                                                            var ext = await ExtensionService.Instance.InstallExtensionFromCrxAsync(webView.CoreWebView2.Profile, download.FilePath);
                                                            ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? extName, ext?.Id);
                                                            await RefreshExtensionsListAsync();
                                                        }
                                                    }
                                                }
                                                catch (Exception ex)
                                                {
                                                    ThemedDialogWindow.ShowMessage(this, "Echo-Browser Erweiterungen", $"Automatische Installation der Erweiterung fehlgeschlagen:\n{ex.Message}", MessageBoxImage.Warning);
                                                }
                                            });
                                        }
                                    }
                                    else if (args.DownloadOperation.State == CoreWebView2DownloadState.Interrupted)
                                    {
                                        download.IsCancelled = true;
                                        download.State = "Unterbrochen";
                                        downloadBadge.Visibility = Visibility.Collapsed;
                                    }
                                });
                            };
                        });
                    };

                    // Title change
                    webView.CoreWebView2.DocumentTitleChanged += (s, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (tab.Url != StartPageService.StartPageUrl && tab.Url != SettingsPageService.SettingsPageUrl)
                            {
                                tab.Title = webView.CoreWebView2.DocumentTitle;

                                // Record history if NOT in incognito mode
                                if (!_isIncognito && !string.IsNullOrWhiteSpace(tab.Url) && !tab.Url.StartsWith("echo://", StringComparison.OrdinalIgnoreCase))
                                {
                                    _historyService.AddEntry(tab.Title, tab.Url);
                                }
                            }
                        });
                    };

                    // History change (Back / Forward state)
                    webView.CoreWebView2.HistoryChanged += (s, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            tab.CanGoBack = webView.CanGoBack;
                            tab.CanGoForward = webView.CanGoForward;
                            if (tab == ActiveTab)
                            {
                                UpdateNavigationControls();
                            }
                        });
                    };
                }

                // Navigation Starting
                webView.NavigationStarting += async (s, args) =>
                {
                    // Check domain whitelist
                    if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var navUri) && !string.IsNullOrEmpty(navUri.Host))
                    {
                        bool isWhitelisted = AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Contains(navUri.Host);
                        tab.TrackingProtectionEnabled = !isWhitelisted;
                    }

                    await SyncShieldStateAsync(tab);

                    Dispatcher.Invoke(() =>
                    {
                        tab.BlockedTrackersCount = 0;
                        tab.IsLoading = true;
                        if (!args.Uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                        {
                            tab.Url = args.Uri;
                        }
                        if (tab == ActiveTab)
                        {
                            txtUrl.Text = IsStartPage(tab.Url) ? "" : tab.Url;
                            UpdateNavigationControls();
                            UpdateShieldBadge();
                            if (popupShield.IsOpen)
                            {
                                UpdateShieldUi();
                            }
                        }
                    });
                };

                // Navigation Completed
                webView.NavigationCompleted += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        tab.IsLoading = false;
                        if (tab == ActiveTab)
                        {
                            UpdateNavigationControls();
                            CheckBookmarkStatus();
                        }

                        // Record history if NOT in incognito mode
                        if (!_isIncognito && !string.IsNullOrWhiteSpace(tab.Url) && 
                            tab.Url != StartPageService.StartPageUrl && 
                            tab.Url != SettingsPageService.SettingsPageUrl &&
                            !tab.Url.StartsWith("echo://", StringComparison.OrdinalIgnoreCase) &&
                            !tab.Url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                        {
                            _historyService.AddEntry(tab.Title, tab.Url);
                        }
                    });
                };

                // Source Changed
                webView.SourceChanged += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        string currentSrc = webView.Source.ToString();
                        if (!currentSrc.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
                        {
                            tab.Url = currentSrc;
                        }
                        if (tab == ActiveTab)
                        {
                            txtUrl.Text = IsStartPage(tab.Url) ? "" : tab.Url;
                            CheckBookmarkStatus();
                            UpdateShieldUi();
                        }
                    });
                };

                // Navigate initially (Show custom startpage or settings if requested)
                if (initialUrl == SettingsPageService.SettingsPageUrl)
                {
                    NavigateToSettingsPage(tab);
                }
                else if (initialUrl == StartPageService.StartPageUrl || initialUrl == "about:blank")
                {
                    tab.Url = StartPageService.StartPageUrl;
                    tab.Title = _isIncognito ? "Neuer Tab (Inkognito)" : "Neuer Tab";
                    webView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                }
                else
                {
                    webView.CoreWebView2?.Navigate(initialUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing tab webview: {ex.Message}");
            }
        }

        public void SelectTab(BrowserTab tab)
        {
            if (tab == null) return;

            foreach (var t in Tabs)
            {
                bool isActive = (t == tab);
                t.IsActive = isActive;
                if (t.WebView != null)
                {
                    t.WebView.Visibility = isActive ? Visibility.Visible : Visibility.Collapsed;
                }
            }

            ActiveTab = tab;
        }

        public void CloseTab(BrowserTab tab)
        {
            if (tab == null) return;

            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            // Remove WebView control from container
            if (tab.WebView != null)
            {
                WebViewContainer.Children.Remove(tab.WebView);
            }

            // Dispose tab and webview resources explicitly
            tab.Dispose();
            Tabs.Remove(tab);

            // If we closed the active tab, select an adjacent one
            if (ActiveTab == tab)
            {
                if (Tabs.Count > 0)
                {
                    int newIndex = Math.Clamp(index, 0, Tabs.Count - 1);
                    SelectTab(Tabs[newIndex]);
                }
                else
                {
                    // No tabs left: Open custom startpage
                    AddNewTab(StartPageService.StartPageUrl);
                }
            }
        }

        private void OnActiveTabChanged()
        {
            if (ActiveTab == null) return;

            txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
            UpdateNavigationControls();
            CheckBookmarkStatus();
            UpdateShieldBadge();
            UpdateShieldUi();
        }

        private void BtnNewTab_Click(object sender, RoutedEventArgs e)
        {
            AddNewTab(StartPageService.StartPageUrl);
        }

        private void BtnCloseTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is BrowserTab tab)
            {
                CloseTab(tab);
            }
        }

        // Tab Drag-and-Drop Reordering with Live Feedback
        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is BrowserTab tab)
            {
                // If close button was clicked, don't start dragging
                if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
                {
                    return;
                }
                _tabDragStartPoint = e.GetPosition(null);
                _draggedTab = tab;
                SelectTab(tab);
            }
        }

        private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedTab != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _tabDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe)
                    {
                        var data = new DataObject("EchoBrowserTab", _draggedTab);
                        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        _draggedTab = null;
                    }
                }
            }
        }

        private void Tab_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedTab = null;
        }

        private void Tab_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBrowserTab"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                // Live reorder while dragging
                if (e.Data.GetData("EchoBrowserTab") is BrowserTab sourceTab &&
                    sender is FrameworkElement fe && fe.DataContext is BrowserTab targetTab)
                {
                    if (sourceTab != targetTab)
                    {
                        int oldIndex = Tabs.IndexOf(sourceTab);
                        int newIndex = Tabs.IndexOf(targetTab);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            Tabs.Move(oldIndex, newIndex);
                        }
                    }
                }
            }
        }

        private void Tab_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

        #endregion

        #region Bookmarks Bar Reordering & Interactions

        private void CheckBookmarkStatus()
        {
            if (ActiveTab == null || IsStartPage(ActiveTab.Url))
            {
                pathBookmarkStar.Data = Geometry.Parse("M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z");
                pathBookmarkStar.Fill = FindResource("AccentSilverDimBrush") as Brush ?? Brushes.Gray;
                btnBookmark.ToolTip = "Startseite kann nicht als Lesezeichen gespeichert werden";
                return;
            }

            bool isBookmarked = _bookmarkService.IsBookmarked(ActiveTab.Url);
            if (isBookmarked)
            {
                pathBookmarkStar.Data = Geometry.Parse("M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z");
                pathBookmarkStar.Fill = FindResource("StatusWarningBrush") as Brush ?? Brushes.Gold;
                btnBookmark.ToolTip = "Lesezeichen bearbeiten oder entfernen";
            }
            else
            {
                pathBookmarkStar.Data = Geometry.Parse("M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z");
                pathBookmarkStar.Fill = FindResource("AccentSilverDimBrush") as Brush ?? Brushes.Gray;
                btnBookmark.ToolTip = "Diese Seite als Lesezeichen speichern (Ctrl+D)";
            }
        }

        private void BtnBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab == null || string.IsNullOrWhiteSpace(ActiveTab.Url) || IsStartPage(ActiveTab.Url)) return;

            string title = string.IsNullOrWhiteSpace(ActiveTab.Title) ? ActiveTab.Url : ActiveTab.Title;
            _bookmarkService.ToggleBookmark(title, ActiveTab.Url);
            CheckBookmarkStatus();
        }

        private void BookmarkChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                NavigateToInput(url);
            }
        }

        private void MenuItemOpenBookmarkNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string url)
            {
                AddNewTab(url);
            }
        }

        private void MenuItemDeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm)
            {
                _bookmarkService.RemoveBookmark(bm);
                CheckBookmarkStatus();
            }
        }

        // Bookmark Drag-and-Drop Reordering with Live Feedback
        private void BookmarkChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                _bmDragStartPoint = e.GetPosition(null);
                _draggedBookmark = bm;
            }
        }

        private void BookmarkChip_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedBookmark != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe)
                    {
                        var data = new DataObject("EchoBookmark", _draggedBookmark);
                        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        _draggedBookmark = null;
                    }
                }
            }
        }

        private void BookmarkChip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedBookmark = null;
        }

        private void BookmarkChip_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                // Live reorder only between non-group bookmarks that are already top-level
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm)
                {
                    if (sourceBm != targetBm && !targetBm.IsGroup && !sourceBm.IsGroup &&
                        Bookmarks.Contains(sourceBm) && Bookmarks.Contains(targetBm))
                    {
                        int oldIndex = Bookmarks.IndexOf(sourceBm);
                        int newIndex = Bookmarks.IndexOf(targetBm);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            Bookmarks.Move(oldIndex, newIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
        }

        private void BookmarkChip_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm)
            {
                e.Handled = true;
                popupBookmarkGroup.IsOpen = false;

                if (sourceBm != targetBm)
                {
                    // If sourceBm was in a group, remove from group and insert next to targetBm in top-level Bookmarks
                    Bookmark? parentGroup = null;
                    if (e.Data.GetDataPresent("EchoBookmarkSourceGroup") &&
                        e.Data.GetData("EchoBookmarkSourceGroup") is Bookmark sg)
                    {
                        parentGroup = sg;
                    }
                    else
                    {
                        parentGroup = Bookmarks.FirstOrDefault(b => b.IsGroup && b.Children.Contains(sourceBm));
                    }

                    if (parentGroup != null)
                    {
                        parentGroup.Children.Remove(sourceBm);

                        int targetIndex = Bookmarks.IndexOf(targetBm);
                        if (targetIndex >= 0)
                        {
                            Bookmarks.Insert(targetIndex, sourceBm);
                        }
                        else
                        {
                            Bookmarks.Add(sourceBm);
                        }

                        _bookmarkService.SaveBookmarks();
                        CheckBookmarkStatus();
                    }
                    else if (Bookmarks.Contains(sourceBm) && Bookmarks.Contains(targetBm))
                    {
                        int oldIndex = Bookmarks.IndexOf(sourceBm);
                        int targetIndex = Bookmarks.IndexOf(targetBm);
                        if (oldIndex >= 0 && targetIndex >= 0 && oldIndex != targetIndex)
                        {
                            Bookmarks.Move(oldIndex, targetIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
            else
            {
                e.Handled = true;
            }
        }

        private void BookmarksBar_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void BookmarksBar_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm)
            {
                e.Handled = true;
                popupBookmarkGroup.IsOpen = false;

                // Check if bookmark was in a group
                Bookmark? parentGroup = null;
                if (e.Data.GetDataPresent("EchoBookmarkSourceGroup") &&
                    e.Data.GetData("EchoBookmarkSourceGroup") is Bookmark sg)
                {
                    parentGroup = sg;
                }
                else
                {
                    parentGroup = Bookmarks.FirstOrDefault(b => b.IsGroup && b.Children.Contains(sourceBm));
                }

                if (parentGroup != null)
                {
                    parentGroup.Children.Remove(sourceBm);
                }
                else if (Bookmarks.Contains(sourceBm))
                {
                    Bookmarks.Remove(sourceBm);
                }

                // Determine insertion index based on mouse position relative to itemsBookmarksBar
                Point dropPos = e.GetPosition(itemsBookmarksBar);
                int dropIndex = GetBookmarkDropIndexAtPoint(dropPos);
                if (dropIndex >= 0 && dropIndex <= Bookmarks.Count)
                {
                    Bookmarks.Insert(dropIndex, sourceBm);
                }
                else
                {
                    Bookmarks.Add(sourceBm);
                }

                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();
            }
        }

        private int GetBookmarkDropIndexAtPoint(Point pointInItemsControl)
        {
            if (itemsBookmarksBar == null || Bookmarks.Count == 0) return Bookmarks.Count;

            for (int i = 0; i < itemsBookmarksBar.Items.Count; i++)
            {
                var container = itemsBookmarksBar.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null && container.IsLoaded)
                {
                    try
                    {
                        GeneralTransform transform = container.TransformToAncestor(itemsBookmarksBar);
                        Point containerPos = transform.Transform(new Point(0, 0));
                        double containerMidX = containerPos.X + (container.ActualWidth / 2.0);

                        if (pointInItemsControl.X < containerMidX)
                        {
                            return i;
                        }
                    }
                    catch
                    {
                        // Fallback if transform fails
                    }
                }
            }

            return Bookmarks.Count;
        }

        private void GroupChip_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm && !sourceBm.IsGroup)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;

                    if (sender is FrameworkElement fe)
                    {
                        var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                        if (border != null)
                        {
                            border.BorderBrush = FindResource("AccentSilverBrightBrush") as Brush;
                            border.Background = FindResource("BookmarkItemHoverBrush") as Brush;
                        }
                    }
                }
            }
        }

        private void GroupChip_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                if (border != null)
                {
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BackgroundProperty);
                }
            }
        }

        private void GroupChip_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark targetGroup && targetGroup.IsGroup)
            {
                var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                if (border != null)
                {
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BackgroundProperty);
                }

                if (e.Data.GetDataPresent("EchoBookmark") &&
                    e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    !sourceBm.IsGroup &&
                    sourceBm != targetGroup)
                {
                    popupBookmarkGroup.IsOpen = false;

                    // Remove from top-level Bookmarks or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    // Add to target group
                    targetGroup.Children.Add(sourceBm);
                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    e.Handled = true;
                }
            }
        }

        #region Bookmarks Bar Empty Space & Group Handling

        private Bookmark? _targetGroupForAdd;

        private void MenuAddBookmark_Click(object sender, RoutedEventArgs e)
        {
            OpenAddBookmarkDialog(null);
        }

        private void MenuAddGroup_Click(object sender, RoutedEventArgs e)
        {
            txtAddGroupName.Text = "";
            popupAddGroup.IsOpen = true;
            txtAddGroupName.Focus();
        }

        private void OpenAddBookmarkDialog(Bookmark? preselectedGroup)
        {
            _targetGroupForAdd = preselectedGroup;

            if (ActiveTab != null && ActiveTab.Url != StartPageService.StartPageUrl)
            {
                txtAddBmTitle.Text = ActiveTab.Title;
                txtAddBmUrl.Text = ActiveTab.Url;
            }
            else
            {
                txtAddBmTitle.Text = "";
                txtAddBmUrl.Text = "https://";
            }

            cmbAddBmGroup.Items.Clear();
            var mainItem = new ComboBoxItem { Content = "(Hauptleiste)", Tag = null };
            cmbAddBmGroup.Items.Add(mainItem);
            cmbAddBmGroup.SelectedItem = mainItem;

            foreach (var b in Bookmarks.Where(b => b.IsGroup))
            {
                var item = new ComboBoxItem { Content = "📁 " + b.Title, Tag = b };
                cmbAddBmGroup.Items.Add(item);
                if (preselectedGroup == b)
                {
                    cmbAddBmGroup.SelectedItem = item;
                }
            }

            popupAddBookmark.IsOpen = true;
            txtAddBmTitle.Focus();
        }

        private void BtnCancelAddBm_Click(object sender, RoutedEventArgs e)
        {
            popupAddBookmark.IsOpen = false;
        }

        private void BtnSaveAddBm_Click(object sender, RoutedEventArgs e)
        {
            string title = txtAddBmTitle.Text.Trim();
            string url = txtAddBmUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = url.Replace("https://", "").Replace("http://", "").Split('/')[0];
            }

            Bookmark? targetGroup = null;
            if (cmbAddBmGroup.SelectedItem is ComboBoxItem cItem && cItem.Tag is Bookmark grp)
            {
                targetGroup = grp;
            }

            _bookmarkService.AddBookmark(title, url, targetGroup);
            CheckBookmarkStatus();
            popupAddBookmark.IsOpen = false;
        }

        private void BtnCancelAddGroup_Click(object sender, RoutedEventArgs e)
        {
            popupAddGroup.IsOpen = false;
        }

        private void BtnSaveAddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = txtAddGroupName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            _bookmarkService.AddGroup(name);
            popupAddGroup.IsOpen = false;
        }

        private void GroupChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && ((fe.Tag as Bookmark) ?? (fe.DataContext as Bookmark)) is Bookmark group && group.IsGroup)
            {
                _activeGroupForPopup = group;
                txtBookmarkGroupName.Text = group.Title;
                txtBookmarkGroupCount.Text = group.Children.Count.ToString();
                txtGroupEmptyState.Visibility = group.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                itemsGroupBookmarks.ItemsSource = group.Children;

                popupBookmarkGroup.PlacementTarget = fe;
                popupBookmarkGroup.IsOpen = true;
            }
        }

        private void BtnCloseGroupPopup_Click(object sender, RoutedEventArgs e)
        {
            popupBookmarkGroup.IsOpen = false;
        }

        private void BtnAddBookmarkToCurrentGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = _activeGroupForPopup;
            popupBookmarkGroup.IsOpen = false;
            OpenAddBookmarkDialog(group);
        }

        private void BtnDeleteCurrentGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_activeGroupForPopup != null)
            {
                var group = _activeGroupForPopup;
                popupBookmarkGroup.IsOpen = false;
                _bookmarkService.RemoveBookmark(group);
                CheckBookmarkStatus();
            }
        }

        private void GroupBookmarkItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                _bmDragStartPoint = e.GetPosition(null);
                _draggedBookmark = bm;
            }
        }

        private void GroupBookmarkItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedBookmark != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var bm = _draggedBookmark;
                    var sourceGroup = _activeGroupForPopup;

                    var data = new DataObject("EchoBookmark", bm);
                    if (sourceGroup != null)
                    {
                        data.SetData("EchoBookmarkSourceGroup", sourceGroup);
                    }

                    try
                    {
                        popupBookmarkGroup.StaysOpen = true;
                        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
                    }
                    finally
                    {
                        popupBookmarkGroup.StaysOpen = false;
                        _draggedBookmark = null;
                    }
                }
            }
        }

        private void GroupBookmarkItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedBookmark != null && sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;
                if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                {
                    _draggedBookmark = null;
                    popupBookmarkGroup.IsOpen = false;
                    NavigateToInput(bm.Url);
                }
            }
            _draggedBookmark = null;
        }

        private void GroupBookmarkItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                popupBookmarkGroup.IsOpen = false;
                AddNewTab(bm.Url);
            }
        }

        private void MenuGroupBookmarkOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm)
            {
                popupBookmarkGroup.IsOpen = false;
                AddNewTab(bm.Url);
            }
        }

        private void MenuGroupBookmarkMoveToBar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm && _activeGroupForPopup != null)
            {
                _activeGroupForPopup.Children.Remove(bm);
                Bookmarks.Add(bm);
                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();

                txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MenuGroupBookmarkDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm && _activeGroupForPopup != null)
            {
                _activeGroupForPopup.Children.Remove(bm);
                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();

                txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GroupBookmarkItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm &&
                    _activeGroupForPopup != null)
                {
                    if (sourceBm != targetBm && _activeGroupForPopup.Children.Contains(sourceBm) && _activeGroupForPopup.Children.Contains(targetBm))
                    {
                        int oldIndex = _activeGroupForPopup.Children.IndexOf(sourceBm);
                        int newIndex = _activeGroupForPopup.Children.IndexOf(targetBm);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            _activeGroupForPopup.Children.Move(oldIndex, newIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
        }

        private void GroupBookmarkItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm &&
                _activeGroupForPopup != null)
            {
                e.Handled = true;

                if (!_activeGroupForPopup.Children.Contains(sourceBm) && !sourceBm.IsGroup)
                {
                    // Remove from top-level or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    int targetIndex = _activeGroupForPopup.Children.IndexOf(targetBm);
                    if (targetIndex >= 0)
                    {
                        _activeGroupForPopup.Children.Insert(targetIndex, sourceBm);
                    }
                    else
                    {
                        _activeGroupForPopup.Children.Add(sourceBm);
                    }

                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                    txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void GroupFlyout_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm && !sourceBm.IsGroup)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            }
        }

        private void GroupFlyout_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                !sourceBm.IsGroup &&
                _activeGroupForPopup != null)
            {
                e.Handled = true;

                if (!_activeGroupForPopup.Children.Contains(sourceBm))
                {
                    // Remove from top-level or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    _activeGroupForPopup.Children.Add(sourceBm);
                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                    txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void MenuItemAddBookmarkToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark group)
            {
                OpenAddBookmarkDialog(group);
            }
        }

        private void MenuItemDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark group)
            {
                _bookmarkService.RemoveBookmark(group);
                CheckBookmarkStatus();
            }
        }

        #endregion

        #endregion

        #region Navigation & Omnibox

        private void UpdateNavigationControls()
        {
            if (ActiveTab == null) return;

            btnBack.IsEnabled = ActiveTab.CanGoBack;
            btnForward.IsEnabled = ActiveTab.CanGoForward;

            if (ActiveTab.IsLoading)
            {
                pathReload.Visibility = Visibility.Collapsed;
                pathStop.Visibility = Visibility.Visible;
                btnReload.ToolTip = "Laden abbrechen (Esc)";
                progressLoading.Visibility = Visibility.Visible;
            }
            else
            {
                pathReload.Visibility = Visibility.Visible;
                pathStop.Visibility = Visibility.Collapsed;
                btnReload.ToolTip = "Neu laden (Ctrl+R / F5)";
                progressLoading.Visibility = Visibility.Collapsed;
            }
        }

        public static bool IsStartPage(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return true;
            return url.Equals(StartPageService.StartPageUrl, StringComparison.OrdinalIgnoreCase) ||
                   url.Equals("echo://newtab", StringComparison.OrdinalIgnoreCase) ||
                   url.Equals("about:blank", StringComparison.OrdinalIgnoreCase) ||
                   url.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);
        }

        private void NavigateToInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input) || ActiveTab?.WebView == null) return;

            string target = input.Trim();

            // Check if navigating to custom startpage
            if (target.Equals("echo://start", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("echo://newtab", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
            {
                ActiveTab.Url = StartPageService.StartPageUrl;
                ActiveTab.Title = _isIncognito ? "Neuer Tab (Inkognito)" : "Neuer Tab";
                ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                txtUrl.Text = "";
                return;
            }

            // Check if navigating to custom settings page
            if (target.Equals("echo://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("about:preferences", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("chrome://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("edge://settings", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("settings", StringComparison.OrdinalIgnoreCase))
            {
                NavigateToSettingsPage(ActiveTab);
                return;
            }

            // Direct URL vs Search detection
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
            {
                // Direct complete URL
            }
            else if (target.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ||
                     target.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase))
            {
                target = "http://" + target;
            }
            else if (Regex.IsMatch(target, @"^[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(/.*)?$") && !target.Contains(' '))
            {
                // Domain format
                target = "https://" + target;
            }
            else
            {
                // Query configured search engine from AppSettingsService!
                target = AppSettingsService.Instance.GetSearchUrl(target);
            }

            try
            {
                ActiveTab.WebView.CoreWebView2?.Navigate(target);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Navigation error: {ex.Message}");
            }
        }

        private void TxtUrl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                NavigateToInput(txtUrl.Text);
                WebViewContainer.Focus();
            }
            else if (e.Key == Key.Escape)
            {
                if (ActiveTab != null)
                {
                    txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
                }
                WebViewContainer.Focus();
            }
        }

        private void TxtUrl_GotFocus(object sender, RoutedEventArgs e)
        {
            txtUrl.SelectAll();
        }

        private void TxtUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ActiveTab != null && string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                txtUrl.Text = IsStartPage(ActiveTab.Url) ? "" : ActiveTab.Url;
            }
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoBack)
            {
                ActiveTab.WebView.GoBack();
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoForward)
            {
                ActiveTab.WebView.GoForward();
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView == null) return;

            if (ActiveTab.IsLoading)
            {
                ActiveTab.WebView.Stop();
            }
            else
            {
                if (IsStartPage(ActiveTab.Url))
                {
                    ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                    txtUrl.Text = "";
                }
                else
                {
                    ActiveTab.WebView.Reload();
                }
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView == null) return;

            if (IsStartPage(ActiveTab.Url))
            {
                ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml(_isIncognito));
                txtUrl.Text = "";
            }
            else
            {
                NavigateToInput(StartPageService.StartPageUrl);
            }
        }

        #endregion

        #region Favoriten SideBar & Custom Favorites

        private void BtnToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = borderSidebar.Visibility == Visibility.Visible;
            borderSidebar.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            menuChkSidebar.IsChecked = !isVisible;

            // Persist setting
            AppSettingsService.Instance.Settings.IsSidebarVisible = !isVisible;
            AppSettingsService.Instance.Save();
        }

        private void SidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is SidebarFavorite fav)
            {
                NavigateToInput(fav.Url);
            }
        }

        private void MenuSidebarOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SidebarFavorite fav)
            {
                AddNewTab(fav.Url);
            }
        }

        private void MenuSidebarDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is SidebarFavorite fav)
            {
                _sidebarService.RemoveFavorite(fav);
            }
        }

        private void BtnAddSidebarFavorite_Click(object sender, RoutedEventArgs e)
        {
            txtAddFavTitle.Text = "";
            txtAddFavUrl.Text = "https://";
            _selectedFavIconKey = "globe";
            _selectedFavColor = "#C4C7CC";
            popupAddFavorite.IsOpen = true;
            txtAddFavTitle.Focus();
        }

        private void SelectFavIcon_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string iconKey)
            {
                _selectedFavIconKey = iconKey;
                _selectedFavColor = iconKey switch
                {
                    "youtube" => "#FF4444",
                    "reddit" => "#FF6633",
                    "chatgpt" => "#34D399",
                    "google" => "#4285F4",
                    _ => "#C4C7CC"
                };
            }
        }

        private void BtnSaveAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            string title = txtAddFavTitle.Text.Trim();
            string url = txtAddFavUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(title))
            {
                title = url.Replace("https://", "").Replace("http://", "").TrimEnd('/');
            }

            if (string.IsNullOrWhiteSpace(url) || url == "https://" || url == "http://")
            {
                MessageBox.Show("Bitte geben Sie eine gültige Web-Adresse (URL) ein.", "Favorit hinzufügen", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            _sidebarService.AddFavorite(title, url, _selectedFavIconKey, _selectedFavColor);
            popupAddFavorite.IsOpen = false;
        }

        private void BtnCancelAddFavorite_Click(object sender, RoutedEventArgs e)
        {
            popupAddFavorite.IsOpen = false;
        }

        #endregion

        #region Site-Info & Brave Shield Popup

        private void BtnShield_Click(object sender, RoutedEventArgs e)
        {
            UpdateShieldUi();
            popupShield.IsOpen = true;
        }

        private void UpdateShieldBadge()
        {
            if (ActiveTab == null)
            {
                borderShieldBadge.Visibility = Visibility.Collapsed;
                return;
            }

            bool shieldActive = AppSettingsService.Instance.Settings.IsAdBlockerEnabled && ActiveTab.TrackingProtectionEnabled;
            int count = ActiveTab.BlockedTrackersCount;

            if (shieldActive && count > 0)
            {
                txtShieldBadgeCount.Text = count > 999 ? "999+" : count.ToString();
                borderShieldBadge.Visibility = Visibility.Visible;
            }
            else
            {
                borderShieldBadge.Visibility = Visibility.Collapsed;
            }

            if (shieldActive)
            {
                pathShieldIcon.Fill = FindResource("ShieldActiveBrush") as Brush ?? Brushes.CornflowerBlue;
            }
            else
            {
                pathShieldIcon.Fill = FindResource("TextMutedBrush") as Brush ?? Brushes.Gray;
            }
        }

        private void UpdateShieldUi()
        {
            if (ActiveTab == null) return;

            string url = ActiveTab.Url;
            string host = "Lokale Seite";

            if (IsStartPage(url))
            {
                host = "Echo Startseite";
            }
            else
            {
                try
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
                    {
                        host = uri.Host;
                        bool isWhitelisted = AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Contains(host);
                        ActiveTab.TrackingProtectionEnabled = !isWhitelisted;
                    }
                }
                catch { }
            }

            txtShieldHost.Text = string.IsNullOrWhiteSpace(host) ? "Echo-Browser" : host;

            if (ActiveTab.IsSecure || IsStartPage(url))
            {
                txtShieldStatus.Text = "Sichere Verbindung (TLS/HTTPS)";
                txtShieldStatus.Foreground = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
            }
            else
            {
                txtShieldStatus.Text = "Verbindung nicht verschlüsselt (HTTP)";
                txtShieldStatus.Foreground = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
            }

            _isUpdatingShieldUi = true;
            try
            {
                bool isGlobalOn = AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
                bool isTabOn = ActiveTab.TrackingProtectionEnabled;
                bool isProtectionActive = isGlobalOn && isTabOn;

                chkGlobalShield.IsChecked = isGlobalOn;
                chkTrackingProtection.IsChecked = isTabOn;
                chkJavaScript.IsChecked = ActiveTab.JavaScriptEnabled;
                chkPopups.IsChecked = ActiveTab.PopupsBlocked;

                if (isProtectionActive)
                {
                    txtShieldActiveState.Text = LocalizationService.Instance.GetString("Shield_ActiveStateOn", "Echo Shield: Aktiviert");
                    txtShieldActiveState.Foreground = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
                    pathShieldPopupIcon.Fill = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
                }
                else
                {
                    txtShieldActiveState.Text = LocalizationService.Instance.GetString("Shield_ActiveStateOff", "Echo Shield: Deaktiviert");
                    txtShieldActiveState.Foreground = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
                    pathShieldPopupIcon.Fill = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
                }

                txtTrackersBlocked.Text = string.Format(LocalizationService.Instance.GetString("Shield_TrackersBlockedFormat", "{0} Tracker und Werbeanzeigen blockiert"), ActiveTab.BlockedTrackersCount);
                txtFilterRuleCount.Text = string.Format(LocalizationService.Instance.GetString("Shield_FilterRuleCountFormat", "{0:N0} Filterregeln geladen"), AdBlockerService.Instance.BlockedDomainsCount);

                UpdateShieldBadge();
            }
            finally
            {
                _isUpdatingShieldUi = false;
            }
        }

        /// <summary>
        /// Bringt Cosmetic-Script und Netzwerkfilter eines Tabs in Einklang mit dem Shield-Status
        /// (global aktiv UND für die aktuelle Website nicht deaktiviert).
        /// </summary>
        private async Task SyncShieldStateAsync(BrowserTab tab)
        {
            var core = tab.WebView?.CoreWebView2;
            if (core == null) return;

            bool shouldBeActive = AppSettingsService.Instance.Settings.IsAdBlockerEnabled && tab.TrackingProtectionEnabled;

            // Netzwerkfilter nur registrieren, wenn wirklich geblockt werden soll
            try
            {
                if (shouldBeActive && !tab.IsAdBlockFilterRegistered)
                {
                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    tab.IsAdBlockFilterRegistered = true;
                }
                else if (!shouldBeActive && tab.IsAdBlockFilterRegistered)
                {
                    core.RemoveWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                    tab.IsAdBlockFilterRegistered = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Shield-Netzwerkfilter konnte nicht aktualisiert werden: {ex.Message}");
            }

            // Cosmetic element-hiding script
            try
            {
                if (shouldBeActive && tab.CosmeticScriptId == null)
                {
                    tab.CosmeticScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(AdBlockerService.Instance.GetCosmeticScript());
                }
                else if (!shouldBeActive && tab.CosmeticScriptId != null)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(tab.CosmeticScriptId);
                    tab.CosmeticScriptId = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Echo] Shield-Cosmetic-Script konnte nicht aktualisiert werden: {ex.Message}");
            }
        }

        private async Task SyncShieldStateForAllTabsAsync()
        {
            foreach (var t in Tabs.ToList())
            {
                await SyncShieldStateAsync(t);
            }
        }

        /// <summary>
        /// Fasst Badge-Updates zusammen: bei vielen geblockten Requests wird die UI
        /// einmal pro Dispatcher-Durchlauf statt einmal pro Request aktualisiert.
        /// </summary>
        private void ScheduleShieldBadgeUpdate()
        {
            if (_shieldBadgeUpdatePending) return;
            _shieldBadgeUpdatePending = true;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                _shieldBadgeUpdatePending = false;
                UpdateShieldBadge();
                if (popupShield.IsOpen)
                {
                    UpdateShieldUi();
                }
            }));
        }

        private async void ShieldOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingShieldUi || ActiveTab == null) return;

            bool prevGlobal = AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
            bool newGlobal = chkGlobalShield.IsChecked ?? true;
            if (prevGlobal != newGlobal)
            {
                AppSettingsService.Instance.Settings.IsAdBlockerEnabled = newGlobal;
                AppSettingsService.Instance.Save();
            }

            bool newTabShield = chkTrackingProtection.IsChecked ?? true;
            ActiveTab.TrackingProtectionEnabled = newTabShield;
            ActiveTab.JavaScriptEnabled = chkJavaScript.IsChecked ?? true;
            ActiveTab.PopupsBlocked = chkPopups.IsChecked ?? true;

            // Remember domain in whitelist if tracking protection is turned off
            if (Uri.TryCreate(ActiveTab.Url, UriKind.Absolute, out var curUri) && !string.IsNullOrEmpty(curUri.Host))
            {
                if (!newTabShield)
                {
                    AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Add(curUri.Host);
                }
                else
                {
                    AppSettingsService.Instance.Settings.WhitelistedShieldDomains.Remove(curUri.Host);
                }
                AppSettingsService.Instance.Save();
            }

            ActiveTab.ApplyScriptSetting();
            UpdateShieldUi();
            UpdateShieldBadge();

            // Netzwerkfilter & Cosmetic-Script synchronisieren: der globale Schalter betrifft alle Tabs
            if (prevGlobal != newGlobal)
            {
                await SyncShieldStateForAllTabsAsync();
            }
            else
            {
                await SyncShieldStateAsync(ActiveTab);
            }

            // Immediately synchronize DOM state on active tab
            bool isProtectionActive = newGlobal && newTabShield;
            if (ActiveTab?.WebView?.CoreWebView2 != null)
            {
                if (!isProtectionActive)
                {
                    try
                    {
                        await ActiveTab.WebView.CoreWebView2.ExecuteScriptAsync(
                            "window.__echoShieldDisabled = true; const s = document.getElementById('echo-shield-cosmetic'); if (s) s.remove();"
                        );
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        await ActiveTab.WebView.CoreWebView2.ExecuteScriptAsync("window.__echoShieldDisabled = false;");
                    }
                    catch { }
                }

                // If on normal web page, reload tab so that scripts and ads load cleanly without blocked state
                if (!IsStartPage(ActiveTab.Url) && !ActiveTab.Url.StartsWith("echo://", StringComparison.OrdinalIgnoreCase) && !ActiveTab.Url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    ActiveTab.WebView.Reload();
                }
            }
        }

        private async void BtnUpdateFilterList_Click(object sender, RoutedEventArgs e)
        {
            btnUpdateFilterList.IsEnabled = false;
            btnUpdateFilterList.Content = "Lade Filter...";

            try
            {
                string url = AppSettingsService.Instance.Settings.AdBlockerFilterUrl;
                await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(url, force: true);
                txtFilterRuleCount.Text = $"{AdBlockerService.Instance.BlockedDomainsCount:N0} Filterregeln geladen";
                btnUpdateFilterList.Content = "Aktualisiert!";
                await Task.Delay(1800);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Filteraktualisierung fehlgeschlagen: {ex.Message}", "Echo Shield", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                btnUpdateFilterList.Content = "Filter aktualisieren";
                btnUpdateFilterList.IsEnabled = true;
            }
        }

        private async void BtnClearSiteData_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView?.CoreWebView2 != null)
            {
                try
                {
                    await ActiveTab.WebView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                        CoreWebView2BrowsingDataKinds.Cookies | 
                        CoreWebView2BrowsingDataKinds.CacheStorage | 
                        CoreWebView2BrowsingDataKinds.IndexedDb);

                    popupShield.IsOpen = false;
                    MessageBox.Show(
                        "Cookies und Website-Cache wurden für diese Sitzung erfolgreich geleert.",
                        "Echo Shield",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to clear browsing data: {ex.Message}");
                }
            }
        }

        #endregion

        #region Toolbar Buttons, Menus & Flyouts

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
                    txtEmptyExtensions.Visibility = Visibility.Visible;
                    icExtensionsList.ItemsSource = null;
                    return;
                }

                var extensions = await ExtensionService.Instance.GetInstalledExtensionsAsync(profile);
                if (extensions.Count == 0)
                {
                    txtEmptyExtensions.Visibility = Visibility.Visible;
                    icExtensionsList.ItemsSource = null;
                }
                else
                {
                    txtEmptyExtensions.Visibility = Visibility.Collapsed;
                    icExtensionsList.ItemsSource = extensions;
                }

                UpdatePinnedExtensionsToolbar();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to refresh extensions: {ex.Message}");
            }
        }

        private async void UpdatePinnedExtensionsToolbar()
        {
            try
            {
                pnlPinnedExtensions.Children.Clear();
                var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
                if (profile == null) return;

                var pinnedIds = AppSettingsService.Instance.Settings.PinnedExtensionIds;
                if (pinnedIds == null || pinnedIds.Count == 0) return;

                var extensions = await ExtensionService.Instance.GetInstalledExtensionsAsync(profile);
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
                        var miPopup = new MenuItem { Header = "Erweiterung öffnen (Popup)" };
                        miPopup.Click += (s, e) => OpenExtensionPopup(ext, btn);
                        ctx.Items.Add(miPopup);
                    }

                    var miOptions = new MenuItem { Header = "Optionen / Einstellungen" };
                    miOptions.Click += (s, e) =>
                    {
                        string? optPage = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
                        if (!string.IsNullOrWhiteSpace(optPage))
                        {
                            AddNewTab($"chrome-extension://{ext.Id}/{optPage}");
                        }
                        else
                        {
                            ThemedDialogWindow.ShowMessage(this, ext.Name, $"Keine separate Einstellungsseite für '{ext.Name}' gefunden.");
                        }
                    };
                    ctx.Items.Add(miOptions);

                    var miUnpin = new MenuItem { Header = "Aus Symbolleiste lösen" };
                    miUnpin.Click += (s, e) =>
                    {
                        AppSettingsService.Instance.Settings.PinnedExtensionIds.Remove(ext.Id);
                        AppSettingsService.Instance.Save();
                        UpdatePinnedExtensionsToolbar();
                    };
                    ctx.Items.Add(miUnpin);

                    ctx.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });

                    var miRemove = new MenuItem { Header = "Erweiterung entfernen..." };
                    miRemove.Click += async (s, e) =>
                    {
                        if (ThemedDialogWindow.ShowExtensionRemovePrompt(this, ext.Name))
                        {
                            await ext.RemoveAsync();
                            AppSettingsService.Instance.Settings.PinnedExtensionIds.Remove(ext.Id);
                            AppSettingsService.Instance.Save();
                            await RefreshExtensionsListAsync();
                        }
                    };
                    ctx.Items.Add(miRemove);

                    btn.ContextMenu = ctx;
                    pnlPinnedExtensions.Children.Add(btn);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update pinned extensions toolbar: {ex.Message}");
            }
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

        private void BtnTogglePinExtension_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CoreWebView2BrowserExtension ext)
            {
                var pinned = AppSettingsService.Instance.Settings.PinnedExtensionIds;
                if (pinned.Contains(ext.Id))
                {
                    pinned.Remove(ext.Id);
                }
                else
                {
                    pinned.Add(ext.Id);
                }
                AppSettingsService.Instance.Save();
                UpdatePinnedExtensionsToolbar();
            }
        }

        private void BtnOpenExtensionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CoreWebView2BrowserExtension ext)
            {
                popupExtensions.IsOpen = false;
                string? optPage = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
                if (!string.IsNullOrWhiteSpace(optPage))
                {
                    AddNewTab($"chrome-extension://{ext.Id}/{optPage}");
                }
                else
                {
                    ThemedDialogWindow.ShowMessage(this, ext.Name, $"Für '{ext.Name}' ist keine separate Einstellungsseite definiert.");
                }
            }
        }

        private async void OpenExtensionPopup(CoreWebView2BrowserExtension ext, FrameworkElement anchor)
        {
            if (_webViewEnvironment == null) return;

            if (_activeExtensionPopup != null)
            {
                bool isSame = _activeExtensionPopup.ExtensionId.Equals(ext.Id, StringComparison.OrdinalIgnoreCase);
                try { _activeExtensionPopup.Close(); } catch { }
                _activeExtensionPopup = null;
                if (isSame) return;
            }

            string? popupPage = ExtensionService.Instance.GetExtensionPopupPage(ext.Id, ext.Name);
            if (string.IsNullOrWhiteSpace(popupPage))
            {
                string? optPage = ExtensionService.Instance.GetExtensionOptionsPage(ext.Id, ext.Name);
                if (!string.IsNullOrWhiteSpace(optPage))
                {
                    AddNewTab($"chrome-extension://{ext.Id}/{optPage}");
                }
                else
                {
                    ThemedDialogWindow.ShowMessage(this, ext.Name, $"Die Erweiterung '{ext.Name}' ist aktiv. Es wurde kein separates Aktionsmenü (Popup) definiert.");
                }
                return;
            }

            string popupUrl = $"chrome-extension://{ext.Id}/{popupPage}";
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

            win.PositionUnderneath(anchor);
            win.Show();
            _activeExtensionPopup = win;

            await win.InitializeAndNavigateAsync(_webViewEnvironment, ext.Id, ext.Name, popupUrl, optPageFallback, iconPath);
        }

        private void BtnOpenExtensionPopup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CoreWebView2BrowserExtension ext)
            {
                popupExtensions.IsOpen = false;
                OpenExtensionPopup(ext, btn);
            }
        }

        private async void BtnInstallExtensionFromFile_Click(object sender, RoutedEventArgs e)
        {
            var profile = ActiveTab?.WebView?.CoreWebView2?.Profile;
            if (profile == null)
            {
                ThemedDialogWindow.ShowMessage(this, "Erweiterungen", "Das Browser-Profil ist noch nicht bereit.", MessageBoxImage.Warning);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Erweiterungsdatei auswählen (.crx)",
                Filter = "Chromium Extension (*.crx)|*.crx|Alle Dateien (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                string extName = Path.GetFileNameWithoutExtension(dlg.FileName);
                if (ThemedDialogWindow.ShowExtensionInstallPrompt(this, extName, null, dlg.FileName))
                {
                    try
                    {
                        var ext = await ExtensionService.Instance.InstallExtensionFromCrxAsync(profile, dlg.FileName);
                        ThemedDialogWindow.ShowExtensionInstalledSuccess(this, ext?.Name ?? extName, ext?.Id);
                        await RefreshExtensionsListAsync();
                    }
                    catch (Exception ex)
                    {
                        ThemedDialogWindow.ShowMessage(this, "Installation fehlgeschlagen", $"Fehler beim Installieren der Erweiterung:\n{ex.Message}", MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void BtnRemoveExtension_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CoreWebView2BrowserExtension ext)
            {
                if (ThemedDialogWindow.ShowExtensionRemovePrompt(this, ext.Name))
                {
                    try
                    {
                        await ext.RemoveAsync();
                        AppSettingsService.Instance.Settings.PinnedExtensionIds.Remove(ext.Id);
                        AppSettingsService.Instance.Save();
                        await RefreshExtensionsListAsync();
                    }
                    catch (Exception ex)
                    {
                        ThemedDialogWindow.ShowMessage(this, "Erweiterungen", $"Fehler beim Entfernen der Erweiterung: {ex.Message}", MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void ChkExtensionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox chk && chk.Tag is CoreWebView2BrowserExtension ext)
            {
                try
                {
                    bool enable = chk.IsChecked ?? true;
                    await ext.EnableAsync(enable);
                    await RefreshExtensionsListAsync();
                }
                catch (Exception ex)
                {
                    ThemedDialogWindow.ShowMessage(this, "Erweiterungen", $"Fehler beim Umschalten der Erweiterung: {ex.Message}", MessageBoxImage.Warning);
                }
            }
        }

        private void BtnOpenChromeWebStore_Click(object sender, RoutedEventArgs e)
        {
            popupExtensions.IsOpen = false;
            AddNewTab("https://chromewebstore.google.com/");
        }

        private void BtnDownloads_Click(object sender, RoutedEventArgs e)
        {
            popupDownloads.IsOpen = true;
        }

        private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = true;
        }

        private void MenuNewTab_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            AddNewTab(StartPageService.StartPageUrl);
        }

        private void MenuNewWindow_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            var win = new MainWindow();
            win.Show();
        }

        private void MenuNewIncognitoWindow_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            OpenNewIncognitoWindow();
        }

        private void OpenNewIncognitoWindow()
        {
            var win = new MainWindow(isIncognito: true);
            win.Show();
        }

        private void MenuToggleBookmarksBar_Click(object sender, RoutedEventArgs e)
        {
            _isBookmarksBarVisible = !_isBookmarksBarVisible;
            borderBookmarksBar.Visibility = _isBookmarksBarVisible ? Visibility.Visible : Visibility.Collapsed;
            menuChkBookmarksBar.IsChecked = _isBookmarksBarVisible;

            // Persist setting
            AppSettingsService.Instance.Settings.IsBookmarksBarVisible = _isBookmarksBarVisible;
            AppSettingsService.Instance.Save();

            popupMenu.IsOpen = false;
        }

        private void MenuOpenThemePicker_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            popupTheme.IsOpen = true;
        }

        private void MenuDevTools_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            ActiveTab?.WebView?.CoreWebView2?.OpenDevToolsWindow();
        }

        #region Browser History Operations

        public void UpdateFilteredHistory(string filter = "")
        {
            FilteredHistory.Clear();
            string query = filter?.Trim() ?? "";

            var source = _historyService.Entries;
            foreach (var item in source)
            {
                if (string.IsNullOrWhiteSpace(query) ||
                    item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredHistory.Add(item);
                }
            }

            txtEmptyHistory.Visibility = FilteredHistory.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MenuHistory_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            bannerIncognitoHistory.Visibility = _isIncognito ? Visibility.Visible : Visibility.Collapsed;
            txtSearchHistory.Text = "";
            UpdateFilteredHistory();
            popupHistory.IsOpen = true;
            txtSearchHistory.Focus();
        }

        private void TxtSearchHistory_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateFilteredHistory(txtSearchHistory.Text);
        }

        private void HistoryItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
            {
                popupHistory.IsOpen = false;
                NavigateToInput(url);
            }
        }

        private void BtnDeleteHistoryItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is HistoryItem item)
            {
                _historyService.RemoveEntry(item);
                UpdateFilteredHistory(txtSearchHistory.Text);
            }
        }

        private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var res = MessageBox.Show(
                "Möchtest du den gesamten Browser-Verlauf wirklich löschen?",
                "Verlauf leeren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                _historyService.ClearHistory();
                UpdateFilteredHistory();
            }
        }

        #endregion

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            OpenSettingsTab();
        }

        private void BtnThemePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string presetName)
            {
                if (Enum.TryParse<ThemePreset>(presetName, out var preset))
                {
                    ThemeManager.Instance.ApplyPreset(preset);
                    AppSettingsService.Instance.Settings.ThemePreset = presetName;
                    AppSettingsService.Instance.Save();
                }
                popupTheme.IsOpen = false;
            }
        }

        private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                try
                {
                    Color color = ThemeManager.ColorFromHex(hex);
                    ThemeManager.Instance.SetAccentColor(color);
                    AppSettingsService.Instance.Settings.AccentColor = hex;
                    AppSettingsService.Instance.Save();
                }
                catch { }
                popupTheme.IsOpen = false;
            }
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

        #region Settings & Session Management

        public void NavigateToSettingsPage(BrowserTab? tab)
        {
            if (tab?.WebView == null) return;
            tab.Url = SettingsPageService.SettingsPageUrl;
            tab.Title = "Einstellungen";
            string webViewVer = _webViewEnvironment?.BrowserVersionString ?? "120.0";
            string html = SettingsPageService.GetSettingsPageHtml(AppSettingsService.Instance.Settings, webViewVer, "1.2");
            tab.WebView.NavigateToString(html);
            if (tab == ActiveTab)
            {
                txtUrl.Text = SettingsPageService.SettingsPageUrl;
                UpdateNavigationControls();
            }
        }

        public void OpenSettingsTab()
        {
            // If already open, switch to that tab
            foreach (var tab in Tabs)
            {
                if (tab.Url.Equals(SettingsPageService.SettingsPageUrl, StringComparison.OrdinalIgnoreCase))
                {
                    SelectTab(tab);
                    return;
                }
            }

            // If current tab is clean start page, navigate within it
            if (ActiveTab != null && (IsStartPage(ActiveTab.Url) || string.IsNullOrWhiteSpace(ActiveTab.Url)))
            {
                NavigateToSettingsPage(ActiveTab);
            }
            else
            {
                AddNewTab(SettingsPageService.SettingsPageUrl);
            }
        }

        private void SaveCurrentSession()
        {
            if (_isIncognito) return;
            try
            {
                var urls = Tabs.Select(t => t.Url)
                               .Where(u => !string.IsNullOrWhiteSpace(u) && 
                                           u != StartPageService.StartPageUrl && 
                                           u != SettingsPageService.SettingsPageUrl &&
                                           !u.StartsWith("echo://", StringComparison.OrdinalIgnoreCase))
                               .ToList();
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string file = Path.Combine(appData, "EchoBrowser", "session.json");
                AtomicFile.WriteAllText(file, JsonSerializer.Serialize(urls));
            }
            catch { }
        }

        private void RestorePreviousSession()
        {
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string file = Path.Combine(appData, "EchoBrowser", "session.json");
                if (File.Exists(file))
                {
                    var urls = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file));
                    if (urls != null && urls.Count > 0)
                    {
                        foreach (var url in urls)
                        {
                            AddNewTab(url);
                        }
                        return;
                    }
                }
            }
            catch { }

            AddNewTab(StartPageService.StartPageUrl);
        }

        private void ApplyTrackingPrevention(string level)
        {
            var preventionLevel = level.ToLowerInvariant() switch
            {
                "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                "none" => CoreWebView2TrackingPreventionLevel.None,
                _ => CoreWebView2TrackingPreventionLevel.Balanced
            };

            foreach (var tab in Tabs)
            {
                try
                {
                    if (tab.WebView?.CoreWebView2?.Profile != null)
                    {
                        tab.WebView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = preventionLevel;
                    }
                }
                catch { }
            }
        }

        private void ApplyDefaultZoom(int percent)
        {
            double zoomFactor = Math.Clamp(percent / 100.0, 0.5, 3.0);
            foreach (var tab in Tabs)
            {
                try
                {
                    if (tab.WebView?.CoreWebView2 != null && 
                        tab.Url != StartPageService.StartPageUrl && 
                        tab.Url != SettingsPageService.SettingsPageUrl)
                    {
                        tab.WebView.CoreWebView2.Settings.IsZoomControlEnabled = true;
                        tab.WebView.ZoomFactor = zoomFactor;
                    }
                }
                catch { }
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+, -> Settings
            if (e.Key == Key.OemComma && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OpenSettingsTab();
                e.Handled = true;
            }
            // Ctrl+T -> New Tab
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                AddNewTab(StartPageService.StartPageUrl);
                e.Handled = true;
            }
            // Ctrl+W -> Close Tab
            else if (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ActiveTab != null) CloseTab(ActiveTab);
                e.Handled = true;
            }
            // Ctrl+Shift+N -> New Incognito Window
            else if (e.Key == Key.N && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OpenNewIncognitoWindow();
                e.Handled = true;
            }
            // Ctrl+N -> New Window
            else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var win = new MainWindow();
                win.Show();
                e.Handled = true;
            }
            // Ctrl+Shift+B -> Toggle Bookmarks Bar
            else if (e.Key == Key.B && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                MenuToggleBookmarksBar_Click(sender, e);
                e.Handled = true;
            }
            // Ctrl+H -> History
            else if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (popupHistory.IsOpen)
                {
                    popupHistory.IsOpen = false;
                }
                else
                {
                    MenuHistory_Click(sender, e);
                }
                e.Handled = true;
            }
            // Ctrl+J -> Downloads
            else if (e.Key == Key.J && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                popupDownloads.IsOpen = !popupDownloads.IsOpen;
                e.Handled = true;
            }
            // Ctrl+L or Alt+D -> Focus Omnibox
            else if ((e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                     (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                txtUrl.Focus();
                txtUrl.SelectAll();
                e.Handled = true;
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isIncognito && Tabs.Count > 1 && AppSettingsService.Instance.Settings.WarnOnClosingMultipleTabs)
            {
                string title = LocalizationService.Instance.GetString("Dialog_CloseBrowserTitle", "Echo-Browser beenden");
                string msg = string.Format(LocalizationService.Instance.GetString("Dialog_CloseMultipleTabsMessage", "Möchtest du wirklich alle {0} geöffneten Tabs schließen?"), Tabs.Count);
                string confirm = LocalizationService.Instance.GetString("Dialog_CloseAllTabs", "Alle Tabs schließen");
                string cancel = LocalizationService.Instance.GetString("Dialog_Cancel", "Abbrechen");

                bool confirmed = ThemedDialogWindow.ShowConfirm(this, title, msg, confirm, cancel);
                if (!confirmed)
                {
                    e.Cancel = true;
                    return;
                }
            }

            SaveCurrentSession();

            if (!_isIncognito && AppSettingsService.Instance.Settings.ClearDataOnExit)
            {
                try
                {
                    _historyService.ClearHistory();
                }
                catch { }
            }

            if (_isIncognito && !string.IsNullOrEmpty(_incognitoFolder))
            {
                try
                {
                    if (Directory.Exists(_incognitoFolder))
                    {
                        Directory.Delete(_incognitoFolder, true);
                    }
                }
                catch { }
            }
        }

        #endregion

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

        #endregion

        #region Keyboard Shortcuts

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+T: New Tab
            if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                AddNewTab(StartPageService.StartPageUrl);
                e.Handled = true;
            }
            // Ctrl+W: Close Tab
            else if (e.Key == Key.W && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (ActiveTab != null) CloseTab(ActiveTab);
                e.Handled = true;
            }
            // Ctrl+Shift+N: New Incognito Window
            else if (e.Key == Key.N && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                OpenNewIncognitoWindow();
                e.Handled = true;
            }
            // Ctrl+N: New Window
            else if (e.Key == Key.N && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                var win = new MainWindow();
                win.Show();
                e.Handled = true;
            }
            // Ctrl+L or Alt+D: Focus Omnibox
            else if ((e.Key == Key.L && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control) ||
                     (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt))
            {
                txtUrl.Focus();
                txtUrl.SelectAll();
                e.Handled = true;
            }
            // Ctrl+D: Bookmark Current Page
            else if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnBookmark_Click(sender, e);
                e.Handled = true;
            }
            // Ctrl+Shift+B: Toggle Bookmarks Bar
            else if (e.Key == Key.B && 
                     (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && 
                     (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                MenuToggleBookmarksBar_Click(sender, e);
                e.Handled = true;
            }
            // Ctrl+J: Open Downloads
            else if (e.Key == Key.J && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                popupDownloads.IsOpen = !popupDownloads.IsOpen;
                e.Handled = true;
            }
            // Ctrl+H: Open History
            else if (e.Key == Key.H && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (popupHistory.IsOpen)
                {
                    popupHistory.IsOpen = false;
                }
                else
                {
                    MenuHistory_Click(sender, e);
                }
                e.Handled = true;
            }
            // F12: Open DevTools
            else if (e.Key == Key.F12)
            {
                ActiveTab?.WebView?.CoreWebView2?.OpenDevToolsWindow();
                e.Handled = true;
            }
            // F5 or Ctrl+R: Reload
            else if (e.Key == Key.F5 || (e.Key == Key.R && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control))
            {
                BtnReload_Click(sender, e);
                e.Handled = true;
            }
            // Esc: Stop Loading
            else if (e.Key == Key.Escape)
            {
                if (ActiveTab?.IsLoading == true)
                {
                    ActiveTab.WebView?.Stop();
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Visual Helpers

        private static T? FindVisualParent<T>(DependencyObject? child, Func<T, bool>? predicate = null) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent && (predicate == null || predicate(parent)))
                {
                    return parent;
                }
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent, Func<T, bool>? predicate = null) where T : DependencyObject
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (predicate == null || predicate(typed)))
                {
                    return typed;
                }
                var desc = FindVisualChild<T>(child, predicate);
                if (desc != null) return desc;
            }
            return null;
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Ausstehende (verzögerte) Verlaufsänderungen sofort schreiben
            if (!_isIncognito)
            {
                _historyService.SaveHistory();
            }

            foreach (var tab in Tabs.ToList())
            {
                tab.Dispose();
            }
            Tabs.Clear();

            if (_isIncognito && !string.IsNullOrEmpty(_incognitoFolder))
            {
                try
                {
                    if (Directory.Exists(_incognitoFolder))
                    {
                        Directory.Delete(_incognitoFolder, true);
                    }
                }
                catch { }
            }
        }
    }
}