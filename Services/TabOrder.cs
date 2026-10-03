using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Reihenfolge der Tabs: angeheftete Tabs stehen vorne, die Tabs einer Gruppe stehen zusammen.
    /// Reine Funktionen über beliebige Typen, damit sie ohne WPF testbar sind.
    /// </summary>
    public static class TabOrder
    {
        /// <summary>
        /// Angeheftete Tabs nach vorne (in ihrer Reihenfolge), danach die übrigen. Jede Gruppe steht geschlossen
        /// an der Stelle ihres ersten Tabs – ein Tab, der eine Gruppe "zerteilt" hätte, landet dahinter.
        /// </summary>
        public static List<T> Normalize<T, TGroup>(IReadOnlyList<T> tabs, Func<T, TGroup?> groupOf, Func<T, bool> isPinned)
            where TGroup : class
        {
            var result = new List<T>(tabs.Count);
            foreach (var tab in tabs)
            {
                if (isPinned(tab)) result.Add(tab);
            }

            var emitted = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < tabs.Count; i++)
            {
                var tab = tabs[i];
                if (isPinned(tab)) continue;

                var group = groupOf(tab);
                if (group == null)
                {
                    result.Add(tab);
                }
                else if (emitted.Add(group))
                {
                    for (int j = i; j < tabs.Count; j++)
                    {
                        if (!isPinned(tabs[j]) && ReferenceEquals(groupOf(tabs[j]), group)) result.Add(tabs[j]);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Gruppe eines Tabs, nachdem er per Drag &amp; Drop zwischen <paramref name="left"/> und
        /// <paramref name="right"/> (Gruppen der Nachbarn) gelandet ist: mitten in einer Gruppe tritt er bei,
        /// ohne Nachbarn aus seiner bisherigen Gruppe verlässt er sie.
        /// </summary>
        public static TGroup? GroupAfterMove<TGroup>(TGroup? left, TGroup? right, TGroup? current) where TGroup : class
        {
            if (left != null && ReferenceEquals(left, right)) return left;
            if (current != null && !ReferenceEquals(left, current) && !ReferenceEquals(right, current)) return null;
            return current;
        }

        /// <summary>Inhalt der Tab-Leiste: vor dem ersten Tab jeder Gruppe steht ihr Kopf (einmal pro Gruppe).</summary>
        public static List<object> Compose<T, TGroup>(IReadOnlyList<T> tabs, Func<T, TGroup?> groupOf)
            where T : notnull
            where TGroup : class
        {
            var result = new List<object>(tabs.Count + 4);
            var emitted = new HashSet<TGroup>(ReferenceEqualityComparer.Instance);
            foreach (var tab in tabs)
            {
                var group = groupOf(tab);
                if (group != null && emitted.Add(group)) result.Add(group);
                result.Add(tab);
            }
            return result;
        }
    }

    public static class ListSync
    {
        /// <summary>
        /// Bringt <paramref name="target"/> mit möglichst wenigen Einfüge-, Verschiebe- und Löschschritten auf den
        /// Stand von <paramref name="desired"/>. Vorhandene Elemente bleiben erhalten – in einer ItemsControl also
        /// auch ihre Container (keine erneute Einblend-Animation, keine neu aufgebaute WebView).
        /// </summary>
        public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        {
            var wanted = new HashSet<T>(desired);
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!wanted.Contains(target[i])) target.RemoveAt(i);
            }

            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < desired.Count; i++)
            {
                if (i < target.Count && comparer.Equals(target[i], desired[i])) continue;

                int existing = -1;
                for (int j = i + 1; j < target.Count; j++)
                {
                    if (comparer.Equals(target[j], desired[i])) { existing = j; break; }
                }

                if (existing >= 0) target.Move(existing, i);
                else target.Insert(i, desired[i]);
            }

            while (target.Count > desired.Count) target.RemoveAt(target.Count - 1);
        }
    }
}
