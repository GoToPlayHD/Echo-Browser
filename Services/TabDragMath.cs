using System;
using System.Collections.Generic;
using System.Windows;

namespace EchoBrowser.Services
{
    /// <summary>Wohin ein gezogener Tab gerade zeigt.</summary>
    public enum TabDragZone
    {
        /// <summary>Noch unter der Schwelle – bisher nur ein Klick.</summary>
        None,
        /// <summary>Über der eigenen Tab-Leiste: Reihenfolge ändern.</summary>
        Reorder,
        /// <summary>Über der Tab-Leiste eines anderen (passenden) Fensters: dort einfügen.</summary>
        Dock,
        /// <summary>Irgendwo sonst: beim Loslassen ein neues Fenster an dieser Stelle.</summary>
        Detach
    }

    /// <summary>
    /// Reine Rechenregeln für das Ziehen von Tabs (MainWindow.TabDrag.cs) – ohne WPF-Zustand, damit testbar.
    /// Alle Bildschirmwerte sind physische Pixel (GetCursorPos/PointToScreen); so springt nichts zwischen
    /// Monitoren mit verschiedener Skalierung.
    /// </summary>
    public static class TabDragMath
    {
        /// <summary>
        /// Wie weit der Mauszeiger die Tab-Leiste quer zur Leiste verlassen darf, bevor der Tab herausgelöst wird
        /// (geräteunabhängige Pixel, ungefähr eine Tab-Höhe wie bei Chrome).
        /// </summary>
        public const double DetachMargin = 36;

        /// <summary>So viel vom neuen Fenster (physische Pixel bei 100 %) bleibt mindestens auf dem Monitor sichtbar.</summary>
        public const double MinVisibleWindowPart = 96;

        /// <summary>Erst ab dieser Bewegung ist es ein Ziehen und kein Klick (Systemwerte, in Pixel umgerechnet).</summary>
        public static bool ExceedsThreshold(Vector delta, double thresholdX, double thresholdY) =>
            Math.Abs(delta.X) > thresholdX || Math.Abs(delta.Y) > thresholdY;

        /// <summary>
        /// Liegt ein Punkt (relativ zur Tab-Leiste) noch in ihrem Bereich? Längs der Leiste zählt ihre ganze Länge,
        /// quer dazu zusätzlich <paramref name="margin"/> – so löst ein leicht schräges Ziehen den Tab nicht gleich heraus.
        /// </summary>
        public static bool IsWithinStrip(Point local, Size strip, bool vertical, double margin)
        {
            if (strip.Width <= 0 || strip.Height <= 0) return false;

            return vertical
                ? local.Y >= 0 && local.Y <= strip.Height && local.X >= -margin && local.X <= strip.Width + margin
                : local.X >= 0 && local.X <= strip.Width && local.Y >= -margin && local.Y <= strip.Height + margin;
        }

        /// <summary>
        /// Einfügeposition zwischen den übrigen Tabs: Anzahl der Tab-Mitten vor dem Mauszeiger.
        /// Stabil beim Verschieben – der gezogene Tab ist in <paramref name="centers"/> nicht enthalten.
        /// </summary>
        public static int InsertionIndex(IReadOnlyList<double> centers, double position)
        {
            int index = 0;
            while (index < centers.Count && centers[index] < position) index++;
            return index;
        }

        /// <summary>Angeheftete Tabs stehen vorne und bleiben unter sich – die Position entsprechend begrenzen.</summary>
        /// <param name="index">Position in der Liste ohne den gezogenen Tab.</param>
        /// <param name="pinnedCount">Angeheftete Tabs in dieser Liste.</param>
        /// <param name="count">Länge dieser Liste.</param>
        public static int ClampToPinnedRange(int index, int pinnedCount, int count, bool isPinned) =>
            isPinned ? Math.Clamp(index, 0, pinnedCount) : Math.Clamp(index, pinnedCount, Math.Max(pinnedCount, count));

        /// <summary>
        /// Lage eines neuen Fensters: der gezogene Tab liegt wieder unter dem Mauszeiger (<paramref name="grabOffset"/> =
        /// Abstand der Fensterecke zum Zeiger), begrenzt auf den Arbeitsbereich des Monitors, damit die Titelzeile
        /// erreichbar bleibt. Alle Werte in physischen Pixeln.
        /// </summary>
        public static Rect PlaceWindow(Point cursor, Vector grabOffset, Size size, Rect workArea, double minVisible)
        {
            double width = Math.Min(size.Width, workArea.Width);
            double height = Math.Min(size.Height, workArea.Height);
            double visible = Math.Min(minVisible, width);

            double left = cursor.X - grabOffset.X;
            double top = cursor.Y - grabOffset.Y;

            left = Math.Clamp(left, workArea.Left - width + visible, workArea.Right - visible);
            // Oben nie über den Rand, sonst ist die Titelzeile weg; unten mindestens die Tab-Leiste sichtbar
            top = Math.Clamp(top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - visible));

            return new Rect(Math.Round(left), Math.Round(top), Math.Round(width), Math.Round(height));
        }
    }
}
