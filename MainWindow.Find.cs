using System;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Auf Seite suchen (Strg+F, F3, Umschalt+F3) mit der Find-API von WebView2 und eigener Suchleiste im Echo-Design.
    /// Ältere WebView2-Runtimes ohne Find-API fallen auf window.find zurück (dann ohne Trefferzahl).
    /// </summary>
    public partial class MainWindow
    {
        private CoreWebView2? _findCore;
        private bool _findApiUnavailable;

        private void InitializeFindBar()
        {
            findBar.SearchChanged += async (term, matchCase) => await StartFindAsync(term, matchCase);
            findBar.NextRequested += () => FindStep(forward: true);
            findBar.PreviousRequested += () => FindStep(forward: false);
            findBar.CloseRequested += CloseFindBar;
        }

        private bool IsFindBarOpen => findBar.Visibility == Visibility.Visible;

        private void OpenFindBar()
        {
            if (ActiveTab?.WebView?.CoreWebView2 == null) return;

            findBar.Visibility = Visibility.Visible;
            FocusWpfInput(findBar.InputBox);
            if (!string.IsNullOrEmpty(findBar.SearchText))
            {
                _ = StartFindAsync(findBar.SearchText, findBar.MatchCase);
            }
        }

        private void CloseFindBar()
        {
            StopFind();
            if (!IsFindBarOpen) return;

            findBar.Visibility = Visibility.Collapsed;
            ActiveTab?.WebView?.Focus();
        }

        /// <summary>F3 / Umschalt+F3: öffnet die Leiste oder springt zum nächsten bzw. vorherigen Treffer.</summary>
        private void FindNextOrOpen(bool forward)
        {
            if (!IsFindBarOpen || string.IsNullOrEmpty(findBar.SearchText))
            {
                OpenFindBar();
                return;
            }
            FindStep(forward);
        }

        private async Task StartFindAsync(string term, bool matchCase)
        {
            var core = ActiveTab?.WebView?.CoreWebView2;
            StopFind();
            if (core == null || _webViewEnvironment == null || string.IsNullOrEmpty(term))
            {
                findBar.ShowResult(-1, 0);
                return;
            }

            if (_findApiUnavailable)
            {
                await FindWithScriptAsync(core, term, matchCase, forward: true);
                return;
            }

            try
            {
                var options = _webViewEnvironment.CreateFindOptions();
                options.FindTerm = term;
                options.IsCaseSensitive = matchCase;
                options.ShouldHighlightAllMatches = true;
                options.SuppressDefaultFindDialog = true;

                _findCore = core;
                core.Find.MatchCountChanged += OnFindResultChanged;
                core.Find.ActiveMatchIndexChanged += OnFindResultChanged;
                await core.Find.StartAsync(options);
                UpdateFindResult();
            }
            catch (Exception ex) when (ex is NotImplementedException or System.Runtime.InteropServices.COMException or InvalidCastException)
            {
                Log.Warn("Find-API nicht verfügbar, nutze window.find", ex);
                _findApiUnavailable = true;
                StopFind();
                await FindWithScriptAsync(core, term, matchCase, forward: true);
            }
        }

        private void FindStep(bool forward)
        {
            if (_findCore != null)
            {
                if (forward) _findCore.Find.FindNext();
                else _findCore.Find.FindPrevious();
            }
            else if (_findApiUnavailable && ActiveTab?.WebView?.CoreWebView2 is { } core)
            {
                _ = FindWithScriptAsync(core, findBar.SearchText, findBar.MatchCase, forward);
            }
        }

        private void OnFindResultChanged(object? sender, object e) => UpdateFindResult();

        private void UpdateFindResult()
        {
            if (_findCore == null) return;
            findBar.ShowResult(_findCore.Find.ActiveMatchIndex, _findCore.Find.MatchCount);
        }

        private void StopFind()
        {
            if (_findCore == null) return;
            try
            {
                _findCore.Find.MatchCountChanged -= OnFindResultChanged;
                _findCore.Find.ActiveMatchIndexChanged -= OnFindResultChanged;
                _findCore.Find.Stop();
            }
            catch (Exception ex)
            {
                Log.Warn("Suche konnte nicht beendet werden", ex);
            }
            _findCore = null;
        }

        /// <summary>Notlösung ohne Find-API: markiert den nächsten Treffer, zeigt aber keine Anzahl.</summary>
        private async Task FindWithScriptAsync(CoreWebView2 core, string term, bool matchCase, bool forward)
        {
            if (string.IsNullOrEmpty(term)) return;
            string script = $"window.find({JsonSerializer.Serialize(term)}, {(matchCase ? "true" : "false")}, {(forward ? "false" : "true")}, true)";
            string result = await core.ExecuteScriptAsync(script);
            findBar.ShowResult(result == "true" ? 1 : -1, result == "true" ? 1 : 0);
        }
    }
}
