using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Gleicht Lücken der Erweiterungs-Schnittstellen von WebView2 aus, indem Echo in seine eigene, entpackte Kopie
    /// einer Erweiterung eine kleine Kompatibilitätsschicht vor den Hintergrund-Code (Service Worker) setzt.
    /// <para>
    /// Konkret: chrome.tabs.create/update liefern in WebView2 kein Tab-Objekt zurück, obwohl der Tab entsteht.
    /// Erweiterungen wie WEB.DE MailCheck merken sich aber tab.id, um danach in genau diesen Tab zu schreiben
    /// (Anmeldung im Postfach) – ohne ID bleibt die Seite leer. Die Schicht sucht den neuen Tab und gibt ihn zurück.
    /// </para>
    /// Ordner außerhalb von Echos Erweiterungsordner (vom Nutzer selbst geladene) werden nie verändert.
    /// </summary>
    public static class ExtensionCompat
    {
        internal const string CompatFileName = "echo-compat.js";
        internal const string WrapperFileName = "echo-service-worker.js";

        /// <summary>
        /// Merker: WebView2 hat den Service Worker mit dem Wrapper registriert. Fehlt er, muss die Erweiterung einmal
        /// aus- und wieder eingeschaltet werden – WebView2 übernimmt einen geänderten Service Worker einer bereits
        /// installierten Erweiterung beim Start nicht, und ohne Hintergrund laden Popups wie 1Password oder LastPass nie.
        /// </summary>
        internal const string RegisteredMarkerFileName = "echo-service-worker.registered";

        /// <summary>Kompatibilitätsschicht (läuft im Service Worker vor dem Code der Erweiterung).</summary>
        internal const string CompatScript = @"// Von Echo-Browser eingefügt: WebView2 liefert bei chrome.tabs.create/update kein Tab-Objekt zurück.
// Diese Schicht sucht den betroffenen Tab und gibt ihn wie Chrome zurück (Callback und Promise).
(() => {
  const tabs = self.chrome && chrome.tabs;
  if (!tabs || tabs.__echoCompat) return;
  const sleep = ms => new Promise(r => setTimeout(r, ms));
  const allTabs = () => new Promise(r => tabs.query({}, t => r(chrome.runtime.lastError ? [] : (t || []))));

  async function findCreated(before, props) {
    for (let i = 0; i < 50; i++) {
      const fresh = (await allTabs()).filter(t => !before.has(t.id));
      if (fresh.length) {
        const url = props && props.url;
        return fresh.find(t => url && (t.pendingUrl === url || t.url === url)) || fresh[fresh.length - 1];
      }
      await sleep(100);
    }
    return undefined;
  }

  function wrap(name, resolveMissing) {
    const original = tabs[name] && tabs[name].bind(tabs);
    if (!original) return;
    tabs[name] = function (...args) {
      const callback = typeof args[args.length - 1] === 'function' ? args.pop() : null;
      const promise = (async () => {
        const context = await resolveMissing.prepare(args);
        const result = await new Promise((resolve, reject) => original(...args, value => {
          const error = chrome.runtime.lastError;
          error ? reject(new Error(error.message)) : resolve(value);
        }));
        return result && result.id !== undefined ? result : await resolveMissing.find(args, context);
      })();
      if (!callback) return promise;
      promise.then(value => callback(value), () => callback(undefined));
    };
  }

  wrap('create', {
    prepare: async () => new Set((await allTabs()).map(t => t.id)),
    find: (args, before) => findCreated(before, typeof args[0] === 'object' ? args[0] : {})
  });
  wrap('update', {
    prepare: async () => null,
    find: async args => {
      const id = typeof args[0] === 'number' ? args[0] : undefined;
      if (id !== undefined) return new Promise(r => tabs.get(id, t => r(chrome.runtime.lastError ? undefined : t)));
      const [active] = await new Promise(r => tabs.query({ active: true, lastFocusedWindow: true }, r));
      return active;
    }
  });
  tabs.__echoCompat = true;
})();
";

        /// <summary>
        /// Kompatibilitätsschicht in die entpackte Kopie einer Erweiterung einbauen (oder aktualisieren).
        /// Ohne Service Worker (Manifest V2, reine Inhaltsskripte) passiert nichts. Gibt true zurück, wenn sie aktiv ist.
        /// </summary>
        public static bool Apply(string extensionFolder)
        {
            try
            {
                string manifestPath = Path.Combine(extensionFolder, "manifest.json");
                if (!File.Exists(manifestPath)) return false;

                var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                var manifest = JsonNode.Parse(File.ReadAllText(manifestPath), documentOptions: options)?.AsObject();
                if (manifest?["background"] is not JsonObject background ||
                    background["service_worker"]?.GetValue<string>() is not { Length: > 0 } worker)
                {
                    return false;
                }

                bool isModule = background["type"]?.GetValue<string>() == "module";
                string compatPath = Path.Combine(extensionFolder, CompatFileName);
                if (!File.Exists(compatPath) || File.ReadAllText(compatPath) != CompatScript)
                {
                    File.WriteAllText(compatPath, CompatScript);
                }

                // Schon umgestellt: nur die Schicht aktualisieren (der Verweis auf das Original steht im Wrapper)
                if (worker == WrapperFileName) return true;

                string original = worker.TrimStart('/');
                string wrapper = isModule
                    ? $"// Von Echo-Browser eingefügt: Kompatibilitätsschicht vor dem eigentlichen Service Worker laden.\nimport './{CompatFileName}';\nimport './{original}';\n"
                    : $"// Von Echo-Browser eingefügt: Kompatibilitätsschicht vor dem eigentlichen Service Worker laden.\nimportScripts('{CompatFileName}', '{original}');\n";
                File.WriteAllText(Path.Combine(extensionFolder, WrapperFileName), wrapper);

                background["service_worker"] = WrapperFileName;
                File.Delete(Path.Combine(extensionFolder, RegisteredMarkerFileName));
                File.WriteAllText(manifestPath, manifest.ToJsonString(new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                }));
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn($"Kompatibilitätsschicht konnte nicht eingebaut werden: {extensionFolder}", ex);
                return false;
            }
        }

        /// <summary>True, wenn der Service Worker der Erweiterung seit dem Einbau des Wrappers nicht neu registriert wurde.</summary>
        public static bool NeedsServiceWorkerRefresh(string extensionFolder) =>
            File.Exists(Path.Combine(extensionFolder, WrapperFileName)) &&
            !File.Exists(Path.Combine(extensionFolder, RegisteredMarkerFileName));

        /// <summary>Merken, dass WebView2 den Service Worker mit dem Wrapper registriert hat.</summary>
        public static void MarkServiceWorkerRegistered(string extensionFolder)
        {
            try
            {
                if (File.Exists(Path.Combine(extensionFolder, WrapperFileName)))
                {
                    File.WriteAllText(Path.Combine(extensionFolder, RegisteredMarkerFileName), "");
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Merker für den Service Worker konnte nicht geschrieben werden: {extensionFolder}", ex);
            }
        }
    }
}
