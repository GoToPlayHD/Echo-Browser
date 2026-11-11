using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser.Views
{
    /// <summary>
    /// Halbtransparente Vorschau am Mauszeiger, solange ein Tab außerhalb seiner Tab-Leiste gezogen wird:
    /// Tab-Kopf (Symbol + Titel) und darunter eine Miniatur der Seite. Über der Tab-Leiste eines anderen Fensters
    /// schrumpft sie auf den Tab-Kopf. Das Fenster ist durchklickbar und nimmt nie den Fokus.
    /// </summary>
    public sealed class TabDragGhostWindow : Window
    {
        private const double GhostWidth = 260;

        private readonly Image _thumbnail;
        private readonly Border _thumbnailFrame;
        private readonly TextBlock _hint;
        private Vector _grabOffset;

        public TabDragGhostWindow(BrowserTab tab, Vector grabOffsetInTab)
        {
            _grabOffset = new Vector(Math.Clamp(grabOffsetInTab.X, 8, GhostWidth - 8), Math.Clamp(grabOffsetInTab.Y, 4, 30));

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            IsHitTestVisible = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Opacity = 0.88;

            var icon = new Image { Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            icon.SetBinding(Image.SourceProperty, new Binding(nameof(BrowserTab.Favicon)) { Source = tab });
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

            var title = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 12
            };
            title.SetBinding(TextBlock.TextProperty, new Binding(nameof(BrowserTab.DisplayTitle)) { Source = tab });
            title.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var header = new DockPanel { Height = 34, Margin = new Thickness(12, 0, 12, 0), LastChildFill = true };
            DockPanel.SetDock(icon, Dock.Left);
            header.Children.Add(icon);
            header.Children.Add(title);

            _thumbnail = new Image { Stretch = Stretch.UniformToFill, VerticalAlignment = VerticalAlignment.Top };
            RenderOptions.SetBitmapScalingMode(_thumbnail, BitmapScalingMode.HighQuality);
            _thumbnailFrame = new Border
            {
                Height = 150,
                ClipToBounds = true,
                CornerRadius = new CornerRadius(0, 0, 8, 8),
                Child = _thumbnail
            };
            _thumbnailFrame.SetResourceReference(Border.BackgroundProperty, "WindowBackgroundBrush");

            _hint = new TextBlock
            {
                Margin = new Thickness(12, 0, 12, 8),
                FontSize = 11,
                Text = Tr.Get("Tab_DragNewWindowHint")
            };
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextMutedBrush");

            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(_hint);
            stack.Children.Add(_thumbnailFrame);

            var card = new Border
            {
                Width = GhostWidth,
                Margin = new Thickness(10), // Platz für den Schatten
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Child = stack,
                Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 }
            };
            card.SetResourceReference(Border.BackgroundProperty, "TabActiveBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "TabBorderBrush");
            Content = card;

            SourceInitialized += (s, e) => NativeScreen.MakeClickThroughToolWindow(new WindowInteropHelper(this).Handle);
        }

        /// <summary>Miniatur der Seite (kommt asynchron, sobald WebView2 sie geliefert hat).</summary>
        public void SetThumbnail(ImageSource? image)
        {
            _thumbnail.Source = image;
        }

        /// <summary>Über einer fremden Tab-Leiste nur der Tab-Kopf, sonst mit Miniatur und Hinweis.</summary>
        public void SetDockMode(bool docking)
        {
            var visibility = docking ? Visibility.Collapsed : Visibility.Visible;
            _thumbnailFrame.Visibility = visibility;
            _hint.Visibility = visibility;
        }

        /// <summary>
        /// Vorschau so an den Mauszeiger setzen, dass er dieselbe Stelle des Tab-Kopfs greift wie beim Start.
        /// Physische Pixel mit der Skalierung des Monitors unter dem Zeiger – kein Springen bei gemischter DPI.
        /// </summary>
        public void MoveTo(Point cursorPx)
        {
            var (_, scale) = NativeScreen.MonitorAt(cursorPx);
            // + 10: Rand für den Schatten
            var offset = new Vector((_grabOffset.X + 10) * scale, (_grabOffset.Y + 10) * scale);
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeScreen.MoveWindow(hwnd, cursorPx - offset);
            }
        }
    }
}
