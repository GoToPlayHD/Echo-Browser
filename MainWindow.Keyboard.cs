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
    /// <summary>Tastenkürzel.</summary>
    public partial class MainWindow
    {
        #region Keyboard Shortcuts

        /// <summary>
        /// Alle Tastenkürzel des Fensters. Läuft in der Preview-Phase, damit Kürzel auch greifen,
        /// wenn ein Steuerelement (z.B. die Adressleiste) die Taste sonst selbst verarbeiten würde.
        /// WebView2 leitet Tasten aus der Webseite ebenfalls hierher weiter.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Bei gedrückter Alt-Taste meldet WPF Key.System, die eigentliche Taste steht in SystemKey
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            const ModifierKeys Ctrl = ModifierKeys.Control;
            const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;

            Action? action = (key, Keyboard.Modifiers) switch
            {
                (Key.T, Ctrl) => () => AddNewTab(StartPageService.StartPageUrl),
                (Key.W, Ctrl) => () => { if (ActiveTab != null) CloseTab(ActiveTab); },
                (Key.N, Ctrl) => () => new MainWindow().Show(),
                (Key.N, CtrlShift) => OpenNewIncognitoWindow,
                (Key.L, Ctrl) or (Key.D, ModifierKeys.Alt) => FocusAddressBar,
                (Key.D, Ctrl) => () => BtnBookmark_Click(this, e),
                (Key.B, CtrlShift) => () => MenuToggleBookmarksBar_Click(this, e),
                (Key.J, Ctrl) => () => popupDownloads.IsOpen = !popupDownloads.IsOpen,
                (Key.H, Ctrl) => () => ToggleHistoryPopup(e),
                (Key.OemComma, Ctrl) => OpenSettingsTab,
                (Key.F5, ModifierKeys.None) or (Key.R, Ctrl) => () => BtnReload_Click(this, e),
                (Key.F12, ModifierKeys.None) => () => ActiveTab?.WebView?.CoreWebView2?.OpenDevToolsWindow(),
                // Esc in der Adressleiste setzt deren Text zurück (TxtUrl_KeyDown) – dort nicht abfangen
                (Key.Escape, ModifierKeys.None) when ActiveTab?.IsLoading == true && !txtUrl.IsKeyboardFocusWithin
                    => () => ActiveTab?.WebView?.Stop(),
                _ => null
            };

            if (action != null)
            {
                action();
                e.Handled = true;
            }
        }

        private void FocusAddressBar()
        {
            txtUrl.Focus();
            txtUrl.SelectAll();
        }

        private void ToggleHistoryPopup(RoutedEventArgs e)
        {
            if (popupHistory.IsOpen)
            {
                popupHistory.IsOpen = false;
            }
            else
            {
                MenuHistory_Click(this, e);
            }
        }

        #endregion
    }
}
