using System;
using System.Collections.Generic;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>
    /// Hosts, für die WebView2 einen Zertifikatsfehler gemeldet hat (CoreWebView2.ServerCertificateErrorDetected).
    /// Chromium zeigt dafür seine Warnseite. Geht der Nutzer über »Weiter zu … (unsicher)« trotzdem auf die Seite,
    /// gilt diese Ausnahme im Profil bis zum Ende der Sitzung – so lange darf der Host nicht als »Sicher« erscheinen.
    /// Eine Liste pro Profil: das normale Profil und das Inkognito-Profil haben getrennte Ausnahmen.
    /// </summary>
    public sealed class CertificateErrorTracker
    {
        private static readonly CertificateErrorTracker NormalProfile = new();
        private static readonly CertificateErrorTracker IncognitoProfile = new();

        public static CertificateErrorTracker ForProfile(bool incognito) => incognito ? IncognitoProfile : NormalProfile;

        // Alle Fenster laufen auf dem UI-Thread, die Ereignisse kommen dort an
        private readonly Dictionary<string, CoreWebView2WebErrorStatus> _hosts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fehler für den Host der Adresse merken. true, wenn sich dadurch etwas geändert hat.</summary>
        public bool Record(string? requestUri, CoreWebView2WebErrorStatus status)
        {
            if (HostOf(requestUri) is not { } host) return false;
            if (_hosts.TryGetValue(host, out var known) && known == status) return false;

            _hosts[host] = status;
            return true;
        }

        /// <summary>Gemerkter Zertifikatsfehler für eine https-Adresse, sonst null.</summary>
        public CoreWebView2WebErrorStatus? Find(string? url)
        {
            if (url == null || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;
            return HostOf(url) is { } host && _hosts.TryGetValue(host, out var status) ? status : null;
        }

        /// <summary>Alle Ausnahmen vergessen (das Inkognito-Profil endet mit dem letzten Inkognito-Fenster).</summary>
        public void Clear() => _hosts.Clear();

        /// <summary>Übersetzungsschlüssel für den Grund, z. B. »Zertifikat abgelaufen«.</summary>
        public static string ReasonKey(CoreWebView2WebErrorStatus status) => status switch
        {
            CoreWebView2WebErrorStatus.CertificateExpired => "Security_CertExpired",
            CoreWebView2WebErrorStatus.CertificateCommonNameIsIncorrect => "Security_CertNameMismatch",
            CoreWebView2WebErrorStatus.CertificateRevoked => "Security_CertRevoked",
            _ => "Security_CertInvalid"
        };

        private static string? HostOf(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : null;
    }
}
