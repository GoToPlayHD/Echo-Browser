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
    public class HistoryService
    {
        private readonly string _filePath;
        private const int MaxEntries = 1000;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        // Bündelt Schreibvorgänge: statt bei jedem Seitenaufruf die komplette Datei
        // synchron auf dem UI-Thread zu schreiben, wird kurz nach der letzten Änderung gespeichert.
        private readonly DispatcherTimer _saveTimer;
        private readonly object _writeLock = new();

        public ObservableCollection<HistoryItem> Entries { get; } = new();

        public HistoryService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "history.json");

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
            catch
            {
                // Fallback on corrupt file
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
                    System.Diagnostics.Debug.WriteLine($"[Echo] Verlauf konnte nicht gespeichert werden: {ex.Message}");
                }
            }
        }

        public void AddEntry(string? title, string? url)
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

            // Prevent duplicate entries for the exact same URL within 15 seconds
            var mostRecent = Entries.FirstOrDefault();
            if (mostRecent != null &&
                mostRecent.Url.Equals(cleanUrl, StringComparison.OrdinalIgnoreCase) &&
                (DateTime.Now - mostRecent.VisitedAt).TotalSeconds < 15)
            {
                // Update title if previously blank
                if (string.IsNullOrWhiteSpace(mostRecent.Title) || mostRecent.Title == cleanUrl)
                {
                    if (!string.IsNullOrWhiteSpace(title) && title != cleanUrl)
                    {
                        mostRecent.Title = title;
                        ScheduleSave();
                    }
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

        public void RemoveEntry(HistoryItem item)
        {
            if (Entries.Contains(item))
            {
                Entries.Remove(item);
                SaveHistory();
            }
        }

        public void ClearHistory()
        {
            Entries.Clear();
            SaveHistory();
        }
    }
}
