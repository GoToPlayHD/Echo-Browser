using System;
using System.Text.Json.Serialization;

namespace EchoBrowser.Models
{
    public class HistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public DateTime VisitedAt { get; set; } = DateTime.Now;

        [JsonIgnore]
        public string DisplayTime => VisitedAt.ToString("HH:mm");

        [JsonIgnore]
        public string DisplayDate
        {
            get
            {
                var now = DateTime.Now;
                if (VisitedAt.Date == now.Date)
                    return "Heute";
                if (VisitedAt.Date == now.AddDays(-1).Date)
                    return "Gestern";
                return VisitedAt.ToString("dd.MM.yyyy");
            }
        }

        [JsonIgnore]
        public string DisplayDomain
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Url)) return string.Empty;
                try
                {
                    var uri = new Uri(Url);
                    return uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
                }
                catch
                {
                    return Url;
                }
            }
        }

        [JsonIgnore]
        public string DisplayInitial
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title))
                    return Title.Trim().Substring(0, 1).ToUpperInvariant();
                if (!string.IsNullOrWhiteSpace(DisplayDomain))
                    return DisplayDomain.Trim().Substring(0, 1).ToUpperInvariant();
                return "?";
            }
        }

        public HistoryItem() { }

        public HistoryItem(string title, string url)
        {
            Url = url;
            Title = string.IsNullOrWhiteSpace(title) ? url : title;
            VisitedAt = DateTime.Now;
        }
    }
}
