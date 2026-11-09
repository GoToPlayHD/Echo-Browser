using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Schreibt Dateien atomar im Hintergrund statt auf dem UI-Thread. Mehrere Aufträge für dieselbe Datei werden
    /// zusammengefasst (nur der neueste Inhalt wird geschrieben). Der Inhalt wird vom Aufrufer auf dem UI-Thread
    /// erzeugt – so sieht der Hintergrund nie halb geänderte Daten.
    /// Beim Beenden wartet <see cref="Flush"/>, bis alles auf der Platte ist.
    /// </summary>
    public static class BackgroundFileWriter
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, string> Pending = new(StringComparer.OrdinalIgnoreCase);
        private static Task _worker = Task.CompletedTask;
        private static bool _running;

        public static void Enqueue(string path, string contents)
        {
            lock (Sync)
            {
                Pending[path] = contents;
                if (_running) return;
                _running = true;
                _worker = Task.Run(Drain);
            }
        }

        /// <summary>Wartet, bis alle ausstehenden Dateien geschrieben sind (beim Beenden der App).</summary>
        public static void Flush(TimeSpan? timeout = null)
        {
            Task worker;
            lock (Sync) worker = _worker;
            try
            {
                worker.Wait(timeout ?? TimeSpan.FromSeconds(5));
            }
            catch (AggregateException)
            {
                // Fehler wurden bereits protokolliert
            }

            // Hängt der Hintergrund (z.B. Datei gesperrt), den Rest direkt schreiben
            WriteAll(TakePending());
        }

        private static void Drain()
        {
            while (true)
            {
                KeyValuePair<string, string>[] items;
                lock (Sync)
                {
                    if (Pending.Count == 0)
                    {
                        _running = false;
                        return;
                    }
                    items = Pending.ToArray();
                    Pending.Clear();
                }
                WriteAll(items);
            }
        }

        private static KeyValuePair<string, string>[] TakePending()
        {
            lock (Sync)
            {
                var items = Pending.ToArray();
                Pending.Clear();
                return items;
            }
        }

        private static void WriteAll(IEnumerable<KeyValuePair<string, string>> items)
        {
            foreach (var (path, contents) in items)
            {
                try
                {
                    AtomicFile.WriteAllText(path, contents);
                }
                catch (Exception ex)
                {
                    Log.Warn($"{System.IO.Path.GetFileName(path)} konnte nicht gespeichert werden", ex);
                }
            }
        }
    }
}
