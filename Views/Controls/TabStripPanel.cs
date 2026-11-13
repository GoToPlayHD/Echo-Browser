using System;
using System.Windows;
using System.Windows.Controls;
using EchoBrowser.Models;

namespace EchoBrowser.Views.Controls
{
    /// <summary>
    /// Panel der Tab-Leiste wie in Chrome: alle Tabs teilen sich die Breite gleichmäßig (zwischen
    /// <see cref="MinTabWidth"/> und <see cref="MaxTabWidth"/>). Schmale Tabs werden als "kompakt" markiert –
    /// das Tab-Template zeigt dann nur das Symbol (siehe <see cref="IsCompactProperty"/>).
    /// Angeheftete Tabs haben eine feste Breite, Gruppenköpfe ihre natürliche Breite, Tabs eingeklappter
    /// Gruppen keine.
    /// </summary>
    public sealed class TabStripPanel : Panel
    {
        public static readonly DependencyProperty MinTabWidthProperty = DependencyProperty.Register(
            nameof(MinTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(40.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MaxTabWidthProperty = DependencyProperty.Register(
            nameof(MaxTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Ab dieser Breite und darunter gilt ein Tab als kompakt (nur Symbol).</summary>
        public static readonly DependencyProperty CompactWidthProperty = DependencyProperty.Register(
            nameof(CompactWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(92.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty PinnedTabWidthProperty = DependencyProperty.Register(
            nameof(PinnedTabWidth), typeof(double), typeof(TabStripPanel),
            new FrameworkPropertyMetadata(44.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Wird auf jedem Tab-Container gesetzt; das Template blendet damit Titel und Schließen-Knopf aus.</summary>
        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
            "IsCompact", typeof(bool), typeof(TabStripPanel), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);
        public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);

        public double MinTabWidth { get => (double)GetValue(MinTabWidthProperty); set => SetValue(MinTabWidthProperty, value); }
        public double MaxTabWidth { get => (double)GetValue(MaxTabWidthProperty); set => SetValue(MaxTabWidthProperty, value); }
        public double CompactWidth { get => (double)GetValue(CompactWidthProperty); set => SetValue(CompactWidthProperty, value); }
        public double PinnedTabWidth { get => (double)GetValue(PinnedTabWidthProperty); set => SetValue(PinnedTabWidthProperty, value); }

        private enum SlotKind { Tab, Pinned, Header, Hidden }

        private double _tabWidth;
        private double[] _widths = Array.Empty<double>();
        private double _scrollOffset;
        private object? _lastActiveItem;

        /// <summary>Breite pro Tab bei gegebenem Platz – als reine Funktion testbar.</summary>
        public static double TabWidthFor(double availableWidth, int count, double min, double max)
        {
            if (count <= 0) return max;
            if (double.IsInfinity(availableWidth) || double.IsNaN(availableWidth)) return max;
            return Math.Clamp(Math.Floor(availableWidth / count), min, max);
        }

        /// <summary>
        /// Verschiebung, damit der aktive Tab sichtbar ist, wenn nicht alle Tabs hineinpassen.
        /// Bleibt der aktive Tab sichtbar, ändert sich die bisherige Verschiebung nicht (kein Springen).
        /// </summary>
        public static double ScrollOffsetFor(double viewport, double tabWidth, int count, int activeIndex, double currentOffset) =>
            activeIndex < 0
                ? ScrollOffsetFor(viewport, tabWidth * count, null, currentOffset)
                : ScrollOffsetFor(viewport, tabWidth * count, (activeIndex * tabWidth, (activeIndex + 1) * tabWidth), currentOffset);

        /// <summary>Wie oben, für unterschiedlich breite Einträge: <paramref name="active"/> = linker und rechter Rand des aktiven Tabs.</summary>
        public static double ScrollOffsetFor(double viewport, double contentWidth, (double Left, double Right)? active, double currentOffset)
        {
            double maxOffset = Math.Max(0, contentWidth - viewport);
            double offset = Math.Clamp(currentOffset, 0, maxOffset);
            if (active is not var (left, right)) return offset;

            if (left < offset) offset = left;
            else if (right > offset + viewport) offset = right - viewport;
            return Math.Clamp(offset, 0, maxOffset);
        }

        private static SlotKind KindOf(UIElement child) => (child as FrameworkElement)?.DataContext switch
        {
            TabGroup => SlotKind.Header,
            BrowserTab { IsHiddenByGroup: true } => SlotKind.Hidden,
            BrowserTab { IsPinned: true } => SlotKind.Pinned,
            _ => SlotKind.Tab
        };

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = InternalChildren.Count;
            _widths = new double[count];

            // Erst alles mit fester Breite (Köpfe, angeheftete Tabs), der Rest teilt sich den übrigen Platz
            double fixedWidth = 0;
            int tabCount = 0;
            double height = 0;
            for (int i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                switch (KindOf(child))
                {
                    case SlotKind.Header:
                        child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
                        _widths[i] = child.DesiredSize.Width;
                        break;
                    case SlotKind.Pinned:
                        SetIsCompact(child, true);
                        child.Measure(new Size(PinnedTabWidth, availableSize.Height));
                        _widths[i] = PinnedTabWidth;
                        break;
                    case SlotKind.Hidden:
                        child.Measure(new Size(0, availableSize.Height));
                        _widths[i] = 0;
                        continue;
                    default:
                        tabCount++;
                        continue;
                }
                fixedWidth += _widths[i];
                height = Math.Max(height, child.DesiredSize.Height);
            }

            double rest = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - fixedWidth);
            _tabWidth = TabWidthFor(rest, tabCount, MinTabWidth, MaxTabWidth);
            // Nach dem Schließen per Maus: Breite halten, damit das nächste Kreuz unter dem Zeiger liegt
            if (!double.IsNaN(_frozenTabWidth)) _tabWidth = Math.Min(_tabWidth, _frozenTabWidth);
            bool compact = _tabWidth <= CompactWidth;

            for (int i = 0; i < count; i++)
            {
                var child = InternalChildren[i];
                if (KindOf(child) != SlotKind.Tab) continue;

                SetIsCompact(child, compact);
                child.Measure(new Size(_tabWidth, availableSize.Height));
                _widths[i] = _tabWidth;
                height = Math.Max(height, child.DesiredSize.Height);
            }

            double width = fixedWidth + _tabWidth * tabCount;
            return new Size(double.IsInfinity(availableSize.Width) ? width : Math.Min(width, availableSize.Width), height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int count = InternalChildren.Count;
            if (_widths.Length != count) return finalSize;

            // Passen nicht alle Tabs hinein: zum aktiven Tab scrollen (nur wenn er gewechselt hat)
            double total = 0;
            (double, double)? active = null;
            object? activeItem = null;
            for (int i = 0; i < count; i++)
            {
                if (InternalChildren[i] is FrameworkElement { DataContext: BrowserTab { IsActive: true } tab })
                {
                    active = (total, total + _widths[i]);
                    activeItem = tab;
                }
                total += _widths[i];
            }

            bool activeChanged = !ReferenceEquals(activeItem, _lastActiveItem);
            _lastActiveItem = activeItem;
            _scrollOffset = ScrollOffsetFor(finalSize.Width, total, activeChanged ? active : null, _scrollOffset);

            double x = -_scrollOffset;
            for (int i = 0; i < count; i++)
            {
                InternalChildren[i].Arrange(new Rect(x, 0, _widths[i], finalSize.Height));
                x += _widths[i];
            }

            double maxOffset = Math.Max(0, total - finalSize.Width);
            UpdateEdgeFade(finalSize.Width, canScrollLeft: _scrollOffset > 0.5, canScrollRight: _scrollOffset < maxOffset - 0.5);
            return finalSize;
        }

        /// <summary>Breite des Ausblendens am Rand, wenn dort weitere Tabs verborgen sind.</summary>
        private const double EdgeFadeWidth = 28;

        private (double Width, bool Left, bool Right) _edgeFade;

        /// <summary>
        /// Weitere Tabs links bzw. rechts außerhalb der Leiste: den Rand weich ausblenden, damit sichtbar ist,
        /// dass man (per Mausrad) weiterblättern kann – statt die Tabs hart abzuschneiden.
        /// </summary>
        private void UpdateEdgeFade(double width, bool canScrollLeft, bool canScrollRight)
        {
            if (_edgeFade == (width, canScrollLeft, canScrollRight)) return;
            _edgeFade = (width, canScrollLeft, canScrollRight);

            if ((!canScrollLeft && !canScrollRight) || width <= EdgeFadeWidth * 2)
            {
                OpacityMask = null;
                return;
            }

            double fade = EdgeFadeWidth / width;
            var mask = new System.Windows.Media.LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            mask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(canScrollLeft ? (byte)0 : (byte)255, 0, 0, 0), 0));
            mask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Black, fade));
            mask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Colors.Black, 1 - fade));
            mask.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromArgb(canScrollRight ? (byte)0 : (byte)255, 0, 0, 0), 1));
            mask.Freeze();
            OpacityMask = mask;
        }

