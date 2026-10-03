using System;
using System.Collections.Generic;
using System.IO;

namespace EchoBrowser.Models
{
    public class AppSettings
    {
        // 1. General & Startup
        public string StartupBehavior { get; set; } = "startpage"; // "startpage", "restore_session", "custom_url"
        public string CustomStartupUrl { get; set; } = "https://www.google.com";
        public bool ShowHomeButton { get; set; } = true;

        // 2. Search
        public string SearchEngine { get; set; } = "duckduckgo"; // duckduckgo, google, bing, ecosia, brave, startpage
        public bool EnableSearchSuggestions { get; set; } = true;

        // 3. Appearance
        public string ThemePreset { get; set; } = "SilverAnthracite"; // SilverAnthracite, MidnightOled, TitaniumLight, CobaltSlate
        public string AccentColor { get; set; } = "#C4C7CC";
        public bool IsBookmarksBarVisible { get; set; } = true;
        public bool IsSidebarVisible { get; set; } = true;
        public bool IsStartpageFavoritesVisible { get; set; } = true;
        /// <summary>Eigenes Hintergrundbild der Startseite (Dateipfad, leer = Farbverlauf des Themes).</summary>
        public string StartpageBackgroundPath { get; set; } = "";
        /// <summary>Windows-11-Transparenzeffekt (Mica) in der Tab-Leiste.</summary>
        public bool UseMica { get; set; } = true;
        public int DefaultZoomPercent { get; set; } = 100; // 75, 90, 100, 110, 125, 150

        // Toolbar Buttons Visibility
        public bool ShowSidebarButton { get; set; } = true;
        public bool ShowBackButton { get; set; } = true;
        public bool ShowForwardButton { get; set; } = true;
        public bool ShowReloadButton { get; set; } = true;
        public bool ShowSearchEngineSelector { get; set; } = true;
        public bool ShowExtensionsButton { get; set; } = true;
        public bool ShowDownloadsButton { get; set; } = true;
        public bool ShowSplitViewButton { get; set; } = true;

        // 4. Privacy & Security
        public bool IsAdBlockerEnabled { get; set; } = true;
        public string AdBlockerFilterUrl { get; set; } = "https://raw.githubusercontent.com/StevenBlack/hosts/master/hosts";
        public string TrackingPreventionLevel { get; set; } = "balanced"; // "none", "balanced", "strict"
        public bool BlockPopups { get; set; } = true;
        public bool EnableJavaScript { get; set; } = true;
        public bool SendDoNotTrack { get; set; } = true;
        public bool ClearDataOnExit { get; set; } = false;
        public bool SavePasswords { get; set; } = true;
        public bool AutofillForms { get; set; } = true;

        // 5. Downloads
        public string DownloadPath { get; set; } = GetDefaultDownloadPath();
        public bool AskDownloadLocation { get; set; } = false;

        // 6. Tabs & Behavior
        public bool OpenNewTabInBackground { get; set; } = false;
        public bool WarnOnClosingMultipleTabs { get; set; } = true;

        /// <summary>Inaktive Tabs nach so vielen Minuten schlafen legen (0 = nie), siehe TabSleepPolicy.</summary>
        public int TabSleepMinutes { get; set; } = Services.TabSleepPolicy.DefaultMinutes;

        /// <summary>Tabs als Liste links statt oben (wie Edge); schmal = nur Symbole.</summary>
        public bool VerticalTabs { get; set; } = false;
        public bool VerticalTabsNarrow { get; set; } = false;

        // 7. Startpage Shortcuts
        public List<StartpageShortcut> StartpageShortcuts { get; set; } = new();

        // 8. Extensions
        public List<string> PinnedExtensionIds { get; set; } = new();

        // 9. Localization
        public string Language { get; set; } = "de"; // "de", "en", "fr", "es"
        public bool HasCompletedFirstRunLanguageSetup { get; set; } = false;

        // 10. Shield Whitelist (Domains where tracking protection is turned off)
        public HashSet<string> WhitelistedShieldDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // Websites, die Popups ohne Klick öffnen dürfen
        public HashSet<string> PopupAllowedDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        // 11. Updates (Velopack)
        public bool AutoCheckForUpdates { get; set; } = true;
        public string UpdateUrl { get; set; } = "https://github.com/GoToPlayHD/Echo-Browser";
        public bool CheckPrereleaseUpdates { get; set; } = false;

        public static string GetDefaultDownloadPath()
        {
            try
            {
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string downloads = Path.Combine(userProfile, "Downloads");
                if (Directory.Exists(downloads)) return downloads;
            }
            catch { }

            return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }
    }
}

