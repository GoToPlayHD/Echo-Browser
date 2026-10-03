using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Meldet Echo bei Windows als Browser an (nur für den aktuellen Benutzer, HKCU – keine Adminrechte nötig).
    /// Danach erscheint Echo unter Einstellungen → Apps → Standard-Apps; die Auswahl selbst trifft der Nutzer dort
    /// (Windows erlaubt Apps nicht, sich selbst zum Standard zu machen).
    /// </summary>
    public static class DefaultBrowserService
    {
        public const string AppName = "EchoBrowser";
        public const string UrlProgId = "EchoBrowserURL";
        public const string HtmlProgId = "EchoBrowserHTML";

        private const string ClientKey = @"Software\Clients\StartMenuInternet\" + AppName;
        private const string CapabilitiesKey = ClientKey + @"\Capabilities";

        private static readonly string[] UrlSchemes = { "http", "https" };
        private static readonly string[] FileTypes = { ".htm", ".html", ".shtml", ".xht", ".xhtml", ".svg", ".webp", ".pdf" };

        /// <summary>
        /// Dauerhafter Pfad zur EXE: bei einer Velopack-Installation der Starter im Installationsordner
        /// (der Ordner "current" wird bei Updates ersetzt), sonst die laufende EXE.
        /// </summary>
        public static string ExecutablePath
        {
            get
            {
                string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
                if (Path.GetFileName(baseDir).Equals("current", StringComparison.OrdinalIgnoreCase))
                {
                    string stub = Path.Combine(Path.GetDirectoryName(baseDir)!, AppName + ".exe");
                    if (File.Exists(stub)) return stub;
                }
                return Environment.ProcessPath ?? Path.Combine(baseDir, AppName + ".exe");
            }
        }

        /// <summary>Registrierung schreiben. <paramref name="root"/> ist für Tests austauschbar (Standard: HKCU).</summary>
        public static void Register(string exePath, RegistryKey? root = null)
        {
            root ??= Registry.CurrentUser;
            string open = $"\"{exePath}\" \"%1\"";
            string icon = $"\"{exePath}\",0";

            WriteProgId(root, UrlProgId, "Echo-Browser URL", icon, open, isUrlProtocol: true);
            WriteProgId(root, HtmlProgId, "Echo-Browser HTML", icon, open, isUrlProtocol: false);

            using (var client = root.CreateSubKey(ClientKey))
            {
                client.SetValue("", "Echo-Browser");
                using var defaultIcon = client.CreateSubKey("DefaultIcon");
                defaultIcon.SetValue("", icon);
                using var command = client.CreateSubKey(@"shell\open\command");
                command.SetValue("", $"\"{exePath}\"");
            }

            using (var capabilities = root.CreateSubKey(CapabilitiesKey))
            {
                capabilities.SetValue("ApplicationName", "Echo-Browser");
                capabilities.SetValue("ApplicationDescription", "Echo-Browser – schneller, privater Browser mit Echo Shield");
                capabilities.SetValue("ApplicationIcon", icon);

                using var startMenu = capabilities.CreateSubKey("StartMenu");
                startMenu.SetValue("StartMenuInternet", AppName);

                using var urls = capabilities.CreateSubKey("URLAssociations");
                foreach (string scheme in UrlSchemes) urls.SetValue(scheme, UrlProgId);

                using var files = capabilities.CreateSubKey("FileAssociations");
                foreach (string type in FileTypes) files.SetValue(type, HtmlProgId);
            }

            using (var registered = root.CreateSubKey(@"Software\RegisteredApplications"))
            {
                registered.SetValue(AppName, CapabilitiesKey);
            }

            if (ReferenceEquals(root, Registry.CurrentUser))
            {
                NotifyShell();
            }
        }

        /// <summary>Registrierung entfernen (bei der Deinstallation).</summary>
        public static void Unregister(RegistryKey? root = null)
        {
            root ??= Registry.CurrentUser;
            root.DeleteSubKeyTree(@"Software\Classes\" + UrlProgId, throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(@"Software\Classes\" + HtmlProgId, throwOnMissingSubKey: false);
            root.DeleteSubKeyTree(ClientKey, throwOnMissingSubKey: false);
            using (var registered = root.OpenSubKey(@"Software\RegisteredApplications", writable: true))
            {
                registered?.DeleteValue(AppName, throwOnMissingValue: false);
            }

            if (ReferenceEquals(root, Registry.CurrentUser))
            {
                NotifyShell();
            }
        }

        /// <summary>Ist Echo der Standardbrowser für https-Links?</summary>
        public static bool IsDefault()
        {
            try
            {
                using var choice = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
                return string.Equals(choice?.GetValue("ProgId") as string, UrlProgId, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Log.Warn("Standardbrowser konnte nicht ermittelt werden", ex);
                return false;
            }
        }

        /// <summary>Windows-Einstellungen "Standard-Apps" direkt bei Echo öffnen (Windows 11), sonst die Übersicht.</summary>
        public static void OpenDefaultAppsSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = $"ms-settings:defaultapps?registeredAppUser={AppName}", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warn("Standard-Apps-Einstellungen konnten nicht geöffnet werden", ex);
                Process.Start(new ProcessStartInfo { FileName = "ms-settings:defaultapps", UseShellExecute = true });
            }
        }

        private static void WriteProgId(RegistryKey root, string progId, string description, string icon, string open, bool isUrlProtocol)
        {
            using var key = root.CreateSubKey(@"Software\Classes\" + progId);
            key.SetValue("", description);
            if (isUrlProtocol) key.SetValue("URL Protocol", "");

            using (var application = key.CreateSubKey("Application"))
            {
                application.SetValue("ApplicationName", "Echo-Browser");
                application.SetValue("ApplicationIcon", icon);
            }
            using (var defaultIcon = key.CreateSubKey("DefaultIcon"))
            {
                defaultIcon.SetValue("", icon);
            }
            using var command = key.CreateSubKey(@"shell\open\command");
            command.SetValue("", open);
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        /// <summary>Explorer über geänderte Dateizuordnungen informieren.</summary>
        private static void NotifyShell() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
