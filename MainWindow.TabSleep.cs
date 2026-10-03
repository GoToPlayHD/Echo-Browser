using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views.Popups;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>Tab-Schlaf (einfrieren, verwerfen, aufwecken) und Speicheranzeige (Leistungs-Panel, Tab-Infokarte).</summary>
    public partial class MainWindow
    {
        #region Tab-Schlaf & Speicher

        private DispatcherTimer? _sleepTimer;

        // Alle Fenster teilen sich eine WebView2-Umgebung: eine Messung gilt für alle
        private static Task<PerformanceSnapshot?>? _memoryMeasurement;
        private static DateTime _lastMemoryMeasurement = DateTime.MinValue;
        private static PerformanceSnapshot? _lastSnapshot;

        private void InitializeTabSleep()
        {
            performancePanel.SleepNowRequested += async () => await SleepInactiveTabsNowAsync();
            performancePanel.SleepMinutesChanged += minutes =>
            {
                AppSettingsService.Instance.Settings.TabSleepMinutes = minutes;
                AppSettingsService.Instance.Save();
            };
            performancePanel.TabChosen += tab =>
            {
                popupPerformance.IsOpen = false;
                if (Tabs.Contains(tab)) SelectTab(tab);
                else if (FindTabOwner(tab) is MainWindow owner) { owner.SelectTab(tab); owner.BringToFront(); }
            };

            _sleepTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMinutes(1) };
            _sleepTimer.Tick += async (s, e) => await CheckSleepingTabsAsync();
            _sleepTimer.Start();
            Closed += (s, e) => _sleepTimer.Stop();
        }

        private static MainWindow? FindTabOwner(BrowserTab tab) =>
            Application.Current.Windows.OfType<MainWindow>().FirstOrDefault(w => w.Tabs.Contains(tab));

        private static TabSleepState SleepStateOf(BrowserTab tab) => new(
            IsVisible: tab.IsActive || tab.WebView?.Visibility == Visibility.Visible,
            HasWebView: tab.WebView?.CoreWebView2 != null,
            IsSleeping: tab.IsSleeping,
            IsDiscarded: tab.IsDiscarded,
            IsAudible: tab.IsAudible,
            IsLoading: tab.IsLoading,
            SleepRefused: tab.SleepRefused,
            LastActiveAt: tab.LastActiveAt);

        /// <summary>Jede Minute: lange inaktive Tabs einfrieren bzw. verwerfen.</summary>
        private async Task CheckSleepingTabsAsync()
        {
            int minutes = AppSettingsService.Instance.Settings.TabSleepMinutes;
            var now = DateTime.Now;

            foreach (var tab in Tabs.ToList())
            {
                switch (TabSleepPolicy.Decide(SleepStateOf(tab), now, minutes))
                {
                    case TabSleepAction.Sleep:
                        await SleepTabAsync(tab);
                        break;
                    case TabSleepAction.Discard:
                        DiscardTab(tab);
                        break;
                }
            }

            if (popupPerformance.IsOpen) await RefreshPerformancePanelAsync();
        }

        /// <summary>
        /// Tab einfrieren: Skripte und Timer stehen still, der Speicherbedarf sinkt. Die Seite bleibt
        /// erhalten und ist beim Aktivieren sofort wieder da (<see cref="WakeTab"/>).
        /// </summary>
        private async Task SleepTabAsync(BrowserTab tab)
        {
            var core = tab.WebView?.CoreWebView2;
            if (core == null || tab.IsActive) return;

            try
            {
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                bool suspended = await core.TrySuspendAsync();

                // Während des Wartens wieder angeklickt? Dann gleich wach bleiben
                if (tab.IsActive)
                {
                    WakeTab(tab);
                    return;
                }

                tab.IsSleeping = suspended;
                tab.SleepRefused = !suspended;
            }
            catch (Exception ex)
            {
                // z.B. wenn die WebView gerade sichtbar wurde
                tab.SleepRefused = true;
                Log.Warn($"Tab konnte nicht schlafen gelegt werden ({tab.Url})", ex);
            }
        }

        /// <summary>
        /// Tab verwerfen: die WebView wird freigegeben, Adresse, Titel und Symbol bleiben.
        /// Beim Aktivieren lädt die Seite neu (wie Chrome bei "verworfenen" Tabs).
        /// </summary>
        private void DiscardTab(BrowserTab tab)
        {
            if (tab.IsActive || tab.WebView == null) return;

            WebViewContainer.Children.Remove(tab.WebView);
            tab.ReleaseWebView();
            tab.IsDiscarded = true;
            tab.IsSleeping = true;
        }

        /// <summary>Beim Aktivieren: eingefrorene Tabs fortsetzen, verworfene bzw. noch nicht geladene Tabs laden.</summary>
        private void WakeTab(BrowserTab tab)
        {
            tab.SleepRefused = false;

            if (tab.IsDiscarded)
            {
                tab.IsDiscarded = false;
                CreateTabWebView(tab, tab.Url);
            }
            else if (tab.WebView?.CoreWebView2 is CoreWebView2 core)
            {
                try
                {
                    if (core.IsSuspended) core.Resume();
                    core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                }
                catch (Exception ex)
                {
                    Log.Warn("Tab konnte nicht aufgeweckt werden", ex);
                }
            }

            tab.IsSleeping = false;
        }

        /// <summary>Knopf "Jetzt schlafen legen": alle unsichtbaren Tabs ohne Ton sofort einfrieren.</summary>
        private async Task SleepInactiveTabsNowAsync()
        {
            foreach (var tab in Tabs.ToList().Where(t => TabSleepPolicy.CanSleepNow(SleepStateOf(t))))
            {
                await SleepTabAsync(tab);
            }

            // Chromium gibt den Speicher erst nach einem Moment frei
            await Task.Delay(1500);
            await RefreshPerformancePanelAsync(force: true);
        }

        private void MenuPerformance_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, async () => await OpenPerformancePanelAsync());
        }

        private async Task OpenPerformancePanelAsync()
        {
            performancePanel.ShowSleepSetting(AppSettingsService.Instance.Settings.TabSleepMinutes);
            performancePanel.ShowSnapshot(_lastSnapshot);
            performancePanel.ShowTabs(AllTabsOfThisMode());
            popupPerformance.IsOpen = true;
            await RefreshPerformancePanelAsync(force: true);
        }

        private async Task RefreshPerformancePanelAsync(bool force = false)
        {
            var snapshot = await MeasureMemoryAsync(force);
            if (!popupPerformance.IsOpen) return;

            performancePanel.ShowSnapshot(snapshot);
            performancePanel.ShowTabs(AllTabsOfThisMode());
        }

        /// <summary>Tabs aller normalen bzw. aller Inkognito-Fenster (wie bei "Zu Tab wechseln").</summary>
        private List<BrowserTab> AllTabsOfThisMode() =>
            Application.Current.Windows.OfType<MainWindow>()
                .Where(w => w.IsIncognito == _isIncognito)
                .SelectMany(w => w.Tabs)
                .ToList();

        /// <summary>Infokarte eines Tabs: Speicher kurz vor dem Anzeigen auffrischen.</summary>
        private async void Tab_ToolTipOpening(object sender, System.Windows.Controls.ToolTipEventArgs e)
        {
            await MeasureMemoryAsync(force: false);
        }

        /// <summary>
        /// Speicher messen: alle WebView2-Prozesse plus Echo selbst; Renderer werden über die Frame-IDs
        /// ihren Tabs zugeordnet (<see cref="TabMemory.Attribute"/>). Mehrfache Aufrufe kurz hintereinander
        /// teilen sich eine Messung.
        /// </summary>
        private static Task<PerformanceSnapshot?> MeasureMemoryAsync(bool force)
        {
            if (_memoryMeasurement is { IsCompleted: false }) return _memoryMeasurement;
            if (!force && DateTime.Now - _lastMemoryMeasurement < TimeSpan.FromSeconds(5)) return Task.FromResult(_lastSnapshot);

            _lastMemoryMeasurement = DateTime.Now;
            return _memoryMeasurement = MeasureMemoryCoreAsync();
        }

        private static async Task<PerformanceSnapshot?> MeasureMemoryCoreAsync()
        {
            try
            {
                var windows = Application.Current.Windows.OfType<MainWindow>().ToList();
                var env = windows.Select(w => w._webViewEnvironment).FirstOrDefault(e => e != null);
                if (env == null) return null;

                var infos = await env.GetProcessExtendedInfosAsync();
                var allTabs = windows.SelectMany(w => w.Tabs).ToList();

                var tabIdByFrame = new Dictionary<uint, string>();
                foreach (var tab in allTabs)
                {
                    try
                    {
                        if (tab.WebView?.CoreWebView2 is CoreWebView2 core) tabIdByFrame[core.FrameId] = tab.Id;
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
                    {
                        // WebView wird gerade geschlossen
                    }
                }

                // Messen im Hintergrund – das Abfragen der Prozesse soll die Oberfläche nicht bremsen
                var processes = infos.Select(i => (Id: i.ProcessInfo.ProcessId, i.ProcessInfo.Kind,
                    Frames: i.AssociatedFrameInfos.Select(TopFrameId).ToList())).ToList();
                var measured = await Task.Run(() => processes
                    .Select(p => (p.Id, p.Kind, p.Frames, Bytes: ProcessMemoryReader.PrivateWorkingSet(p.Id)))
                    .ToList());
                long own = await Task.Run(() => ProcessMemoryReader.PrivateWorkingSet(Environment.ProcessId));

                var renderers = measured
                    .Where(p => p.Kind == CoreWebView2ProcessKind.Renderer)
                    .Select(p => new ProcessMemory(p.Id, p.Bytes, p.Frames))
                    .ToList();
                var perTab = TabMemory.Attribute(renderers, tabIdByFrame);

                foreach (var tab in allTabs)
                {
                    tab.MemoryBytes = perTab.TryGetValue(tab.Id, out long bytes) ? bytes : null;
                }

                long total = own + measured.Sum(p => p.Bytes);
                long tabsTotal = perTab.Values.Sum();
                _lastSnapshot = new PerformanceSnapshot(
                    TotalBytes: total,
                    SharedBytes: Math.Max(0, total - tabsTotal),
                    SleepingTabs: allTabs.Count(t => t.IsSleeping || t.IsDiscarded),
                    TabCount: allTabs.Count);
                return _lastSnapshot;
            }
            catch (Exception ex)
            {
                Log.Warn("Speicher konnte nicht gemessen werden", ex);
                return _lastSnapshot;
            }
        }

        /// <summary>Hauptframe (Tab) eines Frames – auch iframes anderer Websites in eigenen Prozessen.</summary>
        private static uint TopFrameId(CoreWebView2FrameInfo frame)
        {
            while (frame.ParentFrameInfo != null) frame = frame.ParentFrameInfo;
            return frame.FrameId;
        }

        #endregion
    }
}
