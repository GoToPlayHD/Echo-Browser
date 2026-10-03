using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace EchoBrowser.Services
{
    public enum ThemePreset
    {
        SilverAnthracite,
        MidnightOled,
        TitaniumLight,
        CobaltSlate,
        /// <summary>Folgt dem hellen bzw. dunklen Modus von Windows (Titanium Light bzw. Silber &amp; Anthrazit).</summary>
        System
    }

    public class ThemeManager
    {
        private static ThemeManager? _instance;
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        private const string DefaultAccentHex = "#C4C7CC";

        /// <summary>Gewähltes Preset (kann "System" sein).</summary>
        public ThemePreset CurrentPreset { get; private set; } = ThemePreset.SilverAnthracite;

        /// <summary>Tatsächlich angezeigtes Preset ("System" aufgelöst).</summary>
        public ThemePreset EffectivePreset => CurrentPreset == ThemePreset.System
            ? (WindowsUsesLightTheme() ? ThemePreset.TitaniumLight : ThemePreset.SilverAnthracite)
            : CurrentPreset;

        /// <summary>Helles Design aktiv – für Webseiten (prefers-color-scheme) und die internen Seiten.</summary>
        public bool IsLight => EffectivePreset == ThemePreset.TitaniumLight;

        /// <summary>Preset oder Akzentfarbe hat sich geändert (auch durch Umschalten des Windows-Modus).</summary>
        public event Action<ThemePreset>? ThemeChanged;

        private Color? _customAccent;
        private bool _followsSystem;

        private ThemeManager() { }

        public void ApplyPreset(ThemePreset preset)
        {
            CurrentPreset = preset;
            foreach (var kvp in GetPaletteForPreset(EffectivePreset))
            {
                SetResourceColor(kvp.Key, kvp.Value);
            }

            // Eigene Akzentfarbe gilt für alle Presets
            if (_customAccent is { } accent)
            {
                ApplyAccentBrushes(accent);
            }

            FollowSystemTheme(preset == ThemePreset.System);
            ThemeChanged?.Invoke(preset);
        }

        public void SetCustomColor(string resourceKey, Color color)
        {
            SetResourceColor(resourceKey, color);
        }

        public void SetAccentColor(Color color)
        {
            _customAccent = string.Equals(ToHex(color), DefaultAccentHex, StringComparison.OrdinalIgnoreCase) ? null : color;
            ApplyAccentBrushes(color);
            ThemeChanged?.Invoke(CurrentPreset);
        }

        private void ApplyAccentBrushes(Color color)
        {
            // Im hellen Design ist die Akzentfarbe Text auf hellem Grund: dort muss sie dunkel genug sein,
            // und die "helle" Variante (Hover) wird dunkler statt heller.
            bool light = IsLight;
            if (light && Luminance(color) > 0.55)
            {
                color = Shift(color, -90);
            }
            SetResourceColor("AccentSilverBrush", color);

            Color bright = Shift(color, light ? -50 : 40);
            Color dim = Shift(color, light ? 50 : -40);

            SetResourceColor("AccentSilverBrightBrush", bright);
            SetResourceColor("AccentSilverDimBrush", dim);
            SetResourceColor("OmniboxFocusBorderBrush", bright);
            SetResourceColor("TabActiveIndicatorBrush", bright);
        }

        private static Color Shift(Color c, int delta) => Color.FromArgb(
            c.A,
            (byte)Math.Clamp(c.R + delta, 0, 255),
            (byte)Math.Clamp(c.G + delta, 0, 255),
            (byte)Math.Clamp(c.B + delta, 0, 255));

        /// <summary>
        /// Farbe so anpassen, dass sie auf dem aktuellen Hintergrund sichtbar bleibt: zu helle Töne im hellen Design
        /// werden dunkler, zu dunkle im dunklen Design heller. Ungültige Werte bleiben unverändert.
        /// </summary>
        public string ReadableOnBackground(string hex)
        {
            Color color;
            try
            {
                color = ColorFromHex(hex);
            }
            catch (FormatException)
            {
                return hex;
            }

            double luminance = Luminance(color);
            if (IsLight && luminance > 0.7) return ToHex(Shift(color, -120));
            if (!IsLight && luminance < 0.2) return ToHex(Shift(color, 100));
            return hex;
        }

        /// <summary>Relative Helligkeit 0 (schwarz) bis 1 (weiß).</summary>
        private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        private void SetResourceColor(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }

        #region Windows-Modus (hell/dunkel)

        /// <summary>Windows-Einstellung "App-Modus": hell (true) oder dunkel (false).</summary>
        public static bool WindowsUsesLightTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
            }
            catch
            {
                return false;
            }
        }

        private void FollowSystemTheme(bool follow)
        {
            if (follow == _followsSystem) return;
            _followsSystem = follow;
            if (follow) SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            else SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }

        private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General || CurrentPreset != ThemePreset.System) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                var shownBefore = (Application.Current.Resources["WindowBackgroundBrush"] as SolidColorBrush)?.Color;
                var expected = GetPaletteForPreset(EffectivePreset)["WindowBackgroundBrush"];
                if (shownBefore != expected)
                {
                    ApplyPreset(ThemePreset.System);
                }
            });
        }

        #endregion

        #region CSS für interne Seiten

        /// <summary>
        /// Die aktuelle Palette als CSS-Variablen (--echo-…) für Start-, Einstellungs- und Absturzseite.
        /// Die Seiten übernehmen damit Preset und Akzentfarbe des Browsers.
        /// </summary>
        public string GetCssVariables()
        {
            bool light = IsLight;
            var css = new StringBuilder(":root{");
            css.Append("color-scheme:").Append(light ? "light" : "dark").Append(';');

            void Var(string name, string resourceKey) => css.Append("--echo-").Append(name).Append(':').Append(ResourceHex(resourceKey)).Append(';');
            Var("window", "WindowBackgroundBrush");
            Var("toolbar", "ToolbarBackgroundBrush");
            Var("surface", "SurfaceBrush");
            Var("surface-hover", "SurfaceHoverBrush");
            Var("surface-active", "ButtonPressedBackgroundBrush");
            Var("border", "BorderBrush");
            Var("border-subtle", "BorderSubtleBrush");
            Var("omnibox", "OmniboxBackgroundBrush");
            Var("focus", "OmniboxFocusBorderBrush");
            Var("text", "TextPrimaryBrush");
            Var("text-secondary", "TextSecondaryBrush");
            Var("text-muted", "TextMutedBrush");
            Var("accent", "AccentSilverBrush");
            Var("accent-bright", "AccentSilverBrightBrush");
            Var("accent-dim", "AccentSilverDimBrush");
            Var("shield", "ShieldActiveBrush");
            Var("success", "StatusSuccessBrush");
            Var("warning", "StatusWarningBrush");
            Var("danger", "StatusDangerBrush");

            // Schatten sind im hellen Design deutlich zarter; das Logo braucht dort dunkle statt helle Töne
            css.Append("--echo-shadow-k:").Append(light ? "0.35" : "1").Append(';');
            string[] logo = light
                ? new[] { "#1C2028", "#3E4654", "#7E8796", "#A0A8B4" }
                : new[] { "#FFFFFF", "#E6E9EE", "#9DA3AF", "#525866" };
            for (int i = 0; i < logo.Length; i++)
            {
                css.Append("--echo-logo-").Append(i + 1).Append(':').Append(logo[i]).Append(';');
            }

            return css.Append('}').ToString();
        }

        private static string ResourceHex(string key) =>
            Application.Current?.Resources[key] is SolidColorBrush brush ? ToHex(brush.Color) : "#808080";

        public static string ToHex(Color color) => string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");

        #endregion

        private Dictionary<string, Color> GetPaletteForPreset(ThemePreset preset)
        {
            return preset switch
            {
                ThemePreset.MidnightOled => new Dictionary<string, Color>
                {
                    { "WindowBackgroundBrush", ColorFromHex("#07080A") },
                    { "TabBarBackgroundBrush", ColorFromHex("#050507") },
                    { "TabBackgroundBrush", ColorFromHex("#0C0D10") },
                    { "TabHoverBackgroundBrush", ColorFromHex("#15171C") },
                    { "TabActiveBackgroundBrush", ColorFromHex("#16181F") },
                    { "TabActiveIndicatorBrush", ColorFromHex("#E6E8ED") },
                    { "TabBorderBrush", ColorFromHex("#1C1F26") },
                    { "ToolbarBackgroundBrush", ColorFromHex("#111317") },
                    { "SurfaceBrush", ColorFromHex("#0F1014") },
                    { "SurfaceHoverBrush", ColorFromHex("#1A1C22") },
                    { "SurfaceBorderBrush", ColorFromHex("#22252D") },
                    { "OmniboxBackgroundBrush", ColorFromHex("#090A0D") },
                    { "OmniboxBorderBrush", ColorFromHex("#252830") },
                    { "OmniboxFocusBorderBrush", ColorFromHex("#E2E5EB") },
                    { "BookmarksBarBackgroundBrush", ColorFromHex("#0E0F13") },
                    { "BookmarkItemHoverBrush", ColorFromHex("#1C1E26") },
                    { "AccentSilverBrush", ColorFromHex("#D0D3D9") },
                    { "AccentSilverBrightBrush", ColorFromHex("#FFFFFF") },
                    { "AccentSilverDimBrush", ColorFromHex("#7B808C") },
                    { "BorderBrush", ColorFromHex("#20232B") },
                    { "BorderSubtleBrush", ColorFromHex("#14161B") },
                    { "TextPrimaryBrush", ColorFromHex("#FFFFFF") },
                    { "TextSecondaryBrush", ColorFromHex("#8F94A0") },
                    { "TextMutedBrush", ColorFromHex("#5E636E") },
                    { "ButtonHoverBackgroundBrush", ColorFromHex("#1E2129") },
                    { "ButtonPressedBackgroundBrush", ColorFromHex("#2A2E39") },
                    { "StatusSuccessBrush", ColorFromHex("#34D399") },
                    { "StatusWarningBrush", ColorFromHex("#FBBF24") },
                    { "StatusDangerBrush", ColorFromHex("#F87171") },
                    { "ShieldActiveBrush", ColorFromHex("#38BDF8") },
                    { "ScrollBarThumbBrush", ColorFromHex("#2E323D") },
                    { "ScrollBarThumbHoverBrush", ColorFromHex("#484E5E") },
                    { "ScrollBarTrackBrush", ColorFromHex("#0A0B0E") },
                },

                ThemePreset.TitaniumLight => new Dictionary<string, Color>
                {
                    { "WindowBackgroundBrush", ColorFromHex("#E8EBEF") },
                    { "TabBarBackgroundBrush", ColorFromHex("#DEE2E8") },
                    { "TabBackgroundBrush", ColorFromHex("#E6E9EE") },
                    { "TabHoverBackgroundBrush", ColorFromHex("#EFF1F5") },
                    { "TabActiveBackgroundBrush", ColorFromHex("#FFFFFF") },
                    { "TabActiveIndicatorBrush", ColorFromHex("#4A5260") },
                    { "TabBorderBrush", ColorFromHex("#CBD1DA") },
                    { "ToolbarBackgroundBrush", ColorFromHex("#F3F5F8") },
                    { "SurfaceBrush", ColorFromHex("#FFFFFF") },
                    { "SurfaceHoverBrush", ColorFromHex("#F0F2F6") },
                    { "SurfaceBorderBrush", ColorFromHex("#D1D6DE") },
                    { "OmniboxBackgroundBrush", ColorFromHex("#FFFFFF") },
                    { "OmniboxBorderBrush", ColorFromHex("#C8CED8") },
                    { "OmniboxFocusBorderBrush", ColorFromHex("#3E4654") },
                    { "BookmarksBarBackgroundBrush", ColorFromHex("#E9EDF2") },
                    { "BookmarkItemHoverBrush", ColorFromHex("#DAE0E8") },
                    { "AccentSilverBrush", ColorFromHex("#4E5664") },
                    { "AccentSilverBrightBrush", ColorFromHex("#1C2028") },
                    { "AccentSilverDimBrush", ColorFromHex("#7E8796") },
                    { "BorderBrush", ColorFromHex("#CCD2DC") },
                    { "BorderSubtleBrush", ColorFromHex("#E0E4EB") },
                    { "TextPrimaryBrush", ColorFromHex("#181B20") },
                    { "TextSecondaryBrush", ColorFromHex("#586070") },
                    { "TextMutedBrush", ColorFromHex("#8A93A2") },
                    { "ButtonHoverBackgroundBrush", ColorFromHex("#E0E4EB") },
                    { "ButtonPressedBackgroundBrush", ColorFromHex("#D2D7E0") },
                    { "StatusSuccessBrush", ColorFromHex("#10B981") },
                    { "StatusWarningBrush", ColorFromHex("#D97706") },
                    { "StatusDangerBrush", ColorFromHex("#EF4444") },
                    { "ShieldActiveBrush", ColorFromHex("#0284C7") },
                    { "ScrollBarThumbBrush", ColorFromHex("#B0B7C3") },
                    { "ScrollBarThumbHoverBrush", ColorFromHex("#949CA9") },
                    { "ScrollBarTrackBrush", ColorFromHex("#E8EBEF") },
                },

                ThemePreset.CobaltSlate => new Dictionary<string, Color>
                {
                    { "WindowBackgroundBrush", ColorFromHex("#111827") },
                    { "TabBarBackgroundBrush", ColorFromHex("#0D1321") },
                    { "TabBackgroundBrush", ColorFromHex("#151F32") },
                    { "TabHoverBackgroundBrush", ColorFromHex("#1C2942") },
                    { "TabActiveBackgroundBrush", ColorFromHex("#1F2D48") },
                    { "TabActiveIndicatorBrush", ColorFromHex("#60A5FA") },
                    { "TabBorderBrush", ColorFromHex("#2A3C5C") },
                    { "ToolbarBackgroundBrush", ColorFromHex("#1A253C") },
                    { "SurfaceBrush", ColorFromHex("#172136") },
                    { "SurfaceHoverBrush", ColorFromHex("#212F4B") },
                    { "SurfaceBorderBrush", ColorFromHex("#2E4166") },
                    { "OmniboxBackgroundBrush", ColorFromHex("#0E1626") },
                    { "OmniboxBorderBrush", ColorFromHex("#2B3E63") },
                    { "OmniboxFocusBorderBrush", ColorFromHex("#60A5FA") },
                    { "BookmarksBarBackgroundBrush", ColorFromHex("#151F33") },
                    { "BookmarkItemHoverBrush", ColorFromHex("#223352") },
                    { "AccentSilverBrush", ColorFromHex("#93C5FD") },
                    { "AccentSilverBrightBrush", ColorFromHex("#E0F2FE") },
                    { "AccentSilverDimBrush", ColorFromHex("#6B8AB4") },
                    { "BorderBrush", ColorFromHex("#263654") },
                    { "BorderSubtleBrush", ColorFromHex("#1C273E") },
                    { "TextPrimaryBrush", ColorFromHex("#F1F5F9") },
                    { "TextSecondaryBrush", ColorFromHex("#94A3B8") },
                    { "TextMutedBrush", ColorFromHex("#64748B") },
                    { "ButtonHoverBackgroundBrush", ColorFromHex("#263756") },
                    { "ButtonPressedBackgroundBrush", ColorFromHex("#32476E") },
                    { "StatusSuccessBrush", ColorFromHex("#34D399") },
                    { "StatusWarningBrush", ColorFromHex("#FBBF24") },
                    { "StatusDangerBrush", ColorFromHex("#F87171") },
                    { "ShieldActiveBrush", ColorFromHex("#38BDF8") },
                    { "ScrollBarThumbBrush", ColorFromHex("#2B3E63") },
                    { "ScrollBarThumbHoverBrush", ColorFromHex("#3D578A") },
                    { "ScrollBarTrackBrush", ColorFromHex("#0E1626") },
                },

                // Default: SilverAnthracite
                _ => new Dictionary<string, Color>
                {
                    { "WindowBackgroundBrush", ColorFromHex("#1C1D21") },
                    { "TabBarBackgroundBrush", ColorFromHex("#16171A") },
                    { "TabBackgroundBrush", ColorFromHex("#202227") },
                    { "TabHoverBackgroundBrush", ColorFromHex("#282A31") },
                    { "TabActiveBackgroundBrush", ColorFromHex("#2B2E35") },
                    { "TabActiveIndicatorBrush", ColorFromHex("#C4C7CC") },
                    { "TabBorderBrush", ColorFromHex("#363A42") },
                    { "ToolbarBackgroundBrush", ColorFromHex("#25272D") },
                    { "SurfaceBrush", ColorFromHex("#222429") },
                    { "SurfaceHoverBrush", ColorFromHex("#2E3138") },
                    { "SurfaceBorderBrush", ColorFromHex("#3B3F48") },
                    { "OmniboxBackgroundBrush", ColorFromHex("#18191D") },
                    { "OmniboxBorderBrush", ColorFromHex("#363942") },
                    { "OmniboxFocusBorderBrush", ColorFromHex("#D6D9DE") },
                    { "BookmarksBarBackgroundBrush", ColorFromHex("#202227") },
                    { "BookmarkItemHoverBrush", ColorFromHex("#2E3139") },
                    { "AccentSilverBrush", ColorFromHex("#C4C7CC") },
                    { "AccentSilverBrightBrush", ColorFromHex("#F0F2F5") },
                    { "AccentSilverDimBrush", ColorFromHex("#8A8F99") },
                    { "BorderBrush", ColorFromHex("#353942") },
                    { "BorderSubtleBrush", ColorFromHex("#25272D") },
                    { "TextPrimaryBrush", ColorFromHex("#F0F2F5") },
                    { "TextSecondaryBrush", ColorFromHex("#A0A4AB") },
                    { "TextMutedBrush", ColorFromHex("#6B707B") },
                    { "ButtonHoverBackgroundBrush", ColorFromHex("#353841") },
                    { "ButtonPressedBackgroundBrush", ColorFromHex("#444853") },
                    { "StatusSuccessBrush", ColorFromHex("#34D399") },
                    { "StatusWarningBrush", ColorFromHex("#FBBF24") },
                    { "StatusDangerBrush", ColorFromHex("#F87171") },
                    { "ShieldActiveBrush", ColorFromHex("#38BDF8") },
                    { "ScrollBarThumbBrush", ColorFromHex("#3F444F") },
                    { "ScrollBarThumbHoverBrush", ColorFromHex("#575E6D") },
                    { "ScrollBarTrackBrush", ColorFromHex("#18191D") },
                }
            };
        }

        /// <summary>Preset anwenden und in den Einstellungen speichern. Unbekannte Namen werden ignoriert.</summary>
        public bool ApplyAndSavePreset(string presetName)
        {
            if (!Enum.TryParse<ThemePreset>(presetName, out var preset)) return false;

            ApplyPreset(preset);
            AppSettingsService.Instance.Settings.ThemePreset = presetName;
            AppSettingsService.Instance.Save();
            return true;
        }

        /// <summary>Akzentfarbe (#RRGGBB) anwenden und speichern. Ungültige Werte werden ignoriert.</summary>
        public bool ApplyAndSaveAccentColor(string hex)
        {
            Color color;
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#?[0-9A-Fa-f]{6}$")) return false;
            try
            {
                color = ColorFromHex(hex);
            }
            catch (FormatException)
            {
                return false;
            }

            SetAccentColor(color);
            AppSettingsService.Instance.Settings.AccentColor = hex;
            AppSettingsService.Instance.Save();
            return true;
        }

        public static Color ColorFromHex(string hex)
        {
            hex = hex.Replace("#", "").Trim();
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return Color.FromRgb(r, g, b);
            }
            if (hex.Length == 8)
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                return Color.FromArgb(a, r, g, b);
            }
            return Colors.Gray;
        }
    }
}
