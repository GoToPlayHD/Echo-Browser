using System;
using System.IO;
using System.Text;
using System.Web;
using System.Windows;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>
    /// Interne Seiten unter echo:// (Startseite, Einstellungen, Absturzseite) und die Erholung nach Abstürzen.
    /// Die Seiten werden bei jedem Aufruf frisch erzeugt – Sprache, Einstellungen und Token sind so immer aktuell.
    /// </summary>
    public partial class MainWindow
    {
        private const string HtmlHeaders = "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store";

        private bool _isUnresponsivePromptOpen;

        private void AttachInternalPages(CoreWebView2Environment env, CoreWebView2 core)
        {
            core.AddWebResourceRequestedFilter(InternalPages.Scheme + "://*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (s, args) =>
            {
                if (!InternalPages.IsInternalUrl(args.Request.Uri)) return;

                string? page = InternalPages.GetPageName(args.Request.Uri);
                if (page is InternalPages.Favicon or InternalPages.Wallpaper)
                {
                    // Bilder nur für interne Seiten – sonst könnte eine Webseite per <img src="echo://favicon/…">
                    // ausprobieren, welche Websites im Verlauf stehen
                    args.Response = InternalPages.IsInternalUrl(core.Source)
                        ? BuildInternalImage(env, page, args.Request.Uri)
                        : env.CreateWebResourceResponse(null, 403, "Forbidden", "");
                    return;
                }

                string? html = BuildInternalPageHtml(args.Request.Uri);
                args.Response = html == null
                    ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
                    : env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", HtmlHeaders);
            };

            // Interne Seiten nie in fremde Seiten einbetten lassen (Schutz vor Clickjacking)
            core.FrameNavigationStarting += (s, args) =>
            {
                if (InternalPages.IsInternalUrl(args.Uri))
                {
                    args.Cancel = true;
                }
            };
        }

        private static CoreWebView2WebResourceResponse BuildInternalImage(CoreWebView2Environment env, string page, string url)
        {
            byte[]? bytes = null;
            string contentType = "image/png";

            if (page == InternalPages.Favicon && Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                bytes = FaviconCache.Instance.ReadPng(Uri.UnescapeDataString(uri.AbsolutePath.Trim('/')));
            }
            else if (page == InternalPages.Wallpaper)
            {
                string path = AppSettingsService.Instance.Settings.StartpageBackgroundPath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    bytes = File.ReadAllBytes(path);
                    contentType = Path.GetExtension(path).ToLowerInvariant() switch
                    {
                        ".jpg" or ".jpeg" => "image/jpeg",
                        ".webp" => "image/webp",
                        ".gif" => "image/gif",
                        ".bmp" => "image/bmp",
                        _ => "image/png"
                    };
                }
            }

            return bytes == null
                ? env.CreateWebResourceResponse(null, 404, "Not Found", "")
                : env.CreateWebResourceResponse(new MemoryStream(bytes), 200, "OK", $"Content-Type: {contentType}\r\nCache-Control: max-age=600");
        }

        private string? BuildInternalPageHtml(string url)
        {
            switch (InternalPages.GetPageName(url))
            {
                case InternalPages.Start:
                case InternalPages.NewTab:
                    return StartPageService.GetStartPageHtml(_isIncognito);

                case InternalPages.Settings:
                    return SettingsPageService.GetSettingsPageHtml(
                        AppSettingsService.Instance.Settings,
                        _webViewEnvironment?.BrowserVersionString ?? "",
                        UpdateService.Instance.CurrentVersion);

                case InternalPages.Crashed:
                    string? originalUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri)
                        ? HttpUtility.ParseQueryString(uri.Query)["url"]
                        : null;
                    return CrashPageService.GetCrashPageHtml(originalUrl);

                default:
                    return null;
            }
        }

        /// <summary>
        /// Renderer abgestürzt: eigene Absturzseite mit "Neu laden". Renderer hängt: nachfragen (nur für den sichtbaren Tab).
        /// Browser-Prozess weg: WebView neu aufbauen, die Umgebung wird dabei neu erzeugt.
        /// </summary>
        private void HandleProcessFailed(BrowserTab tab, WebView2 webView, CoreWebView2ProcessFailedEventArgs args)
        {
            Log.Warn($"Prozessfehler {args.ProcessFailedKind} ({args.Reason}, Exit-Code {args.ExitCode}) in Tab {tab.Url}");

            switch (args.ProcessFailedKind)
            {
                case CoreWebView2ProcessFailedKind.RenderProcessExited:
                    ShowCrashPage(tab, webView);
                    break;

                case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                    if (tab == ActiveTab && !_isUnresponsivePromptOpen)
                    {
                        AskToReloadUnresponsiveTab(tab, webView);
                    }
                    break;

                case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                    if (_webViewEnvironment != null)
                    {
                        BrowserEnvironment.Invalidate(_webViewEnvironment);
                    }
                    RecreateTabWebView(tab, webView);
                    break;
            }
        }

        private static void ShowCrashPage(BrowserTab tab, WebView2 webView)
        {
            try
            {
                if (InternalPages.IsInternalUrl(tab.Url))
                {
                    // Interne Seiten lassen sich einfach neu erzeugen
                    webView.CoreWebView2?.Reload();
                }
                else
                {
                    webView.CoreWebView2?.Navigate(InternalPages.CrashedPageFor(tab.Url));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Absturzseite konnte nicht angezeigt werden", ex);
            }
        }

        private void AskToReloadUnresponsiveTab(BrowserTab tab, WebView2 webView)
        {
            _isUnresponsivePromptOpen = true;
            try
            {
                string site = Uri.TryCreate(tab.Url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : tab.Title;
                bool reload = ThemedDialogWindow.ShowConfirm(
                    this,
                    Tr.Get("Unresponsive_Title"),
                    Tr.Format("Unresponsive_Message", site),
                    Tr.Get("Unresponsive_Reload"),
                    Tr.Get("Unresponsive_Wait"));

                if (reload && tab.WebView == webView)
                {
                    webView.CoreWebView2?.Reload();
                }
            }
            finally
            {
                _isUnresponsivePromptOpen = false;
            }
        }

        /// <summary>Ersetzt die (tote) WebView eines Tabs durch eine neue und lädt die bisherige Adresse.</summary>
        private void RecreateTabWebView(BrowserTab tab, WebView2 deadWebView)
        {
            if (tab.WebView != deadWebView || !Tabs.Contains(tab)) return;

            WebViewContainer.Children.Remove(deadWebView);
            tab.ReleaseWebView();
            CreateTabWebView(tab, tab.Url);
            ApplySplitLayout(); // neue WebView in die richtige Spalte der geteilten Ansicht
        }
    }
}
