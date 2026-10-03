using System;
using System.Linq;
using System.Windows;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>
    /// Zoom pro Website wie in Chrome: Strg+Plus/Minus/0, Strg+Mausrad, Anzeige in der Adressleiste
    /// und Zoom-Zeile im Hauptmenü. Der Zoom einer Website wird in zoom.json gemerkt (nicht im Inkognito-Modus).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Wir setzen den Zoom gerade selbst – das ZoomFactorChanged-Ereignis dann nicht speichern.</summary>
        private bool _isApplyingZoom;

        private void AttachZoomEvents(BrowserTab tab, WebView2 webView)
        {
            webView.ZoomFactorChanged += (s, e) =>
            {
                if (!_isApplyingZoom && !_isIncognito)
                {
                    ZoomService.Instance.Set(tab.Url, webView.ZoomFactor, ZoomService.DefaultFactor);
                }
                if (tab == ActiveTab)
                {
                    UpdateZoomIndicator();
                }
            };

            // Beim Laden einer neuen Seite den Zoom dieser Website herstellen
            if (webView.CoreWebView2 != null)
            {
                webView.CoreWebView2.ContentLoading += (s, e) => ApplySiteZoom(tab);
            }
        }

        private void ApplySiteZoom(BrowserTab tab)
        {
            if (tab.WebView == null) return;

            double target = ZoomService.Instance.Get(tab.Url) ?? ZoomService.DefaultFactor;
            if (ZoomLevels.AreEqual(tab.WebView.ZoomFactor, target)) return;

            SetZoom(tab.WebView, target, remember: false);
        }

        /// <summary>
        /// Zoom setzen. WebView2 meldet ZoomFactorChanged nur bei Zoom durch den Nutzer (Strg+Mausrad),
        /// nicht wenn wir ihn setzen – deshalb hier selbst merken und die Anzeige aktualisieren.
        /// </summary>
        private void SetZoom(WebView2 webView, double factor, bool remember)
        {
            factor = Math.Clamp(factor, ZoomLevels.Min, ZoomLevels.Max);
            _isApplyingZoom = true;
            try
            {
                webView.ZoomFactor = factor;
            }
            finally
            {
                _isApplyingZoom = false;
            }

            var tab = Tabs.FirstOrDefault(t => t.WebView == webView);
            if (remember && !_isIncognito && tab != null)
            {
                ZoomService.Instance.Set(tab.Url, factor, ZoomService.DefaultFactor);
            }
            if (tab != null && tab == ActiveTab)
            {
                UpdateZoomIndicator();
            }
        }

        /// <summary>Strg+Plus (+1) bzw. Strg+Minus (-1).</summary>
        private void ZoomActiveTab(int direction)
        {
            var webView = ActiveTab?.WebView;
            if (webView?.CoreWebView2 == null) return;
            SetZoom(webView, ZoomLevels.Next(webView.ZoomFactor, direction), remember: true);
        }

        /// <summary>Strg+0 bzw. Klick auf die Zoom-Anzeige: zurück zum Standard-Zoom.</summary>
        private void ResetZoom()
        {
            var webView = ActiveTab?.WebView;
            if (webView?.CoreWebView2 == null) return;
            SetZoom(webView, ZoomService.DefaultFactor, remember: true);
        }

        /// <summary>Lupe mit Prozentzahl in der Adressleiste, nur wenn die Seite vom Standard abweicht.</summary>
        private void UpdateZoomIndicator()
        {
            double factor = ActiveTab?.WebView?.ZoomFactor ?? 1.0;
            bool differs = ActiveTab?.WebView?.CoreWebView2 != null && !ZoomLevels.AreEqual(factor, ZoomService.DefaultFactor);

            btnZoomIndicator.Visibility = differs ? Visibility.Visible : Visibility.Collapsed;
            txtZoomIndicator.Text = ZoomLevels.Format(factor);
            btnZoomIndicator.ToolTip = Tr.Format("Zoom_IndicatorTooltip", ZoomLevels.Format(factor));
            txtMenuZoom.Text = ZoomLevels.Format(factor);
        }

        private void BtnZoomIndicator_Click(object sender, RoutedEventArgs e) => ResetZoom();

        private void MenuZoomIn_Click(object sender, RoutedEventArgs e) => ZoomActiveTab(+1);

        private void MenuZoomOut_Click(object sender, RoutedEventArgs e) => ZoomActiveTab(-1);
    }
}
