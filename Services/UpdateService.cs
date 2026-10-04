using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EchoBrowser.Models;
using Velopack;
using Velopack.Sources;

namespace EchoBrowser.Services
{
    public enum UpdateStatus
    {
        Idle,
        Checking,
        UpdateAvailable,
        Downloading,
        ReadyToRestart,
        UpToDate,
        Error,
        NotInstalled
    }

    public sealed class UpdateStatusEventArgs : EventArgs
    {
        public UpdateStatus Status { get; }
        public string Message { get; }
        public int Progress { get; }
        public string? AvailableVersion { get; }

        public UpdateStatusEventArgs(UpdateStatus status, string message, int progress = 0, string? availableVersion = null)
        {
            Status = status;
            Message = message;
            Progress = progress;
            AvailableVersion = availableVersion;
        }
    }

    public sealed class UpdateCheckResult
    {
        public bool Success { get; }
        public UpdateStatus Status { get; }
        public string Message { get; }
        public string? AvailableVersion { get; }
        public Exception? Error { get; }

        public UpdateCheckResult(bool success, UpdateStatus status, string message, string? availableVersion = null, Exception? error = null)
        {
            Success = success;
            Status = status;
            Message = message;
            AvailableVersion = availableVersion;
            Error = error;
        }
    }

    /// <summary>
    /// Zentrale Update-Infrastruktur mit dem Velopack-Framework.
    /// Sucht im Hintergrund automatisch nach Updates, lädt Delta-Patches herunter
    /// und meldet den Status an Menü, Einstellungen und Dialoge.
    /// </summary>
    public sealed class UpdateService : IDisposable
    {
        private static readonly Lazy<UpdateService> _instance = new(() => new UpdateService());
        public static UpdateService Instance => _instance.Value;

        private readonly SemaphoreSlim _lock = new(1, 1);
        private Timer? _autoCheckTimer;
        private CancellationTokenSource? _downloadCts;
        private UpdateInfo? _pendingUpdate;

        public const string DefaultUpdateUrl = "https://github.com/GoToPlayHD/Echo-Browser";

        public string UpdateUrl => !string.IsNullOrWhiteSpace(AppSettingsService.Instance.Settings.UpdateUrl)
            ? AppSettingsService.Instance.Settings.UpdateUrl
            : DefaultUpdateUrl;

        public UpdateStatus Status { get; private set; } = UpdateStatus.Idle;
        public string StatusMessage { get; private set; } = string.Empty;
        public int DownloadProgress { get; private set; } = 0;
        public string? AvailableVersion { get; private set; }
        public string? LastError { get; private set; }
        public DateTime? LastCheckedUtc { get; private set; }

        public bool IsUpdateReadyToRestart => Status == UpdateStatus.ReadyToRestart && _pendingUpdate != null;

        public event EventHandler<UpdateStatusEventArgs>? StatusChanged;

        private UpdateService()
        {
            // Initialen Status setzen
            StatusMessage = Tr.Get("Update_Idle");
        }

        private string? _cachedCurrentVersion;
        private bool? _cachedIsInstalled;

        /// <summary>
        /// Liefert die aktuell ausgeführte Version der App (entweder aus Velopack oder der Assembly).
        /// </summary>
        public string CurrentVersion
        {
            get
            {
                if (_cachedCurrentVersion != null) return _cachedCurrentVersion;

                try
                {
                    var source = CreateUpdateSource();
                    var mgr = new UpdateManager(source);
                    if (mgr.IsInstalled && mgr.CurrentVersion != null)
                    {
                        _cachedCurrentVersion = mgr.CurrentVersion.ToString();
                        return _cachedCurrentVersion;
                    }
                }
                catch
                {
                    // Fallback auf Assembly
                }

                var ver = Assembly.GetExecutingAssembly().GetName().Version;
                _cachedCurrentVersion = ver != null ? $"{ver.Major}.{ver.Minor}.{ver.Build}" : "1.2.0";
                return _cachedCurrentVersion;
            }
        }

        /// <summary>
        /// Gibt an, ob die Anwendung über Velopack installiert wurde.
        /// </summary>
        public bool IsInstalled
        {
            get
            {
                if (_cachedIsInstalled.HasValue) return _cachedIsInstalled.Value;

                try
                {
                    var source = CreateUpdateSource();
                    var mgr = new UpdateManager(source);
                    _cachedIsInstalled = mgr.IsInstalled;
                    return _cachedIsInstalled.Value;
                }
                catch
                {
                    _cachedIsInstalled = false;
                    return false;
                }
            }
        }

