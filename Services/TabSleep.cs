using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace EchoBrowser.Services
{
    public enum TabSleepAction { None, Sleep, Discard }

    /// <summary>Was der Schlaf-Timer über einen Tab wissen muss.</summary>
    public readonly record struct TabSleepState(
        bool IsVisible,
        bool HasWebView,
        bool IsSleeping,
        bool IsDiscarded,
        bool IsAudible,
        bool IsLoading,
        bool SleepRefused,
        DateTime LastActiveAt);

    /// <summary>
    /// Tab-Schlaf wie in Edge: inaktive Tabs werden nach einer einstellbaren Zeit eingefroren
    /// (<c>TrySuspendAsync</c>, kaum noch CPU und weniger Speicher) und nach deutlich längerer Zeit verworfen
    /// (WebView freigegeben; Adresse, Titel und Symbol bleiben, beim Aktivieren wird neu geladen).
    /// </summary>
    public static class TabSleepPolicy
    {
        /// <summary>Wählbare Zeiten in Minuten; 0 = nie.</summary>
        public static readonly IReadOnlyList<int> Choices = new[] { 5, 15, 30, 60, 120, 0 };

        public const int DefaultMinutes = 30;

        /// <summary>Verworfen wird nach der vierfachen Schlafzeit, frühestens nach zwei Stunden.</summary>
        public static TimeSpan DiscardAfter(int sleepMinutes) => TimeSpan.FromMinutes(Math.Max(sleepMinutes * 4, 120));

        public static TabSleepAction Decide(TabSleepState tab, DateTime now, int sleepMinutes)
        {
            if (sleepMinutes <= 0) return TabSleepAction.None;

            // Sichtbare Tabs, Tabs mit Ton und ladende Tabs bleiben wach; verworfene sind schon "tief im Schlaf"
            if (tab.IsVisible || tab.IsDiscarded || !tab.HasWebView || tab.IsAudible || tab.IsLoading) return TabSleepAction.None;

            TimeSpan idle = now - tab.LastActiveAt;
            if (idle >= DiscardAfter(sleepMinutes)) return TabSleepAction.Discard;
            if (idle >= TimeSpan.FromMinutes(sleepMinutes) && !tab.IsSleeping && !tab.SleepRefused) return TabSleepAction.Sleep;
            return TabSleepAction.None;
        }

        /// <summary>Darf ein Tab sofort (Knopf "Jetzt schlafen legen") schlafen, unabhängig von der Zeit?</summary>
        public static bool CanSleepNow(TabSleepState tab) =>
            !tab.IsVisible && tab.HasWebView && !tab.IsDiscarded && !tab.IsSleeping && !tab.IsAudible;
    }

    /// <summary>Ein WebView2-Prozess mit seinem Speicher und den Hauptframes (Tabs), deren Inhalte er rendert.</summary>
    public sealed record ProcessMemory(int ProcessId, long Bytes, IReadOnlyCollection<uint> MainFrameIds);

    public static class TabMemory
    {
        /// <summary>
        /// Speicher pro Tab: Jeder Renderer-Prozess wird zu gleichen Teilen auf die Tabs verteilt, deren Seiten
        /// er darstellt (Chromium legt Tabs derselben Website oft in einen Prozess). Gemeinsame Prozesse
        /// (Browser, GPU, Netzwerk …) und Frames ohne bekannten Tab zählen nicht zu einem Tab.
        /// </summary>
        public static Dictionary<string, long> Attribute(IEnumerable<ProcessMemory> renderers, IReadOnlyDictionary<uint, string> tabIdByFrameId)
        {
            var result = new Dictionary<string, long>();
            foreach (var process in renderers)
            {
                var tabIds = process.MainFrameIds
                    .Select(id => tabIdByFrameId.TryGetValue(id, out var tabId) ? tabId : null)
                    .OfType<string>()
                    .Distinct()
                    .ToList();
                if (tabIds.Count == 0) continue;

                long share = process.Bytes / tabIds.Count;
                foreach (var tabId in tabIds)
                {
                    result[tabId] = result.GetValueOrDefault(tabId) + share;
                }
            }
            return result;
        }
    }

    public static class MemoryFormat
    {
        /// <summary>"85 MB", "1,4 GB" – im Format der aktuellen Sprache.</summary>
        public static string Format(long bytes, IFormatProvider? culture = null)
        {
            culture ??= CultureInfo.CurrentCulture;
            double mb = bytes / 1024.0 / 1024.0;
            return mb >= 1024
                ? string.Format(culture, "{0:0.0} GB", mb / 1024)
                : string.Format(culture, "{0:0} MB", Math.Max(mb, bytes > 0 ? 1 : 0));
        }
    }
}
