using System.Collections.Generic;

namespace EchoBrowser.Models
{
    public class AppSettings
    {
        public bool IsBookmarksBarVisible { get; set; } = true;
        public bool IsSidebarVisible { get; set; } = true;
        public bool IsStartpageFavoritesVisible { get; set; } = true;
        public string SearchEngine { get; set; } = "duckduckgo"; // duckduckgo, google, bing, ecosia, brave, startpage
        public List<StartpageShortcut> StartpageShortcuts { get; set; } = new();
    }
}
