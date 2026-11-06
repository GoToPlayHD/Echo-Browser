using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Views
{
    public partial class ExtensionPopupWindow : Window
    {
        public string ExtensionId { get; private set; } = "";
        public string ExtensionName { get; private set; } = "";
        public string? OptionsPage { get; private set; }
        public string PopupUrl { get; private set; } = "";

        public event Action<string, string>? OpenOptionsRequested;

        /// <summary>Anheften geändert (Erweiterungs-ID, angeheftet) – das Hauptfenster merkt es sich pro Erweiterung.</summary>
        public event Action<string, bool>? KeepOpenChanged;

        /// <summary>Angeheftet: bleibt offen, wenn man woanders hinklickt, und lässt sich frei verschieben.</summary>
        public bool IsKeptOpen { get; private set; }

        /// <summary>Was beim Klick auf die Erweiterung passiert ist.</summary>
        public enum ActionResult { PopupShown, ClickDispatched, NoAction }

        public ExtensionPopupWindow()
        {
            InitializeComponent();

            Deactivated += (s, e) =>
            {
                if (IsKeptOpen) return;
                try { Close(); } catch { }
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    try { Close(); } catch { }
                }
            };
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        public void SetKeptOpen(bool keepOpen)
        {
            IsKeptOpen = keepOpen;
            // Angeheftet verhält es sich wie ein normales Fenster über Echo, sonst schwebt es über allem
            Topmost = !keepOpen;
            pathKeepOpen.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, keepOpen ? "AccentSilverBrightBrush" : "TextSecondaryBrush");
            btnKeepOpen.ToolTip = Tr.Get(keepOpen ? "ExtPopup_KeepOpenActive" : "ExtPopup_KeepOpen");
        }

        private void BtnKeepOpen_Click(object sender, RoutedEventArgs e)
        {
            SetKeptOpen(!IsKeptOpen);
            KeepOpenChanged?.Invoke(ExtensionId, IsKeptOpen);
        }

        /// <summary>
        /// Klick auf die Erweiterung wie in Chrome: zuerst ein zur Laufzeit gesetztes Popup (chrome.action.setPopup),
        /// sonst das Popup aus dem Manifest; ohne Popup bekommt die Erweiterung das Klick-Ereignis (chrome.action.onClicked).
        /// Das Fenster muss dafür schon angezeigt sein (die WebView braucht es); sichtbar machen nur bei PopupShown.
        /// </summary>
        public async Task<ActionResult> InitializeAndNavigateAsync(
            CoreWebView2Environment env,
            string extensionId,
            string extensionName,
            string? manifestPopupPage,
            string? optionsPage = null,
            string? iconPath = null,
            CoreWebView2ControllerOptions? controllerOptions = null)
        {
            ExtensionId = extensionId;
            ExtensionName = extensionName;
            OptionsPage = optionsPage;

            txtExtTitle.Text = extensionName;
            btnOpenOptions.Visibility = !string.IsNullOrWhiteSpace(optionsPage) ? Visibility.Visible : Visibility.Collapsed;

            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                try
                {
                    imgExtIcon.Source = new BitmapImage(new Uri(iconPath, UriKind.Absolute));
                    imgExtIcon.Visibility = Visibility.Visible;
                    pathDefaultIcon.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    imgExtIcon.Visibility = Visibility.Collapsed;
                    pathDefaultIcon.Visibility = Visibility.Visible;
                }
            }
            else
            {
                imgExtIcon.Visibility = Visibility.Collapsed;
                pathDefaultIcon.Visibility = Visibility.Visible;
            }

            try
            {
                // Dasselbe Profil wie das Fenster (Inkognito: temporäres Profil mit Erweiterungen)
                await webViewPopup.EnsureCoreWebView2Async(env, controllerOptions);
                var core = webViewPopup.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = true;
                core.Settings.AreDevToolsEnabled = true;

                // Eine Seite der Erweiterung laden, damit chrome.action verfügbar ist (manifest.json hat keine Nebenwirkungen)
                string origin = $"chrome-extension://{extensionId}/";
                string? popup = await NavigateAndWaitAsync(core, origin + "manifest.json")
                    ? await ExtensionActions.GetDynamicPopupAsync(core)
                    : null;
                popup ??= string.IsNullOrWhiteSpace(manifestPopupPage) ? null : origin + manifestPopupPage.TrimStart('/');

                if (popup != null)
                {
                    PopupUrl = popup.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) ? popup : origin + popup.TrimStart('/');

                    // Wie Chrome: klein anfangen und auf die Größe des Inhalts wachsen (höchstens 800 × 600)
                    SetContentSize(MinContentWidth, MinContentHeight);
                    await NavigateAndWaitAsync(core, PopupUrl);
                    await FitToContentAsync(core);
                    _ = KeepFittingAsync(core);
                    return ActionResult.PopupShown;
                }

                return await ExtensionActions.DispatchActionClickAsync(core, extensionId)
                    ? ActionResult.ClickDispatched
                    : ActionResult.NoAction;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to load extension popup", ex);
                return ActionResult.NoAction;
            }
        }

        private static async Task<bool> NavigateAndWaitAsync(CoreWebView2 core, string url)
        {
            var done = new TaskCompletionSource<bool>();
            void OnCompleted(object? s, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
            core.NavigationCompleted += OnCompleted;
            try
            {
                core.Navigate(url);
                return await Task.WhenAny(done.Task, Task.Delay(5000)) == done.Task && done.Task.Result;
            }
            finally
            {
                core.NavigationCompleted -= OnCompleted;
            }
        }

        // Grenzen wie in Chrome (Inhalt ohne Kopfzeile)
        private const double MinContentWidth = 160, MinContentHeight = 60, MaxContentWidth = 800, MaxContentHeight = 600;

        /// <summary>Kopfzeile, Rahmen und Schattenrand um den Inhalt (siehe XAML: Margin 10, Rahmen 1, Kopf 38).</summary>
        private const double FrameWidth = 22, FrameHeight = 22 + 38;

        private FrameworkElement? _anchor;
        private bool _sizingByCode;
        private bool _userResized;

        private void SetContentSize(double width, double height)
        {
            _sizingByCode = true;
            Width = Math.Clamp(width, MinContentWidth, MaxContentWidth) + FrameWidth;
            Height = Math.Clamp(height, MinContentHeight, MaxContentHeight) + FrameHeight;
            _sizingByCode = false;
            if (_anchor != null) PositionUnderneath(_anchor);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // Hat der Nutzer die Größe selbst geändert (Griff unten rechts), nicht mehr automatisch anpassen
            if (!_sizingByCode && IsLoaded && Opacity > 0) _userResized = true;
        }

        /// <summary>Fenster an die Größe des Popup-Inhalts anpassen (nur wachsen, damit nichts springt).</summary>
        private async Task FitToContentAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            if (_userResized) return;
            try
            {
                // Bis der Inhalt ganz hineinpasst (ohne Scrollbalken): Breite = natürliche Breite (max-content), auch bei
                // Popups ohne feste Breite; Höhe erst danach, weil sich der Umbruch mit der Breite ändert.
                // Verglichen wird mit dem tatsächlichen Sichtbereich der Seite (innerWidth/innerHeight).
                const string measure =
                    "(() => { const d = document.documentElement, b = document.body, old = d.style.width; d.style.width = 'max-content'; " +
                    "const natural = Math.max(d.scrollWidth, b ? b.scrollWidth : 0); d.style.width = old; " +
                    "return [Math.ceil(Math.max(natural, d.scrollWidth)), Math.ceil(Math.max(d.scrollHeight, b ? b.scrollHeight : 0)), " +
                    "innerWidth, innerHeight, innerWidth - d.clientWidth, innerHeight - d.clientHeight]; })()";
                for (int round = 0; round < 4 && !_userResized; round++)
                {
                    var m = System.Text.Json.JsonSerializer.Deserialize<double[]>(await core.ExecuteScriptAsync(measure));
                    if (m is not { Length: 6 }) return;
                    // scrollWidth/-Height runden ab: Ragt der Inhalt nur um Bruchteile eines Pixels hinaus, zeigt die Seite
                    // trotzdem Scrollbalken (m[4]/m[5] > 0) – dann ein paar Pixel zugeben
                    double missingWidth = Math.Max(m[0] - m[2], m[5] > 0 ? 2 : 0);
                    double missingHeight = Math.Max(m[1] - m[3], m[4] > 0 ? 2 : 0);
                    if (missingWidth <= 0 && missingHeight <= 0) return;

                    SetContentSize(Width - FrameWidth + Math.Max(0, missingWidth), Height - FrameHeight + Math.Max(0, missingHeight));
                    await Task.Delay(60); // neu umbrechen lassen, dann erneut messen
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Größe des Erweiterungs-Popups konnte nicht ermittelt werden", ex);
            }
        }

        /// <summary>Viele Popups bauen ihren Inhalt erst nach dem Laden auf – kurz danach noch einmal anpassen.</summary>
        private async Task KeepFittingAsync(Microsoft.Web.WebView2.Core.CoreWebView2 core)
        {
            foreach (int delay in new[] { 300, 700, 1500 })
            {
                await Task.Delay(delay);
                if (!IsLoaded) return;
                await FitToContentAsync(core);
            }
        }

        public void PositionUnderneath(FrameworkElement anchorElement)
        {
            _anchor = anchorElement;
            try
            {
                // PointToScreen liefert Bildschirmpixel, Left/Top und WorkArea sind geräteunabhängig (DIP) –
                // bei 125 %/150 % Skalierung sonst versetzt
                Point screenPoint = anchorElement.PointToScreen(new Point(0, anchorElement.ActualHeight + 4));
                if (PresentationSource.FromVisual(anchorElement)?.CompositionTarget is { } target)
                {
                    screenPoint = target.TransformFromDevice.Transform(screenPoint);
                }
                double left = screenPoint.X - 10;
                double top = screenPoint.Y;

                var workArea = SystemParameters.WorkArea;
                if (left + Width > workArea.Right)
                {
                    left = workArea.Right - Width - 10;
                }
                if (left < workArea.Left)
                {
                    left = workArea.Left + 10;
                }

                if (top + Height > workArea.Bottom)
                {
                    top = screenPoint.Y - Height - anchorElement.ActualHeight - 8;
                }

                Left = left;
                Top = top;
            }
            catch (Exception ex)
            {
                Log.Warn("Error positioning extension popup", ex);
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                webViewPopup.CoreWebView2?.Reload();
            }
            catch { }
        }

        private void BtnOpenOptions_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(OptionsPage))
            {
                OpenOptionsRequested?.Invoke(ExtensionId, OptionsPage);
                Close();
            }
        }
    }
}
