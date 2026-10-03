using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>
    /// Omnibox wie in Chrome: Vorschläge aus offenen Tabs, Lesezeichen, Verlauf und der Suchmaschine,
    /// Inline-Vervollständigung, Pfeiltasten-Auswahl, Strg+Enter, Alt+Enter (neuer Tab), Umschalt+Entf
    /// (Verlaufseintrag löschen). Ohne Fokus: Domain hervorgehoben, Hinweis "Nicht sicher" für http.
    /// </summary>
    public partial class MainWindow
    {
        private const int MaxSearchSuggestions = 4;

        private bool _isSettingAddressText;
        private bool _isEditingAddress;
        private string _typedAddressText = "";
        private CancellationTokenSource? _remoteSuggestCts;
        private DispatcherTimer? _remoteSuggestTimer;
        private List<OmniboxSuggestion> _remoteSuggestions = new();
        private string _remoteSuggestionsFor = "";

        private void InitializeOmnibox()
        {
            omniboxPanel.SuggestionChosen += suggestion => ChooseSuggestion(suggestion, inNewTab: false);
            Deactivated += (s, e) => CloseOmniboxPopup();
            LocationChanged += (s, e) => CloseOmniboxPopup();
            SizeChanged += (s, e) => CloseOmniboxPopup();
        }

        #region Adresse anzeigen

        /// <summary>Adresse des Tabs in der Adressleiste zeigen – überschreibt keine laufende Eingabe des Nutzers.</summary>
        private void ShowAddress(BrowserTab tab)
        {
            if (tab != ActiveTab) return;
            if (_isEditingAddress && txtUrl.IsKeyboardFocusWithin)
            {
                UpdateSecurityChip(tab.Url);
                return;
            }

            SetAddressText(IsStartPage(tab.Url) ? "" : tab.Url);
            UpdateSecurityChip(tab.Url);
            UpdateAddressDisplay();
        }

        private void SetAddressText(string text)
        {
            _isSettingAddressText = true;
            try
            {
                txtUrl.Text = text;
            }
            finally
            {
                _isSettingAddressText = false;
            }
        }

        /// <summary>"Nicht sicher" links in der Adressleiste für unverschlüsselte Seiten (http).</summary>
        private void UpdateSecurityChip(string? url)
        {
            bool insecure = url != null && url.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
            chipNotSecure.Visibility = insecure ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Ohne Fokus: Host in voller Farbe, Rest gedimmt (wie Chrome) – erleichtert das Erkennen gefälschter Adressen.
        /// Mit Fokus zeigt die Textbox den vollständigen, bearbeitbaren Text.
        /// </summary>
        private void UpdateAddressDisplay()
        {
            string text = txtUrl.Text;
            Uri? uri = null;
            bool show = !txtUrl.IsKeyboardFocusWithin && Uri.TryCreate(text, UriKind.Absolute, out uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.Host.Length > 0;

            txtUrlDisplay.Inlines.Clear();
            if (show && uri != null)
            {
                int hostStart = text.IndexOf(uri.Host, StringComparison.OrdinalIgnoreCase);
                if (hostStart < 0)
                {
                    show = false;
                }
                else
                {
                    string before = text[..hostStart];
                    string host = text.Substring(hostStart, uri.Host.Length);
                    string after = text[(hostStart + uri.Host.Length)..];
                    if (after == "/") after = "";

                    txtUrlDisplay.Inlines.Add(DimmedRun(before));
                    txtUrlDisplay.Inlines.Add(new Run(host));
                    txtUrlDisplay.Inlines.Add(DimmedRun(after));
                }
            }

            txtUrlDisplay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            // Der echte Text bleibt für Kopieren & Barrierefreiheit erhalten, ist aber unsichtbar.
            // Sonst gilt wieder die (dynamische) Textfarbe aus dem Stil – folgt also Theme-Wechseln.
            if (show)
            {
                txtUrl.Foreground = System.Windows.Media.Brushes.Transparent;
            }
            else
            {
                txtUrl.ClearValue(ForegroundProperty);
            }
        }

        private Run DimmedRun(string text)
        {
            var run = new Run(text);
            run.SetResourceReference(TextElement.ForegroundProperty, "TextMutedBrush");
            return run;
        }

        #endregion

        #region Eingabe & Vorschläge

        private void TxtUrl_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSettingAddressText) return;
            if (!txtUrl.IsKeyboardFocusWithin)
            {
                UpdateAddressDisplay();
                return;
            }

            string text = txtUrl.Text;
            bool typedForward = text.Length > _typedAddressText.Length
                                && text.StartsWith(_typedAddressText, StringComparison.Ordinal)
                                && txtUrl.CaretIndex == text.Length;
            _typedAddressText = text;
            _isEditingAddress = true;

            UpdateSuggestions(text, allowInlineCompletion: typedForward);
        }

        private void UpdateSuggestions(string text, bool allowInlineCompletion)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                CloseOmniboxPopup();
                return;
            }

            var local = OmniboxRanker.Rank(text, OpenTabSources(), BookmarkSources(), HistorySources(), DateTime.Now);
            OmniboxSuggestion? completed = null;
            string? completion = allowInlineCompletion ? OmniboxRanker.InlineCompletion(text, local, out completed) : null;

            var rows = new List<OmniboxSuggestion>();
            if (completed != null && completion != null)
            {
                // Oben steht – ausgewählt – genau das, was die Adressleiste nach der Vervollständigung zeigt:
                // der Treffer selbst, oder bei nur vervollständigtem Host ("github.com") die Startseite der Website.
                bool wholeAddress = UrlHelper.NormalizeForComparison(completed.Url).Equals(completion, StringComparison.OrdinalIgnoreCase);
                rows.Add(wholeAddress
                    ? completed
                    : new OmniboxSuggestion(SuggestionKind.Url, completion, "https://" + completion));
            }

            rows.Add(TypedSuggestion(text));
            rows.AddRange(local.Where(s => !rows.Contains(s)));
            if (_remoteSuggestionsFor == text.Trim())
            {
                rows.AddRange(_remoteSuggestions.Where(r => !rows.Any(x => x.IsSearch && x.Title.Equals(r.Title, StringComparison.OrdinalIgnoreCase))));
            }

            omniboxPanel.SetItems(rows, 0);
            OpenOmniboxPopup();

            if (completion != null)
            {
                _isSettingAddressText = true;
                txtUrl.Text = completion;
                txtUrl.Select(text.Length, completion.Length - text.Length);
                _isSettingAddressText = false;
            }

            ScheduleRemoteSuggestions(text.Trim());
        }

        /// <summary>Erste Zeile: genau das Getippte – als Adresse öffnen oder suchen.</summary>
        private static OmniboxSuggestion TypedSuggestion(string text)
        {
            string trimmed = text.Trim();
            string? url = UrlHelper.ToNavigableUrl(trimmed);
            return url != null
                ? new OmniboxSuggestion(SuggestionKind.Url, trimmed, url)
                : new OmniboxSuggestion(SuggestionKind.Search, trimmed, AppSettingsService.Instance.GetSearchUrl(trimmed));
        }

        private IEnumerable<SuggestionSource> OpenTabSources() =>
            Application.Current.Windows.OfType<MainWindow>()
                .Where(w => w.IsIncognito == _isIncognito)
                .SelectMany(w => w.Tabs)
                .Where(t => t != ActiveTab && !InternalPages.IsInternalUrl(t.Url))
                .Select(t => new SuggestionSource(t.Title, t.Url, TabId: t.Id))
                .ToList();

        private IEnumerable<SuggestionSource> BookmarkSources()
        {
            foreach (var bookmark in _bookmarkService.Bookmarks)
            {
                if (bookmark.IsGroup)
                {
                    foreach (var child in bookmark.Children) yield return new SuggestionSource(child.Title, child.Url);
                }
                else
                {
                    yield return new SuggestionSource(bookmark.Title, bookmark.Url);
                }
            }
        }

        private IEnumerable<SuggestionSource> HistorySources() =>
            _historyService.Entries.Select(h => new SuggestionSource(h.Title, h.Url, h.VisitedAt)).ToList();

        /// <summary>Suchvorschläge der Suchmaschine, kurz nach dem letzten Tastendruck (nicht im Inkognito-Modus).</summary>
        private void ScheduleRemoteSuggestions(string query)
        {
            _remoteSuggestCts?.Cancel();
            if (_isIncognito || !AppSettingsService.Instance.Settings.EnableSearchSuggestions) return;
            if (UrlHelper.ToNavigableUrl(query) != null || query.Length > 120) return;

            _remoteSuggestTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _remoteSuggestTimer.Stop();
            _remoteSuggestTimer.Tick -= RemoteSuggestTimer_Tick;
            _remoteSuggestTimer.Tick += RemoteSuggestTimer_Tick;
            _remoteSuggestTimer.Tag = query;
            _remoteSuggestTimer.Start();
        }

        private async void RemoteSuggestTimer_Tick(object? sender, EventArgs e)
        {
            _remoteSuggestTimer?.Stop();
            if (_remoteSuggestTimer?.Tag is not string query) return;

            var cts = new CancellationTokenSource();
            _remoteSuggestCts = cts;
            try
            {
                string engine = AppSettingsService.Instance.Settings.SearchEngine ?? "duckduckgo";
                var phrases = await SearchSuggestionClient.GetAsync(engine, query, cts.Token);
                if (cts.IsCancellationRequested || !popupOmnibox.IsOpen) return;

                _remoteSuggestionsFor = query;
                _remoteSuggestions = phrases
                    .Where(p => !p.Equals(query, StringComparison.OrdinalIgnoreCase))
                    .Take(MaxSearchSuggestions)
                    .Select(p => new OmniboxSuggestion(SuggestionKind.Search, p, AppSettingsService.Instance.GetSearchUrl(p)))
                    .ToList();

                // Liste ergänzen, ohne die aktuelle Auswahl zu verlieren
                var current = omniboxPanel.Items.Where(s => !(s.IsSearch && s.Title != query)).ToList();
                var selected = omniboxPanel.Selected;
                var rows = current.Concat(_remoteSuggestions.Where(r => !current.Any(c => c.IsSearch && c.Title.Equals(r.Title, StringComparison.OrdinalIgnoreCase)))).ToList();
                omniboxPanel.SetItems(rows, selected != null ? Math.Max(0, rows.IndexOf(selected)) : 0);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                // Offline oder Suchmaschine nicht erreichbar – dann eben keine Vorschläge
            }
        }

        #endregion

        #region Tastatur & Auswahl

        private void TxtUrl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool popupOpen = popupOmnibox.IsOpen && omniboxPanel.Count > 0;
            // Mit gedrückter Alt-Taste meldet WPF Key.System (z.B. Alt+Enter = in neuem Tab öffnen)
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            switch (key)
            {
                case Key.Down when popupOpen:
                case Key.Up when popupOpen:
                    var selected = omniboxPanel.MoveSelection(key == Key.Down ? 1 : -1);
                    if (selected != null)
                    {
                        SetAddressText(selected.IsSearch ? selected.Title : selected.Url);
                        txtUrl.CaretIndex = txtUrl.Text.Length;
                    }
                    e.Handled = true;
                    break;

                case Key.Enter:
                    bool newTab = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                    {
                        // Strg+Enter: "example" -> https://www.example.com
                        NavigateFromAddressBar(UrlHelper.CompleteWithWwwAndCom(_typedAddressText.Length > 0 ? _typedAddressText : txtUrl.Text), newTab);
                    }
                    else if (popupOpen && omniboxPanel.Selected is { } choice)
                    {
                        ChooseSuggestion(choice, newTab);
                    }
                    else
                    {
                        NavigateFromAddressBar(txtUrl.Text, newTab);
                    }
                    e.Handled = true;
                    break;

                case Key.Delete when popupOpen && Keyboard.Modifiers == ModifierKeys.Shift
                                     && omniboxPanel.Selected is { IsHistory: true } historyEntry:
                    // Umschalt+Entf: Eintrag aus dem Verlauf löschen
                    foreach (var item in _historyService.Entries.Where(h => UrlHelper.NormalizeForComparison(h.Url) == UrlHelper.NormalizeForComparison(historyEntry.Url)).ToList())
                    {
                        _historyService.RemoveEntry(item);
                    }
                    UpdateSuggestions(_typedAddressText, allowInlineCompletion: false);
                    e.Handled = true;
                    break;

                case Key.Escape:
                    if (popupOpen)
                    {
                        CloseOmniboxPopup();
                        SetAddressText(_typedAddressText);
                        txtUrl.CaretIndex = txtUrl.Text.Length;
                    }
                    else
                    {
                        // Zweites Esc: Eingabe verwerfen und zur Seite zurück
                        _isEditingAddress = false;
                        if (ActiveTab != null) ShowAddress(ActiveTab);
                        ActiveTab?.WebView?.Focus();
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void ChooseSuggestion(OmniboxSuggestion suggestion, bool inNewTab)
        {
            CloseOmniboxPopup();

            if (suggestion.IsTab && suggestion.TabId != null && TryActivateTab(suggestion.TabId))
            {
                FinishAddressEditing();
                return;
            }

            NavigateFromAddressBar(suggestion.Url, inNewTab);
        }

        private void NavigateFromAddressBar(string input, bool inNewTab)
        {
            CloseOmniboxPopup();
            FinishAddressEditing();

            if (string.IsNullOrWhiteSpace(input)) return;
            if (inNewTab)
            {
                AddNewTab(UrlHelper.ToNavigableUrl(input.Trim()) ?? AppSettingsService.Instance.GetSearchUrl(input.Trim()));
            }
            else
            {
                NavigateToInput(input);
            }
            ActiveTab?.WebView?.Focus();
        }

        private void FinishAddressEditing()
        {
            _isEditingAddress = false;
            _typedAddressText = "";
            _remoteSuggestCts?.Cancel();
        }

        /// <summary>Zu einem Tab wechseln – auch in einem anderen Fenster.</summary>
        private static bool TryActivateTab(string tabId)
        {
            foreach (var window in Application.Current.Windows.OfType<MainWindow>())
            {
                var tab = window.Tabs.FirstOrDefault(t => t.Id == tabId);
                if (tab == null) continue;

                window.SelectTab(tab);
                window.BringToFront();
                return true;
            }
            return false;
        }

        private void OpenOmniboxPopup()
        {
            omniboxPanel.Width = omniboxContainer.ActualWidth + 8;
            popupOmnibox.IsOpen = true;
        }

        private void CloseOmniboxPopup()
        {
            _remoteSuggestTimer?.Stop();
            popupOmnibox.IsOpen = false;
        }

        #endregion
    }
}