        /// <summary>
        /// Erstellt die passende Update-Quelle (Standard: GitHub Releases von https://github.com/GoToPlayHD/Echo-Browser oder benutzerdefinierte Update-URL).
        /// </summary>
        private static IUpdateSource CreateUpdateSource()
        {
            var settings = AppSettingsService.Instance.Settings;
            string url = !string.IsNullOrWhiteSpace(settings.UpdateUrl) ? settings.UpdateUrl : DefaultUpdateUrl;

            // GitHub-Repositories werden über die GitHub Releases API abgefragt, andere URLs als SimpleWebSource
            if (url.Contains("github.com", StringComparison.OrdinalIgnoreCase))
            {
                return new GithubSource(url, accessToken: null, prerelease: settings.CheckPrereleaseUpdates);
            }

            return new SimpleWebSource(url);
        }

        /// <summary>
        /// Startet den Timer für die periodische Hintergrundprüfung.
        /// Startet nach einer kurzen Verzögerung von 12 Sekunden (für einen schnellen Browserstart)
        /// und prüft danach alle 4 Stunden. Idempotent: Mehrfache Aufrufe erneuern den Timer nicht unnötig.
        /// </summary>
        public void StartAutoCheckTimer()
        {
            if (_autoCheckTimer != null) return;

            _autoCheckTimer = new Timer(async _ =>
            {
                try
                {
                    if (AppSettingsService.Instance.Settings.AutoCheckForUpdates)
                    {
                        await CheckForUpdatesAsync(isManualCheck: false).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("Automatischer Check fehlgeschlagen", ex);
                }
            }, null, TimeSpan.FromSeconds(12), TimeSpan.FromHours(4));
        }

        public void StopAutoCheckTimer()
        {
            _autoCheckTimer?.Dispose();
            _autoCheckTimer = null;
        }

        /// <summary>
        /// Sucht nach Updates, lädt Delta-Patches bei Fund automatisch herunter und
        /// bereitet die Installation für den nächsten Neustart oder sofortige Ausführung vor.
        /// </summary>
        public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool isManualCheck = false)
        {
            // Wenn bereits ein Update heruntergeladen bereitliegt und es sich nur um den periodischen Check handelt
            if (IsUpdateReadyToRestart && !isManualCheck)
            {
                return new UpdateCheckResult(true, Status, StatusMessage, AvailableVersion);
            }

            // Wenn bereits ein Check oder Download läuft, nicht parallel ausführen
            if (!await _lock.WaitAsync(0).ConfigureAwait(false))
            {
                return new UpdateCheckResult(false, Status, StatusMessage);
            }

            try
            {
                Status = UpdateStatus.Checking;
                StatusMessage = Tr.Get("Update_Checking");
                DownloadProgress = 0;
                NotifyStatusChanged();

                var source = CreateUpdateSource();
                var mgr = new UpdateManager(source);

                if (!mgr.IsInstalled)
                {
                    Status = UpdateStatus.NotInstalled;
                    StatusMessage = Tr.Get("Update_DevMode");
                    NotifyStatusChanged();
                    return new UpdateCheckResult(false, UpdateStatus.NotInstalled, StatusMessage);
                }

                // 1. Suche nach neueren Versionen
                var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(false);
                LastCheckedUtc = DateTime.UtcNow;

                if (updateInfo == null)
                {
                    Status = UpdateStatus.UpToDate;
                    StatusMessage = Tr.Format("Update_UpToDate", CurrentVersion);
                    NotifyStatusChanged();
                    return new UpdateCheckResult(true, UpdateStatus.UpToDate, StatusMessage);
                }

                // 2. Update gefunden -> Zielversion ermitteln
                _pendingUpdate = updateInfo;
                AvailableVersion = updateInfo.TargetFullRelease.Version.ToString();
                Status = UpdateStatus.UpdateAvailable;
                StatusMessage = Tr.Format("Update_Available", AvailableVersion);
                NotifyStatusChanged();

                // 3. Automatischer Download von Delta-Patches (Velopack nutzt automatisch Deltas, falls vorhanden)
                Status = UpdateStatus.Downloading;
                DownloadProgress = 0;
                StatusMessage = Tr.Format("Update_Downloading", AvailableVersion);
                NotifyStatusChanged();

                _downloadCts?.Dispose();
                _downloadCts = new CancellationTokenSource();

                int lastReportedProgress = -1;
                DateTime lastReportedTime = DateTime.MinValue;

                await mgr.DownloadUpdatesAsync(updateInfo, progress =>
                {
                    var now = DateTime.UtcNow;
                    if (progress == 100 || progress - lastReportedProgress >= 2 || (now - lastReportedTime).TotalMilliseconds >= 250)
                    {
                        lastReportedProgress = progress;
                        lastReportedTime = now;
                        DownloadProgress = progress;
                        StatusMessage = Tr.Format("Update_DownloadingPercent", AvailableVersion, progress);
                        NotifyStatusChanged();
                    }
                }, _downloadCts.Token).ConfigureAwait(false);

                // 4. Update heruntergeladen und einsatzbereit.
                // Hinweis: WaitExitThenApplyUpdates wird nicht sofort gerufen, da Velopack einen 60-Sekunden-Timeout hat.
                // Stattdessen wird es bei App.Exit (ApplyPendingUpdateOnExit) oder bei manuellem Neustart angewendet.
                Status = UpdateStatus.ReadyToRestart;
                StatusMessage = Tr.Format("Update_Ready", AvailableVersion);
                NotifyStatusChanged();

                return new UpdateCheckResult(true, UpdateStatus.ReadyToRestart, StatusMessage, AvailableVersion);
            }
            catch (OperationCanceledException)
            {
                Status = UpdateStatus.Idle;
                StatusMessage = Tr.Get("Update_Cancelled");
                NotifyStatusChanged();
                return new UpdateCheckResult(false, UpdateStatus.Idle, StatusMessage);
            }
            catch (Exception ex)
            {
                Log.Warn("Update-Prüfung fehlgeschlagen", ex);
                Status = UpdateStatus.Error;
                LastError = ex.Message;
                string detail = ex.Message;
                if (detail.Contains("404") || (ex.InnerException?.Message.Contains("404") ?? false))
                {
                    detail = "Keine kompatiblen Release-Dateien auf GitHub gefunden (404).";
                }
                StatusMessage = Tr.Format("Update_Error", detail);
                NotifyStatusChanged();
                return new UpdateCheckResult(false, UpdateStatus.Error, StatusMessage, null, ex);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Wendet das heruntergeladene Update an und startet den Browser sofort neu.
        /// Sichert vor dem Beenden die Sitzung und markiert einen sauberen Exit,
        /// damit der Browser nach dem Neustart nahtlos ohne Absturzwarnung fortfährt.
        /// </summary>
        public void RestartAndApplyUpdate()
        {
            if (_pendingUpdate == null) return;

            try
            {
                // Sitzung sofort synchron sichern und sauberen Exit markieren
                SessionService.Instance.SaveNow();
                SessionService.Instance.MarkCleanExit();

                var source = CreateUpdateSource();
                var mgr = new UpdateManager(source);
                mgr.ApplyUpdatesAndRestart(_pendingUpdate.TargetFullRelease, new[] { "--restored-after-update" });
            }
            catch (Exception ex)
            {
                Log.Warn("Fehler beim Neustart mit Update", ex);
                Status = UpdateStatus.Error;
                LastError = ex.Message;
                StatusMessage = Tr.Format("Update_Error", ex.Message);
                NotifyStatusChanged();
            }
        }

        /// <summary>
        /// Wird beim normalen Schließen des Browsers aufgerufen:
        /// Wenn ein Update heruntergeladen wurde, wartet Velopack auf das vollständige Beenden des Prozesses
        /// und installiert das Update im Hintergrund.
        /// </summary>
        public void ApplyPendingUpdateOnExit()
        {
            if (!IsUpdateReadyToRestart || _pendingUpdate == null) return;

            try
            {
                var source = CreateUpdateSource();
                var mgr = new UpdateManager(source);
                if (mgr.IsInstalled)
                {
                    mgr.WaitExitThenApplyUpdates(_pendingUpdate.TargetFullRelease, silent: true, restart: false);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Ausstehendes Update konnte beim Beenden nicht vorbereitet werden", ex);
            }
        }

        private void NotifyStatusChanged()
        {
            try
            {
                StatusChanged?.Invoke(this, new UpdateStatusEventArgs(Status, StatusMessage, DownloadProgress, AvailableVersion));
            }
            catch
            {
                // Ignoriere Handler-Fehler
            }
        }

        public void Dispose()
        {
            _autoCheckTimer?.Dispose();
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _lock.Dispose();
        }
    }
}
