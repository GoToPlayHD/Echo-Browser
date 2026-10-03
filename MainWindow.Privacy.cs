using System;
using System.Threading.Tasks;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Datenschutz-Signale "Do Not Track" (DNT: 1) und Global Privacy Control (Sec-GPC: 1).
    /// Die Header gehen nur bei Seitenaufrufen (Dokumente) raus – so läuft nicht jede Anfrage über den UI-Thread.
    /// Zusätzlich melden navigator.doNotTrack / navigator.globalPrivacyControl den Wunsch an Skripte.
    /// </summary>
    public partial class MainWindow
    {
        private const string PrivacySignalsScript = @"(() => {
            try {
                Object.defineProperty(Navigator.prototype, 'doNotTrack', { get: () => '1', configurable: true });
                Object.defineProperty(Navigator.prototype, 'globalPrivacyControl', { get: () => true, configurable: true });
            } catch (e) { }
        })();";

        private async Task AttachPrivacySignalsAsync(BrowserTab tab, CoreWebView2 core)
        {
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.Document);
            core.WebResourceRequested += (s, args) =>
            {
                if (!AppSettingsService.Instance.Settings.SendDoNotTrack) return;
                if (args.ResourceContext != CoreWebView2WebResourceContext.Document) return;
                if (!args.Request.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

                try
                {
                    args.Request.Headers.SetHeader("DNT", "1");
                    args.Request.Headers.SetHeader("Sec-GPC", "1");
                }
                catch (Exception ex)
                {
                    Log.Warn("Datenschutz-Header konnten nicht gesetzt werden", ex);
                }
            };

            await SyncPrivacySignalsScriptAsync(tab, core);
        }

        /// <summary>Skript für navigator.doNotTrack passend zur Einstellung an- oder abmelden (wirkt ab dem nächsten Seitenaufruf).</summary>
        private static async Task SyncPrivacySignalsScriptAsync(BrowserTab tab, CoreWebView2 core)
        {
            bool enabled = AppSettingsService.Instance.Settings.SendDoNotTrack;
            try
            {
                if (enabled && tab.PrivacySignalsScriptId == null)
                {
                    tab.PrivacySignalsScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(PrivacySignalsScript);
                }
                else if (!enabled && tab.PrivacySignalsScriptId != null)
                {
                    core.RemoveScriptToExecuteOnDocumentCreated(tab.PrivacySignalsScriptId);
                    tab.PrivacySignalsScriptId = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Datenschutz-Skript konnte nicht aktualisiert werden", ex);
            }
        }

        private async Task SyncPrivacySignalsForAllTabsAsync()
        {
            foreach (var tab in Tabs)
            {
                if (tab.WebView?.CoreWebView2 is { } core)
                {
                    await SyncPrivacySignalsScriptAsync(tab, core);
                }
            }
        }
    }
}
