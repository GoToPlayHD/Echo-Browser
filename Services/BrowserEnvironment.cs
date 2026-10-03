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

        /// <summary>Optionen pro WebView: für Inkognito ein InPrivate-Profil (Daten nur im Speicher).</summary>
        public static CoreWebView2ControllerOptions? CreateControllerOptions(CoreWebView2Environment env, bool isIncognito)
        {
            if (!isIncognito) return null;

            var options = env.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            return options;
        }

        private static async Task<CoreWebView2Environment> CreateAsync()
        {
            try
            {
                Directory.CreateDirectory(AppPaths.WebViewDataFolder);

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
