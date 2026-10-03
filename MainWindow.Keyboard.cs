using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>Tastenkürzel (wie in Chrome/Edge).</summary>
    public partial class MainWindow
    {
        #region Keyboard Shortcuts

        /// <summary>
        /// Alle Tastenkürzel des Fensters. Läuft in der Preview-Phase, damit Kürzel auch greifen,
        /// wenn ein Steuerelement (z.B. die Adressleiste) die Taste sonst selbst verarbeiten würde.
        /// WebView2 leitet Tasten aus der Webseite ebenfalls hierher weiter; was hier behandelt wird,
        /// führt WebView2 nicht zusätzlich selbst aus.
        /// Die Hauptkürzel stehen im <see cref="CommandCatalog"/> (gemeinsam mit der Befehlspalette),
        /// hier folgen nur zusätzliche Varianten wie F5 oder Strg+Tab.
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Bei gedrückter Alt-Taste meldet WPF Key.System, die eigentliche Taste steht in SystemKey
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            ModifierKeys modifiers = Keyboard.Modifiers;
            const ModifierKeys Ctrl = ModifierKeys.Control;
            const ModifierKeys CtrlShift = ModifierKeys.Control | ModifierKeys.Shift;
            const ModifierKeys Alt = ModifierKeys.Alt;
            const ModifierKeys None = ModifierKeys.None;

            var command = CommandCatalog.All.FirstOrDefault(c => c.Matches(key, modifiers));
            Action? action = command != null ? () => ExecuteCommand(command.Id) : (key, modifiers) switch
            {
                // Tabs
                (Key.F4, Ctrl) => () => ExecuteCommand("closeTab"),
                (Key.Tab, Ctrl) or (Key.PageDown, Ctrl) => () => SelectAdjacentTab(+1),
                (Key.Tab, CtrlShift) or (Key.PageUp, Ctrl) => () => SelectAdjacentTab(-1),
                (>= Key.D1 and <= Key.D9, Ctrl) => () => SelectTabByNumber(key - Key.D1 + 1),
                (>= Key.NumPad1 and <= Key.NumPad9, Ctrl) => () => SelectTabByNumber(key - Key.NumPad1 + 1),

                // Navigation & Adressleiste
                (Key.D, Alt) or (Key.F6, None) => FocusAddressBar,
                (Key.F5, None) => () => ExecuteCommand("reload"),
                (Key.F5, Ctrl) or (Key.R, CtrlShift) => () => ActiveTab?.WebView?.CoreWebView2?.CallDevToolsProtocolMethodAsync("Page.reload", "{\"ignoreCache\":true}"),
                // Esc in Adressleiste und Befehlspalette gehört diesen selbst – dort nicht abfangen
                (Key.Escape, None) when ActiveTab?.IsLoading == true && !txtUrl.IsKeyboardFocusWithin && !IsFindBarOpen && !popupPalette.IsOpen
                    => () => ActiveTab?.WebView?.Stop(),

                // Seite
                (Key.F3, None) or (Key.G, Ctrl) => () => FindNextOrOpen(forward: true),
                (Key.F3, ModifierKeys.Shift) or (Key.G, CtrlShift) => () => FindNextOrOpen(forward: false),
                (Key.Add, Ctrl) or (Key.OemPlus, CtrlShift) => () => ExecuteCommand("zoomIn"),
                (Key.Subtract, Ctrl) => () => ExecuteCommand("zoomOut"),
                (Key.NumPad0, Ctrl) => () => ExecuteCommand("zoomReset"),
                (Key.I, CtrlShift) => () => ExecuteCommand("devTools"),
                _ => null
            };

            if (action == null) return;

            // Ein Kürzel aus der offenen Palette heraus schließt sie (außer Strg+K selbst, das schaltet um)
            if (popupPalette.IsOpen && command?.Id != "commandPalette")
            {
                CloseCommandPalette(focusPage: false);
            }
            action();
            e.Handled = true;
        }

        private void FocusAddressBar() => FocusWpfInput(txtUrl);

        /// <summary>
        /// Tastaturfokus in ein WPF-Eingabefeld holen – auch wenn er gerade in der Webseite liegt.
        /// Kommt das Tastenkürzel aus der WebView, behält diese sonst den Win32-Fokus und das Feld bekommt
        /// keine Eingaben. Deshalb erst nach Abschluss des Tastenereignisses und mit SetFocus aufs Fenster.
        /// </summary>
        private void FocusWpfInput(System.Windows.Controls.TextBox input)
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                SetFocus(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                input.Focus();
                Keyboard.Focus(input);
                input.SelectAll();
            });
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

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

        private void PrintActivePage()
        {
            popupMenu.IsOpen = false;
            ActiveTab?.WebView?.CoreWebView2?.ShowPrintUI(Microsoft.Web.WebView2.Core.CoreWebView2PrintDialogKind.Browser);
        }

        private async void SaveActivePage()
        {
            popupMenu.IsOpen = false;
            var core = ActiveTab?.WebView?.CoreWebView2;
            if (core == null || InternalPages.IsInternalUrl(ActiveTab!.Url)) return;
            try
            {
                await core.ShowSaveAsUIAsync();
            }
            catch (Exception ex)
            {
                Log.Warn("Seite speichern fehlgeschlagen", ex);
            }
        }

        /// <summary>Strg+U: Quelltext der Seite in einem neuen Tab neben dem aktuellen.</summary>
        private void ViewPageSource()
        {
            if (ActiveTab == null) return;
            string url = ActiveTab.Url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
            AddNewTabAt(Tabs.IndexOf(ActiveTab) + 1, "view-source:" + url);
        }

        private void MenuFind_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            OpenFindBar();
        }

        private void MenuPrint_Click(object sender, RoutedEventArgs e) => PrintActivePage();

        private void MenuSavePage_Click(object sender, RoutedEventArgs e) => SaveActivePage();

        #endregion
    }
}
