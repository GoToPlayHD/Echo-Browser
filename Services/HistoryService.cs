using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Browserverlauf – eine Instanz für alle Fenster (muss auf dem UI-Thread erzeugt werden, wegen DispatcherTimer).
    /// </summary>
    public class HistoryService
    {
        private static HistoryService? _instance;
        public static HistoryService Instance => _instance ??= new HistoryService();

        private readonly string _filePath;
        private const int MaxEntries = 1000;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        // Bündelt Schreibvorgänge: statt bei jedem Seitenaufruf die komplette Datei
        // synchron auf dem UI-Thread zu schreiben, wird kurz nach der letzten Änderung gespeichert.
        private readonly DispatcherTimer _saveTimer;
        private readonly object _writeLock = new();

        public ObservableCollection<HistoryItem> Entries { get; } = new();

        private HistoryService()
        {
            _filePath = AppPaths.File("history.json");

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer.Stop();
                SaveHistoryInBackground();
            };

            LoadHistory();
        }

        public void LoadHistory()
        {
            Entries.Clear();
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var items = JsonSerializer.Deserialize<ObservableCollection<HistoryItem>>(json);
                    if (items != null)
                    {
                        var ordered = items.OrderByDescending(x => x.VisitedAt).Take(MaxEntries);
                        foreach (var item in ordered)
                        {
                            Entries.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AtomicFile.Quarantine(_filePath, ex);
            }
        }

        /// <summary>Plant ein verzögertes Speichern (Debounce).</summary>
        public void ScheduleSave()
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        /// <summary>Speichert sofort und synchron, z.B. beim Schließen des Fensters.</summary>
        public void SaveHistory()
        {
            _saveTimer.Stop();
            WriteSnapshot(Entries.ToList());
        }

        private void SaveHistoryInBackground()
        {
            // Snapshot auf dem UI-Thread ziehen, Serialisieren + Schreiben im Hintergrund
            var snapshot = Entries.ToList();
            _ = Task.Run(() => WriteSnapshot(snapshot));
        }

        private void WriteSnapshot(System.Collections.Generic.List<HistoryItem> snapshot)
        {
            lock (_writeLock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(snapshot, JsonOptions);
                    AtomicFile.WriteAllText(_filePath, json);
                }
                catch (Exception ex)
                {
                    Log.Warn("Verlauf konnte nicht gespeichert werden", ex);
                }
            }
        }

        /// <param name="titleChangeOnly">
        /// true, wenn nur der Seitentitel wechselte: ist die Adresse schon der letzte Eintrag, wird nur dessen Titel aktualisiert.
        /// </param>
        public void AddEntry(string? title, string? url, bool titleChangeOnly = false)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            string cleanUrl = url.Trim();

            // Ignore internal browser schemes
            if (cleanUrl.StartsWith("echo://", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                cleanUrl.StartsWith("chrome:", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Gleiche Adresse wie der letzte Eintrag: kein neuer Besuch, nur den Titel aktuell halten.
            // Bei einer Titeländerung der Seite ("(3) Posteingang") gilt das unabhängig vom Alter des Eintrags.
            var mostRecent = Entries.FirstOrDefault();
            if (mostRecent != null &&
                mostRecent.Url.Equals(cleanUrl, StringComparison.OrdinalIgnoreCase) &&
                (titleChangeOnly || (DateTime.Now - mostRecent.VisitedAt).TotalSeconds < 15))
            {
                string newTitle = title?.Trim() ?? "";
                if (newTitle.Length > 0 && newTitle != cleanUrl && newTitle != mostRecent.Title)
                {
                    mostRecent.Title = newTitle;
                    ScheduleSave();
                }
                return;
            }

            string cleanTitle = string.IsNullOrWhiteSpace(title) ? cleanUrl : title.Trim();
            var item = new HistoryItem(cleanTitle, cleanUrl);

            Entries.Insert(0, item);

            // Trim excess entries
            while (Entries.Count > MaxEntries)
            {
                Entries.RemoveAt(Entries.Count - 1);
            }

            ScheduleSave();
        }

        public void RemoveEntry(HistoryItem item) => RemoveEntries(new[] { item });

        /// <summary>Einträge löschen und einmal speichern (sofort – Löschen soll auch bei einem Absturz gelten).</summary>
        public void RemoveEntries(IEnumerable<HistoryItem> items)
        {
            bool removed = false;
            foreach (var item in items.ToList())
            {
                removed |= Entries.Remove(item);
            }
            if (removed) SaveHistory();
        }

        public void ClearHistory()
        {
            Entries.Clear();
            SaveHistory();
        }
    }
}
