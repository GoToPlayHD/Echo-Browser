using System;
using System.Net;

namespace EchoBrowser.Services
{
    /// <summary>Seite, die ein Tab zeigt, wenn sein Renderer-Prozess abgestürzt ist (echo://crashed/?url=…).</summary>
    public static class CrashPageService
    {
        public static string GetCrashPageHtml(string? originalUrl)
        {
            string url = originalUrl ?? "";
            string html = PageLocalizer.Apply(RawHtmlTemplate);
            html = html.Replace("##URL_TEXT##", WebUtility.HtmlEncode(url));
            // Als JSON-String einsetzen, damit die Adresse im Skript nichts ausbrechen kann
            html = html.Replace("##URL_JSON##", System.Text.Json.JsonSerializer.Serialize(url));
            html = html.Replace("##THEME_CSS##", ThemeManager.Instance.GetCssVariables());
            html = InternalPageSecurity.InjectToken(html);
            return html;
        }

        private const string RawHtmlTemplate = @"<!DOCTYPE html>
<html lang=""{{lang}}"">
<head>
    <meta charset=""UTF-8"">
    <title>{{t:Crash_Title}}</title>
    <style id=""echo-theme"">##THEME_CSS##</style>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: 'Segoe UI', -apple-system, Roboto, sans-serif;
            background: var(--echo-window);
            color: var(--echo-text);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
        }
        .card { max-width: 520px; padding: 32px; text-align: center; }
        .icon { width: 56px; height: 56px; margin-bottom: 20px; opacity: 0.8; }
        h1 { font-size: 22px; font-weight: 600; margin-bottom: 10px; }
        p { color: var(--echo-text-secondary); font-size: 14px; line-height: 1.5; }
        .url { margin-top: 10px; font-size: 12px; color: var(--echo-text-muted); word-break: break-all; }
        button {
            margin-top: 24px;
            padding: 9px 22px;
            border: 1px solid var(--echo-border);
            border-radius: 8px;
            background: var(--echo-surface);
            color: var(--echo-text);
            font-size: 14px;
            cursor: pointer;
        }
        button:hover { background: var(--echo-surface-hover); }
        button:focus-visible { outline: 2px solid var(--echo-focus); outline-offset: 2px; }
    </style>
</head>
<body>
    <div class=""card"">
        <svg class=""icon"" viewBox=""0 0 24 24"" fill=""currentColor"" style=""color: var(--echo-accent-dim)"" aria-hidden=""true"">
            <path d=""M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-2h2v2zm0-4h-2V7h2v6z""/>
        </svg>
        <h1>{{t:Crash_Title}}</h1>
        <p>{{t:Crash_Message}}</p>
        <p class=""url"">##URL_TEXT##</p>
        <button id=""reload"" autofocus>{{t:Crash_Reload}}</button>
    </div>
    <script>
        const originalUrl = ##URL_JSON##;
        document.getElementById('reload').addEventListener('click', () => {
            if (window.chrome && window.chrome.webview && originalUrl) {
                window.chrome.webview.postMessage({ type: 'navigate', url: originalUrl, __echoToken: '##ECHO_BRIDGE_TOKEN##' });
            }
        });
    </script>
</body>
</html>";
    }
}
