using System;
using System.Text.Json;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Theme in die Webinhalte tragen: interne Seiten (echo://) übernehmen die Farben live,
    /// Webseiten erhalten über prefers-color-scheme hell bzw. dunkel passend zum Browser.
    /// </summary>
    public partial class MainWindow
    {
        private void InitializeThemeSync()
        {
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            Closed += (s, e) => ThemeManager.Instance.ThemeChanged -= OnThemeChanged;
        }

        private void OnThemeChanged(ThemePreset preset)
        {
            Dispatcher.BeginInvoke(() =>
            {
                ApplyThemeToWebContent();
                ApplyWindowEffects();
                foreach (var favorite in SidebarFavorites)
                {
                    favorite.RefreshThemeColors();
                }
            });
        }

        private void ApplyThemeToWebContent()
        {
            string css = ThemeManager.Instance.GetCssVariables();
            string script = "(() => { const s = document.getElementById('echo-theme'); if (s) s.textContent = " + JsonSerializer.Serialize(css) + "; })();";

            foreach (var tab in Tabs)
            {
                var core = tab.WebView?.CoreWebView2;
                if (core == null) continue;

                ApplyColorScheme(core);
                if (InternalPages.IsInternalUrl(core.Source))
                {
                    _ = core.ExecuteScriptAsync(script);
                }
            }
        }

        /// <summary>Webseiten mit hellem/dunklem Design (prefers-color-scheme) folgen dem Echo-Theme.</summary>
        private static void ApplyColorScheme(CoreWebView2 core)
        {
            try
            {
                core.Profile.PreferredColorScheme = ThemeManager.Instance.IsLight
                    ? CoreWebView2PreferredColorScheme.Light
                    : CoreWebView2PreferredColorScheme.Dark;
            }
            catch (Exception ex)
            {
                Log.Warn("Farbschema für Webseiten konnte nicht gesetzt werden", ex);
            }
        }
    }
}
