using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>Einstellungsseite öffnen, Start-Tabs, Sitzung speichern/wiederherstellen, Zoom & Tracking-Prävention, Schließen-Logik.</summary>
    public partial class MainWindow
    {
        /// <summary>Wird gesetzt, sobald das Fenster endgültig geschlossen wird – es gehört dann nicht mehr zur Sitzung.</summary>
        private bool _isClosingForGood;

        /// <summary>Normale Fenster, die nicht gerade geschlossen werden, landen in der gespeicherten Sitzung.</summary>
        public bool IsPartOfSession => !_isIncognito && !_isClosingForGood && IsLoaded;

        #region Settings & Session Management

        /// <summary>Startseite bzw. Einstellungsseite eines Tabs neu erzeugen (z.B. nach Sprachwechsel).</summary>
        private void RefreshInternalPage(BrowserTab tab)
        {
            if (tab.WebView?.CoreWebView2 == null || !InternalPages.IsInternalUrl(tab.Url)) return;

            if (tab.Url == StartPageService.StartPageUrl)
            {
                tab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
            }
            else if (tab.Url == SettingsPageService.SettingsPageUrl)
            {
                tab.Title = Tr.Get("Tab_Settings");
            }

            // Die Seite wird beim Neuladen frisch erzeugt (MainWindow.InternalPages.cs)
            tab.WebView.CoreWebView2.Reload();
        }

        public void NavigateToSettingsPage(BrowserTab? tab)
        {
            if (tab?.WebView?.CoreWebView2 == null) return;
            tab.Title = Tr.Get("Tab_Settings");
            tab.WebView.CoreWebView2.Navigate(SettingsPageService.SettingsPageUrl);
        }

        public void OpenSettingsTab()
        {
            // If already open, switch to that tab
            foreach (var tab in Tabs)
            {
                if (InternalPages.Is(tab.Url, InternalPages.Settings))
                {
                    SelectTab(tab);
                    return;
                }
            }

            // If current tab is clean start page, navigate within it
            if (ActiveTab != null && (IsStartPage(ActiveTab.Url) || string.IsNullOrWhiteSpace(ActiveTab.Url)))
            {
                NavigateToSettingsPage(ActiveTab);
            }
            else
            {
                AddNewTab(SettingsPageService.SettingsPageUrl);
            }
        }

        /// <summary>
        /// Die ersten Tabs eines Fensters: wiederhergestellte Sitzung, Startverhalten aus den Einstellungen
        /// (nur im ersten Fenster der App) und Adressen aus der Kommandozeile.
        /// </summary>
        private void OpenInitialTabs()
        {
            if (WindowToRestore != null)
            {
                RestoreWindowTabs(WindowToRestore);
            }
            else if (IsInitialWindow && !_isIncognito)
            {
                RunStartupBehavior();
            }

            foreach (string url in StartupUrls)
            {
                AddNewTab(url);
            }

            if (Tabs.Count == 0)
            {
                AddNewTab(StartPageService.StartPageUrl);
            }
        }

        private void RunStartupBehavior()
        {
            var settings = AppSettingsService.Instance.Settings;
            var session = SessionService.Instance.Load();
            bool hasSession = session.Windows.Count > 0;

            bool restore = hasSession && (settings.StartupBehavior == "restore_session" || OfferRestoreAfterCrash());
            if (restore)
            {
                RestoreSession(session);
            }
            else if (StartupUrls.Count == 0 && settings.StartupBehavior == "custom_url" && !string.IsNullOrWhiteSpace(settings.CustomStartupUrl))
            {
                AddNewTab(settings.CustomStartupUrl);
            }
        }

        /// <summary>Nach einem Absturz fragen, ob die zuletzt offenen Tabs zurückkommen sollen (wie bei Chrome).</summary>
        private bool OfferRestoreAfterCrash()
        {
            if (!SessionService.Instance.PreviousRunCrashed) return false;

            return ThemedDialogWindow.ShowConfirm(
                this,
                Tr.Get("Session_RestoreTitle"),
                Tr.Get("Session_RestoreMessage"),
                Tr.Get("Session_Restore"),
                Tr.Get("Session_StartFresh"));
        }

        /// <summary>Erstes gespeichertes Fenster in dieses Fenster, alle weiteren in neue Fenster.</summary>
        private void RestoreSession(SessionSnapshot session)
        {
            RestoreWindowTabs(session.Windows[0]);

            foreach (var windowState in session.Windows.Skip(1))
            {
                new MainWindow { WindowToRestore = windowState }.Show();
            }
        }

        private void RestoreWindowTabs(SessionWindow windowState)
        {
            var groups = windowState.Groups.ToDictionary(g => g.Id, g => new TabGroup
            {
                Id = g.Id,
                Name = g.Name,
                Color = Enum.TryParse<TabGroupColor>(g.Color, out var color) ? color : TabGroupColor.Grey,
                IsCollapsed = g.IsCollapsed
            });

            int active = Math.Clamp(windowState.ActiveIndex, 0, Math.Max(0, windowState.Tabs.Count - 1));
            for (int i = 0; i < windowState.Tabs.Count; i++)
            {
                var saved = windowState.Tabs[i];
                // Nur der aktive Tab lädt sofort, die übrigen beim ersten Anklicken – das macht den Start schnell
                var tab = AddNewTab(saved.Url, activateTab: false, title: saved.Title, deferLoad: i != active);
                if (tab == null) continue;

                tab.IsPinned = saved.IsPinned;
                tab.Group = !saved.IsPinned && saved.GroupId != null ? groups.GetValueOrDefault(saved.GroupId) : null;
                if (i == active) SelectTab(tab);
            }
            NormalizeTabs();
        }

        /// <summary>Zustand dieses Fensters für die gespeicherte Sitzung.</summary>
        public SessionWindow CaptureSession()
        {
            var tabs = Tabs.Where(t => SessionSerializer.IsRestorableUrl(t.Url)).ToList();

            // Ist der aktive Tab nicht speicherbar (z.B. die Startseite), wird der nächstgelegene gespeicherte Tab aktiv
            var active = ActiveTab;
            if (active != null && !tabs.Contains(active) && tabs.Count > 0)
            {
                int position = Tabs.IndexOf(active);
                active = tabs.OrderBy(t => Math.Abs(Tabs.IndexOf(t) - position)).First();
            }

            return new SessionWindow
            {
                Tabs = tabs.Select(t => new SessionTab { Url = t.Url, Title = t.Title, IsPinned = t.IsPinned, GroupId = t.Group?.Id }).ToList(),
                ActiveIndex = active != null ? Math.Max(0, tabs.IndexOf(active)) : 0,
                Groups = tabs.Select(t => t.Group).OfType<TabGroup>().Distinct()
                    .Select(g => new SessionGroup { Id = g.Id, Name = g.Name, Color = g.Color.ToString(), IsCollapsed = g.IsCollapsed })
                    .ToList()
            };
        }

        private void ScheduleSessionSave()
        {
            if (IsPartOfSession)
            {
                SessionService.Instance.ScheduleSave();
            }
        }

        /// <summary>Vor einem Neustart (Update): Sitzung sofort sichern, damit die Tabs danach wieder da sind.</summary>
        private static void SaveSessionForRestart()
        {
            SessionService.Instance.SaveNow();
            SessionService.Instance.Freeze();
        }

        private void ApplyTrackingPrevention(string level)
        {
            var preventionLevel = level.ToLowerInvariant() switch
            {
                "strict" => CoreWebView2TrackingPreventionLevel.Strict,
                "none" => CoreWebView2TrackingPreventionLevel.None,
                _ => CoreWebView2TrackingPreventionLevel.Balanced
            };

            foreach (var tab in Tabs)
            {
                try
                {
                    if (tab.WebView?.CoreWebView2?.Profile != null)
                    {
                        tab.WebView.CoreWebView2.Profile.PreferredTrackingPreventionLevel = preventionLevel;
                    }
                }
                catch { }
            }
        }

        /// <summary>Neuer Standard-Zoom: alle Tabs ohne eigenen Website-Zoom übernehmen ihn sofort.</summary>
        private void ApplyDefaultZoom()
        {
            foreach (var tab in Tabs)
            {
                ApplySiteZoom(tab);
            }
            UpdateZoomIndicator();
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_isIncognito && Tabs.Count > 1 && AppSettingsService.Instance.Settings.WarnOnClosingMultipleTabs)
            {
                string title = LocalizationService.Instance.GetString("Dialog_CloseBrowserTitle", "Echo-Browser beenden");
                string msg = string.Format(LocalizationService.Instance.GetString("Dialog_CloseMultipleTabsMessage", "Möchtest du wirklich alle {0} geöffneten Tabs schließen?"), Tabs.Count);
                string confirm = LocalizationService.Instance.GetString("Dialog_CloseAllTabs", "Alle Tabs schließen");
                string cancel = LocalizationService.Instance.GetString("Dialog_Cancel", "Abbrechen");

                bool confirmed = ThemedDialogWindow.ShowConfirm(this, title, msg, confirm, cancel);
                if (!confirmed)
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (_isIncognito)
            {
                _isClosingForGood = true;
                return;
            }

            // Wie bei Chrome: Schließt man das letzte Fenster, bleibt es in der Sitzung (Neustart stellt es wieder her).
            // Schließt man eines von mehreren Fenstern, verschwindet es aus der Sitzung.
            bool isLastNormalWindow = !Application.Current.Windows.OfType<MainWindow>()
                .Any(w => w != this && w.IsPartOfSession);

            if (isLastNormalWindow)
            {
                SessionService.Instance.SaveNow();
                SessionService.Instance.Freeze();
                _isClosingForGood = true;

                if (AppSettingsService.Instance.Settings.ClearDataOnExit)
                {
                    _historyService.ClearHistory();
                }
            }
            else
            {
                _isClosingForGood = true;
                SessionService.Instance.SaveNow();
            }
        }

        #endregion
    }
}
