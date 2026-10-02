using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using EchoBrowser.Models;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Eine eingehende Nachricht einer Seite (window.chrome.webview.postMessage) samt Kontext.
    /// Nur innerhalb des Handlers gültig – das zugrunde liegende JsonDocument wird danach freigegeben.
    /// </summary>
    public sealed class WebMessageContext
    {
        public WebMessageContext(string type, JsonElement root, BrowserTab tab, CoreWebView2 core, string? source)
        {
            Type = type;
            Root = root;
            Tab = tab;
            Core = core;
            Source = source;
        }

        public string Type { get; }
        public JsonElement Root { get; }
        public BrowserTab Tab { get; }
        public CoreWebView2 Core { get; }
        public string? Source { get; }

        public bool TryGet(string name, out JsonElement value) => Root.TryGetProperty(name, out value);

        public string? GetString(string name) =>
            Root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        public bool GetBool(string name) =>
            Root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.True;

        /// <summary>Ruft eine Callback-Funktion der Seite auf, falls sie existiert. Argumente werden als JSON übergeben.</summary>
        public Task CallPageAsync(string functionName, params object?[] args)
        {
            string json = JsonSerializer.Serialize(args);
            string argList = json.Substring(1, json.Length - 2); // [a,b] -> a,b
            return Core.ExecuteScriptAsync($"window.{functionName} && window.{functionName}({argList});");
        }
    }

    /// <summary>
    /// Verteilt Nachrichten der internen Seiten (Startseite, Einstellungen) und des Chrome Web Store
    /// an registrierte Handler. Übernimmt Parsing, Herkunftsprüfung und Fehlerbehandlung zentral.
    /// </summary>
    public sealed class WebMessageRouter
    {
        private readonly Dictionary<string, Func<WebMessageContext, Task>> _handlers = new(StringComparer.Ordinal);

        public void Register(string type, Func<WebMessageContext, Task> handler) => _handlers[type] = handler;

        public void Register(string type, Action<WebMessageContext> handler) =>
            _handlers[type] = ctx => { handler(ctx); return Task.CompletedTask; };

        public async Task HandleAsync(BrowserTab tab, CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs args)
        {
            string? source = args.Source;
            string type = "";

            try
            {
                using var doc = JsonDocument.Parse(args.WebMessageAsJson);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    !root.TryGetProperty("type", out var typeEl) ||
                    typeEl.ValueKind != JsonValueKind.String)
                {
                    return;
                }

                type = typeEl.GetString() ?? "";

                // Jede Webseite kann postMessage aufrufen: nur interne Seiten (mit Token)
                // und der Chrome Web Store (nur Installationsanfragen) werden akzeptiert.
                if (!InternalPageSecurity.IsTrustedInternalMessage(source, root) &&
                    !InternalPageSecurity.IsAllowedFromWebStore(source, type))
                {
                    Debug.WriteLine($"[Echo] WebMessage '{type}' von nicht vertrauenswürdiger Quelle verworfen: {source}");
                    return;
                }

                if (!_handlers.TryGetValue(type, out var handler))
                {
                    Debug.WriteLine($"[Echo] Unbekannter WebMessage-Typ: '{type}'");
                    return;
                }

                await handler(new WebMessageContext(type, root, tab, core, source));
            }
            catch (Exception ex)
            {
                // Fehler eines Handlers dürfen den Browser nicht abstürzen lassen
                Debug.WriteLine($"[Echo] Fehler beim Verarbeiten von WebMessage '{type}': {ex}");
            }
        }
    }
}
