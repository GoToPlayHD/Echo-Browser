using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using EchoBrowser.Services;
using EchoBrowser.Views.Popups;

namespace EchoBrowser
{
    /// <summary>Befehle des Browsers: Ausführung für Tastenkürzel und Befehlspalette (Strg+K).</summary>
    public partial class MainWindow
    {
        #region Befehle & Befehlspalette

        private const int PaletteMaxCommands = 8;
        private const int PaletteMaxWeakCommands = 3;
        private const int PaletteMaxPages = 8;
        private const int PaletteMaxTabsWhenEmpty = 6;
        private const string SettingsCommandPrefix = "settings:";

        private Dictionary<string, Action>? _commandActions;

        /// <summary>Was jeder Befehl aus <see cref="CommandCatalog"/> tut (Schlüssel = Id).</summary>
        private Dictionary<string, Action> CommandActions => _commandActions ??= new Dictionary<string, Action>
        {
            // Tabs & Fenster
            ["newTab"] = () => AddNewTab(StartPageService.StartPageUrl),
            ["newWindow"] = () => new MainWindow().Show(),
            ["newIncognitoWindow"] = OpenNewIncognitoWindow,
            ["closeTab"] = () => { if (ActiveTab != null) CloseTab(ActiveTab); },
            ["reopenClosedTab"] = ReopenClosedTab,
            ["duplicateTab"] = () => { if (ActiveTab != null) DuplicateTab(ActiveTab); },
            ["muteTab"] = () => { if (ActiveTab != null) ToggleMute(ActiveTab); },
            ["closeOtherTabs"] = () => { if (ActiveTab != null) CloseOtherTabs(ActiveTab); },
            ["pinTab"] = () => { if (ActiveTab != null) TogglePin(ActiveTab); },
            ["splitView"] = ToggleSplitView,
            ["groupTab"] = () => { if (ActiveTab != null) AddTabToNewGroup(ActiveTab); },
            ["commandPalette"] = ToggleCommandPalette,

            // Navigation
            ["reload"] = () => BtnReload_Click(this, new RoutedEventArgs()),
            ["back"] = () => BtnBack_Click(this, new RoutedEventArgs()),
            ["forward"] = () => BtnForward_Click(this, new RoutedEventArgs()),
            ["home"] = () => BtnHome_Click(this, new RoutedEventArgs()),
            ["focusAddressBar"] = FocusAddressBar,

            // Seite
            ["find"] = OpenFindBar,
            ["print"] = PrintActivePage,
            ["savePage"] = SaveActivePage,
            ["viewSource"] = ViewPageSource,
            ["zoomIn"] = () => ZoomActiveTab(+1),
            ["zoomOut"] = () => ZoomActiveTab(-1),
            ["zoomReset"] = ResetZoom,
            ["fullscreen"] = ToggleFullscreen,
            ["devTools"] = () => ActiveTab?.WebView?.CoreWebView2?.OpenDevToolsWindow(),

            // Browser
            ["bookmarkPage"] = () => BtnBookmark_Click(this, new RoutedEventArgs()),
            ["toggleBookmarksBar"] = () => MenuToggleBookmarksBar_Click(this, new RoutedEventArgs()),
            ["toggleSidebar"] = () => BtnToggleSidebar_Click(this, new RoutedEventArgs()),
            ["toggleVerticalTabs"] = ToggleVerticalTabs,
            ["downloads"] = () => popupDownloads.IsOpen = !popupDownloads.IsOpen,
            ["history"] = () => ToggleHistoryPopup(new RoutedEventArgs()),
            ["settings"] = OpenSettingsTab,
            ["clearData"] = () => OpenSettingsSection("section-privacy"),
            ["toggleShield"] = ToggleShieldGlobally,
            ["performance"] = async () => await OpenPerformancePanelAsync(),
            ["sleepInactiveTabs"] = async () => await SleepInactiveTabsNowAsync(),

            // Design
            ["themeSystem"] = () => ThemeManager.Instance.ApplyAndSavePreset(nameof(ThemePreset.System)),
            ["themeSilver"] = () => ThemeManager.Instance.ApplyAndSavePreset(nameof(ThemePreset.SilverAnthracite)),
            ["themeMidnight"] = () => ThemeManager.Instance.ApplyAndSavePreset(nameof(ThemePreset.MidnightOled)),
            ["themeTitanium"] = () => ThemeManager.Instance.ApplyAndSavePreset(nameof(ThemePreset.TitaniumLight)),
            ["themeCobalt"] = () => ThemeManager.Instance.ApplyAndSavePreset(nameof(ThemePreset.CobaltSlate)),
        };

