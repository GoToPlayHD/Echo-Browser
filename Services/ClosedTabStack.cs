using System;
using System.Collections.Generic;

namespace EchoBrowser.Services
{
    /// <summary>Ein geschlossener Tab, der mit Strg+Umschalt+T wieder geöffnet werden kann.</summary>
    public sealed record ClosedTab(string Url, string Title, int Index);

    /// <summary>Zuletzt geschlossene Tabs eines Fensters (die neuesten zuerst wieder heraus).</summary>
    public sealed class ClosedTabStack
    {
        public const int Capacity = 25;

        private readonly List<ClosedTab> _items = new();

        public int Count => _items.Count;

        public void Push(string? url, string? title, int index)
        {
            if (!IsReopenable(url)) return;

            _items.Add(new ClosedTab(url!, title ?? url!, Math.Max(0, index)));
            if (_items.Count > Capacity)
            {
                _items.RemoveAt(0);
            }
        }

        public bool TryPop(out ClosedTab? tab)
        {
            if (_items.Count == 0)
            {
                tab = null;
                return false;
            }

            tab = _items[^1];
            _items.RemoveAt(_items.Count - 1);
            return true;
        }

        /// <summary>Leere Startseiten lohnen das Wiederherstellen nicht.</summary>
        public static bool IsReopenable(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            if (InternalPages.Is(url, InternalPages.Start) || InternalPages.Is(url, InternalPages.NewTab)) return false;
            return !url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                   !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
        }
    }
}
