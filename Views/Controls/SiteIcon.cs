using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Controls
{
    /// <summary>
    /// Website-Symbol aus dem <see cref="FaviconCache"/>. Ohne gespeichertes Symbol erscheint ein Kreis
    /// mit dem Anfangsbuchstaben. Aktualisiert sich, sobald ein Tab das Symbol der Website lädt.
    /// </summary>
    public sealed class SiteIcon : Grid
    {
        public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(
            nameof(Url), typeof(string), typeof(SiteIcon), new PropertyMetadata(null, (d, e) => ((SiteIcon)d).Refresh()));

        public static readonly DependencyProperty FallbackProperty = DependencyProperty.Register(
            nameof(Fallback), typeof(string), typeof(SiteIcon), new PropertyMetadata("", (d, e) => ((SiteIcon)d)._initial.Text = (string)e.NewValue));

        public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(
            nameof(IconSize), typeof(double), typeof(SiteIcon), new PropertyMetadata(16.0, (d, e) => ((SiteIcon)d).ApplySize()));

        private readonly Border _circle;
        private readonly TextBlock _initial;
        private readonly Image _image;

        public string? Url { get => (string?)GetValue(UrlProperty); set => SetValue(UrlProperty, value); }
        public string Fallback { get => (string)GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }
        public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }

        public SiteIcon()
        {
            VerticalAlignment = VerticalAlignment.Center;

            _initial = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _initial.SetResourceReference(TextBlock.ForegroundProperty, "AccentSilverBrush");

            _circle = new Border { Child = _initial };
            _circle.SetResourceReference(Border.BackgroundProperty, "SurfaceHoverBrush");

            _image = new Image { Visibility = Visibility.Collapsed, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

            Children.Add(_circle);
            Children.Add(_image);
            ApplySize();

            Loaded += (s, e) => { FaviconCache.Instance.Updated += OnFaviconUpdated; Refresh(); };
            Unloaded += (s, e) => FaviconCache.Instance.Updated -= OnFaviconUpdated;
        }

        private void ApplySize()
        {
            double size = IconSize;
            Width = Height = size;
            _circle.CornerRadius = new CornerRadius(size / 2);
            _initial.FontSize = System.Math.Max(8, size * 0.62);
        }

        private void OnFaviconUpdated(string hostKey)
        {
            if (hostKey == FaviconCache.HostKey(Url))
            {
                Refresh();
            }
        }

        private void Refresh()
        {
            var image = FaviconCache.Instance.Get(Url);
            _image.Source = image;
            _image.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
            _circle.Visibility = image == null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