        /// <summary>Befehl ausführen – eine Id aus <see cref="CommandCatalog"/> oder "settings:&lt;Bereich&gt;".</summary>
        private void ExecuteCommand(string id)
        {
            if (id.StartsWith(SettingsCommandPrefix, StringComparison.Ordinal))
            {
                OpenSettingsSection(id[SettingsCommandPrefix.Length..]);
            }
            else if (CommandActions.TryGetValue(id, out var action))
            {
                action();
            }
        }

        private void InitializeCommandPalette()
        {
            commandPalette.QueryChanged += UpdatePaletteItems;
            commandPalette.ItemChosen += ExecutePaletteItem;
            commandPalette.CloseRequested += () => CloseCommandPalette(focusPage: true);

            // Klick in die Webseite (eigenes HWND) erreicht den Popup nicht – Fokusverlust schließt ihn deshalb auch
            commandPalette.InputBox.LostKeyboardFocus += (s, e) =>
            {
                if (popupPalette.IsOpen && !(e.NewFocus is DependencyObject d && commandPalette.IsAncestorOf(d)))
                {
                    CloseCommandPalette(focusPage: false);
                }
            };

            popupPalette.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => new[]
            {
                new CustomPopupPlacement(new Point((targetSize.Width - popupSize.Width) / 2, 6), PopupPrimaryAxis.Horizontal)
            };
        }

        private void MenuCommandPalette_Click(object sender, RoutedEventArgs e)
        {
            popupMenu.IsOpen = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, OpenCommandPalette);
        }

        private void ToggleCommandPalette()
        {
            if (popupPalette.IsOpen)
            {
                CloseCommandPalette(focusPage: true);
            }
            else
            {
                OpenCommandPalette();
            }
        }

        private void OpenCommandPalette()
        {
            CloseOmniboxPopup();
            popupMenu.IsOpen = false;

            commandPalette.Reset();
            UpdatePaletteItems("");
            popupPalette.IsOpen = true;
            FocusWpfInput(commandPalette.InputBox);
        }

        private void CloseCommandPalette(bool focusPage)
        {
            if (!popupPalette.IsOpen) return;
            popupPalette.IsOpen = false;
            if (focusPage) ActiveTab?.WebView?.Focus();
        }

        /// <summary>
        /// Treffer zur Eingabe: gut passende Befehle zuerst, dann offene Tabs, Lesezeichen und Verlauf
        /// (Ranking der Omnibox), dann schwächer passende Befehle und zuletzt "… suchen".
        /// Ohne Eingabe: die anderen offenen Tabs und alle Befehle mit ihren Tastenkürzeln.
        /// </summary>
        private void UpdatePaletteItems(string query)
        {
            string text = query.Trim();
            var commands = PaletteCommands();
            var items = new List<PaletteItem>();

            if (text.Length == 0)
            {
                items.AddRange(OpenTabSources().Take(PaletteMaxTabsWhenEmpty).Select(t => PageItem(
                    new OmniboxSuggestion(SuggestionKind.SwitchToTab, t.Title, t.Url) { TabId = t.TabId })));
                items.AddRange(commands);
                commandPalette.SetItems(items);
                return;
            }

            var scored = commands
                .Select(c => (Item: c, Score: Math.Max(FuzzyMatcher.Score(text, c.Title), IdScore(text, c.CommandId!))))
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ToList();

            string? url = UrlHelper.ToNavigableUrl(text);
            if (url != null) items.Add(new PaletteItem(PaletteItemKind.Url, text, url, Tr.Get("Palette_Open")));

            items.AddRange(scored.Where(x => FuzzyMatcher.IsStrong(text, x.Score)).Take(PaletteMaxCommands).Select(x => x.Item));
            items.AddRange(OmniboxRanker.Rank(text, OpenTabSources(), BookmarkSources(), HistorySources(), DateTime.Now, PaletteMaxPages)
                .Select(PageItem));
            items.AddRange(scored.Where(x => !FuzzyMatcher.IsStrong(text, x.Score)).Take(PaletteMaxWeakCommands).Select(x => x.Item));

            if (url == null)
            {
                items.Add(new PaletteItem(PaletteItemKind.Search, Tr.Format("Palette_SearchFor", text),
                    AppSettingsService.Instance.GetSearchUrl(text)));
            }
            commandPalette.SetItems(items);
        }

        /// <summary>Englische Kennung als Zusatz-Suchbegriff ("zoom" findet "Vergrößern") – nur bei gutem Treffer.</summary>
        private static int IdScore(string text, string commandId)
        {
            if (commandId.StartsWith(SettingsCommandPrefix, StringComparison.Ordinal)) return 0;
            int score = FuzzyMatcher.Score(text, commandId);
            return FuzzyMatcher.IsStrong(text, score) ? score * 3 / 4 : 0;
        }

