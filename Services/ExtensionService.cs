using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
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
                throw new FileNotFoundException("CRX-Datei nicht gefunden.", crxPath);

            string extName = Path.GetFileNameWithoutExtension(crxPath);
            string targetFolder = Path.Combine(_extensionsDirectory, $"{extName}_{Guid.NewGuid():N}");

            ExtractCrx(crxPath, targetFolder);

            // Find directory containing manifest.json
            string? manifestFolder = FindManifestDirectory(targetFolder);
            if (manifestFolder == null)
            {
                throw new InvalidOperationException("manifest.json wurde in der entpackten Erweiterung nicht gefunden.");
            }

            return await profile.AddBrowserExtensionAsync(manifestFolder);
        }

        public async Task<CoreWebView2BrowserExtension?> InstallExtensionFromFolderAsync(CoreWebView2Profile profile, string folderPath)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Ordner nicht gefunden: {folderPath}");

            string? manifestFolder = FindManifestDirectory(folderPath);
            if (manifestFolder == null)
            {
                throw new InvalidOperationException("manifest.json wurde im ausgewählten Ordner nicht gefunden.");
            }

            return await profile.AddBrowserExtensionAsync(manifestFolder);
        }

        public async Task<IReadOnlyList<CoreWebView2BrowserExtension>> GetInstalledExtensionsAsync(CoreWebView2Profile profile)
        {
            try
            {
                var list = await profile.GetBrowserExtensionsAsync();
                return list ?? Array.Empty<CoreWebView2BrowserExtension>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to get extensions: {ex.Message}");
                return Array.Empty<CoreWebView2BrowserExtension>();
            }
        }

        private static void ExtractCrx(string crxPath, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);

            byte[] fileBytes = File.ReadAllBytes(crxPath);
            int zipOffset = FindZipSignatureOffset(fileBytes);

            if (zipOffset < 0)
            {
                throw new InvalidDataException("Ungültiges CRX-Format: Kein ZIP-Archiv gefunden.");
            }

            using var ms = new MemoryStream(fileBytes, zipOffset, fileBytes.Length - zipOffset);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            archive.ExtractToDirectory(destinationDirectory, overwriteFiles: true);
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
    }
}