        /// <summary>Mausrad über der Tab-Leiste blättert durch die Tabs, wenn nicht alle hineinpassen.</summary>
        private double _frozenTabWidth = double.NaN;
        private System.Windows.Threading.DispatcherTimer? _freezeTimer;

        /// <summary>
        /// Wie Chrome: Wird ein Tab per Maus geschlossen, behalten die übrigen Tabs ihre Breite, bis der Zeiger die
        /// Tab-Leiste verlässt. So rückt der nächste Tab mit seinem Schließen-Kreuz genau unter den Zeiger und
        /// mehrere Tabs lassen sich nacheinander schließen, ohne der Maus hinterherzujagen.
        /// </summary>
        public void FreezeTabWidth()
        {
            if (_tabWidth <= 0) return;
            _frozenTabWidth = _tabWidth;
            // Über die Bildschirmposition prüfen: über den Lücken zwischen Tabs liegt die Fenster-Titelleiste,
            // dort kämen MouseLeave-Ereignisse zu früh
            _freezeTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(150),
                System.Windows.Threading.DispatcherPriority.Input, (s, e) => ReleaseFreezeIfPointerLeft(), Dispatcher);
            _freezeTimer.Start();
        }

        private void ReleaseFreezeIfPointerLeft()
        {
            if (IsVisible && PresentationSource.FromVisual(this) != null && GetCursorPos(out var screen))
            {
                var p = PointFromScreen(new Point(screen.X, screen.Y));
                // Etwas Spielraum nach oben/unten, damit kurzes Zittern über den Rand nicht sofort auflöst
                if (p.X >= 0 && p.X <= ActualWidth && p.Y >= -6 && p.Y <= ActualHeight + 6) return;
            }
            _freezeTimer?.Stop();
            _frozenTabWidth = double.NaN;
            InvalidateMeasure();
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativePoint { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out NativePoint point);

        protected override void OnMouseWheel(System.Windows.Input.MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            double total = 0;
            foreach (double w in _widths) total += w;
            double maxOffset = Math.Max(0, total - RenderSize.Width);
            if (maxOffset <= 0) return;

            _scrollOffset = Math.Clamp(_scrollOffset - e.Delta / 120.0 * Math.Max(_tabWidth, 60), 0, maxOffset);
            InvalidateArrange();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Anordnung der Tab-Leiste: die Tabs links, der "+"-Knopf direkt dahinter. Der Knopf bekommt immer
    /// seinen Platz, die Tabs teilen sich den Rest – so wandert "+" mit, bis die Leiste voll ist.
    /// </summary>
    public sealed class TabStripLayout : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (InternalChildren.Count < 2) return default;

            var tabs = InternalChildren[0];
            var button = InternalChildren[1];

            button.Measure(availableSize);
            double tabsWidth = double.IsInfinity(availableSize.Width)
                ? double.PositiveInfinity
                : Math.Max(0, availableSize.Width - button.DesiredSize.Width);
            tabs.Measure(new Size(tabsWidth, availableSize.Height));

            return new Size(
                Math.Min(tabs.DesiredSize.Width + button.DesiredSize.Width, double.IsInfinity(availableSize.Width) ? double.MaxValue : availableSize.Width),
                Math.Max(tabs.DesiredSize.Height, button.DesiredSize.Height));
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (InternalChildren.Count < 2) return finalSize;

            var tabs = InternalChildren[0];
            var button = InternalChildren[1];
            double tabsWidth = Math.Min(tabs.DesiredSize.Width, Math.Max(0, finalSize.Width - button.DesiredSize.Width));

            tabs.Arrange(new Rect(0, 0, tabsWidth, finalSize.Height));
            button.Arrange(new Rect(tabsWidth, (finalSize.Height - button.DesiredSize.Height) / 2,
                button.DesiredSize.Width, button.DesiredSize.Height));
            return finalSize;
        }
    }
}
