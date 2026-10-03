using System;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>Eine gespeicherte Berechtigung einer Website (für die Liste im Shield-Panel).</summary>
    public sealed record SitePermission(CoreWebView2PermissionKind Kind, string Origin, CoreWebView2PermissionState State)
    {
        public string Name => SitePermissionTexts.Name(Kind);

        public string StateText => Tr.Get(State == CoreWebView2PermissionState.Allow ? "Perm_Allowed" : "Perm_Blocked");
    }

    /// <summary>Übersetzte Bezeichnungen für Berechtigungen und welche davon Echo selbst abfragt.</summary>
    public static class SitePermissionTexts
    {
        /// <summary>Für diese Berechtigungen zeigt Echo eine eigene Abfrage; alle anderen regelt WebView2 selbst.</summary>
        public static bool IsPrompted(CoreWebView2PermissionKind kind) => kind is
            CoreWebView2PermissionKind.Camera or
            CoreWebView2PermissionKind.Microphone or
            CoreWebView2PermissionKind.Geolocation or
            CoreWebView2PermissionKind.Notifications or
            CoreWebView2PermissionKind.ClipboardRead or
            CoreWebView2PermissionKind.MultipleAutomaticDownloads or
            CoreWebView2PermissionKind.MidiSystemExclusiveMessages or
            CoreWebView2PermissionKind.WindowManagement or
            CoreWebView2PermissionKind.LocalFonts or
            CoreWebView2PermissionKind.FileReadWrite;

        /// <summary>Kurzname, z.B. "Kamera".</summary>
        public static string Name(CoreWebView2PermissionKind kind) => Tr.Get("PermName_" + KeySuffix(kind));

        /// <summary>Satzteil für die Abfrage, z.B. "deine Kamera verwenden".</summary>
        public static string Request(CoreWebView2PermissionKind kind) => Tr.Get("Perm_" + KeySuffix(kind));

        /// <summary>Symbol (Material-Pfad) für die Abfrage.</summary>
        public static string IconPath(CoreWebView2PermissionKind kind) => kind switch
        {
            CoreWebView2PermissionKind.Camera => "M17 10.5V7c0-.55-.45-1-1-1H4c-.55 0-1 .45-1 1v10c0 .55.45 1 1 1h12c.55 0 1-.45 1-1v-3.5l4 4v-11l-4 4z",
            CoreWebView2PermissionKind.Microphone => "M12 14c1.66 0 2.99-1.34 2.99-3L15 5c0-1.66-1.34-3-3-3S9 3.34 9 5v6c0 1.66 1.34 3 3 3zm5.3-3c0 3-2.54 5.1-5.3 5.1S6.7 14 6.7 11H5c0 3.41 2.72 6.23 6 6.72V21h2v-3.28c3.28-.48 6-3.3 6-6.72h-1.7z",
            CoreWebView2PermissionKind.Geolocation => "M12 2C8.13 2 5 5.13 5 9c0 5.25 7 13 7 13s7-7.75 7-13c0-3.87-3.13-7-7-7zm0 9.5c-1.38 0-2.5-1.12-2.5-2.5s1.12-2.5 2.5-2.5 2.5 1.12 2.5 2.5-1.12 2.5-2.5 2.5z",
            CoreWebView2PermissionKind.Notifications => "M12 22c1.1 0 2-.9 2-2h-4c0 1.1.89 2 2 2zm6-6v-5c0-3.07-1.64-5.64-4.5-6.32V4c0-.83-.67-1.5-1.5-1.5s-1.5.67-1.5 1.5v.68C7.63 5.36 6 7.92 6 11v5l-2 2v1h16v-1l-2-2z",
            CoreWebView2PermissionKind.ClipboardRead => "M19 2h-4.18C14.4.84 13.3 0 12 0c-1.3 0-2.4.84-2.82 2H5c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-7 0c.55 0 1 .45 1 1s-.45 1-1 1-1-.45-1-1 .45-1 1-1zm7 18H5V4h2v3h10V4h2v16z",
            CoreWebView2PermissionKind.MultipleAutomaticDownloads => "M19 9h-4V3H9v6H5l7 7 7-7zM5 18v2h14v-2H5z",
            _ => "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4zm0 10.99h7c-.53 4.12-3.28 7.79-7 8.94V12H5V6.3l7-3.11v8.8z"
        };

        private static string KeySuffix(CoreWebView2PermissionKind kind) => kind switch
        {
            CoreWebView2PermissionKind.Camera => "Camera",
            CoreWebView2PermissionKind.Microphone => "Microphone",
            CoreWebView2PermissionKind.Geolocation => "Geolocation",
            CoreWebView2PermissionKind.Notifications => "Notifications",
            CoreWebView2PermissionKind.ClipboardRead => "ClipboardRead",
            CoreWebView2PermissionKind.MultipleAutomaticDownloads => "Downloads",
            CoreWebView2PermissionKind.MidiSystemExclusiveMessages => "Midi",
            CoreWebView2PermissionKind.WindowManagement => "WindowManagement",
            CoreWebView2PermissionKind.LocalFonts => "LocalFonts",
            CoreWebView2PermissionKind.FileReadWrite => "FileReadWrite",
            CoreWebView2PermissionKind.Autoplay => "Autoplay",
            _ => "Other"
        };

        /// <summary>"https://www.example.com:8443" → "www.example.com" für die Anzeige.</summary>
        public static string HostOf(string? originOrUrl) =>
            Uri.TryCreate(originOrUrl, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : originOrUrl ?? "";
    }
}
