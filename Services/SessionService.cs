using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Threading;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>Lesen und Schreiben von session.json – ohne UI, damit es testbar ist.</summary>
    public static class SessionSerializer
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static string Serialize(SessionSnapshot snapshot) => JsonSerializer.Serialize(snapshot, JsonOptions);

        /// <summary>Liest das aktuelle Format und das alte Format (bis Oktober 2026: nur eine Liste von URLs).</summary>
        public static SessionSnapshot Deserialize(string json)
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var urls = doc.RootElement.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!)
                    .Where(IsRestorableUrl)
                    .ToList();

                var snapshot = new SessionSnapshot();
                if (urls.Count > 0)
                {
                    snapshot.Windows.Add(new SessionWindow { Tabs = urls.Select(u => new SessionTab { Url = u }).ToList() });
                }
                return snapshot;
            }

            var result = JsonSerializer.Deserialize<SessionSnapshot>(json) ?? new SessionSnapshot();
            foreach (var window in result.Windows)
            {
                window.Tabs.RemoveAll(t => !IsRestorableUrl(t.Url));
                window.ActiveIndex = Math.Clamp(window.ActiveIndex, 0, Math.Max(0, window.Tabs.Count - 1));
            }
            result.Windows.RemoveAll(w => w.Tabs.Count == 0);
            return result;
        }

        /// <summary>Nur echte Webadressen werden gespeichert – keine internen Seiten (echo://), about: oder data:.</summary>
        public static bool IsRestorableUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || InternalPages.IsInternalUrl(url)) return false;
            return !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                   !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Speichert die Sitzung laufend (kurz nach jeder Änderung), damit sie auch einen Absturz übersteht.
    /// Die Fenster liefern ihren Zustand über einen Collector, den die App beim Start setzt.
    /// Erkennt außerdem, ob der letzte Lauf unsauber beendet wurde (Marker-Datei).
    /// </summary>
    public sealed class SessionService
    {
        private static SessionService? _instance;
        public static SessionService Instance => _instance ??= new SessionService();

        private readonly string _sessionFile = AppPaths.File("session.json");
        private readonly string _runningMarker = AppPaths.File("running.lock");
        private readonly DispatcherTimer _saveTimer;
        private Func<IEnumerable<SessionWindow>>? _collector;
        private bool _frozen;

        /// <summary>True, wenn Echo beim letzten Mal nicht ordentlich beendet wurde (Absturz, Task-Manager, Stromausfall).</summary>
        public bool PreviousRunCrashed { get; }

        /// <summary>True, wenn Echo nach einem automatischen Update mit gespeicherter Sitzung neu gestartet wurde.</summary>
        public bool IsRestoredAfterUpdate { get; set; }

        private SessionService()
        {
            PreviousRunCrashed = File.Exists(_runningMarker);

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _saveTimer.Tick += (s, e) => SaveNow();
        }

        public void SetCollector(Func<IEnumerable<SessionWindow>> collector) => _collector = collector;

        /// <summary>Speichern kurz nach der letzten Änderung (Debounce).</summary>
        public void ScheduleSave()
        {
            if (_frozen) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void SaveNow()
        {
            _saveTimer.Stop();
            if (_frozen || _collector == null) return;

            try
            {
                var snapshot = new SessionSnapshot { Windows = _collector().Where(w => w.Tabs.Count > 0).ToList() };
                AtomicFile.WriteAllText(_sessionFile, SessionSerializer.Serialize(snapshot));
            }
            catch (Exception ex)
            {
                Log.Warn("Sitzung konnte nicht gespeichert werden", ex);
            }
        }

        /// <summary>
        /// Nach dem Speichern beim Schließen des letzten Fensters: weitere Änderungen (z.B. beim Abbau der Tabs)
        /// dürfen die gespeicherte Sitzung nicht mehr überschreiben.
        /// </summary>
        public void Freeze()
        {
            _saveTimer.Stop();
            _frozen = true;
        }

        /// <summary>Ein neues normales Fenster wurde geöffnet (z.B. aus einem Inkognito-Fenster) – wieder mitschreiben.</summary>
        public void Resume() => _frozen = false;

        public SessionSnapshot Load()
        {
            try
            {
                if (File.Exists(_sessionFile))
                {
                    return SessionSerializer.Deserialize(File.ReadAllText(_sessionFile));
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Sitzung konnte nicht gelesen werden", ex);
            }
            return new SessionSnapshot();
        }

        public void MarkRunning()
        {
            try { File.WriteAllText(_runningMarker, Environment.ProcessId.ToString()); }
            catch (Exception ex) { Log.Warn("Laufzeit-Marker konnte nicht angelegt werden", ex); }
        }

        public void MarkCleanExit()
        {
            try { File.Delete(_runningMarker); }
            catch (Exception ex) { Log.Warn("Laufzeit-Marker konnte nicht gelöscht werden", ex); }
        }
    }
}
