using System;
using System.IO;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Ablageorte der Benutzerdaten unter %LOCALAPPDATA%\EchoBrowser. Mit der Umgebungsvariablen
    /// ECHO_USER_DATA_DIR lässt sich ein eigener Ordner wählen (wie --user-data-dir bei Chrome) –
    /// z.B. um einen Entwicklungsstand neben dem installierten Echo zu testen.
    /// </summary>
    public static class AppPaths
    {
        public const string DataFolderVariable = "ECHO_USER_DATA_DIR";

        public static string DataFolder { get; } = ResolveDataFolder(Environment.GetEnvironmentVariable(DataFolderVariable));

        /// <summary>True, wenn ein eigener Datenordner gewählt wurde.</summary>
        public static bool IsCustomDataFolder { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DataFolderVariable));

        public static string ResolveDataFolder(string? custom) =>
            string.IsNullOrWhiteSpace(custom)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EchoBrowser")
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(custom.Trim()));

        public static string LogFolder => Path.Combine(DataFolder, "logs");

        public static string WebViewDataFolder => Path.Combine(DataFolder, "WebView2Data");

        /// <summary>Datei im Datenordner; der Ordner wird bei Bedarf angelegt.</summary>
        public static string File(string fileName)
        {
            Directory.CreateDirectory(DataFolder);
            return Path.Combine(DataFolder, fileName);
        }
    }
}
