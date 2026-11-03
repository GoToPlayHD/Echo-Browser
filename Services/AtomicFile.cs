using System.IO;
using System.Text;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Schreibt Dateien atomar: erst in eine temporäre Datei, dann per Move ersetzen.
    /// Stürzt der Browser während des Schreibens ab, bleibt die alte Datei intakt
    /// statt halb geschrieben (was sonst beim nächsten Start zu stillem Datenverlust führt).
    /// </summary>
    public static class AtomicFile
    {
        public static void WriteAllText(string path, string contents)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents, new UTF8Encoding(false));
            File.Move(tempPath, path, overwrite: true);
        }

        /// <summary>
        /// Unlesbare Datei beiseitelegen ("name.json.corrupt-20261103-142500"), statt sie beim nächsten Speichern
        /// still mit Standardwerten zu überschreiben – so lassen sich die Daten notfalls noch retten.
        /// </summary>
        public static void Quarantine(string path, System.Exception reason)
        {
            try
            {
                if (!File.Exists(path)) return;
                string backup = $"{path}.corrupt-{System.DateTime.Now:yyyyMMdd-HHmmss}";
                File.Move(path, backup, overwrite: true);
                Log.Warn($"{Path.GetFileName(path)} war unlesbar und wurde nach {Path.GetFileName(backup)} verschoben", reason);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"{Path.GetFileName(path)} konnte nicht beiseitegelegt werden", ex);
            }
        }
    }
}
