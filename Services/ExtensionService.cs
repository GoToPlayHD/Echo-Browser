using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
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
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _extensionsDirectory = Path.Combine(appData, "EchoBrowser", "Extensions");
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

            return await profile.AddBrowserExtensionAsync(manifestFolder);
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

            return await profile.AddBrowserExtensionAsync(manifestFolder);
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
                Debug.WriteLine($"Failed to get extensions: {ex.Message}");
                return Array.Empty<CoreWebView2BrowserExtension>();
            }
        }

        public string? GetExtensionPopupPage(string extensionId, string? extensionName = null)
        {
            try
            {
                if (!Directory.Exists(_extensionsDirectory)) return null;

                var manifestFiles = Directory.GetFiles(_extensionsDirectory, "manifest.json", SearchOption.AllDirectories);
                foreach (var manifestPath in manifestFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string folder = Path.GetDirectoryName(manifestPath) ?? "";
                        bool isMatch = false;
                        if (!string.IsNullOrWhiteSpace(extensionId) && folder.Contains(extensionId, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(extensionName) && root.TryGetProperty("name", out var nameProp))
                        {
                            string mName = nameProp.GetString() ?? "";
                            if (mName.Equals(extensionName, StringComparison.OrdinalIgnoreCase) ||
                                folder.Contains(extensionName, StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                            }
                        }
                        else if (manifestFiles.Length == 1)
                        {
                            isMatch = true;
                        }

                        if (isMatch)
                        {
                            if (root.TryGetProperty("action", out var action) && action.TryGetProperty("default_popup", out var actionPopup) && !string.IsNullOrWhiteSpace(actionPopup.GetString()))
                            {
                                return actionPopup.GetString()!.TrimStart('/');
                            }
                            if (root.TryGetProperty("browser_action", out var bAction) && bAction.TryGetProperty("default_popup", out var bActionPopup) && !string.IsNullOrWhiteSpace(bActionPopup.GetString()))
                            {
                                return bActionPopup.GetString()!.TrimStart('/');
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding extension popup page: {ex.Message}");
            }
            return null;
        }

        public bool HasPopup(string extensionId, string? extensionName = null)
        {
            return !string.IsNullOrWhiteSpace(GetExtensionPopupPage(extensionId, extensionName));
        }

        public string? GetExtensionOptionsPage(string extensionId, string? extensionName = null)
        {
            try
            {
                if (!Directory.Exists(_extensionsDirectory)) return null;

                var manifestFiles = Directory.GetFiles(_extensionsDirectory, "manifest.json", SearchOption.AllDirectories);
                foreach (var manifestPath in manifestFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;

                        string folder = Path.GetDirectoryName(manifestPath) ?? "";
                        bool isMatch = false;
                        if (!string.IsNullOrWhiteSpace(extensionId) && folder.Contains(extensionId, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(extensionName) && root.TryGetProperty("name", out var nameProp))
                        {
                            string mName = nameProp.GetString() ?? "";
                            if (mName.Equals(extensionName, StringComparison.OrdinalIgnoreCase) ||
                                folder.Contains(extensionName, StringComparison.OrdinalIgnoreCase))
                            {
                                isMatch = true;
                            }
                        }
                        else if (manifestFiles.Length == 1)
                        {
                            isMatch = true;
                        }

                        if (isMatch)
                        {
                            if (root.TryGetProperty("options_page", out var optPage) && !string.IsNullOrWhiteSpace(optPage.GetString()))
                            {
                                return optPage.GetString()!.TrimStart('/');
                            }
                            if (root.TryGetProperty("options_ui", out var optUi) && optUi.TryGetProperty("page", out var optUiPage) && !string.IsNullOrWhiteSpace(optUiPage.GetString()))
                            {
                                return optUiPage.GetString()!.TrimStart('/');
                            }
                            // Fallback to popup if no dedicated options page exists
                            if (root.TryGetProperty("action", out var action) && action.TryGetProperty("default_popup", out var actionPopup) && !string.IsNullOrWhiteSpace(actionPopup.GetString()))
                            {
                                return actionPopup.GetString()!.TrimStart('/');
                            }
                            if (root.TryGetProperty("browser_action", out var bAction) && bAction.TryGetProperty("default_popup", out var bActionPopup) && !string.IsNullOrWhiteSpace(bActionPopup.GetString()))
                            {
                                return bActionPopup.GetString()!.TrimStart('/');
                            }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding options page: {ex.Message}");
            }
            return null;
        }

        public string? GetExtensionIconPath(string extensionId, string? extensionName = null)
        {
            try
            {
                if (!Directory.Exists(_extensionsDirectory)) return null;

                var manifestFiles = Directory.GetFiles(_extensionsDirectory, "manifest.json", SearchOption.AllDirectories);
                foreach (var manifestPath in manifestFiles)
                {
                    try
                    {
                        string folder = Path.GetDirectoryName(manifestPath) ?? "";
                        bool isMatch = false;
                        if (!string.IsNullOrWhiteSpace(extensionId) && folder.Contains(extensionId, StringComparison.OrdinalIgnoreCase))
                        {
                            isMatch = true;
                        }
                        else if (!string.IsNullOrWhiteSpace(extensionName))
                        {
                            string json = File.ReadAllText(manifestPath);
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("name", out var nProp) &&
                                (nProp.GetString()?.Equals(extensionName, StringComparison.OrdinalIgnoreCase) == true ||
                                 folder.Contains(extensionName, StringComparison.OrdinalIgnoreCase)))
                            {
                                isMatch = true;
                            }
                        }
                        else if (manifestFiles.Length == 1)
                        {
                            isMatch = true;
                        }

                        if (isMatch)
                        {
                            string json = File.ReadAllText(manifestPath);
                            using var doc = System.Text.Json.JsonDocument.Parse(json);
                            var root = doc.RootElement;
                            if (root.TryGetProperty("icons", out var icons))
                            {
                                string[] sizes = { "32", "16", "48", "128" };
                                foreach (var s in sizes)
                                {
                                    if (icons.TryGetProperty(s, out var iconProp))
                                    {
                                        string relPath = iconProp.GetString() ?? "";
                                        string full = Path.Combine(folder, relPath.Replace('/', Path.DirectorySeparatorChar));
                                        if (File.Exists(full)) return full;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

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
