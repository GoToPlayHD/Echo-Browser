using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Tabs ziehen wie in Chrome: Klick wählt aus, Ziehen längs der Leiste sortiert um, Ziehen aus der Leiste heraus
    /// zeigt eine Vorschau am Zeiger und öffnet beim Loslassen dort ein neues Fenster; über der Tab-Leiste eines
    /// anderen Fensters (gleicher Modus) wird der Tab dort eingefügt. Esc bricht ab und stellt die Ausgangslage her.
    ///
    /// Statt OLE-Drag&amp;Drop (modal, keine eigene Vorschau, kein Ziel außerhalb von Fenstern) hält das Fenster die
    /// Maus fest (Mouse.Capture) und liest Zeiger, Maustaste und Esc zusätzlich per Timer – so endet der Vorgang
    /// auch dann sauber, wenn die Maus das Fenster verlässt, die Webseite den Tastaturfokus hat oder ein anderes
    /// Programm die Maus übernimmt. Alle Bildschirmrechnungen laufen in physischen Pixeln (siehe TabDragMath).
    /// </summary>
    public partial class MainWindow
    {
        private TabDragSession? _tabDrag;

        private sealed class TabDragSession
        {
            public required BrowserTab Tab { get; init; }
            /// <summary>Zeigerposition beim Drücken (Bildschirm-Pixel).</summary>
            public required Point PressPx { get; init; }
            /// <summary>Griffpunkt im Tab (DIP) – die Vorschau und das neue Fenster greifen den Tab an derselben Stelle.</summary>
            public required Vector GrabInTab { get; init; }
            /// <summary>Griffpunkt relativ zur linken oberen Fensterecke (DIP), für die Lage des neuen Fensters.</summary>
            public required Vector GrabInWindow { get; init; }
            public required int OriginalIndex { get; init; }
            public required TabGroup? OriginalGroup { get; init; }
            public required DispatcherTimer Timer { get; init; }

            public bool IsDragging { get; set; }
            public bool IsEnding { get; set; }
            public TabDragZone Zone { get; set; }
            public MainWindow? DockTarget { get; set; }
            public TabDragGhostWindow? Ghost { get; set; }
            public ImageSource? Thumbnail { get; set; }
        }

        #region Maus-Ereignisse

        private void Tab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not BrowserTab tab) return;

            // Schließen-Kreuz & Co. bekommen ihren Klick
            if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null) return;

            SelectTab(tab);
            if (_tabDrag != null) EndTabDrag(cancel: true);

            Point pressPx = NativeScreen.CursorPosition();
            if (double.IsNaN(pressPx.X)) return;

            Point inTab = e.GetPosition(fe);
            // Im neuen Fenster steht der Tab ganz vorne: Griffpunkt = erster Platz der Leiste + Stelle im Tab
            Point firstSlot = VisibleTabItems.TranslatePoint(new Point(0, 0), this);
            Point inWindow = new(firstSlot.X + inTab.X, firstSlot.Y + inTab.Y);
            var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(15) };
            timer.Tick += (s, args) => OnTabDragTimerTick();

            _tabDrag = new TabDragSession
            {
                Tab = tab,
                PressPx = pressPx,
                GrabInTab = new Vector(inTab.X, inTab.Y),
                GrabInWindow = new Vector(inWindow.X, inWindow.Y),
                OriginalIndex = Tabs.IndexOf(tab),
                OriginalGroup = tab.Group,
                Timer = timer
            };

            // Maus am Fenster festhalten (nicht am Tab-Element: das wird beim Umsortieren u.U. neu erzeugt)
            if (!Mouse.Capture(this, CaptureMode.Element))
            {
                _tabDrag = null;
                return;
            }
            PreviewMouseMove += TabDrag_PreviewMouseMove;
            PreviewMouseLeftButtonUp += TabDrag_PreviewMouseLeftButtonUp;
            LostMouseCapture += TabDrag_LostMouseCapture;
            Deactivated += TabDrag_Deactivated;
            timer.Start();
        }

        private void TabDrag_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_tabDrag == null) return;
            UpdateTabDrag();
            e.Handled = true;
        }

        private void TabDrag_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_tabDrag == null) return;
            bool wasDragging = _tabDrag.IsDragging;
            EndTabDrag(cancel: false);
            // Ein einfacher Klick läuft normal weiter (Tab ist schon beim Drücken ausgewählt)
            if (wasDragging) e.Handled = true;
        }

        /// <summary>Maus von außen weggenommen (Alt+Tab, anderes Programm, Systemdialog): abbrechen.</summary>
        private void TabDrag_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_tabDrag is { IsEnding: false }) EndTabDrag(cancel: true);
        }

        private void TabDrag_Deactivated(object? sender, EventArgs e)
        {
            // Die Vorschau aktiviert sich nie – ein Wechsel des aktiven Fensters kommt von außen
            if (_tabDrag is { IsEnding: false }) EndTabDrag(cancel: true);
        }

        /// <summary>
        /// Rückfallebene für alles, was WPF nicht meldet: Esc bei Fokus in der Webseite, losgelassene Maustaste
        /// ohne MouseUp, und Zeigerbewegungen, während kein Mausereignis kommt.
        /// </summary>
        private void OnTabDragTimerTick()
        {
            var drag = _tabDrag;
            if (drag == null || drag.IsEnding) return;

            if (NativeScreen.IsEscapePressed())
            {
                EndTabDrag(cancel: true);
            }
            else if (!NativeScreen.IsPrimaryButtonPressed())
            {
                EndTabDrag(cancel: false);
            }
            else if (drag.IsDragging)
            {
                UpdateTabDrag();
            }
        }

        /// <summary>Esc, wenn WPF den Tastaturfokus hat (MainWindow.Keyboard.cs).</summary>
        private bool TryCancelTabDragByKey(Key key)
        {
            if (_tabDrag == null || key != Key.Escape) return false;
            EndTabDrag(cancel: true);
            return true;
        }

        #endregion

        #region Ablauf

        private void UpdateTabDrag()
        {
            var drag = _tabDrag;
            if (drag == null || drag.IsEnding) return;

            Point cursor = NativeScreen.CursorPosition();
            if (double.IsNaN(cursor.X)) return;

            if (!drag.IsDragging)
            {
                double scale = DeviceScale;
                if (!TabDragMath.ExceedsThreshold(cursor - drag.PressPx,
                        SystemParameters.MinimumHorizontalDragDistance * scale,
                        SystemParameters.MinimumVerticalDragDistance * scale)) return;

                drag.IsDragging = true;
                _ = CaptureTabThumbnailAsync(drag);
            }

            var (zone, target) = ClassifyDragPosition(cursor, drag.Tab);
            drag.Zone = zone;
            drag.DockTarget = target;

            if (zone == TabDragZone.Reorder)
            {
                ReorderDraggedTab(drag.Tab, cursor);
                HideDragGhost(drag);
                SetDraggedTabOpacity(drag.Tab, 0.6);
            }
            else
            {
                // Ein Tab, der das Fenster verlässt, steht wieder an seinem Platz (bis er wirklich geht)
                RestoreOriginalOrder(drag);
                SetDraggedTabOpacity(drag.Tab, 0.35);
                ShowDragGhost(drag, cursor, docking: zone == TabDragZone.Dock);
            }
        }

        /// <summary>Wohin zeigt der Mauszeiger: eigene Leiste, Leiste eines anderen Fensters oder ins Freie?</summary>
        private (TabDragZone Zone, MainWindow? Target) ClassifyDragPosition(Point cursorPx, BrowserTab tab)
        {
            // Die eigene Leiste mit Toleranz quer dazu – auch wenn ein anderes Fenster knapp darüber liegt
            if (IsOverTabStrip(cursorPx, TabDragMath.DetachMargin)) return (TabDragZone.Reorder, null);

            // Fremdes Fenster: nur das oberste unter dem Zeiger, und nur über seiner Tab-Leiste
            var hwnd = NativeScreen.TopLevelWindowAt(cursorPx);
            var target = Application.Current.Windows.OfType<MainWindow>()
                .FirstOrDefault(w => w != this && new WindowInteropHelper(w).Handle == hwnd);
            if (target != null && CanExchangeTabsWith(target) && target.CanAcceptTabs && target.IsOverTabStrip(cursorPx, margin: 6))
            {
                return (TabDragZone.Dock, target);
            }

            return (TabDragZone.Detach, null);
        }

        /// <summary>Tab-Leiste dieses Fensters (oben bzw. links), unabhängig davon, wie viel davon mit Tabs belegt ist.</summary>
        private FrameworkElement TabStripArea => IsVerticalTabs ? borderVerticalTabs : gridHorizontalTabs;

        private bool IsOverTabStrip(Point cursorPx, double margin)
        {
            var strip = TabStripArea;
            if (!strip.IsVisible || PresentationSource.FromVisual(strip) == null) return false;

            Point local = strip.PointFromScreen(cursorPx);
            return TabDragMath.IsWithinStrip(local, new Size(strip.ActualWidth, strip.ActualHeight), IsVerticalTabs, margin);
        }

        /// <summary>
        /// Position in <see cref="Tabs"/>, an der ein Tab unter dem Zeiger landen würde. Gemessen an den Mitten der
        /// sichtbaren übrigen Tabs (Bildschirm-Pixel) – Tabs eingeklappter Gruppen haben keine Breite und zählen nicht.
        /// </summary>
        private int InsertionIndexAt(Point cursorPx, BrowserTab dragged)
        {
            var others = Tabs.Where(t => t != dragged).ToList();
            var items = VisibleTabItems;
            bool vertical = IsVerticalTabs;

            var visible = new List<(BrowserTab Tab, double Center)>();
            foreach (var tab in others)
            {
                if (items.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container ||
                    !container.IsVisible || container.ActualWidth <= 0 || container.ActualHeight <= 0) continue;

                Point center = container.PointToScreen(new Point(container.ActualWidth / 2, container.ActualHeight / 2));
                visible.Add((tab, vertical ? center.Y : center.X));
            }

            int slot = TabDragMath.InsertionIndex(visible.Select(v => v.Center).ToList(), vertical ? cursorPx.Y : cursorPx.X);
            int index = slot < visible.Count
                ? others.IndexOf(visible[slot].Tab)
                : visible.Count > 0 ? others.IndexOf(visible[^1].Tab) + 1 : others.Count;

            return TabDragMath.ClampToPinnedRange(index, others.Count(t => t.IsPinned), others.Count, dragged.IsPinned);
        }

        private void ReorderDraggedTab(BrowserTab tab, Point cursorPx)
        {
            int current = Tabs.IndexOf(tab);
            if (current < 0) return;

            int target = InsertionIndexAt(cursorPx, tab);
            if (target != current) Tabs.Move(current, target);
        }

        private void RestoreOriginalOrder(TabDragSession drag)
        {
            int current = Tabs.IndexOf(drag.Tab);
            int original = Math.Clamp(drag.OriginalIndex, 0, Tabs.Count - 1);
            if (current >= 0 && current != original) Tabs.Move(current, original);
        }

        /// <summary>
        /// Ziehen beenden. Abbruch (Esc, Maus verloren): Tab an seinen Ausgangsplatz samt Gruppe. Sonst je nach Ziel
        /// umsortieren, in ein anderes Fenster einfügen oder in ein neues Fenster lösen.
        /// </summary>
        private void EndTabDrag(bool cancel)
        {
            var drag = _tabDrag;
            if (drag == null || drag.IsEnding) return;
            drag.IsEnding = true;

            // Erst alles Globale abräumen – egal, was danach passiert
            drag.Timer.Stop();
            PreviewMouseMove -= TabDrag_PreviewMouseMove;
            PreviewMouseLeftButtonUp -= TabDrag_PreviewMouseLeftButtonUp;
            LostMouseCapture -= TabDrag_LostMouseCapture;
            Deactivated -= TabDrag_Deactivated;
            if (Mouse.Captured == this) Mouse.Capture(null);
            drag.Ghost?.Close();
            drag.Ghost = null;
            SetDraggedTabOpacity(drag.Tab, 1.0);
            _tabDrag = null;

            if (!drag.IsDragging || !Tabs.Contains(drag.Tab)) return;

            if (cancel)
            {
                RestoreOriginalOrder(drag);
                drag.Tab.Group = drag.OriginalGroup;
                NormalizeTabs();
                return;
            }

            // Endgültiges Ziel an der Stelle, an der losgelassen wurde
            Point cursor = NativeScreen.CursorPosition();
            var (zone, target) = double.IsNaN(cursor.X) ? (drag.Zone, drag.DockTarget) : ClassifyDragPosition(cursor, drag.Tab);

            try
            {
                switch (zone)
                {
                    case TabDragZone.Dock when target != null:
                        if (!MoveTabToWindow(drag.Tab, target, target.InsertionIndexAt(cursor, drag.Tab)))
                        {
                            RestoreOriginalOrder(drag);
                        }
                        break;

                    case TabDragZone.Detach:
                        RestoreOriginalOrder(drag);
                        DetachTabToNewWindow(drag.Tab, cursor, drag.GrabInWindow);
                        break;

                    default:
                        // Gruppe nach der neuen Position anpassen (MainWindow.TabGroups.cs)
                        FinishTabDrag(drag.Tab);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Tab konnte nicht verschoben werden", ex);
            }
        }

        #endregion

        #region Darstellung

        /// <summary>Skalierung dieses Fensters (DIP -> Pixel).</summary>
        private double DeviceScale =>
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 is double scale and > 0 ? scale : 1.0;

        /// <summary>Gezogenen Tab abblenden. Das Element wird bei jedem Aufruf neu gesucht – beim Umsortieren kann es wechseln.</summary>
        private void SetDraggedTabOpacity(BrowserTab tab, double opacity)
        {
            foreach (var items in new[] { itemsTabs, itemsVerticalTabs })
            {
                if (items.ItemContainerGenerator.ContainerFromItem(tab) is UIElement container)
                {
                    container.Opacity = opacity;
                }
            }
        }

        private void ShowDragGhost(TabDragSession drag, Point cursorPx, bool docking)
        {
            if (drag.Ghost == null)
            {
                drag.Ghost = new TabDragGhostWindow(drag.Tab, drag.GrabInTab);
                drag.Ghost.SetThumbnail(drag.Thumbnail);
                drag.Ghost.Show();
            }
            drag.Ghost.SetDockMode(docking);
            drag.Ghost.MoveTo(cursorPx);
        }

        private static void HideDragGhost(TabDragSession drag)
        {
            drag.Ghost?.Close();
            drag.Ghost = null;
        }

        /// <summary>Miniatur der Seite für die Vorschau – einmal pro Ziehen, asynchron.</summary>
        private static async Task CaptureTabThumbnailAsync(TabDragSession drag)
        {
            var core = drag.Tab.WebView?.CoreWebView2;
            if (core == null || drag.Tab.IsSleeping) return;

            try
            {
                using var stream = new MemoryStream();
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Jpeg, stream);
                if (drag.IsEnding || stream.Length == 0) return;

                stream.Position = 0;
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 520; // 260 DIP auch bei 200 % scharf
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();

                drag.Thumbnail = image;
                drag.Ghost?.SetThumbnail(image);
            }
            catch (Exception ex)
            {
                // Vorschau ist nur Beiwerk (z.B. WebView gerade beschäftigt oder geschlossen)
                Log.Warn("Vorschau des gezogenen Tabs fehlt", ex);
            }
        }

        #endregion
    }
}
