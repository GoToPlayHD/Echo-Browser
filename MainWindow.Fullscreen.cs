using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Vollbild: F11 (ganzer Browser ohne Leisten) und Vollbild-Elemente von Webseiten (z.B. Videos).
    /// Das Fenster deckt dabei den ganzen Monitor ab, auch die Taskleiste.
    /// </summary>
    public partial class MainWindow
    {
        private bool _isFullscreen;
        private bool _fullscreenFromPage;
        private WindowState _stateBeforeFullscreen;
        private Rect _boundsBeforeFullscreen;
        private ResizeMode _resizeModeBeforeFullscreen;
        private DispatcherTimer? _toastTimer;

        private void AttachFullscreenEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.ContainsFullScreenElementChanged += (s, e) =>
            {
                if (tab == ActiveTab)
                {
                    SetFullscreen(core.ContainsFullScreenElement, fromPage: true);
                }
            };
        }

        private void ToggleFullscreen() => SetFullscreen(!_isFullscreen, fromPage: false);

        private void MenuFullscreen_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            ToggleFullscreen();
        }

        private void SetFullscreen(bool enable, bool fromPage)
        {
            if (enable == _isFullscreen) return;
            // Endet das Video-Vollbild, während F11 aktiv war, bleibt der Browser im Vollbild
            if (!enable && fromPage && !_fullscreenFromPage) return;

            _isFullscreen = enable;
            var chrome = WindowChrome.GetWindowChrome(this);

            if (enable)
            {
                _fullscreenFromPage = fromPage;
                _stateBeforeFullscreen = WindowState;
                _boundsBeforeFullscreen = WindowState == WindowState.Normal
                    ? new Rect(Left, Top, Width, Height)
                    : RestoreBounds;
                _resizeModeBeforeFullscreen = ResizeMode;

                rowTabStrip.Height = new GridLength(0);
                rowToolbar.Height = new GridLength(0);
                borderBookmarksBar.Visibility = Visibility.Collapsed;
                borderSidebar.Visibility = Visibility.Collapsed;
                borderVerticalTabs.Visibility = Visibility.Collapsed;
                CloseFindBar();
                ApplySplitLayout();
                if (chrome != null) chrome.CaptionHeight = 0;

                ResizeMode = ResizeMode.NoResize;
                WindowState = WindowState.Normal;
                var monitor = GetMonitorBounds();
                Left = monitor.Left;
                Top = monitor.Top;
                Width = monitor.Width;
                Height = monitor.Height;
                Topmost = true;
                Topmost = false;

                UpdateMaximizedMargin();
                ApplyWindowEffects();
                ShowToast(Tr.Get(fromPage ? "Fullscreen_HintPage" : "Fullscreen_Hint"));
            }
            else
            {
                _fullscreenFromPage = false;
                rowTabStrip.Height = new GridLength(TitleBarHeight);
                rowToolbar.Height = new GridLength(46);
                borderBookmarksBar.Visibility = _isBookmarksBarVisible ? Visibility.Visible : Visibility.Collapsed;
                borderSidebar.Visibility = AppSettingsService.Instance.Settings.IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
                if (chrome != null) chrome.CaptionHeight = TitleBarHeight;
                ApplyTabLayout();
                ApplySplitLayout();

                ResizeMode = _resizeModeBeforeFullscreen;
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
                WindowState = _stateBeforeFullscreen;
                UpdateMaximizedMargin();
                ApplyWindowEffects();

                // Verlässt der Nutzer F11 während ein Video im Vollbild läuft, das Video ebenfalls beenden
                _ = ActiveTab?.WebView?.CoreWebView2?.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
                HideToast();
            }
        }

        /// <summary>Bildschirm, auf dem das Fenster gerade liegt – in WPF-Einheiten (berücksichtigt die Skalierung).</summary>
        private Rect GetMonitorBounds()
        {
            var handle = new WindowInteropHelper(this).Handle;
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(MonitorFromWindow(handle, MonitorDefaultToNearest), ref info))
            {
                return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
            }

            var deviceRect = new Rect(info.rcMonitor.Left, info.rcMonitor.Top,
                info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top);
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return deviceRect;

            var toDip = source.CompositionTarget.TransformFromDevice;
            return new Rect(toDip.Transform(deviceRect.TopLeft), toDip.Transform(deviceRect.BottomRight));
        }

        #region Hinweis-Toast

        /// <summary>Kurzer Hinweis oben in der Mitte des Fensters, verschwindet nach einigen Sekunden.</summary>
        private void ShowToast(string message, double seconds = 3.5)
        {
            txtToast.Text = message;
            popupToast.HorizontalOffset = 0;
            popupToast.IsOpen = true;

            _toastTimer ??= new DispatcherTimer();
            _toastTimer.Stop();
            _toastTimer.Interval = TimeSpan.FromSeconds(seconds);
            _toastTimer.Tick -= ToastTimer_Tick;
            _toastTimer.Tick += ToastTimer_Tick;
            _toastTimer.Start();
        }

        private void ToastTimer_Tick(object? sender, EventArgs e) => HideToast();

        private void HideToast()
        {
            _toastTimer?.Stop();
            popupToast.IsOpen = false;
        }

        #endregion

        #region Win32

        private const uint MonitorDefaultToNearest = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int cbSize;
            public NativeRect rcMonitor;
            public NativeRect rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo info);

        #endregion
    }
}
