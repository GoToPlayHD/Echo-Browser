using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser.Services
{
    /// <summary>Stand der Erweiterungs-Updates für das Erweiterungen-Flyout.</summary>
    /// <param name="PendingCount">Updates, die beim nächsten Start eingespielt werden.</param>
    /// <param name="AwaitingConsentCount">Updates, die neue Berechtigungen brauchen und noch nicht bestätigt sind.</param>
    public sealed record ExtensionUpdateStatus(DateTime? LastCheckUtc, int PendingCount, int AwaitingConsentCount, string? Error, bool IsChecking);

    /// <summary>Erlaubtes Update: die entpackte neue Version ersetzt beim nächsten Start den Ordner der alten.</summary>
    public sealed record PendingExtensionUpdate(string TargetFolder, string StagedRoot, string StagedManifestFolder, string StoreId, string Version);

    /// <summary>
    /// Updates für Erweiterungen aus dem Chrome Web Store (Audit S1). Ablauf: beim Update-Server nach neuen Versionen
    /// fragen, laden (Prüfsumme, Größengrenze, ID im CRX-Kopf), in einen Zwischenordner entpacken und beim nächsten
    /// Start – bevor der Browser-Prozess die Dateien sperrt – in den Ordner der alten Version tauschen. Der Ordner
    /// bleibt derselbe: WebView2 berechnet die ID entpackter Erweiterungen aus dem Pfad, und an der ID hängen ihre
    /// gespeicherten Daten (Anmeldungen, Einstellungen).
    /// </summary>
    public partial class ExtensionService
    {
        private const long MaxCrxBytes = 100L * 1024 * 1024;

        // Ein HttpClient für alle Downloads (nicht einer pro Installation)
        private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
        {
            Timeout = TimeSpan.FromSeconds(120)
        };

        private readonly object _updateLock = new();
        private Dictionary<string, string>? _storeIds;              // Manifest-Ordner -> Store-ID
        private List<PendingExtensionUpdate>? _pendingUpdates;
        private readonly HashSet<string> _awaitingConsent = new(StringComparer.OrdinalIgnoreCase);    // "id@version"
        private readonly HashSet<string> _declinedUpdates = new(StringComparer.OrdinalIgnoreCase);    // in dieser Sitzung abgelehnt
        private DateTime? _lastUpdateCheckUtc;
        private string? _lastUpdateError;
        private bool _isCheckingUpdates;

        internal string UpdateStagingDirectory => Path.Combine(AppPaths.DataFolder, "ExtensionUpdates");
        private string StoreIdsFilePath => Path.Combine(_extensionsDirectory, "extension-sources.json");
        private string PendingUpdatesFilePath => Path.Combine(_extensionsDirectory, "extension-updates.json");

        public ExtensionUpdateStatus UpdateStatus
        {
            get
            {
                lock (_updateLock)
                {
                    return new ExtensionUpdateStatus(_lastUpdateCheckUtc, PendingUpdates.Count, _awaitingConsent.Count, _lastUpdateError, _isCheckingUpdates);
                }
            }
        }

        #region Store-IDs und vorgemerkte Updates (JSON neben extensions-index.json)

        private Dictionary<string, string> StoreIds => _storeIds ??= LoadJson<Dictionary<string, string>>(StoreIdsFilePath) is { } loaded
            ? new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<PendingExtensionUpdate> PendingUpdates => _pendingUpdates ??= LoadJson<List<PendingExtensionUpdate>>(PendingUpdatesFilePath) ?? new();

        private static T? LoadJson<T>(string path) where T : class
        {
            try
            {
                return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : null;
            }
            catch (Exception ex)
            {
                Log.Warn($"{Path.GetFileName(path)} konnte nicht gelesen werden", ex);
                return null;
            }
        }

        private static void SaveJson(string path, object value)
        {
            try
            {
                AtomicFile.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                Log.Warn($"{Path.GetFileName(path)} konnte nicht gespeichert werden", ex);
            }
        }

        private void RememberStoreId(string manifestFolder, string storeId)
        {
            lock (_updateLock)
            {
                StoreIds[Path.GetFullPath(manifestFolder)] = storeId;
                SaveJson(StoreIdsFilePath, StoreIds);
            }
        }

        /// <summary>Store-ID einer installierten Erweiterung: gemerkt bei der Installation, sonst aus dem Ordnernamen.</summary>
        internal string? GetStoreId(string manifestFolder)
        {
            string full = Path.GetFullPath(manifestFolder);
            lock (_updateLock)
            {
                if (StoreIds.TryGetValue(full, out var known)) return known;
            }

            if (!IsInside(_extensionsDirectory, full)) return null;
            string topLevel = Path.GetRelativePath(_extensionsDirectory, full).Split(Path.DirectorySeparatorChar)[0];
            return ExtensionUpdates.StoreIdFromFolderName(topLevel);
        }

        /// <summary>Erweiterung entfernt: gemerkte Store-ID und vorgemerkte Updates verwerfen.</summary>
        private void ForgetUpdateData(string manifestFolder)
        {
            string full = Path.GetFullPath(manifestFolder);
            lock (_updateLock)
            {
                if (StoreIds.Remove(full)) SaveJson(StoreIdsFilePath, StoreIds);

                var obsolete = PendingUpdates.Where(p => p.TargetFolder.Equals(full, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var update in obsolete)
                {
                    PendingUpdates.Remove(update);
                    DeleteExtractedFolder(update.StagedRoot);
                }
                if (obsolete.Count > 0) SaveJson(PendingUpdatesFilePath, PendingUpdates);
            }
        }

        /// <summary>Update vormerken; eine ältere vorgemerkte Version derselben Erweiterung wird ersetzt.</summary>
        internal void AddPendingUpdate(PendingExtensionUpdate update)
        {
            lock (_updateLock)
            {
                foreach (var older in PendingUpdates.Where(p => p.TargetFolder.Equals(update.TargetFolder, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    PendingUpdates.Remove(older);
                    DeleteExtractedFolder(older.StagedRoot);
                }
                PendingUpdates.Add(update);
                SaveJson(PendingUpdatesFilePath, PendingUpdates);
            }
        }

        private bool IsPending(string folder, string version)
        {
            lock (_updateLock)
            {
                return PendingUpdates.Any(p => p.TargetFolder.Equals(folder, StringComparison.OrdinalIgnoreCase) &&
                                               !ExtensionUpdates.IsNewerVersion(version, p.Version));
            }
        }

        #endregion

        #region Beim Start einspielen

        /// <summary>
        /// Erlaubte Updates einspielen. Nur vor dem Start des Browser-Prozesses aufrufen – danach sind die Dateien gesperrt.
        /// Klappt der Tausch nicht (Datei noch gesperrt), bleibt die alte Version und das Update vorgemerkt.
        /// </summary>
        public void ApplyPendingUpdates()
        {
            List<PendingExtensionUpdate> pending;
            lock (_updateLock)
            {
                pending = PendingUpdates.ToList();
            }

            var remaining = new List<PendingExtensionUpdate>();
            foreach (var update in pending)
            {
                try
                {
                    // Erweiterung inzwischen entfernt oder Update-Dateien weg: nichts einzuspielen
                    if (!Directory.Exists(update.TargetFolder) || !Directory.Exists(update.StagedManifestFolder) ||
                        !IsInside(_extensionsDirectory, Path.GetFullPath(update.TargetFolder)))
                    {
                        DeleteExtractedFolder(update.StagedRoot);
                        continue;
                    }

                    SwapFolder(update.TargetFolder, update.StagedManifestFolder);
                    DeleteExtractedFolder(update.StagedRoot);
                    Log.Info($"Erweiterung {update.StoreId} auf Version {update.Version} aktualisiert");
                }
                catch (Exception ex)
                {
                    Log.Warn($"Update der Erweiterung {update.StoreId} konnte nicht eingespielt werden", ex);
                    remaining.Add(update);
                }
            }

            lock (_updateLock)
            {
                _pendingUpdates = remaining;
                if (pending.Count > 0) SaveJson(PendingUpdatesFilePath, remaining);
            }
            DeleteUpdateLeftovers(remaining);
        }

        /// <summary>
        /// <paramref name="replacement"/> an die Stelle von <paramref name="target"/> setzen. Erst die alte Version
        /// wegbenennen, dann die neue hinein; scheitert das, kommt die alte zurück.
        /// </summary>
        internal static void SwapFolder(string target, string replacement)
        {
            string backup = target.TrimEnd(Path.DirectorySeparatorChar) + ".echo-old-" + Guid.NewGuid().ToString("N")[..8];
            Directory.Move(target, backup);
            try
            {
                Directory.Move(replacement, target);
            }
            catch
            {
                Directory.Move(backup, target);
                throw;
            }

            try
            {
                Directory.Delete(backup, recursive: true);
            }
            catch (Exception ex)
            {
                Log.Warn("Alte Version der Erweiterung konnte nicht gelöscht werden", ex);
            }
        }

        /// <summary>Reste abgebrochener Updates: alte Versionen (*.echo-old-*) und nicht mehr vorgemerkte Zwischenordner.</summary>
        private void DeleteUpdateLeftovers(IReadOnlyCollection<PendingExtensionUpdate> keep)
        {
            try
            {
                if (Directory.Exists(_extensionsDirectory))
                {
                    foreach (var dir in Directory.GetDirectories(_extensionsDirectory, "*.echo-old-*", SearchOption.AllDirectories))
                    {
                        DeleteExtractedFolder(dir);
                    }
                }

                if (Directory.Exists(UpdateStagingDirectory))
                {
                    var kept = keep.Select(p => Path.GetFullPath(p.StagedRoot)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var entry in Directory.GetFileSystemEntries(UpdateStagingDirectory))
                    {
                        if (kept.Contains(Path.GetFullPath(entry))) continue;
                        if (Directory.Exists(entry)) DeleteExtractedFolder(entry);
                        else File.Delete(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Reste von Erweiterungs-Updates konnten nicht gelöscht werden", ex);
            }
        }

        #endregion

        #region Prüfen und laden

        /// <summary>
        /// Nach neuen Versionen der installierten Web-Store-Erweiterungen fragen, sie laden und für den nächsten Start
        /// vormerken. Braucht eine neue Version mehr Rechte, entscheidet <paramref name="confirmNewPermissions"/>
        /// (null: nicht nachfragen, nur als »wartet auf Zustimmung« zählen – für die automatische Prüfung).
        /// Ordner, die der Nutzer selbst geladen hat, werden nie verändert.
        /// </summary>
        /// <returns>Anzahl der neu vorgemerkten Updates.</returns>
        public async Task<int> CheckForUpdatesAsync(CoreWebView2Profile profile, Func<PreparedExtension, bool>? confirmNewPermissions)
        {
            lock (_updateLock)
            {
                if (_isCheckingUpdates) return 0;
                _isCheckingUpdates = true;
                _lastUpdateError = null;
                _awaitingConsent.Clear();
            }

            int staged = 0;
            try
            {
                var installed = new List<(string Folder, string StoreId, string Version)>();
                foreach (var ext in await GetInstalledExtensionsAsync(profile))
                {
                    string? folder = FindExtensionFolder(ext.Id, ext.Name);
                    if (folder == null || !IsInside(_extensionsDirectory, Path.GetFullPath(folder))) continue;
                    if (GetStoreId(folder) is not { } storeId || ReadManifestVersion(folder) is not { } version) continue;
                    if (installed.Any(i => i.StoreId == storeId)) continue;
                    installed.Add((folder, storeId, version));
                }
                if (installed.Count == 0) return 0;

                string browserVersion = GetBrowserVersion();
                string url = ExtensionUpdates.BuildUpdateCheckUrl(installed.Select(i => (i.StoreId, i.Version)), browserVersion);
                string xml;
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd(UserAgent(browserVersion));
                    using var response = await Http.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    xml = await response.Content.ReadAsStringAsync();
                }

                foreach (var offer in ExtensionUpdates.ParseUpdateCheck(xml))
                {
                    var match = installed.FirstOrDefault(i => i.StoreId == offer.Id);
                    if (match.Folder == null || !ExtensionUpdates.IsNewerVersion(offer.Version, match.Version)) continue;
                    if (IsPending(match.Folder, offer.Version)) continue;
                    lock (_updateLock)
                    {
                        if (_declinedUpdates.Contains($"{offer.Id}@{offer.Version}")) continue;
                    }

                    try
                    {
                        if (await StageUpdateAsync(match.Folder, offer, browserVersion, confirmNewPermissions)) staged++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"Update der Erweiterung {offer.Id} auf {offer.Version} fehlgeschlagen", ex);
                    }
                }
                return staged;
            }
            catch (Exception ex)
            {
                Log.Warn("Erweiterungs-Updates konnten nicht geprüft werden", ex);
                lock (_updateLock)
                {
                    _lastUpdateError = ex.Message;
                }
                return staged;
            }
            finally
            {
                lock (_updateLock)
                {
                    _isCheckingUpdates = false;
                    _lastUpdateCheckUtc = DateTime.UtcNow;
                }
            }
        }

        /// <summary>Neue Version laden, prüfen, entpacken und – mit Zustimmung bei neuen Rechten – vormerken.</summary>
        private async Task<bool> StageUpdateAsync(string folder, ExtensionUpdateOffer offer, string browserVersion,
            Func<PreparedExtension, bool>? confirmNewPermissions)
        {
            Directory.CreateDirectory(UpdateStagingDirectory);
            string crxPath = Path.Combine(UpdateStagingDirectory, $"{offer.Id}_{Guid.NewGuid():N}.crx");
            PreparedExtension prepared;
            try
            {
                await DownloadCrxAsync(offer.Codebase, crxPath, browserVersion, offer.Sha256);
                prepared = PrepareFromCrx(crxPath, UpdateStagingDirectory);
            }
            finally
            {
                try { File.Delete(crxPath); } catch { /* Rest räumt der nächste Start auf */ }
            }

            if (prepared.StoreId != offer.Id)
            {
                // Ohne passende ID im CRX3-Kopf nie eine installierte Erweiterung ersetzen
                DiscardPrepared(prepared);
                throw new InvalidDataException(Tr.Get("Ext_ErrorIdMismatch"));
            }

            var installedPermissions = ReadPermissions(folder);
            if (installedPermissions == null || ExtensionUpdates.NeedsNewConsent(installedPermissions, prepared.Permissions))
            {
                string key = $"{offer.Id}@{offer.Version}";
                if (confirmNewPermissions == null)
                {
                    DiscardPrepared(prepared);
                    lock (_updateLock) _awaitingConsent.Add(key);
                    return false;
                }
                if (!confirmNewPermissions(prepared))
                {
                    DiscardPrepared(prepared);
                    lock (_updateLock) _declinedUpdates.Add(key);
                    return false;
                }
            }

            AddPendingUpdate(new PendingExtensionUpdate(Path.GetFullPath(folder), prepared.RootFolder, prepared.ManifestFolder, offer.Id, offer.Version));
            return true;
        }

        /// <summary>.crx laden: direkt in die Datei, höchstens 100 MB, mit Prüfsumme, wenn der Server eine nennt.</summary>
        private static async Task DownloadCrxAsync(string url, string targetPath, string browserVersion, string? expectedSha256)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.UserAgent.ParseAdd(UserAgent(browserVersion));
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxCrxBytes)
                    throw new InvalidDataException(Tr.Get("Ext_ErrorCrxTooLarge"));

                await using var source = await response.Content.ReadAsStreamAsync();
                await using var file = File.Create(targetPath);
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > MaxCrxBytes) throw new InvalidDataException(Tr.Get("Ext_ErrorCrxTooLarge"));
                    await file.WriteAsync(buffer.AsMemory(0, read));
                }

                // Leere Antwort: Google hat keine passende Version (Erweiterung braucht ein neueres Chromium oder wurde entfernt)
                if (total == 0) throw new InvalidOperationException(Tr.Format("Ext_ErrorNoCompatibleVersion", browserVersion));
            }

            if (expectedSha256 != null)
            {
                await using var file = File.OpenRead(targetPath);
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(file)).ToLowerInvariant();
                if (actual != expectedSha256) throw new InvalidDataException(Tr.Get("Ext_ErrorChecksum"));
            }
        }

        // Google liefert nur Versionen, deren minimum_chrome_version zur gemeldeten Chromium-Version passt
        private static string UserAgent(string browserVersion) =>
            $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{browserVersion} Safari/537.36";

        private static JsonDocument? ParseManifest(string folder)
        {
            try
            {
                var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
                return JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")), options);
            }
            catch (Exception ex)
            {
                Log.Warn($"manifest.json in '{folder}' nicht lesbar", ex);
                return null;
            }
        }

        private static string? ReadManifestVersion(string folder)
        {
            using var manifest = ParseManifest(folder);
            return manifest != null && manifest.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }

        private static ExtensionPermissionSummary? ReadPermissions(string folder)
        {
            using var manifest = ParseManifest(folder);
            return manifest == null ? null : ExtensionPermissions.Describe(manifest.RootElement);
        }

        #endregion

        /// <summary>Liegt <paramref name="fullPath"/> (absolut) innerhalb von <paramref name="directory"/>?</summary>
        private static bool IsInside(string directory, string fullPath)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
    }
}
