using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Tab-Aktionen wie in Chrome/Edge: Kontextmenü, Mittelklick, Stummschalten, Tabs wechseln,
    /// geschlossene Tabs wiederherstellen sowie Website-Symbole (Favicons) und Ton-Anzeige.
    /// </summary>
    public partial class MainWindow
    {
        private readonly ClosedTabStack _closedTabs = new();

        private static ImageSource? _appIcon;

        /// <summary>Symbol für interne Seiten (Startseite, Einstellungen).</summary>
        private static ImageSource AppIcon
        {
            get
            {
                if (_appIcon == null)
                {
                    var bitmap = new BitmapImage(new Uri("pack://application:,,,/app.png"));
                    bitmap.Freeze();
                    _appIcon = bitmap;
                }
                return _appIcon;
            }
        }

        #region Favicon & Ton

        private void AttachTabIndicatorEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.FaviconChanged += async (s, e) => await UpdateFaviconAsync(tab, core);
            core.IsDocumentPlayingAudioChanged += (s, e) => tab.IsAudible = core.IsDocumentPlayingAudio;
            core.IsMutedChanged += (s, e) => tab.IsMuted = core.IsMuted;
        }

        /// <summary>
        /// Beim Seitenwechsel: interne Seiten bekommen das Echo-Symbol, eine neue Website sofort ihr
        /// zwischengespeichertes Symbol (noch bevor die Seite geladen ist).
        /// </summary>
        private void PrepareFaviconForNavigation(BrowserTab tab, string newUrl)
        {
            if (InternalPages.Is(newUrl, InternalPages.Crashed)) return;

            if (InternalPages.IsInternalUrl(newUrl))
            {
                tab.Favicon = AppIcon;
            }
            else if (FaviconCache.HostKey(newUrl) != FaviconCache.HostKey(tab.Url))
            {
                tab.Favicon = _isIncognito ? null : FaviconCache.Instance.Get(newUrl);
            }
        }

        private async Task UpdateFaviconAsync(BrowserTab tab, CoreWebView2 core)
        {
            if (InternalPages.IsInternalUrl(tab.Url)) return;

            if (string.IsNullOrEmpty(core.FaviconUri))
            {
                tab.Favicon = null;
                return;
            }

            try
            {
                using var stream = await core.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png);
                if (stream == null) return;

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                byte[] png = buffer.ToArray();

                // Inkognito: nur im Tab anzeigen, nichts auf der Festplatte ablegen
                tab.Favicon = _isIncognito ? FaviconCache.Decode(png) : FaviconCache.Instance.Store(tab.Url, png);
            }
            catch (Exception ex)
            {
                Log.Warn($"Favicon für {tab.Url} konnte nicht geladen werden", ex);
            }
        }

        private void BtnTabAudio_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement { Tag: BrowserTab tab })
            {
                ToggleMute(tab);
            }
            e.Handled = true;
        }

        private static void ToggleMute(BrowserTab tab)
        {
            var core = tab.WebView?.CoreWebView2;
            if (core != null)
            {
                core.IsMuted = !core.IsMuted;
            }
        }

        #endregion

        #region Maus & Kontextmenü

        /// <summary>Mittelklick schließt den Tab, Rechtsklick öffnet das Tab-Menü.</summary>
        private void Tab_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement { DataContext: BrowserTab tab } element) return;

            if (e.ChangedButton == MouseButton.Middle)
            {
                CloseTab(tab);
                e.Handled = true;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                ShowTabContextMenu(tab, element);
                e.Handled = true;
            }
        }

        private void ShowTabContextMenu(BrowserTab tab, FrameworkElement placementTarget)
        {
            int index = Tabs.IndexOf(tab);
            var menu = new ContextMenu { PlacementTarget = placementTarget, Placement = PlacementMode.MousePoint };

            menu.Items.Add(MenuEntry(Tr.Get("Tab_NewTabRight"), () => AddNewTabAt(index + 1, StartPageService.StartPageUrl)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(Tr.Get("Tab_Reload"), () => tab.WebView?.CoreWebView2?.Reload(), "Ctrl+R"));
            menu.Items.Add(MenuEntry(Tr.Get("Tab_Duplicate"), () => DuplicateTab(tab)));
            menu.Items.Add(MenuEntry(Tr.Get(tab.IsMuted ? "Tab_Unmute" : "Tab_Mute"), () => ToggleMute(tab)));
            menu.Items.Add(new Separator());
            AddPinAndGroupMenuItems(menu, tab);
            menu.Items.Add(new Separator());
            AddSplitMenuItems(menu, tab);
            menu.Items.Add(MenuEntry(Tr.Get("Tab_MoveToNewWindow"), () => MoveTabToNewWindow(tab)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(Tr.Get("Tab_Close"), () => CloseTab(tab), "Ctrl+W"));
            menu.Items.Add(MenuEntry(Tr.Get("Tab_CloseOthers"), () => CloseOtherTabs(tab), enabled: Tabs.Count > 1));
            menu.Items.Add(MenuEntry(Tr.Get("Tab_CloseRight"), () => CloseTabsToRight(tab), enabled: index < Tabs.Count - 1));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry(Tr.Get("Tab_ReopenClosed"), ReopenClosedTab, "Ctrl+Shift+T", enabled: _closedTabs.Count > 0));

            menu.IsOpen = true;
        }

        private static MenuItem MenuEntry(string header, Action action, string? gesture = null, bool enabled = true)
        {
            // TextBlock statt String, damit Unterstriche nicht als Zugriffstasten verschluckt werden
            var item = new MenuItem
            {
                Header = new TextBlock { Text = header },
                InputGestureText = gesture ?? "",
                IsEnabled = enabled
            };
            item.Click += (s, e) => action();
            return item;
        }

        #endregion

        #region Tab-Aktionen

        /// <summary>Neuer Tab an einer bestimmten Position (z.B. "Neuer Tab rechts").</summary>
        /// <summary>Neuer Tab an einer Stelle; direkt hinter einem Tab einer Gruppe gehört er zu dieser Gruppe (wie Chrome).</summary>
        private void AddNewTabAt(int index, string url, string? title = null)
        {
            var opener = index > 0 && index <= Tabs.Count ? Tabs[index - 1] : null;
            var tab = AddNewTab(url, title: title);
            if (tab == null) return;

            int last = Tabs.Count - 1;
            int target = Math.Clamp(index, 0, last);
            if (target != last)
            {
                Tabs.Move(last, target);
            }

            if (opener?.Group != null) tab.Group = opener.Group;
            NormalizeTabs();
        }

        private void DuplicateTab(BrowserTab tab)
        {
            AddNewTabAt(Tabs.IndexOf(tab) + 1, tab.Url, tab.Title);
        }

        private void CloseOtherTabs(BrowserTab keep)
        {
            foreach (var tab in Tabs.Where(t => t != keep).ToList())
            {
                CloseTab(tab);
            }
            SelectTab(keep);
        }

        private void CloseTabsToRight(BrowserTab tab)
        {
            int index = Tabs.IndexOf(tab);
            foreach (var right in Tabs.Skip(index + 1).ToList())
            {
                CloseTab(right);
            }
        }

        /// <summary>Tab in einem neuen Fenster (gleicher Modus: normal bzw. Inkognito) weiterführen.</summary>
        private void MoveTabToNewWindow(BrowserTab tab)
        {
            var window = new MainWindow(_isIncognito) { StartupUrls = new[] { tab.Url } };
            window.Show();
            CloseTab(tab, rememberForReopen: false);
        }

        private void ReopenClosedTab()
        {
            if (_closedTabs.TryPop(out var closed) && closed != null)
            {
                AddNewTabAt(closed.Index, closed.Url, closed.Title);
            }
        }

        /// <summary>Strg+Tab / Strg+Umschalt+Tab: zum nächsten bzw. vorherigen Tab (am Ende wieder von vorn).</summary>
        private void SelectAdjacentTab(int direction)
        {
            if (ActiveTab == null || Tabs.Count < 2) return;
            int index = (Tabs.IndexOf(ActiveTab) + direction + Tabs.Count) % Tabs.Count;
            SelectTab(Tabs[index]);
        }

        /// <summary>Strg+1 … Strg+8: Tab an dieser Stelle, Strg+9: letzter Tab.</summary>
        private void SelectTabByNumber(int number)
        {
            if (Tabs.Count == 0) return;
            int index = number == 9 ? Tabs.Count - 1 : number - 1;
            if (index < Tabs.Count)
            {
                SelectTab(Tabs[index]);
            }
        }

        #endregion
    }
}
