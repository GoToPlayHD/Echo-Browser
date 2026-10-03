using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Einfaches Protokoll: eine Datei pro Tag unter %LOCALAPPDATA%\EchoBrowser\logs, die letzten 7 Tage bleiben erhalten.
    /// Schreibt zusätzlich in die Debug-Ausgabe. Fehler beim Schreiben werden ignoriert – Logging darf nie selbst abstürzen.
    /// </summary>
    public static class Log
    {
        private const int KeepDays = 7;
        private static readonly object Sync = new();
        private static bool _cleanedUp;

        public static void Info(string message) => Write("INFO", message, null);

        public static void Warn(string message, Exception? ex = null) => Write("WARN", message, ex);

        public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

        private static void Write(string level, string message, Exception? ex)
        {
            string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
            if (ex != null)
            {
                line += Environment.NewLine + ex;
            }

            Debug.WriteLine(line);

            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(AppPaths.LogFolder);
                    string file = Path.Combine(AppPaths.LogFolder, $"echo-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(file, line + Environment.NewLine);

                    if (!_cleanedUp)
                    {
                        _cleanedUp = true;
                        DeleteOldFiles();
                    }
                }
                catch
                {
                    // Protokoll nicht schreibbar – ignorieren
                }
            }
        }

        private static void DeleteOldFiles()
        {
            var oldFiles = new DirectoryInfo(AppPaths.LogFolder)
                .GetFiles("echo-*.log")
                .OrderByDescending(f => f.Name)
                .Skip(KeepDays);

            foreach (var file in oldFiles)
            {
                file.Delete();
            }
        }
    }
}
