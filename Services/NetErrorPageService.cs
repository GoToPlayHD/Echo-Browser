using System;
using System.Net;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Eigene Fehlerseite für nicht erreichbare Websites (DNS, offline, Zeitüberschreitung, Verbindungsabbruch).
    /// <para>
    /// Statt auf eine eigene echo://-Adresse umzuleiten, ersetzt Echo den Inhalt von Chromiums Fehlerseite
    /// (chrome-error://). So bleiben Adressleiste, "Neu laden" und Zurück/Vor unverändert bei der fehlgeschlagenen
    /// Adresse – eine Umleitung würde einen zusätzlichen Verlaufseintrag erzeugen.
    /// </para>
    /// Zertifikats- und Anmeldefehler behalten Chromiums eigene Seite (mit deren Warnungen und Optionen).
    /// </summary>
    public static class NetErrorPageService
    {
        public enum ErrorKind { Dns, Offline, Timeout, Connection }

        /// <summary>Art des Fehlers oder null, wenn Echo keine eigene Seite zeigt (abgebrochen, Zertifikat, Anmeldung).</summary>
        public static ErrorKind? Classify(CoreWebView2WebErrorStatus status) => status switch
        {
            CoreWebView2WebErrorStatus.HostNameNotResolved => ErrorKind.Dns,
            CoreWebView2WebErrorStatus.Disconnected => ErrorKind.Offline,
            CoreWebView2WebErrorStatus.Timeout or CoreWebView2WebErrorStatus.ServerUnreachable => ErrorKind.Timeout,
            CoreWebView2WebErrorStatus.CannotConnect or
            CoreWebView2WebErrorStatus.ConnectionAborted or
            CoreWebView2WebErrorStatus.ConnectionReset or
            CoreWebView2WebErrorStatus.ErrorHttpInvalidServerResponse or
            CoreWebView2WebErrorStatus.RedirectFailed or
            CoreWebView2WebErrorStatus.UnexpectedError => ErrorKind.Connection,
            _ => null
        };

        /// <summary>
        /// Skript für ExecuteScriptAsync: ersetzt die Fehlerseite – aber nur, wenn wirklich Chromiums Fehlerseite
        /// angezeigt wird (ein abgebrochener Seitenwechsel lässt die alte Seite stehen, die bleibt unangetastet).
        /// Die Knöpfe werden vom Skript selbst verdrahtet, damit eine CSP der Fehlerseite sie nicht blockiert.
        /// </summary>
        public static string BuildInjectionScript(string failedUrl, ErrorKind kind, CoreWebView2WebErrorStatus status)
        {
            string host = Uri.TryCreate(failedUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : failedUrl;
            string html = BuildHtml(host, failedUrl, kind, status);
            bool canRetry = Uri.TryCreate(failedUrl, UriKind.Absolute, out var retryUri) &&
                            (retryUri.Scheme == Uri.UriSchemeHttp || retryUri.Scheme == Uri.UriSchemeHttps);

            return "(() => {" +
                   "if (!location.href.startsWith('chrome-error://')) return false;" +
                   $"document.documentElement.innerHTML = {JsonSerializer.Serialize(html)};" +
                   $"document.title = {JsonSerializer.Serialize(Title(kind))};" +
                   $"const target = {JsonSerializer.Serialize(canRetry ? failedUrl : "")};" +
                   "const retry = () => { if (target) location.replace(target); };" +
                   "const button = document.getElementById('echo-retry');" +
                   "if (button) { button.addEventListener('click', retry); button.focus(); }" +
                   // Offline: automatisch neu laden, sobald die Verbindung wieder da ist
                   "window.addEventListener('online', retry, { once: true });" +
                   "return true; })()";
        }

        private static string Title(ErrorKind kind) => Tr.Get(kind switch
        {
            ErrorKind.Dns => "NetError_DnsTitle",
            ErrorKind.Offline => "NetError_OfflineTitle",
            ErrorKind.Timeout => "NetError_TimeoutTitle",
            _ => "NetError_ConnectionTitle"
        });

        private static string Message(ErrorKind kind, string host) => kind switch
        {
            ErrorKind.Dns => Tr.Format("NetError_DnsMessage", host),
            ErrorKind.Offline => Tr.Get("NetError_OfflineMessage"),
            ErrorKind.Timeout => Tr.Format("NetError_TimeoutMessage", host),
            _ => Tr.Format("NetError_ConnectionMessage", host)
        };

        /// <summary>Inhalt des html-Elements (head + body); alle Texte sind HTML-kodiert.</summary>
        internal static string BuildHtml(string host, string failedUrl, ErrorKind kind, CoreWebView2WebErrorStatus status)
        {
            static string E(string s) => WebUtility.HtmlEncode(s);
            string icon = kind == ErrorKind.Offline
                // WLAN aus
                ? "M23.64 7c-.45-.34-4.93-4-11.64-4-1.5 0-2.89.19-4.15.48L18.18 13.8 23.64 7zm-6.6 8.22L3.27 1.44 2 2.72l2.05 2.06C1.91 5.76.59 6.82.36 7l11.63 14.49.01.01.01-.01 3.9-4.86 3.32 3.32 1.27-1.27-3.46-3.46z"
                // Wolke mit Ausrufezeichen
                : "M19.35 10.04C18.67 6.59 15.64 4 12 4 9.11 4 6.6 5.64 5.35 8.04 2.34 8.36 0 10.91 0 14c0 3.31 2.69 6 6 6h13c2.76 0 5-2.24 5-5 0-2.64-2.05-4.78-4.65-4.96zM13 17h-2v-2h2v2zm0-4h-2V8h2v5z";

            return "<head><meta charset=\"UTF-8\"><style>" + ThemeManager.Instance.GetCssVariables() + Css + "</style></head>" +
                   "<body><main class=\"card\">" +
                   $"<svg class=\"icon\" viewBox=\"0 0 24 24\" aria-hidden=\"true\"><path d=\"{icon}\"/></svg>" +
                   $"<h1>{E(Title(kind))}</h1>" +
                   $"<p>{E(Message(kind, host))}</p>" +
                   $"<p class=\"url\">{E(failedUrl)}</p>" +
                   $"<button id=\"echo-retry\" type=\"button\">{E(Tr.Get("NetError_Retry"))}</button>" +
                   $"<p class=\"code\">{E(ErrorCode(status))}</p>" +
                   "</main></body>";
        }

        /// <summary>Fehlercode wie in Chrome, z.B. HOST_NAME_NOT_RESOLVED – hilfreich beim Melden von Problemen.</summary>
        internal static string ErrorCode(CoreWebView2WebErrorStatus status) =>
            System.Text.RegularExpressions.Regex.Replace(status.ToString(), "(?<=[a-z])(?=[A-Z])", "_").ToUpperInvariant();

        private const string Css = @"
* { box-sizing: border-box; margin: 0; padding: 0; }
html, body { height: 100%; }
body { font-family: 'Segoe UI Variable Text', 'Segoe UI', sans-serif; background: var(--echo-window); color: var(--echo-text);
       display: flex; align-items: center; justify-content: center; }
.card { max-width: 540px; padding: 32px; text-align: center; }
.icon { width: 56px; height: 56px; margin-bottom: 20px; fill: var(--echo-accent-dim); }
h1 { font-size: 22px; font-weight: 600; margin-bottom: 10px; }
p { color: var(--echo-text-secondary); font-size: 14px; line-height: 1.5; }
.url { margin-top: 10px; font-size: 12px; color: var(--echo-text-muted); word-break: break-all; }
.code { margin-top: 18px; font-size: 11px; letter-spacing: .04em; color: var(--echo-text-muted); }
button { margin-top: 24px; padding: 9px 22px; border: 1px solid var(--echo-border); border-radius: 8px;
         background: var(--echo-surface); color: var(--echo-text); font: inherit; font-size: 14px; cursor: pointer; }
button:hover { background: var(--echo-surface-hover); }
button:active { background: var(--echo-surface-active); }
button:focus-visible { outline: 2px solid var(--echo-focus); outline-offset: 2px; }";
    }
}
