using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Was WebView2 für Erweiterungen nicht selbst anbietet, über das DevTools-Protokoll (CDP):
    /// <list type="bullet">
    /// <item>das zur Laufzeit gesetzte Popup abfragen (chrome.action.setPopup),</item>
    /// <item>einen Klick auf die Erweiterung auslösen (chrome.action.onClicked) – für Erweiterungen ohne Popup,</item>
    /// <item>Seiten erkennen, die Erweiterungen selbst öffnen (chrome.tabs.create, chrome.windows.create). WebView2
    /// lässt sie sonst unsichtbar im Hintergrund laufen; Echo zeigt sie als Tab.</item>
    /// </list>
    /// </summary>
    public static class ExtensionActions
    {
        /// <summary>
        /// Zur Laufzeit gesetztes Popup (leer = keins). Der Aufrufer muss <paramref name="core"/> zuvor auf eine Seite
        /// der Erweiterung geführt haben (z.B. chrome-extension://id/manifest.json), damit chrome.action verfügbar ist.
        /// </summary>
        public static async Task<string?> GetDynamicPopupAsync(CoreWebView2 core)
        {
            try
            {
                string result = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                    JsonSerializer.Serialize(new { expression = "chrome.action.getPopup({})", awaitPromise = true, returnByValue = true }));
                using var doc = JsonDocument.Parse(result);
                return doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v) &&
                       v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
                    ? v.GetString()
                    : null;
            }
            catch (Exception ex)
            {
                Log.Warn("Popup der Erweiterung konnte nicht abgefragt werden", ex);
                return null;
            }
        }

        /// <summary>
        /// Klick auf die Erweiterung im Service Worker auslösen (chrome.action.onClicked). Schläft der Worker (Manifest V3),
        /// wird er zuerst geweckt. <paramref name="core"/> muss auf einer Seite der Erweiterung stehen.
        /// </summary>
        public static async Task<bool> DispatchActionClickAsync(CoreWebView2 core, string extensionId)
        {
            try
            {
                string? worker = await FindServiceWorkerAsync(core, extensionId);
                if (worker == null)
                {
                    // Eine Nachricht an den registrierten Worker startet ihn
                    await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", JsonSerializer.Serialize(new
                    {
                        expression = "navigator.serviceWorker.getRegistration().then(r => { if (r && r.active) r.active.postMessage({ echo: 'wake' }); return !!r; })",
                        awaitPromise = true,
                        returnByValue = true
                    }));
                    for (int i = 0; i < 10 && worker == null; i++)
                    {
                        await Task.Delay(200);
                        worker = await FindServiceWorkerAsync(core, extensionId);
                    }
                }
                if (worker == null) return false;

                string attached = await core.CallDevToolsProtocolMethodAsync("Target.attachToTarget",
                    JsonSerializer.Serialize(new { targetId = worker, flatten = true }));
                string session = JsonDocument.Parse(attached).RootElement.GetProperty("sessionId").GetString()!;
                try
                {
                    // Wie Chrome: der Klick bekommt den aktiven Tab mit
                    const string script = @"(async () => {
                        const tabs = await chrome.tabs.query({ active: true, lastFocusedWindow: true });
                        const tab = tabs[0] || (await chrome.tabs.query({}))[0] || { id: -1, index: 0, windowId: -1, active: true, pinned: false, highlighted: true, incognito: false };
                        chrome.action.onClicked.dispatch(tab);
                        return true;
                    })()";
                    string result = await core.CallDevToolsProtocolMethodForSessionAsync(session, "Runtime.evaluate",
                        JsonSerializer.Serialize(new { expression = script, awaitPromise = true, returnByValue = true }));
                    return !result.Contains("\"exceptionDetails\"", StringComparison.Ordinal);
                }
                finally
                {
                    try { await core.CallDevToolsProtocolMethodAsync("Target.detachFromTarget", JsonSerializer.Serialize(new { sessionId = session })); }
                    catch { /* Worker kann inzwischen beendet sein */ }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Klick auf Erweiterung {extensionId} konnte nicht ausgelöst werden", ex);
                return false;
            }
        }

        private static async Task<string?> FindServiceWorkerAsync(CoreWebView2 core, string extensionId)
        {
            string targets = await core.CallDevToolsProtocolMethodAsync("Target.getTargets", "{}");
            using var doc = JsonDocument.Parse(targets);
            string prefix = $"chrome-extension://{extensionId}/";
            return doc.RootElement.GetProperty("targetInfos").EnumerateArray()
                .Where(t => t.GetProperty("type").GetString() == "service_worker" &&
                            (t.GetProperty("url").GetString() ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(t => t.GetProperty("targetId").GetString())
                .FirstOrDefault();
        }
    }
}
