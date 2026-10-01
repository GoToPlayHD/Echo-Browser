using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
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

        public ExtensionPopupWindow()
        {
            InitializeComponent();

            Deactivated += (s, e) =>
            {
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

        public async Task InitializeAndNavigateAsync(
            CoreWebView2Environment env,
            string extensionId,
            string extensionName,
            string popupUrl,
            string? optionsPage = null,
            string? iconPath = null)
        {
            ExtensionId = extensionId;
            ExtensionName = extensionName;
            OptionsPage = optionsPage;
            PopupUrl = popupUrl;

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
                await webViewPopup.EnsureCoreWebView2Async(env);
                webViewPopup.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webViewPopup.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webViewPopup.CoreWebView2.Settings.AreDevToolsEnabled = true;

                webViewPopup.CoreWebView2.Navigate(popupUrl);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load extension popup: {ex.Message}");
            }
        }

        public void PositionUnderneath(FrameworkElement anchorElement)
        {
            try
            {
                Point screenPoint = anchorElement.PointToScreen(new Point(0, anchorElement.ActualHeight + 4));
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
                Debug.WriteLine($"Error positioning extension popup: {ex.Message}");
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
