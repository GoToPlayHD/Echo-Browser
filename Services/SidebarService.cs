using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using EchoBrowser.Models;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Die Seitenleiste zeigt dieselben Lesezeichen wie die Lesezeichenleiste (<see cref="BookmarkService"/>).
    /// Früher hatte sie eine eigene Liste (sidebar_favorites.json) – diese wird einmalig in die Lesezeichen
    /// übernommen und danach als "sidebar_favorites.json.migrated" aufbewahrt.
    /// </summary>
    public static class SidebarService
    {
        internal static string LegacyFilePath => AppPaths.File("sidebar_favorites.json");

        public static void MigrateInto(BookmarkService bookmarks) => MigrateInto(bookmarks.MergeSidebarFavorites, LegacyFilePath);

        internal static void MigrateInto(Func<IEnumerable<SidebarFavorite>, int> merge, string legacyFile)
        {
            if (!File.Exists(legacyFile)) return;

            try
            {
                var favorites = JsonSerializer.Deserialize<List<SidebarFavorite>>(File.ReadAllText(legacyFile)) ?? new();
                int added = merge(favorites);
                File.Move(legacyFile, legacyFile + ".migrated", overwrite: true);
                Log.Info($"Seitenleisten-Favoriten in die Lesezeichen übernommen ({added} neu)");
            }
            catch (Exception ex)
            {
                AtomicFile.Quarantine(legacyFile, ex);
            }
        }
    }
}
