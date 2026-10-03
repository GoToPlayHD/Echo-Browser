using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>
    /// Geteilte Ansicht (Split View): zwei Tabs nebeneinander mit verschiebbarem Trenner. Adressleiste,
    /// Zurück/Vor, Zoom und Suche gelten für die Seite mit dem Fokus (= <see cref="ActiveTab"/>).
    /// Die Spalten liegen im <c>WebViewContainer</c> – der Trenner ist eine eigene Spalte, kein Airspace-Problem.
    /// </summary>
    public partial class MainWindow
    {
        #region Geteilte Ansicht

        private BrowserTab? _splitLeft;
        private BrowserTab? _splitRight;
        private GridLength _splitLeftWidth = new(1, GridUnitType.Star);
        private GridLength _splitRightWidth = new(1, GridUnitType.Star);

        private const double SplitterWidth = 6;
        private const double PaneFrame = 2;

        private bool IsSplit => _splitLeft != null && _splitRight != null;

        /// <summary>Die geteilte Ansicht ist sichtbar, solange einer ihrer beiden Tabs aktiv ist (wie Edge).</summary>
        private bool IsSplitShown =>
            IsSplit && (ActiveTab == _splitLeft || ActiveTab == _splitRight) && !(_isFullscreen && _fullscreenFromPage);

        private bool IsInSplit(BrowserTab tab) => tab == _splitLeft || tab == _splitRight;

        /// <summary>Zwei Tabs nebeneinander; der rechte bekommt den Fokus.</summary>
        private void OpenSplit(BrowserTab left, BrowserTab right)
        {
            if (left == right || !Tabs.Contains(left) || !Tabs.Contains(right)) return;
            ExitSplit();

            _splitLeft = left;
            _splitRight = right;

            // In der Tab-Leiste nebeneinander (in derselben Gruppe), soweit Anheften das zulässt
            if (left.IsPinned == right.IsPinned)
            {
                right.Group = left.Group;
                int from = Tabs.IndexOf(right);
                int to = Tabs.IndexOf(left) + (from > Tabs.IndexOf(left) ? 1 : 0);
                if (from != to) Tabs.Move(from, to);
                NormalizeTabs();
            }

            SelectTab(right);
            ApplySplitLayout();
        }

        /// <summary>Aktiven Tab mit einem neuen Tab (Startseite) teilen.</summary>
        private void OpenSplitWithNewTab(string? url = null)
        {
            var left = ActiveTab;
            if (left == null) return;

            var right = AddNewTab(url ?? StartPageService.StartPageUrl, activateTab: false);
            if (right != null) OpenSplit(left, right);
        }

        private void ExitSplit()
        {
            if (!IsSplit) return;

            _splitLeft!.IsSplitVisible = false;
            _splitRight!.IsSplitVisible = false;
            _splitLeft = null;
            _splitRight = null;
            ApplySplitLayout();
        }

        /// <summary>Knopf in der Symbolleiste: geteilte Ansicht beenden bzw. mit einem neuen Tab beginnen.</summary>
        private void ToggleSplitView()
        {
            if (IsSplitShown)
            {
                ExitSplit();
            }
            else if (ActiveTab != null && IsSplit && IsInSplit(ActiveTab))
            {
                ApplySplitLayout();
            }
            else
            {
                OpenSplitWithNewTab();
            }
        }

        private void BtnSplitView_Click(object sender, RoutedEventArgs e) => ToggleSplitView();

        /// <summary>Spalten, Rahmen und WebViews nach dem aktuellen Zustand anordnen (auch nach jedem Tabwechsel).</summary>
        private void ApplySplitLayout()
        {
            bool shown = IsSplitShown;

            // Beide Seiten müssen wach sein (Tab-Schlaf, verzögert geladene Tabs)
            if (shown)
            {
                if (_splitLeft != ActiveTab) WakeTab(_splitLeft!);
                if (_splitRight != ActiveTab) WakeTab(_splitRight!);
            }

            if (shown)
            {
                colSplitLeft.Width = _splitLeftWidth;
                colSplitter.Width = new GridLength(SplitterWidth);
                colSplitRight.Width = _splitRightWidth;
            }
            else
            {
                // Vom Nutzer gezogene Breiten für das nächste Mal merken
                if (colSplitter.Width.Value > 0)
                {
                    _splitLeftWidth = colSplitLeft.Width;
                    _splitRightWidth = colSplitRight.Width;
                }
                colSplitLeft.Width = new GridLength(1, GridUnitType.Star);
                colSplitter.Width = new GridLength(0);
                colSplitRight.Width = new GridLength(0);
            }

            splitterPanes.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            paneFrameLeft.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            paneFrameRight.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;

            foreach (var tab in Tabs)
            {
                bool left = shown && tab == _splitLeft;
                bool right = shown && tab == _splitRight;
                tab.IsSplitVisible = left || right;
                if (tab.WebView == null) continue;

                Grid.SetColumn(tab.WebView, right ? 2 : 0);
                Grid.SetColumnSpan(tab.WebView, left || right ? 1 : 3);
                tab.WebView.Margin = left || right ? new Thickness(PaneFrame) : new Thickness(0);
                tab.WebView.Visibility = tab == ActiveTab || left || right ? Visibility.Visible : Visibility.Collapsed;
            }

            UpdatePaneFrames();

            // Wechsel per Tab-Leiste, Palette oder Tastatur: Tastaturfokus wandert mit in die neue Seite
            if (shown)
            {
                var other = ActiveTab == _splitLeft ? _splitRight : _splitLeft;
                if (other?.WebView?.IsKeyboardFocusWithin == true) ActiveTab?.WebView?.Focus();
            }

            if (shown) btnSplitView.SetResourceReference(ForegroundProperty, "ShieldActiveBrush");
            else btnSplitView.ClearValue(ForegroundProperty);
            SetToolTipAndName(btnSplitView, Tr.Get(shown ? "Split_Exit" : "Split_Open"));
        }

        /// <summary>Akzentrahmen um die Seite mit dem Fokus.</summary>
        private void UpdatePaneFrames()
        {
            SetPaneFrame(paneFrameLeft, ActiveTab == _splitLeft);
            SetPaneFrame(paneFrameRight, ActiveTab == _splitRight);
        }

        private static void SetPaneFrame(Border frame, bool focused)
        {
            // Als Ressourcenverweis, damit der Rahmen einem Theme-Wechsel folgt
            if (focused) frame.SetResourceReference(Border.BorderBrushProperty, "TabActiveIndicatorBrush");
            else frame.BorderBrush = Brushes.Transparent;
        }

        /// <summary>
        /// Klick in eine der beiden Seiten: sie wird zum aktiven Tab (Adressleiste folgt). Chromium meldet den
        /// Fokus verzögert – nur werten, wenn die WebView den Tastaturfokus jetzt noch hat, sonst holt ein
        /// veraltetes Ereignis die vorige Seite zurück.
        /// </summary>
        private void OnWebViewFocused(BrowserTab tab, WebView2 webView)
        {
            if (IsSplitShown && IsInSplit(tab) && tab != ActiveTab && webView.IsKeyboardFocusWithin) SelectTab(tab);
        }

        private void AttachSplitFocusEvents(BrowserTab tab, WebView2 webView)
        {
            webView.GotFocus += (s, e) => OnWebViewFocused(tab, webView);
            webView.IsKeyboardFocusWithinChanged += (s, e) =>
            {
                if (webView.IsKeyboardFocusWithin) OnWebViewFocused(tab, webView);
            };
        }

        /// <summary>Schließt ein Tab der geteilten Ansicht, zeigt der andere wieder die ganze Breite.</summary>
        private void OnTabClosingForSplit(BrowserTab tab)
        {
            if (IsInSplit(tab)) ExitSplit();
        }

        /// <summary>Kontextmenü eines Links: "Link in geteilter Ansicht öffnen".</summary>
        private void AttachSplitLinkMenu(BrowserTab tab, CoreWebView2 core)
        {
            core.ContextMenuRequested += (s, args) =>
            {
                var target = args.ContextMenuTarget;
                if (!target.HasLinkUri || _webViewEnvironment == null) return;
                if (!Uri.TryCreate(target.LinkUri, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return;

                var item = _webViewEnvironment.CreateContextMenuItem(Tr.Get("Split_OpenLink"), null, CoreWebView2ContextMenuItemKind.Command);
                string link = target.LinkUri;
                item.CustomItemSelected += (sender, e) => Dispatcher.BeginInvoke(() =>
                {
                    if (!Tabs.Contains(tab)) return;
                    if (IsSplitShown && IsInSplit(tab))
                    {
                        // In der jeweils anderen Seite öffnen
                        var other = tab == _splitLeft ? _splitRight! : _splitLeft!;
                        other.WebView?.CoreWebView2?.Navigate(link);
                    }
                    else
                    {
                        SelectTab(tab);
                        OpenSplitWithNewTab(link);
                    }
                });

                // Direkt nach "Link in neuem Fenster öffnen" einsortieren
                var items = args.MenuItems;
                int index = items.ToList().FindIndex(i => i.Name == "openLinkInNewWindow");
                items.Insert(index >= 0 ? index + 1 : Math.Min(2, items.Count), item);
            };
        }

        /// <summary>Einträge des Tab-Kontextmenüs.</summary>
        private void AddSplitMenuItems(ContextMenu menu, BrowserTab tab)
        {
            if (IsSplit && IsInSplit(tab))
            {
                menu.Items.Add(MenuEntry(Tr.Get("Split_Exit"), ExitSplit));
            }
            else if (tab != ActiveTab && ActiveTab != null)
            {
                var active = ActiveTab;
                menu.Items.Add(MenuEntry(Tr.Get("Split_WithActive"), () => OpenSplit(active, tab)));
            }
            else
            {
                menu.Items.Add(MenuEntry(Tr.Get("Split_Open"), () => { SelectTab(tab); OpenSplitWithNewTab(); }));
            }
        }

        #endregion
    }
}
