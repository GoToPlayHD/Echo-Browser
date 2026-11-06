using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Ablageorte der Benutzerdaten unter %LOCALAPPDATA%\EchoBrowserData. Mit der Umgebungsvariablen
    /// ECHO_USER_DATA_DIR lässt sich ein eigener Ordner wählen (wie --user-data-dir bei Chrome) –
    /// z.B. um einen Entwicklungsstand neben dem installierten Echo zu testen.
    /// </summary>
    /// <remarks>
    /// Nicht %LOCALAPPDATA%\EchoBrowser: Dorthin installiert Velopack das Programm (current\, packages\, Update.exe)
    /// und räumt den Ordner bei Updates und beim Deinstallieren auf – Daten darin gingen bei jedem Update verloren.
    /// </remarks>
    public static class AppPaths
    {
        public const string DataFolderVariable = "ECHO_USER_DATA_DIR";

        /// <summary>Ordnername der Benutzerdaten unter %LOCALAPPDATA%.</summary>
        public const string DataFolderName = "EchoBrowserData";

        /// <summary>Früherer Datenordner = Installationsordner von Velopack (packId "EchoBrowser").</summary>
        public const string LegacyDataFolderName = "EchoBrowser";

        public static string DataFolder { get; } = ResolveDataFolder(Environment.GetEnvironmentVariable(DataFolderVariable));

        /// <summary>True, wenn ein eigener Datenordner gewählt wurde.</summary>
        public static bool IsCustomDataFolder { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataFolderVariable));

        private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        public static string ResolveDataFolder(string? custom) =>
            string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(LocalAppData, DataFolderName)
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(custom.Trim()));

        public static string LogFolder => Path.Combine(DataFolder, "logs");

        public static string WebViewDataFolder => Path.Combine(DataFolder, "WebView2Data");

        /// <summary>Datei im Datenordner; der Ordner wird bei Bedarf angelegt.</summary>
        public static string File(string fileName)
        {
            Directory.CreateDirectory(DataFolder);
            return Path.Combine(DataFolder, fileName);
        }

        /// <summary>Ordner und Dateien, die Echo im Datenordner anlegt (alles andere gehört Velopack).</summary>
        private static readonly string[] DataFolders = { "WebView2Data", "Extensions", "Favicons", "logs" };

        private static bool IsDataFile(string name) =>
            name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            name.Contains(".json.", StringComparison.OrdinalIgnoreCase) ||   // *.json.migrated, *.json.corrupt-…, *.json.tmp
            name.Equals("blocklist.txt", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("running.lock", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Einmalig beim Start (bevor ein Dienst Dateien öffnet): Daten aus dem alten Ordner %LOCALAPPDATA%\EchoBrowser
        /// in den neuen Datenordner verschieben. Programmdateien von Velopack bleiben, wo sie sind.
        /// </summary>
        public static void MigrateLegacyDataFolder()
        {
            if (IsCustomDataFolder) return;
            MigrateLegacyDataFolder(Path.Combine(LocalAppData, LegacyDataFolderName), DataFolder);
        }

        /// <summary>Gibt die Anzahl verschobener Einträge zurück. Vorhandenes im Ziel wird nie überschrieben.</summary>
        internal static int MigrateLegacyDataFolder(string legacyFolder, string targetFolder)
        {
            if (!Directory.Exists(legacyFolder)) return 0;

            int moved = 0;
            var errors = new List<string>();
            Directory.CreateDirectory(targetFolder);

            foreach (string dir in DataFolders)
            {
                string source = Path.Combine(legacyFolder, dir);
                string target = Path.Combine(targetFolder, dir);
                if (!Directory.Exists(source) || Directory.Exists(target)) continue;
                try { Directory.Move(source, target); moved++; }
                catch (Exception ex) { errors.Add($"{dir}: {ex.Message}"); }
            }

            foreach (string file in Directory.GetFiles(legacyFolder).Where(f => IsDataFile(Path.GetFileName(f))))
            {
                string target = Path.Combine(targetFolder, Path.GetFileName(file));
                if (System.IO.File.Exists(target)) continue;
                try { System.IO.File.Move(file, target); moved++; }
                catch (Exception ex) { errors.Add($"{Path.GetFileName(file)}: {ex.Message}"); }
            }

            RewriteExtensionIndex(Path.Combine(targetFolder, "Extensions", "extensions-index.json"), legacyFolder, targetFolder);

            // Erst jetzt protokollieren – das Protokoll liegt selbst im (neuen) Datenordner
            if (moved > 0) Log.Info($"Benutzerdaten aus {legacyFolder} nach {targetFolder} verschoben ({moved} Einträge)");
            foreach (string error in errors) Log.Warn($"Datei konnte nicht in den neuen Datenordner verschoben werden: {error}");
            return moved;
        }

        /// <summary>Die Zuordnung Erweiterung → Ordner enthält absolute Pfade, die nach dem Verschieben angepasst werden.</summary>
        private static void RewriteExtensionIndex(string indexFile, string legacyFolder, string targetFolder)
        {
            try
            {
                if (!System.IO.File.Exists(indexFile)) return;
                var index = JsonSerializer.Deserialize<Dictionary<string, string>>(System.IO.File.ReadAllText(indexFile));
                if (index == null) return;

                string oldPrefix = Path.GetFullPath(legacyFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string newPrefix = Path.GetFullPath(targetFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                bool changed = false;
                foreach (var key in index.Keys.ToList())
                {
                    if (index[key].StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        index[key] = newPrefix + index[key][oldPrefix.Length..];
                        changed = true;
                    }
                }
                if (changed)
                {
                    AtomicFile.WriteAllText(indexFile, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Erweiterungs-Index konnte nicht angepasst werden", ex);
            }
        }
    }
}