        private static List<PaletteItem> PaletteCommands()
        {
            var items = CommandCatalog.All
                .Where(c => c.Id != "commandPalette")
                .Select(c => new PaletteItem(PaletteItemKind.Command, Tr.Get(c.TitleKey), Chip: c.GestureText) { CommandId = c.Id })
                .ToList();

            string settings = Tr.Get("Cmd_Settings");
            items.AddRange(CommandCatalog.SettingsSections.Select(s =>
                new PaletteItem(PaletteItemKind.Command, $"{settings} › {Tr.Get(s.TitleKey)}") { CommandId = SettingsCommandPrefix + s.Section }));
            return items;
        }

        private static PaletteItem PageItem(OmniboxSuggestion suggestion)
        {
            string title = string.IsNullOrWhiteSpace(suggestion.Title) ? suggestion.DisplayUrl : suggestion.Title;
            string host = UrlHelper.ShortHost(suggestion.Url) ?? "";
            string detail = host.Length > 0 && !title.Equals(host, StringComparison.OrdinalIgnoreCase) ? "  " + host : "";

            return suggestion.Kind switch
            {
                SuggestionKind.SwitchToTab => new PaletteItem(PaletteItemKind.Tab, title, suggestion.Url, Tr.Get("Omnibox_SwitchToTab"), detail) { TabId = suggestion.TabId },
                SuggestionKind.Bookmark => new PaletteItem(PaletteItemKind.Bookmark, title, suggestion.Url, Tr.Get("Palette_Bookmark"), detail),
                _ => new PaletteItem(PaletteItemKind.History, title, suggestion.Url, Tr.Get("Palette_History"), detail)
            };
        }

        private void ExecutePaletteItem(PaletteItem item)
        {
            // Den Fokus nicht erst an die bisherige Seite geben: in der geteilten Ansicht würde deren verspätete
            // Fokus-Meldung den Wechsel zur anderen Seite wieder rückgängig machen
            CloseCommandPalette(focusPage: false);

            // Erst nach dem Schließen ausführen: Befehle, die selbst ein Popup öffnen oder den Fokus setzen, gewinnen so
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
            {
                if (item.Kind == PaletteItemKind.Command && item.CommandId != null)
                {
                    ExecuteCommand(item.CommandId);
                    return;
                }

                if (item.Kind != PaletteItemKind.Tab || item.TabId == null || !TryActivateTab(item.TabId))
                {
                    if (string.IsNullOrEmpty(item.Url)) return;
                    OpenFromPalette(item.Url);
                }
                ActiveTab?.WebView?.Focus();
            });
        }

        /// <summary>Adressen aus der Palette ersetzen eine leere Startseite, sonst öffnen sie einen neuen Tab.</summary>
        private void OpenFromPalette(string url)
        {
            if (ActiveTab != null && IsStartPage(ActiveTab.Url))
            {
                NavigateToInput(url);
                ActiveTab.WebView?.Focus();
            }
            else
            {
                AddNewTab(url);
            }
        }

        /// <summary>Einstellungsseite an einem Bereich öffnen (echo://settings#section-…).</summary>
        private void OpenSettingsSection(string section)
        {
            string url = $"{SettingsPageService.SettingsPageUrl}#{section}";
            var open = Tabs.FirstOrDefault(t => InternalPages.Is(t.Url, InternalPages.Settings));
            if (open != null)
            {
                SelectTab(open);
                open.WebView?.CoreWebView2?.Navigate(url);
            }
            else if (ActiveTab?.WebView?.CoreWebView2 != null && (IsStartPage(ActiveTab.Url) || string.IsNullOrWhiteSpace(ActiveTab.Url)))
            {
                ActiveTab.Title = Tr.Get("Tab_Settings");
                ActiveTab.WebView.CoreWebView2.Navigate(url);
            }
            else
            {
                AddNewTab(url, title: Tr.Get("Tab_Settings"));
            }
        }

        /// <summary>Echo Shield für alle Websites ein- bzw. ausschalten (wie der globale Schalter im Shield-Panel).</summary>
        private void ToggleShieldGlobally()
        {
            if (ActiveTab == null) return;

            bool turnOn = !AppSettingsService.Instance.Settings.IsAdBlockerEnabled;
            OnShieldOptionsChanged(new ShieldOptions(turnOn, ActiveTab.TrackingProtectionEnabled, ActiveTab.JavaScriptEnabled, ActiveTab.PopupsBlocked));
            ShowToast(Tr.Get(turnOn ? "Shield_ActiveStateOn" : "Shield_ActiveStateOff"));
        }

        #endregion
    }
}
