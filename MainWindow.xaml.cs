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
        // Gemeinsame Daten aller Fenster
        private readonly BookmarkService _bookmarkService = BookmarkService.Instance;
        private readonly SidebarService _sidebarService = SidebarService.Instance;
        private readonly HistoryService _historyService = HistoryService.Instance;
        private CoreWebView2Environment? _webViewEnvironment;
        private BrowserTab? _activeTab;
        private bool _isBookmarksBarVisible = true;
        private bool _isSyncingSearchEngine;
        private bool _shieldBadgeUpdatePending;
        private ExtensionPopupWindow? _activeExtensionPopup;

        // Drag & Drop State for Tabs and Bookmarks
        private Point _tabDragStartPoint;
        private BrowserTab? _draggedTab;
        private Point _bmDragStartPoint;
        private Bookmark? _draggedBookmark;
        private Bookmark? _activeGroupForPopup;

        // Add Favorite State

        public ObservableCollection<BrowserTab> Tabs { get; } = new();
        public ObservableCollection<Bookmark> Bookmarks => _bookmarkService.Bookmarks;
        public ObservableCollection<SidebarFavorite> SidebarFavorites => _sidebarService.Favorites;

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

        public bool IsIncognito => _isIncognito;

        /// <summary>Nur das erste Fenster der App führt das Startverhalten aus (Startseite, Sitzung, eigene URL).</summary>
        public bool IsInitialWindow { get; init; }

        /// <summary>Adressen, die zusätzlich als Tabs geöffnet werden (Kommandozeile, Link aus einer anderen App).</summary>
        public IReadOnlyList<string> StartupUrls { get; init; } = Array.Empty<string>();

        /// <summary>Beim Wiederherstellen einer Sitzung mit mehreren Fenstern: die Tabs dieses Fensters.</summary>
        public SessionWindow? WindowToRestore { get; init; }

        /// <summary>Das zuletzt aktive Fenster – Ziel für Adressen, die von außen kommen.</summary>
        public static MainWindow? LastActive { get; private set; }

        public MainWindow() : this(false)
        {
        }

        public MainWindow(bool isIncognito)
        {
            _isIncognito = isIncognito;
            // Sprache vor InitializeComponent setzen, damit die XAML-Texte direkt richtig erscheinen
            LocalizationService.Instance.SetLanguage(AppSettingsService.Instance.Settings.Language);
            InitializeComponent();
            InitializeToolbarContextMenus();
            InitializeHistoryPanel();
            InitializeDownloadsPanel();
            themePanel.SelectionMade += () => popupTheme.IsOpen = false;
            InitializeAddFavoritePanel();
            InitializeBookmarkFormPanels();
            InitializeShieldPanel();
            InitializeExtensionsPanel();
            InitializeFindBar();
            InitializeOmnibox();
            InitializePermissions();
            InitializeCommandPalette();
            InitializeTabSleep();
            InitializeTabGroups();
            InitializeVerticalTabs();
            InitializeWindowChrome();
            InitializeThemeSync();

            // Flyouts der rechten Symbolleiste rechtsbündig unter ihrem Knopf (wie Chrome), statt über den Fensterrand zu ragen
            foreach (var popup in new[] { popupExtensions, popupDownloads, popupMenu, popupTheme, popupHistory, popupPerformance })
            {
                AlignPopupRightEdge(popup);
            }
            popupToast.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
            {
                new System.Windows.Controls.Primitives.CustomPopupPlacement(
                    new Point((targetSize.Width - popupSize.Width) / 2, 18),
                    System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
            };
            DataContext = this;
            StateChanged += MainWindow_StateChanged;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            Activated += (s, e) => LastActive = this;
            RegisterWebMessageHandlers();
            LocalizationService.Instance.LanguageChanged += ApplyLocalizationToUi;

            if (!_isIncognito)
            {
                SessionService.Instance.Resume();
                Tabs.CollectionChanged += (s, e) => ScheduleSessionSave();
            }

            if (_isIncognito)
            {
                Title = Tr.Get("Window_TitleIncognito");
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
                    ActiveTab.WebView?.CoreWebView2?.Reload();
                }
            }
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!await InitializeBrowserEnvironmentAsync())
            {
                Close();
                return;
            }

            // 1. Initialize Localization from AppSettings
            LocalizationService.Instance.SetLanguage(AppSettingsService.Instance.Settings.Language);

            // 2. Apple-Style Language Selection Onboarding on First Launch
            if (!_isIncognito && !AppSettingsService.Instance.Settings.HasCompletedFirstRunLanguageSetup)
            {
                var setupWin = new LanguageSetupWindow { Owner = this };
                setupWin.ShowDialog();
            }

            ApplyLocalizationToUi();

            // Startseite, Sitzung oder übergebene Adressen öffnen -> MainWindow.Session.cs
            OpenInitialTabs();

            _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(600);
                UpdatePinnedExtensionsToolbar();
            });

            // 3. Velopack Update-Infrastruktur initialisieren & Hintergrund-Timer starten
            UpdateService.Instance.StatusChanged += OnUpdateStatusChanged;
            UpdateService.Instance.StartAutoCheckTimer();
            UpdateUiForUpdateStatus(UpdateService.Instance.Status, UpdateService.Instance.StatusMessage, UpdateService.Instance.DownloadProgress, UpdateService.Instance.AvailableVersion);
        }

        /// <summary>
        /// Aktualisiert Texte, die per Code gesetzt werden. XAML-Texte ({loc:Loc ...}) aktualisieren sich selbst.
        /// Wird beim Start und bei jedem Sprachwechsel aufgerufen.
        /// </summary>
        private void ApplyLocalizationToUi()
        {
            try
            {
                if (_isIncognito)
                {
                    Title = Tr.Get("Window_TitleIncognito");
                }

                UpdateNavigationControls();
                CheckBookmarkStatus();
                UpdateShieldUi();

                // Interne Seiten neu aufbauen, damit sie in der neuen Sprache erscheinen
                foreach (var tab in Tabs.ToList())
                {
                    RefreshInternalPage(tab);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to apply localization", ex);
            }
        }

        /// <summary>
        /// Holt die gemeinsame WebView2-Umgebung (alle Fenster, auch Inkognito über ein InPrivate-Profil).
        /// Gibt false zurück, wenn der Browser nicht starten kann – das Fenster schließt sich dann.
        /// </summary>
        private async Task<bool> InitializeBrowserEnvironmentAsync()
        {
            try
            {
                _webViewEnvironment = await BrowserEnvironment.GetAsync();
                _ = AdBlockerService.Instance.InitializeAsync();
                borderSplash.Visibility = Visibility.Collapsed;
                return true;
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                Log.Error("WebView2 Runtime nicht gefunden", ex);
                bool download = ThemedDialogWindow.ShowConfirm(
                    this,
                    Tr.Get("Error_WebViewInitTitle"),
                    Tr.Get("Error_WebViewMissing"),
                    Tr.Get("Error_WebViewDownload"),
                    Tr.Get("Common_Close"));
                if (download)
                {
                    Process.Start(new ProcessStartInfo { FileName = WebView2RuntimeDownloadUrl, UseShellExecute = true });
                }
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("WebView2-Umgebung konnte nicht erzeugt werden", ex);
                ThemedDialogWindow.ShowMessage(
                    this,
                    Tr.Get("Error_WebViewInitTitle"),
                    Tr.Format("Error_WebViewInit", ex.Message),
                    MessageBoxImage.Error);
                return false;
            }
        }

        private const string WebView2RuntimeDownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

        /// <summary>Fenster nach vorne holen, auch wenn es minimiert ist.</summary>
        public void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;  // Windows verweigert sonst manchmal das Fokussieren aus dem Hintergrund
            Topmost = false;
            Focus();
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
            UpdateMaximizedMargin();
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

#region Toolbar Buttons, Menus & Flyouts

private void BtnMenu_Click(object sender, RoutedEventArgs e)
        {
            UpdateZoomIndicator();
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

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            OpenSettingsTab();
        }

        private async void MenuUpdate_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;

            if (UpdateService.Instance.IsUpdateReadyToRestart)
            {
                bool confirmed = ThemedDialogWindow.ShowConfirm(
                    this,
                    Tr.Get("Update_PromptHeadline"),
                    Tr.Format("Update_PromptMessage", UpdateService.Instance.AvailableVersion ?? ""),
                    Tr.Get("Update_RestartNow"),
                    Tr.Get("Update_Later")
                );

                if (confirmed)
                {
                    SaveSessionForRestart();
                    UpdateService.Instance.RestartAndApplyUpdate();
                }
                return;
            }

            if (UpdateService.Instance.Status == UpdateStatus.Downloading)
            {
                ThemedDialogWindow.ShowMessage(
                    this,
                    Tr.Get("Update_Title"),
                    Tr.Format("Update_DownloadingPercent", UpdateService.Instance.AvailableVersion ?? "", UpdateService.Instance.DownloadProgress),
                    MessageBoxImage.Information
                );
                return;
            }

            if (UpdateService.Instance.Status == UpdateStatus.Checking)
            {
                ThemedDialogWindow.ShowMessage(
                    this,
                    Tr.Get("Update_Title"),
                    Tr.Get("Update_Checking"),
                    MessageBoxImage.Information
                );
                return;
            }

            // Manuelle Prüfung anstoßen
            var result = await UpdateService.Instance.CheckForUpdatesAsync(isManualCheck: true);

            if (result.Status == UpdateStatus.ReadyToRestart)
            {
                bool confirmed = ThemedDialogWindow.ShowConfirm(
                    this,
                    Tr.Get("Update_PromptHeadline"),
                    Tr.Format("Update_PromptMessage", result.AvailableVersion ?? ""),
                    Tr.Get("Update_RestartNow"),
                    Tr.Get("Update_Later")
                );

                if (confirmed)
                {
                    SaveSessionForRestart();
                    UpdateService.Instance.RestartAndApplyUpdate();
                }
            }
            else if (result.Status == UpdateStatus.UpToDate)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Update_Title"), result.Message, MessageBoxImage.Information);
            }
            else if (result.Status == UpdateStatus.NotInstalled)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Update_Title"), result.Message, MessageBoxImage.Information);
            }
            else if (result.Status == UpdateStatus.Error)
            {
                ThemedDialogWindow.ShowMessage(this, Tr.Get("Update_Title"), result.Message, MessageBoxImage.Warning);
            }
        }

        private void OnUpdateStatusChanged(object? sender, UpdateStatusEventArgs e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                UpdateUiForUpdateStatus(e.Status, e.Message, e.Progress, e.AvailableVersion);

                // Geöffnete Einstellungsseiten in Tabs synchronisieren
                string statusJson = JsonSerializer.Serialize(new
                {
                    status = e.Status.ToString(),
                    message = e.Message,
                    progress = e.Progress,
                    version = e.AvailableVersion
                });

                foreach (var tab in Tabs)
                {
                    if (tab.Url.Equals(SettingsPageService.SettingsPageUrl, StringComparison.OrdinalIgnoreCase))
                    {
                        tab.WebView?.CoreWebView2?.ExecuteScriptAsync($"window.onUpdateStatusChanged && window.onUpdateStatusChanged({statusJson});");
                    }
                }
            });
        }

        private void UpdateUiForUpdateStatus(UpdateStatus status, string message, int progress, string? version)
        {
            switch (status)
            {
                case UpdateStatus.ReadyToRestart:
                    menuUpdateBadge.Visibility = Visibility.Visible;
                    menuFlyoutUpdateBadge.Visibility = Visibility.Visible;
                    menuItemUpdate.Header = Tr.Get("Menu_UpdateReadyRestart");
                    pathMenuUpdateIcon.Fill = (Brush)FindResource("ShieldActiveBrush");
                    break;

                case UpdateStatus.Downloading:
                    menuUpdateBadge.Visibility = Visibility.Collapsed;
                    menuFlyoutUpdateBadge.Visibility = Visibility.Visible;
                    menuItemUpdate.Header = Tr.Format("Menu_DownloadingUpdate", progress);
                    pathMenuUpdateIcon.Fill = (Brush)FindResource("AccentSilverBrush");
                    break;

                case UpdateStatus.Checking:
                    menuUpdateBadge.Visibility = Visibility.Collapsed;
                    menuFlyoutUpdateBadge.Visibility = Visibility.Collapsed;
                    menuItemUpdate.Header = Tr.Get("Menu_CheckingForUpdates");
                    pathMenuUpdateIcon.Fill = (Brush)FindResource("AccentSilverBrush");
                    break;

                default:
                    menuUpdateBadge.Visibility = Visibility.Collapsed;
                    menuFlyoutUpdateBadge.Visibility = Visibility.Collapsed;
                    menuItemUpdate.Header = Tr.Get("Menu_CheckForUpdates");
                    pathMenuUpdateIcon.Fill = (Brush)FindResource("AccentSilverBrush");
                    break;
            }
        }

#endregion

#region Visual Helpers

        private static void AlignPopupRightEdge(Popup popup)
        {
            popup.Placement = PlacementMode.Custom;
            popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
            {
                new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width + 4, targetSize.Height), PopupPrimaryAxis.Horizontal)
            };
        }

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
            LocalizationService.Instance.LanguageChanged -= ApplyLocalizationToUi;

            // Ausstehende (verzögerte) Verlaufsänderungen sofort schreiben
            if (!_isIncognito)
            {
                _historyService.SaveHistory();
            }

            // Inkognito-Daten liegen im InPrivate-Profil und verschwinden mit der letzten Inkognito-WebView
            foreach (var tab in Tabs.ToList())
            {
                tab.Dispose();
            }
            Tabs.Clear();

            if (LastActive == this)
            {
                LastActive = null;
            }
        }
    }
}