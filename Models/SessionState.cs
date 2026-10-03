using System.Collections.Generic;

namespace EchoBrowser.Models
{
    /// <summary>Gespeicherte Sitzung: alle normalen (nicht-Inkognito) Fenster mit ihren Tabs.</summary>
    public sealed class SessionSnapshot
    {
        public int Version { get; set; } = 2;
        public List<SessionWindow> Windows { get; set; } = new();
    }

    public sealed class SessionWindow
    {
        public List<SessionTab> Tabs { get; set; } = new();
        public int ActiveIndex { get; set; }
        public List<SessionGroup> Groups { get; set; } = new();
    }

    public sealed class SessionTab
    {
        public string Url { get; set; } = "";
        public string? Title { get; set; }
        public bool IsPinned { get; set; }
        public string? GroupId { get; set; }
    }

    public sealed class SessionGroup
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Color { get; set; } = "Blue";
        public bool IsCollapsed { get; set; }
    }
}
