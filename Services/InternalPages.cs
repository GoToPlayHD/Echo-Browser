using System;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Adressen der internen Seiten (echo://start, echo://settings, …). Das Schema "echo" ist in
    /// <see cref="BrowserEnvironment"/> bei WebView2 registriert; die Seiten liefert MainWindow.InternalPages.cs aus.
    /// </summary>
    public static class InternalPages
    {
        public const string Scheme = "echo";

        public const string Start = "start";
        public const string NewTab = "newtab";
        public const string Settings = "settings";
        public const string Crashed = "crashed";

        /// <summary>echo://favicon/&lt;host&gt; – Website-Symbol aus dem Cache (nur für interne Seiten).</summary>
        public const string Favicon = "favicon";

        /// <summary>echo://wallpaper – eigenes Hintergrundbild der Startseite (nur für interne Seiten).</summary>
        public const string Wallpaper = "wallpaper";

        public static bool IsInternalUrl(string? url) =>
            url != null && url.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

        /// <summary>Name der Seite ("start", "settings", …) oder null, wenn es keine interne Adresse ist.</summary>
        public static string? GetPageName(string? url)
        {
            if (!IsInternalUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
            return string.IsNullOrEmpty(uri.Host) ? null : uri.Host.ToLowerInvariant();
        }

        public static bool Is(string? url, string pageName) =>
            string.Equals(GetPageName(url), pageName, StringComparison.Ordinal);

        /// <summary>
        /// Chromium hängt an Adressen mit Host einen Schrägstrich an ("echo://start/").
        /// Für Vergleiche und die Adressleiste wird die kurze Form "echo://start" verwendet.
        /// </summary>
        public static string Normalize(string url)
        {
            if (!IsInternalUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
            if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0) return url;
            return $"{Scheme}://{uri.Host.ToLowerInvariant()}";
        }

        /// <summary>Absturzseite, die sich die ursprüngliche Adresse für "Neu laden" merkt.</summary>
        public static string CrashedPageFor(string originalUrl) =>
            $"{Scheme}://{Crashed}/?url={Uri.EscapeDataString(originalUrl)}";
    }
}
