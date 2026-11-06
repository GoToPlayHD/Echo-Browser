using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Die eine WebView2-Umgebung der App, gemeinsam für alle Fenster.
    /// WebView2 verlangt, dass alle Umgebungen eines Browser-Prozesses mit identischen Scheme-Registrierungen
    /// erzeugt werden – deshalb wird sie genau einmal angelegt. Inkognito-Fenster nutzen dieselbe Umgebung
    /// mit einem InPrivate-Profil (siehe <see cref="CreateControllerOptions"/>).
    /// </summary>
    public static class BrowserEnvironment
    {
        private static Task<CoreWebView2Environment>? _creation;

        public static Task<CoreWebView2Environment> GetAsync() => _creation ??= CreateAsync();

        /// <summary>
        /// Nach einem Absturz des Browser-Prozesses ist die Umgebung unbrauchbar. Die nächste Anfrage
        /// erzeugt dann eine neue. Andere (bereits ersetzte) Umgebungen werden nicht angefasst.
        /// </summary>
        public static void Invalidate(CoreWebView2Environment failed)
        {
            if (_creation is { IsCompletedSuccessfully: true } creation && ReferenceEquals(creation.Result, failed))
            {
                _creation = null;
            }
        }

        /// <summary>Präfix der temporären Inkognito-Profile (Ordner "WV2Profile_incognito-…" unter EBWebView).</summary>
        private const string IncognitoProfilePrefix = "Incognito-";

        /// <summary>Temporäres Profil für Inkognito-Fenster mit Erweiterungen – eins pro Programmstart, von allen solchen Fenstern geteilt.</summary>
        public static readonly string IncognitoProfileName = IncognitoProfilePrefix + Guid.NewGuid().ToString("N")[..12];

        /// <summary>
        /// Optionen pro WebView: für Inkognito ein InPrivate-Profil (Daten nur im Speicher). Mit Erweiterungen ein
        /// eigenes temporäres Profil, denn im InPrivate-Profil sperrt WebView2 alle Erweiterungen (es gibt keine
        /// "Im Inkognito-Modus zulassen"-Schnittstelle). Es wird beim Schließen des letzten solchen Fensters gelöscht.
        /// </summary>
        public static CoreWebView2ControllerOptions? CreateControllerOptions(CoreWebView2Environment env, bool isIncognito, bool withExtensions = false)
        {
            if (!isIncognito) return null;

            var options = env.CreateCoreWebView2ControllerOptions();
            if (withExtensions)
            {
                options.ProfileName = IncognitoProfileName;
            }
            else
            {
                options.IsInPrivateModeEnabled = true;
            }
            return options;
        }

        /// <summary>
        /// Reste temporärer Inkognito-Profile löschen (z.B. nach einem Absturz). Nur vor dem Start des
        /// Browser-Prozesses aufrufen – danach sind die Ordner gesperrt.
        /// </summary>
        internal static void DeleteLeftoverIncognitoProfiles(string webViewDataFolder)
        {
            try
            {
                string root = Path.Combine(webViewDataFolder, "EBWebView");
                if (!Directory.Exists(root)) return;
                foreach (var dir in Directory.GetDirectories(root, "WV2Profile_" + IncognitoProfilePrefix.ToLowerInvariant() + "*"))
                {
                    try { Directory.Delete(dir, recursive: true); }
                    catch (Exception ex) { Log.Warn($"Altes Inkognito-Profil konnte nicht gelöscht werden: {Path.GetFileName(dir)}", ex); }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Alte Inkognito-Profile konnten nicht aufgeräumt werden", ex);
            }
        }

        private static async Task<CoreWebView2Environment> CreateAsync()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.WebViewDataFolder);
                DeleteLeftoverIncognitoProfiles(AppPaths.WebViewDataFolder);
                ExtensionService.Instance.ApplyCompatToInstalledExtensions();

                var echoScheme = new CoreWebView2CustomSchemeRegistration(InternalPages.Scheme)
                {
                    TreatAsSecure = true,
                    HasAuthorityComponent = true
                };

                var options = new CoreWebView2EnvironmentOptions(
                    customSchemeRegistrations: new List<CoreWebView2CustomSchemeRegistration> { echoScheme })
                {
                    AreBrowserExtensionsEnabled = true
                };

                return await CoreWebView2Environment.CreateAsync(null, AppPaths.WebViewDataFolder, options);
            }
            catch
            {
                // Beim nächsten Versuch neu probieren statt den Fehler zwischenzuspeichern
                _creation = null;
                throw;
            }
        }
    }
}
