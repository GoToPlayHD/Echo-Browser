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
    }
}
