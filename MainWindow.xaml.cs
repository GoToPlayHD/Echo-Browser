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

        // Drag & Drop State for Tabs and Bookmarks
        private Point _tabDragStartPoint;
        private BrowserTab? _draggedTab;
        private Point _bmDragStartPoint;
        private Bookmark? _draggedBookmark;

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

            // Restore Search Engine selector
            SyncSearchEngineComboBox();
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
            AddNewTab(StartPageService.StartPageUrl);
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

                _webViewEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
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

        public void AddNewTab(string? targetUrl = null)
        {
            if (_webViewEnvironment == null) return;

            string initialUrl = string.IsNullOrWhiteSpace(targetUrl) ? StartPageService.StartPageUrl : targetUrl;
            var tab = new BrowserTab
            {
                Title = "Neuer Tab",
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
            SelectTab(tab);
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

                    try
                    {
                        webView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = 
                            CoreWebView2TrackingPreventionLevel.Balanced;
                    }
                    catch { }

                    // Intercept WebMessages from custom startpage
                    webView.CoreWebView2.WebMessageReceived += (s, args) =>
                    {
                        try
                        {
                            string json = args.WebMessageAsJson;
                            using var doc = JsonDocument.Parse(json);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("type", out var typeProp))
                            {
                                string type = typeProp.GetString() ?? "";
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
                            Dispatcher.Invoke(() => AddNewTab(args.Uri));
                        }
                    };

                    // Handle downloads
                    webView.CoreWebView2.DownloadStarting += (s, args) =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            string fileName = Path.GetFileName(args.ResultFilePath);
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
                            if (tab.Url != StartPageService.StartPageUrl)
                            {
                                tab.Title = webView.CoreWebView2.DocumentTitle;

                                // Record history if NOT in incognito mode
                                if (!_isIncognito && !string.IsNullOrWhiteSpace(tab.Url))
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
                webView.NavigationStarting += (s, args) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        tab.IsLoading = true;
                        tab.Url = args.Uri;
                        if (tab == ActiveTab)
                        {
                            UpdateNavigationControls();
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
                        if (!_isIncognito && !string.IsNullOrWhiteSpace(tab.Url) && tab.Url != StartPageService.StartPageUrl)
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
                            txtUrl.Text = (tab.Url == StartPageService.StartPageUrl) ? "" : tab.Url;
                            CheckBookmarkStatus();
                            UpdateShieldUi();
                        }
                    });
                };

                // Navigate initially (Show custom startpage if requested)
                if (initialUrl == StartPageService.StartPageUrl || initialUrl == "about:blank")
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

            txtUrl.Text = (ActiveTab.Url == StartPageService.StartPageUrl) ? "" : ActiveTab.Url;
            UpdateNavigationControls();
            CheckBookmarkStatus();
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
            if (ActiveTab == null) return;

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
            if (ActiveTab == null || string.IsNullOrWhiteSpace(ActiveTab.Url) || ActiveTab.Url == StartPageService.StartPageUrl) return;

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

                // Live reorder only between non-group bookmarks
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm)
                {
                    if (sourceBm != targetBm && !targetBm.IsGroup && !sourceBm.IsGroup)
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
            e.Handled = true;
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
            if (sender is FrameworkElement fe && fe.Tag is Bookmark group && group.IsGroup)
            {
                var cm = new ContextMenu
                {
                    Background = (Brush)FindResource("SurfaceBrush"),
                    BorderBrush = (Brush)FindResource("BorderBrush"),
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                };

                if (group.Children.Count == 0)
                {
                    var emptyItem = new MenuItem
                    {
                        Header = "(Keine Lesezeichen in dieser Gruppe)",
                        IsEnabled = false,
                        Foreground = (Brush)FindResource("TextMutedBrush")
                    };
                    cm.Items.Add(emptyItem);
                }
                else
                {
                    foreach (var child in group.Children)
                    {
                        var childItem = new MenuItem
                        {
                            Header = child.Title,
                            Tag = child.Url,
                            Foreground = (Brush)FindResource("TextPrimaryBrush")
                        };
                        childItem.Click += (s, args) =>
                        {
                            if (childItem.Tag is string u) NavigateToInput(u);
                        };

                        var subCm = new ContextMenu
                        {
                            Background = (Brush)FindResource("SurfaceBrush"),
                            BorderBrush = (Brush)FindResource("BorderBrush"),
                            Foreground = (Brush)FindResource("TextPrimaryBrush")
                        };
                        var delChild = new MenuItem { Header = "Aus Gruppe entfernen", Tag = child };
                        delChild.Click += (s, args) =>
                        {
                            group.Children.Remove(child);
                            _bookmarkService.SaveBookmarks();
                            CheckBookmarkStatus();
                        };
                        subCm.Items.Add(delChild);
                        childItem.ContextMenu = subCm;

                        cm.Items.Add(childItem);
                    }
                }

                cm.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });

                var addItem = new MenuItem
                {
                    Header = "+ Lesezeichen zu dieser Gruppe hinzufügen...",
                    Foreground = (Brush)FindResource("AccentSilverBrightBrush")
                };
                addItem.Click += (s, args) => OpenAddBookmarkDialog(group);
                cm.Items.Add(addItem);

                var delGroup = new MenuItem
                {
                    Header = "Gruppe löschen",
                    Foreground = (Brush)FindResource("TextPrimaryBrush")
                };
                delGroup.Click += (s, args) =>
                {
                    _bookmarkService.RemoveBookmark(group);
                    CheckBookmarkStatus();
                };
                cm.Items.Add(delGroup);

                cm.PlacementTarget = fe;
                cm.IsOpen = true;
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
                    txtUrl.Text = (ActiveTab.Url == StartPageService.StartPageUrl) ? "" : ActiveTab.Url;
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
                txtUrl.Text = (ActiveTab.Url == StartPageService.StartPageUrl) ? "" : ActiveTab.Url;
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
                if (ActiveTab.Url == StartPageService.StartPageUrl)
                {
                    ActiveTab.WebView.NavigateToString(StartPageService.GetStartPageHtml());
                }
                else
                {
                    ActiveTab.WebView.Reload();
                }
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            NavigateToInput(StartPageService.StartPageUrl);
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

        private void UpdateShieldUi()
        {
            if (ActiveTab == null) return;

            string url = ActiveTab.Url;
            string host = "Lokale Seite";

            if (url == StartPageService.StartPageUrl)
            {
                host = "Echo Startseite";
            }
            else
            {
                try
                {
                    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
                    {
                        host = uri.Host;
                    }
                }
                catch { }
            }

            txtShieldHost.Text = string.IsNullOrWhiteSpace(host) ? "Echo-Browser" : host;

            if (ActiveTab.IsSecure || url == StartPageService.StartPageUrl)
            {
                txtShieldStatus.Text = "Sichere Verbindung (TLS/HTTPS)";
                txtShieldStatus.Foreground = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
                pathShieldIcon.Fill = FindResource("StatusSuccessBrush") as Brush ?? Brushes.Green;
            }
            else
            {
                txtShieldStatus.Text = "Verbindung nicht verschlüsselt (HTTP)";
                txtShieldStatus.Foreground = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
                pathShieldIcon.Fill = FindResource("StatusWarningBrush") as Brush ?? Brushes.Orange;
            }

            chkTrackingProtection.IsChecked = ActiveTab.TrackingProtectionEnabled;
            chkJavaScript.IsChecked = ActiveTab.JavaScriptEnabled;
            chkPopups.IsChecked = ActiveTab.PopupsBlocked;

            txtTrackersBlocked.Text = ActiveTab.TrackingProtectionEnabled
                ? "Tracker & Fingerprinting aktiv blockiert"
                : "Schutz deaktiviert";
        }

        private void ShieldOption_Changed(object sender, RoutedEventArgs e)
        {
            if (ActiveTab == null) return;

            ActiveTab.TrackingProtectionEnabled = chkTrackingProtection.IsChecked ?? true;
            ActiveTab.JavaScriptEnabled = chkJavaScript.IsChecked ?? true;
            ActiveTab.PopupsBlocked = chkPopups.IsChecked ?? true;

            ActiveTab.ApplyScriptSetting();
            txtTrackersBlocked.Text = ActiveTab.TrackingProtectionEnabled
                ? "Tracker & Fingerprinting aktiv blockiert"
                : "Schutz deaktiviert";
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

        private void BtnExtensions_Click(object sender, RoutedEventArgs e)
        {
            popupExtensions.IsOpen = true;
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
            MessageBox.Show(
                "Echo-Browser Version 1.1 (Silver/Anthracite Edition)\nEngine: Microsoft WebView2 / Chromium\nPlattform: .NET 8.0 WPF",
                "Über Echo-Browser",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnThemePreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string presetName)
            {
                if (Enum.TryParse<ThemePreset>(presetName, out var preset))
                {
                    ThemeManager.Instance.ApplyPreset(preset);
                }
                popupTheme.IsOpen = false;
            }
        }

        private void BtnAccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                Color color = ThemeManager.ColorFromHex(hex);
                ThemeManager.Instance.SetAccentColor(color);
                popupTheme.IsOpen = false;
            }
        }

        private void BtnOpenDownloadsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string downloadsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                    "Downloads");
                Process.Start("explorer.exe", downloadsPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not open downloads: {ex.Message}");
            }
        }

        private void BtnOpenDownloadFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath && File.Exists(filePath))
            {
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