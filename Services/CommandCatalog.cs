using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace EchoBrowser.Services
{
    /// <summary>Ein Befehl des Browsers: Kennung, übersetzter Name und (optional) Tastenkürzel.</summary>
    public sealed record CommandDefinition(string Id, string TitleKey, Key Key = Key.None, ModifierKeys Modifiers = ModifierKeys.None)
    {
        public bool HasGesture => Key != Key.None;

        public bool Matches(Key key, ModifierKeys modifiers) => HasGesture && Key == key && Modifiers == modifiers;

        /// <summary>Kürzel zum Anzeigen, z.B. "Ctrl+Shift+T" (wie die übrigen Menüs von Echo).</summary>
        public string GestureText => HasGesture ? CommandCatalog.FormatGesture(Key, Modifiers) : "";
    }

    /// <summary>
    /// Alle Befehle mit ihren Haupt-Tastenkürzeln – die eine Quelle für Tastatursteuerung (MainWindow.Keyboard.cs)
    /// und Befehlspalette (Strg+K). Zusätzliche Varianten (F5, Strg+Tab …) stehen nur in der Tastatursteuerung.
    /// </summary>
    public static class CommandCatalog
    {
        public static readonly IReadOnlyList<CommandDefinition> All = new List<CommandDefinition>
        {
            // Tabs & Fenster
            new("newTab", "Cmd_NewTab", Key.T, ModifierKeys.Control),
            new("newWindow", "Cmd_NewWindow", Key.N, ModifierKeys.Control),
            new("newIncognitoWindow", "Cmd_NewIncognitoWindow", Key.N, ModifierKeys.Control | ModifierKeys.Shift),
            new("closeTab", "Cmd_CloseTab", Key.W, ModifierKeys.Control),
            new("reopenClosedTab", "Cmd_ReopenClosedTab", Key.T, ModifierKeys.Control | ModifierKeys.Shift),
            new("duplicateTab", "Cmd_DuplicateTab"),
            new("muteTab", "Cmd_MuteTab"),
            new("closeOtherTabs", "Cmd_CloseOtherTabs"),
            new("pinTab", "Cmd_PinTab"),
            new("splitView", "Cmd_SplitView"),
            new("groupTab", "Cmd_GroupTab"),
            new("commandPalette", "Cmd_CommandPalette", Key.K, ModifierKeys.Control),

            // Navigation
            new("reload", "Cmd_Reload", Key.R, ModifierKeys.Control),
            new("back", "Cmd_Back", Key.Left, ModifierKeys.Alt),
            new("forward", "Cmd_Forward", Key.Right, ModifierKeys.Alt),
            new("home", "Cmd_Home", Key.Home, ModifierKeys.Alt),
            new("focusAddressBar", "Cmd_FocusAddressBar", Key.L, ModifierKeys.Control),

            // Seite
            new("find", "Cmd_Find", Key.F, ModifierKeys.Control),
            new("print", "Cmd_Print", Key.P, ModifierKeys.Control),
            new("savePage", "Cmd_SavePage", Key.S, ModifierKeys.Control),
            new("viewSource", "Cmd_ViewSource", Key.U, ModifierKeys.Control),
            new("zoomIn", "Cmd_ZoomIn", Key.OemPlus, ModifierKeys.Control),
            new("zoomOut", "Cmd_ZoomOut", Key.OemMinus, ModifierKeys.Control),
            new("zoomReset", "Cmd_ZoomReset", Key.D0, ModifierKeys.Control),
            new("fullscreen", "Cmd_Fullscreen", Key.F11),
            new("devTools", "Cmd_DevTools", Key.F12),

            // Browser
            new("bookmarkPage", "Cmd_BookmarkPage", Key.D, ModifierKeys.Control),
            new("toggleBookmarksBar", "Cmd_ToggleBookmarksBar", Key.B, ModifierKeys.Control | ModifierKeys.Shift),
            new("toggleSidebar", "Cmd_ToggleSidebar"),
            new("toggleVerticalTabs", "Cmd_ToggleVerticalTabs"),
            new("downloads", "Cmd_Downloads", Key.J, ModifierKeys.Control),
            new("history", "Cmd_History", Key.H, ModifierKeys.Control),
            new("settings", "Cmd_Settings", Key.OemComma, ModifierKeys.Control),
            new("clearData", "Cmd_ClearData", Key.Delete, ModifierKeys.Control | ModifierKeys.Shift),
            new("toggleShield", "Cmd_ToggleShield"),
            new("performance", "Cmd_Performance"),
            new("sleepInactiveTabs", "Cmd_SleepInactiveTabs"),

            // Design
            new("themeSystem", "Cmd_ThemeSystem"),
            new("themeSilver", "Cmd_ThemeSilver"),
            new("themeMidnight", "Cmd_ThemeMidnight"),
            new("themeTitanium", "Cmd_ThemeTitanium"),
            new("themeCobalt", "Cmd_ThemeCobalt"),
        };

        /// <summary>Bereiche der Einstellungsseite (echo://settings#…), die die Befehlspalette direkt öffnen kann.</summary>
        public static readonly IReadOnlyList<(string Section, string TitleKey)> SettingsSections = new List<(string, string)>
        {
            ("section-general", "Settings_NavGeneral"),
            ("section-search", "Settings_NavSearch"),
            ("section-appearance", "Settings_NavAppearance"),
            ("section-privacy", "Settings_NavPrivacy"),
            ("section-downloads", "Settings_NavDownloads"),
            ("section-tabs", "Settings_NavTabs"),
            ("section-about", "Settings_NavAbout"),
        };

        public static CommandDefinition? Find(string id) => All.FirstOrDefault(c => c.Id == id);

        public static string FormatGesture(Key key, ModifierKeys modifiers)
        {
            var text = new StringBuilder();
            if (modifiers.HasFlag(ModifierKeys.Control)) text.Append("Ctrl+");
            if (modifiers.HasFlag(ModifierKeys.Shift)) text.Append("Shift+");
            if (modifiers.HasFlag(ModifierKeys.Alt)) text.Append("Alt+");
            text.Append(key switch
            {
                >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
                Key.OemPlus => "+",
                Key.OemMinus => "-",
                Key.OemComma => ",",
                Key.Left => "←",
                Key.Right => "→",
                Key.Delete => "Del",
                _ => key.ToString()
            });
            return text.ToString();
        }
    }

    /// <summary>
    /// Unscharfe Suche wie in VS Code/Arc: alle Zeichen der Eingabe müssen in dieser Reihenfolge vorkommen.
    /// Wortanfänge, zusammenhängende Treffer und Treffer am Anfang zählen mehr.
    /// </summary>
    public static class FuzzyMatcher
    {
        /// <summary>0 = kein Treffer, sonst je höher, desto besser.</summary>
        public static int Score(string query, string text)
        {
            string q = new string(query.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();
            if (q.Length == 0) return 1;
            string t = text.ToLowerInvariant();

            // Jede Fundstelle des ersten Zeichens als Start probieren: "tab" in "Einstellungen › Tabs"
            // soll beim Wort "Tabs" ansetzen und nicht beim ersten "t" mitten in "Einstellungen"
            int best = 0;
            for (int start = t.IndexOf(q[0]); start >= 0; start = t.IndexOf(q[0], start + 1))
            {
                best = Math.Max(best, ScoreFrom(q, t, start));
            }
            if (best == 0) return 0;

            if (t.StartsWith(q, StringComparison.Ordinal)) best += 30;
            best -= (t.Length - q.Length) / 4;                                      // kürzere Treffer bevorzugen
            return Math.Max(1, best);
        }

        private static int ScoreFrom(string q, string t, int start)
        {
            int qi = 0, score = 0, previous = -2;
            for (int ti = start; ti < t.Length && qi < q.Length; ti++)
            {
                if (t[ti] != q[qi]) continue;

                score += 10;
                if (ti == previous + 1) score += 8;                                  // zusammenhängend
                if (ti == 0 || !char.IsLetterOrDigit(t[ti - 1])) score += 12;        // Wortanfang
                previous = ti;
                qi++;
            }
            return qi < q.Length ? 0 : score;
        }

        /// <summary>
        /// Ein "starker" Treffer: die Zeichen liegen überwiegend an Wortanfängen oder hängen zusammen.
        /// Solche Befehle zeigt die Palette vor Tabs und Verlauf, verstreute Treffer erst danach.
        /// </summary>
        public static bool IsStrong(string query, int score) =>
            score > 0 && score >= 16 * query.Count(c => !char.IsWhiteSpace(c));
    }
}
