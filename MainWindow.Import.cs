using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Einstellungsseite: Standardbrowser festlegen, Lesezeichen importieren/exportieren,
    /// Passwörter speichern und Formulare automatisch ausfüllen (eingebaute WebView2-Funktionen).
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>Passwort-Speicher und Formular-Autofill des Profils an die Einstellungen anpassen.</summary>
        private static void ApplyAutofillSettings(CoreWebView2 core)
        {
            var settings = AppSettingsService.Instance.Settings;
            try
            {
                core.Profile.IsPasswordAutosaveEnabled = settings.SavePasswords;
                core.Profile.IsGeneralAutofillEnabled = settings.AutofillForms;
            }
            catch (Exception ex)
            {
                Log.Warn("Passwort-/Autofill-Einstellung konnte nicht übernommen werden", ex);
            }
        }

        /// <summary>Echo bei Windows anmelden und die Standard-Apps-Einstellungen öffnen (dort wählt der Nutzer Echo).</summary>
        private void OnMakeDefaultBrowserMessage(WebMessageContext ctx)
        {
            try
            {
                DefaultBrowserService.Register(DefaultBrowserService.ExecutablePath);
                DefaultBrowserService.OpenDefaultAppsSettings();
            }
            catch (Exception ex)
            {
                Log.Error("Registrierung als Standardbrowser fehlgeschlagen", ex);
            }
        }

        private async System.Threading.Tasks.Task OnImportBookmarksMessage(WebMessageContext ctx)
        {
            string source = ctx.GetString("source") ?? "";
            string message;

            try
            {
                List<ImportedBookmark> items;
                string sourceName;

                if (source == "html")
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = Tr.Get("Bookmarks_ImportFileTitle"),
                        Filter = Tr.Get("Bookmarks_HtmlFilter")
                    };
                    if (dialog.ShowDialog(this) != true) return;

                    items = BookmarkImportService.ParseNetscapeHtml(File.ReadAllText(dialog.FileName));
                    sourceName = Path.GetFileNameWithoutExtension(dialog.FileName);
                }
                else
                {
                    sourceName = source switch { "edge" => "Edge", "brave" => "Brave", _ => "Chrome" };
                    string? file = BookmarkImportService.ChromiumBookmarksPath(source);
                    if (file == null)
                    {
                        await ctx.CallPageAsync("onBookmarksImported", Tr.Format("Settings_ToastImportNotFound", sourceName));
                        return;
                    }
                    items = BookmarkImportService.ParseChromium(File.ReadAllText(file));
                }

                int added = _bookmarkService.ImportAsGroup(Tr.Format("Import_GroupName", sourceName), items);
                message = added > 0 ? Tr.Format("Settings_ToastImported", added) : Tr.Get("Settings_ToastImportNone");
            }
            catch (Exception ex)
            {
                Log.Warn($"Lesezeichen-Import aus '{source}' fehlgeschlagen", ex);
                message = Tr.Format("Settings_ToastImportFailed", ex.Message);
            }

            await ctx.CallPageAsync("onBookmarksImported", message);
        }

        private async System.Threading.Tasks.Task OnChooseStartpageBackgroundMessage(WebMessageContext ctx)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Tr.Get("Settings_ChooseImage"),
                Filter = Tr.Get("Settings_ImageFilter")
            };
            if (dialog.ShowDialog(this) != true) return;

            await SetStartpageBackgroundAsync(ctx, dialog.FileName);
        }

        private async System.Threading.Tasks.Task OnClearStartpageBackgroundMessage(WebMessageContext ctx) =>
            await SetStartpageBackgroundAsync(ctx, "");

        /// <summary>Hintergrundbild speichern und offene Startseiten in allen Fenstern neu aufbauen.</summary>
        private async System.Threading.Tasks.Task SetStartpageBackgroundAsync(WebMessageContext ctx, string path)
        {
            var settings = AppSettingsService.Instance.Settings;
            settings.StartpageBackgroundPath = path;
            AppSettingsService.Instance.Save();

            foreach (var window in System.Windows.Application.Current.Windows.OfType<MainWindow>())
            {
                foreach (var tab in window.Tabs.Where(t => IsStartPage(t.Url)))
                {
                    window.RefreshInternalPage(tab);
                }
            }

            await ctx.CallPageAsync("onStartBackgroundChanged", SettingsPageService.StartBackgroundStatus(settings));
        }

        private async System.Threading.Tasks.Task OnExportBookmarksMessage(WebMessageContext ctx)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = Tr.Get("Bookmarks_ExportFileTitle"),
                Filter = Tr.Get("Bookmarks_HtmlFilter"),
                FileName = $"echo-lesezeichen-{DateTime.Now:yyyy-MM-dd}.html"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                File.WriteAllText(dialog.FileName, BookmarkImportService.ToNetscapeHtml(_bookmarkService.Bookmarks), new UTF8Encoding(false));
                await ctx.CallPageAsync("onBookmarksExported", Tr.Get("Settings_ToastExported"));
            }
            catch (Exception ex)
            {
                Log.Warn("Lesezeichen-Export fehlgeschlagen", ex);
                await ctx.CallPageAsync("onBookmarksExported", Tr.Format("Settings_ToastImportFailed", ex.Message));
            }
        }
    }
}
