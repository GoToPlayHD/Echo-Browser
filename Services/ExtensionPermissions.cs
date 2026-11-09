using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace EchoBrowser.Services
{
    /// <summary>Worauf eine Erweiterung laut manifest.json zugreifen darf.</summary>
    /// <param name="AllSites">Zugriff auf alle Websites (&lt;all_urls&gt;, *://*/* …).</param>
    /// <param name="Sites">Einzelne Websites (Hosts), wenn nicht alle.</param>
    /// <param name="WarningKeys">Übersetzungsschlüssel für Berechtigungen mit Warnung (wie in Chrome).</param>
    /// <param name="OtherPermissions">Weitere Berechtigungen ohne eigenen Text, roh aus dem Manifest.</param>
    public sealed record ExtensionPermissionSummary(
        bool AllSites,
        IReadOnlyList<string> Sites,
        IReadOnlyList<string> WarningKeys,
        IReadOnlyList<string> OtherPermissions)
    {
        public bool IsEmpty => !AllSites && Sites.Count == 0 && WarningKeys.Count == 0 && OtherPermissions.Count == 0;
    }

    /// <summary>
    /// Berechtigungen einer Erweiterung für die Nachfrage vor der Installation (wie Chrome: »Diese Erweiterung kann …«).
    /// Berücksichtigt permissions, host_permissions (Manifest V3) und content_scripts.matches; optionale
    /// Berechtigungen fragt die Erweiterung erst später an.
    /// </summary>
    public static class ExtensionPermissions
    {
        private const int MaxSitesShown = 5;

        // Berechtigungen mit Warnung in Chrome; mehrere können denselben Text haben
        internal static readonly IReadOnlyDictionary<string, string> WarningKeyByPermission = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["history"] = "Ext_Perm_History",
            ["tabs"] = "Ext_Perm_Tabs",
            ["webNavigation"] = "Ext_Perm_Tabs",
            ["topSites"] = "Ext_Perm_Tabs",
            ["declarativeNetRequestFeedback"] = "Ext_Perm_Tabs",
            ["bookmarks"] = "Ext_Perm_Bookmarks",
            ["downloads"] = "Ext_Perm_Downloads",
            ["clipboardRead"] = "Ext_Perm_ClipboardRead",
            ["clipboardWrite"] = "Ext_Perm_ClipboardWrite",
            ["nativeMessaging"] = "Ext_Perm_NativeMessaging",
            ["management"] = "Ext_Perm_Management",
            ["privacy"] = "Ext_Perm_Privacy",
            ["contentSettings"] = "Ext_Perm_Privacy",
            ["proxy"] = "Ext_Perm_Proxy",
            ["debugger"] = "Ext_Perm_Debugger",
            ["geolocation"] = "Ext_Perm_Location",
            ["notifications"] = "Ext_Perm_Notifications",
            ["desktopCapture"] = "Ext_Perm_ScreenCapture",
            ["tabCapture"] = "Ext_Perm_ScreenCapture",
            ["declarativeNetRequest"] = "Ext_Perm_BlockContent",
            ["identity.email"] = "Ext_Perm_Identity",
        };

        // Ohne Warnung in Chrome und ohne Zugriff auf Nutzerdaten über die Website-Rechte hinaus: nicht aufzählen
        private static readonly HashSet<string> Unremarkable = new(StringComparer.Ordinal)
        {
            "activeTab", "alarms", "background", "contextMenus", "cookies", "declarativeNetRequestWithHostAccess",
            "fontSettings", "gcm", "i18n", "identity", "idle", "offscreen", "power", "runtime", "scripting",
            "sidePanel", "storage", "tts", "unlimitedStorage", "webRequest", "webRequestBlocking"
        };

        public static ExtensionPermissionSummary Describe(JsonElement manifest)
        {
            bool allSites = false;
            var sites = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var warnings = new List<string>();
            var other = new SortedSet<string>(StringComparer.Ordinal);

            void AddHostPattern(string pattern)
            {
                if (IsAllSitesPattern(pattern)) allSites = true;
                else if (HostOfPattern(pattern) is { } host) sites.Add(host);
            }

            foreach (string permission in Strings(manifest, "permissions"))
            {
                if (LooksLikeHostPattern(permission))
                {
                    AddHostPattern(permission);   // Manifest V2: Websites stehen bei den Berechtigungen
                }
                else if (WarningKeyByPermission.TryGetValue(permission, out var key))
                {
                    if (!warnings.Contains(key)) warnings.Add(key);
                }
                else if (!Unremarkable.Contains(permission))
                {
                    other.Add(permission);
                }
            }

            foreach (string pattern in Strings(manifest, "host_permissions"))
            {
                AddHostPattern(pattern);
            }

            if (manifest.TryGetProperty("content_scripts", out var scripts) && scripts.ValueKind == JsonValueKind.Array)
            {
                foreach (var script in scripts.EnumerateArray())
                {
                    if (script.ValueKind != JsonValueKind.Object) continue;
                    foreach (string pattern in Strings(script, "matches"))
                    {
                        AddHostPattern(pattern);
                    }
                }
            }

            return new ExtensionPermissionSummary(
                allSites,
                allSites ? Array.Empty<string>() : sites.ToList(),
                warnings,
                other.ToList());
        }

        /// <summary>Text für den Installationsdialog, eine Zeile pro Punkt.</summary>
        public static string Format(ExtensionPermissionSummary summary)
        {
            if (summary.IsEmpty) return Tr.Get("Ext_Perm_None");

            var text = new StringBuilder();
            void Line(string line) => text.Append(text.Length > 0 ? "\n" : "").Append("• ").Append(line);

            if (summary.AllSites)
            {
                Line(Tr.Get("Ext_Perm_AllSites"));
            }
            else if (summary.Sites.Count > 0)
            {
                string list = string.Join(", ", summary.Sites.Take(MaxSitesShown));
                if (summary.Sites.Count > MaxSitesShown)
                {
                    list += " " + Tr.Format("Ext_Perm_MoreSites", summary.Sites.Count - MaxSitesShown);
                }
                Line(Tr.Format("Ext_Perm_Sites", list));
            }

            foreach (string key in summary.WarningKeys)
            {
                Line(Tr.Get(key));
            }

            if (summary.OtherPermissions.Count > 0)
            {
                Line(Tr.Format("Ext_Perm_Other", string.Join(", ", summary.OtherPermissions)));
            }
            return text.ToString();
        }

        /// <summary>Zugriff auf jede Website: &lt;all_urls&gt; oder ein Muster mit Host »*« für http(s).</summary>
        internal static bool IsAllSitesPattern(string pattern)
        {
            if (pattern == "<all_urls>") return true;
            int schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return false;

            string scheme = pattern[..schemeEnd];
            return scheme is "*" or "http" or "https" && HostOfPattern(pattern) == "*";
        }

        /// <summary>Host eines Musters wie »https://*.google.com/*« (»*.google.com«), oder null.</summary>
        internal static string? HostOfPattern(string pattern)
        {
            int schemeEnd = pattern.IndexOf("://", StringComparison.Ordinal);
            if (schemeEnd <= 0) return null;

            string rest = pattern[(schemeEnd + 3)..];
            int pathStart = rest.IndexOf('/');
            string host = pathStart >= 0 ? rest[..pathStart] : rest;
            int portStart = host.LastIndexOf(':');
            if (portStart > 0) host = host[..portStart];
            if (host.Length > 0) return host.ToLowerInvariant();

            // file:///… hat keinen Host
            return pattern.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? "file://" : null;
        }

        private static bool LooksLikeHostPattern(string permission) =>
            permission == "<all_urls>" || permission.Contains("://", StringComparison.Ordinal);

        private static IEnumerable<string> Strings(JsonElement obj, string property)
        {
            if (!obj.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array) yield break;
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } value)
                {
                    yield return value;
                }
            }
        }
    }
}
