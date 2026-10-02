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
    }
}
