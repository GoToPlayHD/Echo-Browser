using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    public class ExtensionService
    {
        private static ExtensionService? _instance;
        public static ExtensionService Instance => _instance ??= new ExtensionService();

        private readonly string _extensionsDirectory;

        private ExtensionService()
        {
            _extensionsDirectory = Path.Combine(AppPaths.DataFolder, "Extensions");
            Directory.CreateDirectory(_extensionsDirectory);
        }

        public async Task<CoreWebView2BrowserExtension?> InstallExtensionFromCrxAsync(CoreWebView2Profile profile, string crxPath)
        {
            if (!File.Exists(crxPath))
                throw new FileNotFoundException(Tr.Get("Ext_ErrorCrxNotFound"), crxPath);

            string extName = Path.GetFileNameWithoutExtension(crxPath);
            string targetFolder = Path.Combine(_extensionsDirectory, $"{extName}_{Guid.NewGuid():N}");

            ExtractCrx(crxPath, targetFolder);

            // Find directory containing manifest.json
            string? manifestFolder = FindManifestDirectory(targetFolder);
            if (manifestFolder == null)
            {
                throw new InvalidOperationException(Tr.Get("Ext_ErrorManifestMissingUnpacked"));
            }

            // Remove any underscore directories (e.g. _metadata) forbidden by Chromium unpacked extensions loader
            SanitizeUnpackedExtension(manifestFolder);

            var ext = await profile.AddBrowserExtensionAsync(manifestFolder);
            RememberExtensionFolder(ext, manifestFolder);
            return ext;
        }

        public async Task<CoreWebView2BrowserExtension?> InstallExtensionFromFolderAsync(CoreWebView2Profile profile, string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException(Tr.Format("Ext_ErrorFolderNotFound", folderPath));

            string? manifestFolder = FindManifestDirectory(folderPath);
            if (manifestFolder == null)
            {
                throw new InvalidOperationException(Tr.Get("Ext_ErrorManifestMissingFolder"));
            }

            SanitizeUnpackedExtension(manifestFolder);

            var ext = await profile.AddBrowserExtensionAsync(manifestFolder);
            RememberExtensionFolder(ext, manifestFolder);
            return ext;
        }

        public async Task<CoreWebView2BrowserExtension?> DownloadAndInstallExtensionAsync(CoreWebView2Profile profile, string extensionId, string? extensionName = null)
        {
            if (string.IsNullOrWhiteSpace(extensionId))
                throw new ArgumentException(Tr.Get("Ext_ErrorInvalidId"), nameof(extensionId));

            string downloadUrl = $"https://clients2.google.com/service/update2/crx?response=redirect&prodversion=128.0&acceptformat=crx2,crx3&x=id%3D{extensionId}%26uc";
            string tempCrxPath = Path.Combine(_extensionsDirectory, $"download_{extensionId}_{Guid.NewGuid():N}.crx");

            using (var handler = new HttpClientHandler { AllowAutoRedirect = true })
            using (var client = new HttpClient(handler))
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36");
                client.Timeout = TimeSpan.FromSeconds(60);

                var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();

                byte[] data = await response.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(tempCrxPath, data);
            }

            try
            {
                return await InstallExtensionFromCrxAsync(profile, tempCrxPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempCrxPath))
                        File.Delete(tempCrxPath);
                }
                catch { }
            }
        }

        private static readonly HashSet<string> SystemExtensionNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft Clipboard extension",
            "Microsoft Edge PDF Viewer"
        };

        public static bool IsSystemExtension(CoreWebView2BrowserExtension? ext)
        {
            if (ext == null) return false;
            string name = ext.Name ?? "";
            if (SystemExtensionNames.Contains(name)) return true;
            if (name.Contains("Microsoft Clipboard", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Edge PDF Viewer", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public async Task<IReadOnlyList<CoreWebView2BrowserExtension>> GetInstalledExtensionsAsync(CoreWebView2Profile profile, bool includeSystem = false)
        {
            try
            {
                var list = await profile.GetBrowserExtensionsAsync();
                if (list == null) return Array.Empty<CoreWebView2BrowserExtension>();
                if (!includeSystem)
                {
                    return list.Where(e => !IsSystemExtension(e)).ToList();
                }
                return list;
            }
            catch (Exception ex)
            {
                Log.Warn("Failed to get extensions", ex);
                return Array.Empty<CoreWebView2BrowserExtension>();
            }
        }

        #region Manifest-Zuordnung (WebView2-ID -> entpackter Ordner)

        // WebView2 vergibt entpackten Erweiterungen eine eigene, aus dem Pfad berechnete ID. Sie stimmt
        // nicht mit der Chrome-Web-Store-ID im Ordnernamen überein, und viele Manifeste enthalten statt
        // des Namens nur "__MSG_extName__". Deshalb merken wir uns bei der Installation, welcher Ordner
        // zu welcher ID gehört (extensions-index.json), und lösen ältere Installationen über den
        // lokalisierten Namen auf.

        private Dictionary<string, string>? _folderIndex;
        private readonly object _indexLock = new();

        private string IndexFilePath => Path.Combine(_extensionsDirectory, "extensions-index.json");

        private Dictionary<string, string> FolderIndex
        {
            get
            {
                if (_folderIndex != null) return _folderIndex;
                _folderIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(IndexFilePath))
                    {
                        var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(IndexFilePath));
                        if (loaded != null)
                        {
                            _folderIndex = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Erweiterungs-Index konnte nicht gelesen werden", ex);
                }
                return _folderIndex;
            }
        }

        private void SaveFolderIndex()
        {
            try
            {
                AtomicFile.WriteAllText(IndexFilePath, JsonSerializer.Serialize(FolderIndex, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Warn("Erweiterungs-Index konnte nicht gespeichert werden", ex);
            }
        }

        private void RememberExtensionFolder(CoreWebView2BrowserExtension? ext, string manifestFolder)
        {
            if (ext == null || string.IsNullOrWhiteSpace(ext.Id)) return;
            lock (_indexLock)
            {
                FolderIndex[ext.Id] = manifestFolder;
                SaveFolderIndex();
            }
        }

        /// <summary>
        /// Nach dem Entfernen einer Erweiterung: Zuordnung vergessen und die von Echo entpackte Kopie löschen.
        /// Ordner außerhalb von %LOCALAPPDATA%\EchoBrowser\Extensions (manuell geladene) werden nie gelöscht.
        /// </summary>
        public void ForgetExtension(string extensionId)
        {
            string? folder;
            lock (_indexLock)
            {
                if (!FolderIndex.TryGetValue(extensionId, out folder)) return;
                FolderIndex.Remove(extensionId);
                SaveFolderIndex();
            }

            try
            {
                string root = Path.GetFullPath(_extensionsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string full = Path.GetFullPath(folder);
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return;

                // Den obersten Ordner unterhalb von Extensions löschen (die Erweiterung kann eine Ebene tiefer liegen)
                string relative = full.Substring(root.Length);
                string topLevel = Path.Combine(root, relative.Split(Path.DirectorySeparatorChar)[0]);
                if (Directory.Exists(topLevel))
                {
                    Directory.Delete(topLevel, recursive: true);
                }
            }
            catch (Exception ex)
            {
                // Dateien können kurz nach dem Entfernen noch gesperrt sein – dann bleibt der Ordner liegen
                Log.Warn("Erweiterungsordner konnte nicht gelöscht werden", ex);
            }
        }

        /// <summary>Ordner mit der manifest.json der Erweiterung, oder null.</summary>
        public string? FindExtensionFolder(string extensionId, string? extensionName = null)
        {
            lock (_indexLock)
            {
                if (FolderIndex.TryGetValue(extensionId, out var known) && File.Exists(Path.Combine(known, "manifest.json")))
                {
                    return known;
                }
            }

            if (!Directory.Exists(_extensionsDirectory)) return null;

            foreach (var topDir in Directory.GetDirectories(_extensionsDirectory))
            {
                string? folder = FindManifestDirectory(topDir);
                if (folder == null) continue;

                bool isMatch = folder.Contains(extensionId, StringComparison.OrdinalIgnoreCase) ||
                               (!string.IsNullOrWhiteSpace(extensionName) &&
                                GetManifestNames(folder).Contains(extensionName, StringComparer.OrdinalIgnoreCase));
                if (!isMatch) continue;

                lock (_indexLock)
                {
                    FolderIndex[extensionId] = folder;
                    SaveFolderIndex();
                }
                return folder;
            }

            return null;
        }

        /// <summary>Alle möglichen Anzeigenamen aus dem Manifest, inkl. aufgelöster "__MSG_...__"-Platzhalter aller Sprachen.</summary>
        private static IEnumerable<string> GetManifestNames(string folder)
        {
            var names = new List<string>();
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
                foreach (var prop in new[] { "name", "short_name" })
                {
                    if (!doc.RootElement.TryGetProperty(prop, out var el) || el.ValueKind != JsonValueKind.String) continue;
                    string value = el.GetString() ?? "";

                    var msg = Regex.Match(value, @"^__MSG_(\w+)__$");
                    if (!msg.Success)
                    {
                        names.Add(value);
                        continue;
                    }

                    string localesDir = Path.Combine(folder, "_locales");
                    if (!Directory.Exists(localesDir)) continue;
                    foreach (var messagesFile in Directory.GetFiles(localesDir, "messages.json", SearchOption.AllDirectories))
                    {
                        string? resolved = ReadLocaleMessage(messagesFile, msg.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(resolved)) names.Add(resolved);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"Manifest-Name in '{folder}' nicht lesbar", ex);
            }
            return names;
        }

        private static string? ReadLocaleMessage(string messagesFile, string key)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(messagesFile));
                foreach (var entry in doc.RootElement.EnumerateObject())
                {
                    // Schlüssel in messages.json sind laut Spezifikation unabhängig von Groß-/Kleinschreibung
                    if (entry.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                        entry.Value.TryGetProperty("message", out var message))
                    {
                        return message.GetString();
                    }
                }
            }
            catch { /* einzelne kaputte Sprachdatei ignorieren */ }
            return null;
        }

        private static JsonDocument? ReadManifest(string folder)
        {
            try
            {
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
            }
            catch (Exception ex)
            {
                Log.Warn($"manifest.json in '{folder}' nicht lesbar", ex);
                return null;
            }
        }

        private static string? GetNestedString(JsonElement root, string obj, string prop) =>
            root.TryGetProperty(obj, out var o) && o.ValueKind == JsonValueKind.Object &&
            o.TryGetProperty(prop, out var p) && p.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(p.GetString())
                ? p.GetString()!.TrimStart('/')
                : null;

        /// <summary>Popup-Seite aus action (MV3), browser_action oder page_action (MV2).</summary>
        private static string? GetPopupFromManifest(JsonElement root) =>
            GetNestedString(root, "action", "default_popup") ??
            GetNestedString(root, "browser_action", "default_popup") ??
            GetNestedString(root, "page_action", "default_popup");

        public string? GetExtensionPopupPage(string extensionId, string? extensionName = null)
        {
            string? folder = FindExtensionFolder(extensionId, extensionName);
            if (folder == null) return null;

            using var doc = ReadManifest(folder);
            return doc == null ? null : GetPopupFromManifest(doc.RootElement);
        }

        public bool HasPopup(string extensionId, string? extensionName = null)
        {
            return !string.IsNullOrWhiteSpace(GetExtensionPopupPage(extensionId, extensionName));
        }

        public string? GetExtensionOptionsPage(string extensionId, string? extensionName = null)
        {
            string? folder = FindExtensionFolder(extensionId, extensionName);
            if (folder == null) return null;

            using var doc = ReadManifest(folder);
            if (doc == null) return null;
            var root = doc.RootElement;

            if (root.TryGetProperty("options_page", out var optPage) && optPage.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(optPage.GetString()))
            {
                return optPage.GetString()!.TrimStart('/');
            }

            // Fallback auf das Popup, falls keine eigene Optionsseite existiert
            return GetNestedString(root, "options_ui", "page") ?? GetPopupFromManifest(root);
        }

        public string? GetExtensionIconPath(string extensionId, string? extensionName = null)
        {
            string? folder = FindExtensionFolder(extensionId, extensionName);
            if (folder == null) return null;

            using var doc = ReadManifest(folder);
            if (doc == null) return null;
            var root = doc.RootElement;

            string[] sizes = { "32", "16", "48", "38", "19", "128" };
            var candidates = new List<JsonElement>();
            if (root.TryGetProperty("icons", out var icons)) candidates.Add(icons);
            foreach (var actionKey in new[] { "action", "browser_action", "page_action" })
            {
                if (root.TryGetProperty(actionKey, out var action) && action.ValueKind == JsonValueKind.Object &&
                    action.TryGetProperty("default_icon", out var defaultIcon))
                {
                    candidates.Add(defaultIcon);
                }
            }

            foreach (var candidate in candidates)
            {
                // default_icon darf auch ein einzelner Pfad sein
                if (candidate.ValueKind == JsonValueKind.String)
                {
                    string? path = ResolveIconFile(folder, candidate.GetString());
                    if (path != null) return path;
                    continue;
                }
                if (candidate.ValueKind != JsonValueKind.Object) continue;

                foreach (var size in sizes)
                {
                    if (candidate.TryGetProperty(size, out var iconProp) && iconProp.ValueKind == JsonValueKind.String)
                    {
                        string? path = ResolveIconFile(folder, iconProp.GetString());
                        if (path != null) return path;
                    }
                }
            }
            return null;
        }

        private static string? ResolveIconFile(string folder, string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;
            string full = Path.Combine(folder, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            // SVG kann WPF nicht direkt als Bild laden
            return File.Exists(full) && !full.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        #endregion

        private static void ExtractCrx(string crxPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            byte[] fileBytes = File.ReadAllBytes(crxPath);
            int zipOffset = FindZipSignatureOffset(fileBytes);

            if (zipOffset < 0)
            {
                throw new InvalidDataException(Tr.Get("Ext_ErrorInvalidCrx"));
            }

            using var ms = new MemoryStream(fileBytes, zipOffset, fileBytes.Length - zipOffset);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destinationDirectory, overwriteFiles: true);

            SanitizeUnpackedExtension(destinationDirectory);
        }

        private static void SanitizeUnpackedExtension(string directory)
        {
            try
            {
                // Chromium unpacked extension loader strictly forbids any directory starting with '_'
                // (e.g. '_metadata' which Chrome Web Store packages contain) EXCEPT '_locales'.
                foreach (var dir in Directory.GetDirectories(directory, "_*", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileName(dir);
                    if (!name.Equals("_locales", StringComparison.OrdinalIgnoreCase))
                    {
                        try { Directory.Delete(dir, true); } catch { }
                    }
                }

                foreach (var file in Directory.GetFiles(directory, "_*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch { }
        }

        private static int FindZipSignatureOffset(byte[] bytes)
        {
            // Standard ZIP local file header signature: PK\x03\x04
            byte[] signature = { 0x50, 0x4B, 0x03, 0x04 };
            int limit = Math.Min(bytes.Length - 4, 1024 * 1024); // Search in first 1MB

            for (int i = 0; i < limit; i++)
            {
                if (bytes[i] == signature[0] &&
                    bytes[i + 1] == signature[1] &&
                    bytes[i + 2] == signature[2] &&
                    bytes[i + 3] == signature[3])
                {
                    return i;
                }
            }

            return -1;
        }

        private static string? FindManifestDirectory(string baseDir)
        {
            if (File.Exists(Path.Combine(baseDir, "manifest.json")))
                return baseDir;

            foreach (var dir in Directory.GetDirectories(baseDir, "*", SearchOption.AllDirectories))
            {
                if (File.Exists(Path.Combine(dir, "manifest.json")))
                    return dir;
            }

            return null;
        }

        public static string GetWebStoreHelperScript()
        {
            return @"
(function() {
    if (!location.hostname.includes('chromewebstore.google.com')) return;

    function getExtensionId() {
        const match = location.pathname.match(/\/detail\/(?:[^\/]+\/)?([a-p]{32})/i);
        return match ? match[1] : null;
    }

    function getExtensionTitle() {
        const h1 = document.querySelector('h1');
        if (h1 && h1.innerText) return h1.innerText.trim();
        return document.title.replace('- Chrome Web Store', '').trim();
    }

    let banner = null;

    function updateBanner() {
        const extId = getExtensionId();
        if (!extId) {
            if (banner) { banner.remove(); banner = null; }
            return;
        }

        if (document.getElementById('echo-ext-install-banner')) return;

        banner = document.createElement('div');
        banner.id = 'echo-ext-install-banner';
        banner.style.cssText = `
            position: fixed;
            bottom: 24px;
            right: 24px;
            z-index: 9999999;
            background: linear-gradient(135deg, #18191D, #22252B);
            border: 1px solid #484D58;
            box-shadow: 0 10px 30px rgba(0,0,0,0.6), 0 0 15px rgba(214,217,222,0.15);
            border-radius: 12px;
            padding: 14px 20px;
            display: flex;
            align-items: center;
            gap: 14px;
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
            color: #E2E5EB;
            backdrop-filter: blur(12px);
            animation: echoSlideIn 0.3s cubic-bezier(0.16, 1, 0.3, 1);
        `;

        banner.innerHTML = `
            <style>
                @keyframes echoSlideIn { from { transform: translateY(30px); opacity: 0; } to { transform: translateY(0); opacity: 1; } }
                @keyframes echoSpin { to { transform: rotate(360deg); } }
                .echo-install-btn {
                    background: linear-gradient(135deg, #2D3139, #3B404B);
                    border: 1px solid #606775;
                    color: #FFFFFF;
                    font-weight: 600;
                    font-size: 13px;
                    padding: 8px 16px;
                    border-radius: 8px;
                    cursor: pointer;
                    display: inline-flex;
                    align-items: center;
                    gap: 8px;
                    transition: all 0.2s ease;
                }
                .echo-install-btn:hover {
                    background: linear-gradient(135deg, #3B404B, #4B5260);
                    border-color: #D6D9DE;
                    box-shadow: 0 4px 12px rgba(255,255,255,0.1);
                    transform: translateY(-1px);
                }
                .echo-install-btn:active { transform: translateY(0); }
                .echo-install-btn.success {
                    background: #1B4728 !important;
                    border-color: #2EA043 !important;
                    color: #56D364 !important;
                }
            </style>
            <div style=""display:flex; align-items:center; gap:10px;"">
                <div style=""width:28px; height:28px; border-radius:6px; background:#2D3139; display:flex; align-items:center; justify-content:center; border:1px solid #484D58;"">
                    <svg width=""16"" height=""16"" viewBox=""0 0 24 24"" fill=""none"" stroke=""#D6D9DE"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"">
                        <polygon points=""12 2 15.09 8.26 22 9.27 17 14.14 18.18 21.02 12 17.77 5.82 21.02 7 14.14 2 9.27 8.91 8.26 12 2""/>
                    </svg>
                </div>
                <div>
                    <div style=""font-size:13px; font-weight:600; color:#FFFFFF; max-width:200px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis;"">${getExtensionTitle()}</div>
                    <div style=""font-size:11px; color:#9BA1AC;"">Echo-Browser Erweiterung</div>
                </div>
            </div>
            <button id=""echo-btn-install"" class=""echo-install-btn"">
                <svg width=""14"" height=""14"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2""><path d=""M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4""/><polyline points=""7 10 12 15 17 10""/><line x1=""12"" y1=""15"" x2=""12"" y2=""3""/></svg>
                In Echo installieren
            </button>
        `;

        document.body.appendChild(banner);

        const btn = document.getElementById('echo-btn-install');
        btn.addEventListener('click', function() {
            btn.innerHTML = `<span style=""display:inline-block; animation:echoSpin 1s linear infinite;"">⏳</span> Wird heruntergeladen...`;
            btn.disabled = true;
            window.chrome.webview.postMessage({
                type: 'installExtensionFromWebStore',
                extensionId: extId,
                extensionName: getExtensionTitle()
            });
        });
    }

    // Intercept clicks on Google's own install buttons on the page
    document.addEventListener('click', function(e) {
        const extId = getExtensionId();
        if (!extId) return;

        const targetBtn = e.target.closest('button');
        if (targetBtn && targetBtn.id !== 'echo-btn-install') {
            const txt = (targetBtn.innerText || targetBtn.textContent || '').trim().toLowerCase();
            if (txt.includes('hinzufügen') || txt.includes('add to') || txt.includes('install')) {
                e.preventDefault();
                e.stopPropagation();
                const echoBtn = document.getElementById('echo-btn-install');
                if (echoBtn) {
                    echoBtn.click();
                } else {
                    window.chrome.webview.postMessage({
                        type: 'installExtensionFromWebStore',
                        extensionId: extId,
                        extensionName: getExtensionTitle()
                    });
                }
            }
        }
    }, true);

    setInterval(updateBanner, 600);
    window.addEventListener('popstate', updateBanner);

    window.onEchoExtensionInstallResult = function(success, message) {
        const btn = document.getElementById('echo-btn-install');
        if (btn) {
            btn.disabled = false;
            if (success) {
                btn.className = 'echo-install-btn success';
                btn.innerHTML = `✓ Installiert`;
            } else {
                btn.className = 'echo-install-btn';
                btn.innerHTML = `Fehler: ${message || 'Wiederholen'}`;
            }
        }
    };
})();
";
        }
    }
}
