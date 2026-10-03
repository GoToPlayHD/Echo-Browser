using System;
using System.Collections.Generic;
using System.IO;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Adressen aus der Kommandozeile, z.B. wenn Windows einen Link oder eine HTML-Datei mit Echo öffnet.
    /// Schalter ("-x", "--veloapp-…") werden übersprungen, gefährliche Schemata (javascript:, data:, …) verworfen.
    /// </summary>
    public static class CommandLineParser
    {
        private static readonly string[] AllowedSchemes = { "http", "https", "file", InternalPages.Scheme };

        public static List<string> GetUrls(IEnumerable<string>? args)
        {
            var urls = new List<string>();
            if (args == null) return urls;

            foreach (string raw in args)
            {
                string arg = raw.Trim().Trim('"');
                if (arg.Length == 0 || arg.StartsWith('-')) continue;

                if (Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                {
                    if (Array.Exists(AllowedSchemes, s => s.Equals(uri.Scheme, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Windows-Pfade wie C:\seite.html werden hier zu file:///C:/seite.html
                        urls.Add(uri.IsFile ? uri.AbsoluteUri : arg);
                    }
                    continue;
                }

                // Relative Dateipfade, z.B. "seite.html" im aktuellen Ordner
                if (File.Exists(arg))
                {
                    urls.Add(new Uri(Path.GetFullPath(arg)).AbsoluteUri);
                }
            }

            return urls;
        }
    }
}
