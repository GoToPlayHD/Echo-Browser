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
            // Sprache vor InitializeComponent setzen, damit die XAML-Texte direkt richtig erscheinen
            LocalizationService.Instance.SetLanguage(AppSettingsService.Instance.Settings.Language);
            InitializeComponent();
            InitializeToolbarContextMenus();
            InitializeHistoryPanel();
            InitializeDownloadsPanel();
            themePanel.SelectionMade += () => popupTheme.IsOpen = false;
            DataContext = this;
            StateChanged += MainWindow_StateChanged;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            Closing += MainWindow_Closing;
            RegisterWebMessageHandlers();
            LocalizationService.Instance.LanguageChanged += ApplyLocalizationToUi;

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
                    Tr.Format("Error_WebViewInit", ex.Message),
                    Tr.Get("Error_WebViewInitTitle"),
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

#region Toolbar Buttons, Menus & Flyouts

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

private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            OpenSettingsTab();
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
            LocalizationService.Instance.LanguageChanged -= ApplyLocalizationToUi;

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