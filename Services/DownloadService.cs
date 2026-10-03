using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Download-Liste aller Fenster (wie in Chrome eine gemeinsame Liste), gespeichert in downloads.json.
    /// Laufende Downloads, die beim Beenden nicht fertig waren, erscheinen beim nächsten Start als abgebrochen.
    /// </summary>
    public sealed class DownloadService
    {
        public const int MaxEntries = 100;

        private static DownloadService? _instance;
        public static DownloadService Instance => _instance ??= new DownloadService(AppPaths.File("downloads.json"));

        private readonly string _filePath;

        public ObservableCollection<DownloadItem> Items { get; } = new();

        /// <summary>Fortschritt aller laufenden Downloads hat sich geändert (für den Ring am Download-Knopf).</summary>
        public event Action? ProgressChanged;

        public DownloadService(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public void Add(DownloadItem item)
        {
            Items.Insert(0, item);
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(DownloadItem.BytesReceived) or nameof(DownloadItem.IsCompleted) or nameof(DownloadItem.IsCancelled) or nameof(DownloadItem.IsPaused))
                {
                    ProgressChanged?.Invoke();
                }
                if (e.PropertyName is nameof(DownloadItem.IsCompleted) or nameof(DownloadItem.IsCancelled))
                {
                    Save();
                }
            };
            Trim();
            Save();
            ProgressChanged?.Invoke();
        }

        public void Remove(DownloadItem item)
        {
            Items.Remove(item);
            Save();
            ProgressChanged?.Invoke();
        }

        /// <summary>Fertige und abgebrochene Einträge entfernen (laufende bleiben).</summary>
        public void ClearFinished()
        {
            foreach (var item in Items.Where(i => !i.IsInProgress).ToList())
            {
                Items.Remove(item);
            }
            Save();
        }

        /// <summary>Gesamtfortschritt der laufenden Downloads (0–1) oder null, wenn keiner läuft. -1 = Größe unbekannt.</summary>
        public double? OverallProgress()
        {
            var running = Items.Where(i => i.IsInProgress).ToList();
            if (running.Count == 0) return null;
            if (running.Any(i => !i.HasKnownSize)) return -1;

            long total = running.Sum(i => i.TotalBytes);
            return total > 0 ? Math.Clamp((double)running.Sum(i => i.BytesReceived) / total, 0, 1) : -1;
        }

        private void Trim()
        {
            while (Items.Count > MaxEntries)
            {
                var oldestFinished = Items.LastOrDefault(i => !i.IsInProgress);
                if (oldestFinished == null) break;
                Items.Remove(oldestFinished);
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                var stored = JsonSerializer.Deserialize<List<DownloadItem>>(File.ReadAllText(_filePath));
                if (stored == null) return;

                foreach (var item in stored.Take(MaxEntries))
                {
                    if (!item.IsCompleted) item.IsCancelled = true; // beim letzten Beenden unterbrochen
                    Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("downloads.json konnte nicht gelesen werden", ex);
            }
        }

        public void Save()
        {
            try
            {
                AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(Items.ToList()));
            }
            catch (Exception ex)
            {
                Log.Warn("downloads.json konnte nicht gespeichert werden", ex);
            }
        }
    }
}
