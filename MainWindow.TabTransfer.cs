using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>
    /// Tabs zwischen Fenstern verschieben, ohne die Seite neu zu laden: Die WebView2 (ein HwndHost) wird aus dem
    /// <c>WebViewContainer</c> des einen Fensters genommen und in den des anderen gesetzt – das WPF-Steuerelement
    /// hängt seinen Controller dabei selbst um (Reparenting). Renderer, Verlauf, Formulareingaben, Scroll-Position,
    /// Ton, DevTools und laufende Downloads bleiben erhalten. Nur die Handler, die an das Fenster gebunden sind
    /// (<see cref="BrowserTab.AddHostBinding"/>), werden gelöst und im neuen Fenster neu angemeldet.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Tab, den dieses (neue) Fenster beim Laden übernimmt – statt Startseite oder Sitzung.</summary>
        private BrowserTab? _tabToAdopt;

        /// <summary>Wohin ein herausgezogener Tab sein neues Fenster bekommt (Bildschirm-Pixel, Griffpunkt in DIP).</summary>
        private (Point CursorPx, Vector GrabOffsetDip)? _initialPlacement;

        /// <summary>Gleiches Profil: Tabs können nur zwischen Fenstern desselben Modus wechseln (normal / Inkognito).</summary>
        private bool CanExchangeTabsWith(MainWindow other) =>
            other._isIncognito == _isIncognito && other._incognitoWithExtensions == _incognitoWithExtensions;

        /// <summary>Kann dieses Fenster gerade Tabs aufnehmen (geladen, nicht im Schließen, kein Vollbild)?</summary>
        private bool CanAcceptTabs => IsLoaded && IsVisible && !_isClosingForGood && !_isFullscreen && WindowState != WindowState.Minimized;

        /// <summary>
        /// Tab aus diesem Fenster lösen, ohne ihn zu schließen: geteilte Ansicht, Vollbild, Suchleiste und offene
        /// Berechtigungsfragen aufräumen, die fensterabhängigen Handler abmelden und die WebView aus dem Container nehmen.
        /// War es der letzte Tab, schließt sich das Fenster danach.
        /// </summary>
        private void ReleaseTabForTransfer(BrowserTab tab)
        {
            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            bool wasActive = ActiveTab == tab;
            if (wasActive)
            {
                // Seiten-Vollbild gehört zu diesem Fenster – im neuen Fenster nicht im Vollbild weiterlaufen
                if (_isFullscreen && _fullscreenFromPage)
                {
                    if (tab.WebView?.CoreWebView2 is { ContainsFullScreenElement: true } core)
                    {
                        _ = core.ExecuteScriptAsync("document.fullscreenElement && document.exitFullscreen()");
                    }
                    SetFullscreen(false, fromPage: true);
                }
                CloseFindBar();
            }

            OnTabClosingForSplit(tab);
            DiscardPermissionRequests(tab);

            tab.ReleaseHostBindings();
            tab.Host = null;
            if (tab.WebView != null)
            {
                tab.WebView.Visibility = Visibility.Collapsed;
                WebViewContainer.Children.Remove(tab.WebView);
            }

            // Gruppen gehören zu diesem Fenster; angeheftet bleibt der Tab (wie in Chrome)
            tab.Group = null;
            tab.IsSplitVisible = false;
            tab.IsActive = false;
            Tabs.Remove(tab);

            if (Tabs.Count == 0)
            {
                _activeTab = null;
                // Verzögert: Der Aufruf kommt mitten aus einem Maus-Ereignis dieses Fensters
                Dispatcher.BeginInvoke(Close);
            }
            else if (wasActive)
            {
                SelectTab(Tabs[Math.Clamp(index, 0, Tabs.Count - 1)]);
            }
        }

        /// <summary>
        /// Gelösten Tab in dieses Fenster aufnehmen: WebView in den Container, Handler neu anmelden, an
        /// <paramref name="index"/> einsortieren (angeheftete bleiben vorne) und – falls gewünscht – aktivieren.
        /// </summary>
        private void AdoptTab(BrowserTab tab, int index, bool activate = true)
        {
            tab.Host = this;
            tab.CertificateErrors = CertificateErrorTracker.ForProfile(_isIncognito);
            BindTabToHost(tab);

            if (tab.WebView is { } webView)
            {
                webView.Visibility = Visibility.Collapsed;
                Grid.SetColumnSpan(webView, 3);
                Grid.SetColumn(webView, 0);
                WebViewContainer.Children.Add(webView);

                // Ist die WebView noch in der Initialisierung, meldet AttachWebViewEvents die Handler hier an
                if (tab.IsWebViewSetUp)
                {
                    BindWebViewToHost(tab, webView);
                    if (!_isIncognito || _incognitoWithExtensions) ShowPinnedExtensionsOnce(Task.CompletedTask);
                }
            }

            int pinned = Tabs.Count(t => t.IsPinned);
            Tabs.Insert(TabDragMath.ClampToPinnedRange(index, pinned, Tabs.Count, tab.IsPinned), tab);
            NormalizeTabs();

            if (activate || ActiveTab == null)
            {
                SelectTab(tab);
            }
            UpdateNavigationControls();
        }

        /// <summary>Tab in ein anderes, bereits offenes Fenster verschieben (Andocken an dessen Tab-Leiste).</summary>
        private bool MoveTabToWindow(BrowserTab tab, MainWindow target, int index)
        {
            if (target == this || !Tabs.Contains(tab) || !CanExchangeTabsWith(target) || !target.CanAcceptTabs) return false;

            ReleaseTabForTransfer(tab);
            target.AdoptTab(tab, index);
            target.FinishTabDrag(tab); // Gruppe nach den neuen Nachbarn (wie beim Verschieben innerhalb der Leiste)
            target.BringToFront();
            return true;
        }

        /// <summary>
        /// Tab in einem neuen Fenster an der Stelle <paramref name="cursorPx"/> weiterführen – ohne Neuladen.
        /// Der letzte Tab eines Fensters nimmt einfach das Fenster mit (es wandert an die neue Stelle),
        /// statt ein neues zu öffnen und das alte zu schließen.
        /// </summary>
        /// <param name="grabOffsetDip">Abstand vom Griffpunkt zur linken oberen Fensterecke (geräteunabhängige Pixel).</param>
        private void DetachTabToNewWindow(BrowserTab tab, Point cursorPx, Vector grabOffsetDip)
        {
            if (!Tabs.Contains(tab)) return;

            if (Tabs.Count == 1)
            {
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                PlaceWindowAt(cursorPx, grabOffsetDip);
                BringToFront();
                return;
            }

            // Größe wie das bisherige Fenster (bei maximiertem Fenster die normale Größe)
            var size = WindowState == WindowState.Normal ? new Size(ActualWidth, ActualHeight) : RestoreBounds.Size;
            if (size.IsEmpty || size.Width <= 0) size = new Size(Width, Height);

            ReleaseTabForTransfer(tab);

            var window = new MainWindow(_isIncognito, _incognitoWithExtensions)
            {
                _tabToAdopt = tab,
                _initialPlacement = (cursorPx, grabOffsetDip),
                WindowStartupLocation = WindowStartupLocation.Manual,
                Width = Math.Max(size.Width, MinWidth),
                Height = Math.Max(size.Height, MinHeight)
            };
            window.SourceInitialized += (s, e) => window.ApplyInitialPlacement();
            window.Show();
            window.Activate();
        }

        /// <summary>Für ein neues Fenster mit übernommenem Tab: Inkognito-Modus samt Erweiterungs-Variante übernehmen.</summary>
        private MainWindow(bool isIncognito, bool incognitoWithExtensions) : this(isIncognito)
        {
            _incognitoWithExtensions = isIncognito && incognitoWithExtensions;
        }

        /// <summary>Beim Laden: übernommenen Tab einsetzen. Gibt false zurück, wenn es keinen gibt.</summary>
        private bool TryAdoptInitialTab()
        {
            var tab = _tabToAdopt;
            if (tab == null) return false;

            _tabToAdopt = null;
            AdoptTab(tab, 0);
            return true;
        }

        /// <summary>Ein nie übernommener Tab (Fenster konnte nicht starten) darf nicht als unsichtbare WebView weiterleben.</summary>
        private void DisposeUnadoptedTab()
        {
            var tab = _tabToAdopt;
            _tabToAdopt = null;
            tab?.Dispose();
        }

        private void ApplyInitialPlacement()
        {
            if (_initialPlacement is { } placement)
            {
                PlaceWindowAt(placement.CursorPx, placement.GrabOffsetDip);
            }
        }

        /// <summary>
        /// Fenster so legen, dass der Griffpunkt unter dem Mauszeiger liegt – in physischen Pixeln des Monitors unter
        /// dem Zeiger. Die Größe bleibt in DIP (WPF rechnet sie bei einem DPI-Wechsel selbst um), deshalb erst
        /// verschieben, dann mit der tatsächlichen Pixelgröße auf den Arbeitsbereich begrenzen.
        /// </summary>
        private void PlaceWindowAt(Point cursorPx, Vector grabOffsetDip)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || double.IsNaN(cursorPx.X)) return;

            var (workArea, scale) = NativeScreen.MonitorAt(cursorPx);
            var grabPx = grabOffsetDip * scale;

            // 1. Grob auf den Zielmonitor (löst dort ggf. die DPI-Anpassung von WPF aus)
            NativeScreen.MoveWindow(hwnd, cursorPx - grabPx);

            // 2. Mit der echten Größe auf dem Zielmonitor begrenzen
            var bounds = NativeScreen.WindowBounds(hwnd);
            var size = bounds.IsEmpty ? new Size(ActualWidth * scale, ActualHeight * scale) : bounds.Size;
            var rect = TabDragMath.PlaceWindow(cursorPx, grabPx, size, workArea, TabDragMath.MinVisibleWindowPart * scale);
            NativeScreen.MoveWindow(hwnd, rect.TopLeft);
        }
    }
}
