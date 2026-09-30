using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    public class HistoryService
    {
        private readonly string _filePath;
        private const int MaxEntries = 1000;

        public ObservableCollection<HistoryItem> Entries { get; } = new();

        public HistoryService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "EchoBrowser");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "history.json");
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

        public void SaveHistory()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Entries, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Suppress file write issues
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
                        SaveHistory();
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

            SaveHistory();
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
