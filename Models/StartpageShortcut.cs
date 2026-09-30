using System;

namespace EchoBrowser.Models
{
    public class StartpageShortcut
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string IconKey { get; set; } = "globe";

        public StartpageShortcut() { }

        public StartpageShortcut(string title, string url, string iconKey = "globe")
        {
            Title = title;
            Url = url;
            IconKey = iconKey;
        }
    }
}
