using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>
    /// Eigene Fensterleiste wie ein Windows-11-Fenster: Mica in der Tab-Leiste, Snap-Layouts beim Zeigen auf
    /// "Maximieren" und kein abgeschnittener Inhalt im maximierten Zustand.
    /// </summary>
    public partial class MainWindow
    {
        private const int WmNcHitTest = 0x0084;
        private const int WmNcMouseLeave = 0x02A2;
        private const int WmNcLButtonDown = 0x00A1;
        private const int WmNcLButtonUp = 0x00A2;
        private const int HtMaxButton = 9;

        private bool _isMaximizeButtonHot;

        private void InitializeWindowChrome()
        {
            SourceInitialized += (s, e) =>
            {
                ApplyWindowEffects();
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowChromeHook);
            };
        }

        /// <summary>Mica und Fensterrahmen passend zum Theme (wird auch bei Theme-Wechsel aufgerufen).</summary>
        private void ApplyWindowEffects()
        {
            if (!IsInitialized || new WindowInteropHelper(this).Handle == IntPtr.Zero) return;

            bool wantMica = AppSettingsService.Instance.Settings.UseMica && WindowEffects.IsMicaSupported && !_isFullscreen;
            if (WindowChrome.GetWindowChrome(this) is { } chrome)
            {
                // Mica braucht einen in den ganzen Client-Bereich erweiterten Rahmen; die System-Knöpfe zeichnen wir selbst
                chrome.UseAeroCaptionButtons = false;
                chrome.GlassFrameThickness = wantMica ? new Thickness(-1) : new Thickness(0);
            }

            bool micaActive = WindowEffects.Apply(this, isDark: !ThemeManager.Instance.IsLight, useMica: wantMica);

            // Mit in den Client-Bereich erweitertem Rahmen zeichnet Windows sonst seine eigenen
            // Minimieren/Maximieren/Schließen-Knöpfe hinter unsere – ohne Systemmenü-Stil entfallen sie.
            SetSystemMenuStyle(enabled: !micaActive);

            if (micaActive)
            {
                Background = Brushes.Transparent;
                rootGrid.Background = Brushes.Transparent;
                gridTabStrip.Background = Brushes.Transparent;
            }
            else
            {
                SetResourceReference(BackgroundProperty, "WindowBackgroundBrush");
                rootGrid.SetResourceReference(Panel.BackgroundProperty, "WindowBackgroundBrush");
                gridTabStrip.SetResourceReference(Panel.BackgroundProperty, "TabBarBackgroundBrush");
            }
        }

        /// <summary>
        /// Maximiert legt Windows den (bei uns unsichtbaren) Rahmen außerhalb des Bildschirms ab – der Inhalt
        /// würde dort abgeschnitten. Deshalb im maximierten Zustand genau um die Rahmenbreite einrücken.
        /// </summary>
        private void UpdateMaximizedMargin()
        {
            if (WindowState != WindowState.Maximized || _isFullscreen)
            {
                rootGrid.Margin = new Thickness(0);
                return;
            }

            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            uint dpi = hwnd != IntPtr.Zero ? GetDpiForWindow(hwnd) : 96;
            if (dpi == 0) dpi = 96;

            double framePixels = GetSystemMetricsForDpi(SmCxSizeFrame, dpi) + GetSystemMetricsForDpi(SmCxPaddedBorder, dpi);
            double frame = framePixels * 96.0 / dpi;
            rootGrid.Margin = new Thickness(frame);
        }

        private IntPtr WindowChromeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            switch (msg)
            {
                case WmNcHitTest:
                    // Windows 11 zeigt die Snap-Layouts nur, wenn der Maximieren-Knopf als HTMAXBUTTON gemeldet wird
                    if (IsOverMaximizeButton(lParam))
                    {
                        SetMaximizeButtonHot(true);
                        handled = true;
                        return new IntPtr(HtMaxButton);
                    }
                    SetMaximizeButtonHot(false);
                    break;

                case WmNcLButtonDown when wParam.ToInt32() == HtMaxButton:
                    handled = true;
                    break;

                case WmNcLButtonUp when wParam.ToInt32() == HtMaxButton:
                    handled = true;
                    SetMaximizeButtonHot(false);
                    BtnMaximize_Click(this, new RoutedEventArgs());
                    break;

                case WmNcMouseLeave:
                    SetMaximizeButtonHot(false);
                    break;
            }
            return IntPtr.Zero;
        }

        private bool IsOverMaximizeButton(IntPtr lParam)
        {
            if (_isFullscreen || !btnMaximize.IsVisible || btnMaximize.ActualWidth <= 0) return false;

            long value = lParam.ToInt64();
            int x = unchecked((short)(value & 0xFFFF));
            int y = unchecked((short)((value >> 16) & 0xFFFF));

            Point topLeft = btnMaximize.PointToScreen(new Point(0, 0));
            Point bottomRight = btnMaximize.PointToScreen(new Point(btnMaximize.ActualWidth, btnMaximize.ActualHeight));
            return x >= topLeft.X && x < bottomRight.X && y >= topLeft.Y && y < bottomRight.Y;
        }

        /// <summary>Hover-Optik selbst setzen: über HTMAXBUTTON bekommt WPF keine Mausereignisse für den Knopf.</summary>
        private void SetMaximizeButtonHot(bool hot)
        {
            if (hot == _isMaximizeButtonHot) return;
            _isMaximizeButtonHot = hot;

            if (hot)
            {
                btnMaximize.SetResourceReference(BackgroundProperty, "ButtonHoverBackgroundBrush");
                btnMaximize.SetResourceReference(ForegroundProperty, "AccentSilverBrightBrush");
            }
            else
            {
                btnMaximize.ClearValue(BackgroundProperty);
                btnMaximize.ClearValue(ForegroundProperty);
            }
        }

        private void SetSystemMenuStyle(bool enabled)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int style = GetWindowLong(hwnd, GwlStyle);
            int updated = enabled ? style | WsSysMenu : style & ~WsSysMenu;
            if (updated == style) return;

            SetWindowLong(hwnd, GwlStyle, updated);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoZOrder | SwpFrameChanged);
        }

        private const int GwlStyle = -16;
        private const int WsSysMenu = 0x00080000;
        private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpFrameChanged = 0x0020;

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        private const int SmCxSizeFrame = 32;
        private const int SmCxPaddedBorder = 92;

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    }
}
