using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EchoBrowser.Models;
using EchoBrowser.Services;
using Microsoft.Web.WebView2.Core;

namespace EchoBrowser
{
    /// <summary>
    /// Website-Berechtigungen (Kamera, Mikrofon, Standort, Benachrichtigungen …) mit eigener Abfrage im Echo-Design.
    /// Entscheidungen speichert WebView2 im Profil; das Shield-Panel zeigt sie pro Website an und setzt sie zurück.
    /// </summary>
    public partial class MainWindow
    {
        private sealed record PendingPermission(BrowserTab Tab, CoreWebView2PermissionRequestedEventArgs Args, CoreWebView2Deferral Deferral);

        private readonly List<PendingPermission> _pendingPermissions = new();
        private PendingPermission? _shownPermission;

        private void InitializePermissions()
        {
            permissionPrompt.Allowed += () => ResolveShownPermission(CoreWebView2PermissionState.Allow, save: true);
            permissionPrompt.Blocked += () => ResolveShownPermission(CoreWebView2PermissionState.Deny, save: true);
            permissionPrompt.Dismissed += () => ResolveShownPermission(CoreWebView2PermissionState.Deny, save: false);
            // Klick daneben = diesmal ablehnen, aber nicht merken (beim nächsten Mal wird wieder gefragt)
            popupPermission.Closed += (s, e) => ResolveShownPermission(CoreWebView2PermissionState.Deny, save: false);

            shieldPanel.ResetPermissionRequested += async permission => await ResetSitePermissionAsync(permission);
        }

        private void AttachPermissionEvents(BrowserTab tab, CoreWebView2 core)
        {
            core.PermissionRequested += (s, args) =>
            {
                if (!SitePermissionTexts.IsPrompted(args.PermissionKind)) return;

                _pendingPermissions.Add(new PendingPermission(tab, args, args.GetDeferral()));
                ShowNextPermissionPrompt();
            };
        }

        /// <summary>Nächste offene Anfrage des sichtbaren Tabs zeigen (Anfragen im Hintergrund warten, bis der Tab aktiv ist).</summary>
        private void ShowNextPermissionPrompt()
        {
            if (_shownPermission != null) return;

            foreach (var orphan in _pendingPermissions.Where(p => !Tabs.Contains(p.Tab)).ToList())
            {
                CompletePermission(orphan, CoreWebView2PermissionState.Deny, save: false);
            }

            var next = _pendingPermissions.FirstOrDefault(p => p.Tab == ActiveTab);
            if (next == null) return;

            _shownPermission = next;
            permissionPrompt.Show(SitePermissionTexts.HostOf(next.Args.Uri), next.Args.PermissionKind);
            popupPermission.IsOpen = true;
        }

        private void ResolveShownPermission(CoreWebView2PermissionState state, bool save)
        {
            var shown = _shownPermission;
            if (shown == null) return;

            _shownPermission = null;
            popupPermission.IsOpen = false;
            CompletePermission(shown, state, save);
            ShowNextPermissionPrompt();
        }

        private void CompletePermission(PendingPermission pending, CoreWebView2PermissionState state, bool save)
        {
            _pendingPermissions.Remove(pending);
            try
            {
                pending.Args.State = state;
                pending.Args.SavesInProfile = save;
                pending.Deferral.Complete();
            }
            catch (Exception ex)
            {
                Log.Warn("Berechtigungsanfrage konnte nicht abgeschlossen werden", ex);
            }
        }

        /// <summary>Tab gewechselt: Abfrage eines anderen Tabs ausblenden (sie bleibt offen) und ggf. die des neuen zeigen.</summary>
        private void OnActiveTabChangedForPermissions()
        {
            if (_shownPermission != null && _shownPermission.Tab != ActiveTab)
            {
                _shownPermission = null;
                popupPermission.IsOpen = false;
            }
            ShowNextPermissionPrompt();
        }

        /// <summary>Der Tab lädt eine neue Seite: Anfragen der alten Seite verfallen.</summary>
        private void DiscardPermissionRequests(BrowserTab tab)
        {
            foreach (var pending in _pendingPermissions.Where(p => p.Tab == tab).ToList())
            {
                if (pending == _shownPermission)
                {
                    _shownPermission = null;
                    popupPermission.IsOpen = false;
                }
                CompletePermission(pending, CoreWebView2PermissionState.Deny, save: false);
            }
        }

        /// <summary>Gespeicherte Berechtigungen der aktuellen Website für das Shield-Panel laden.</summary>
        private async Task LoadSitePermissionsAsync()
        {
            var tab = ActiveTab;
            var core = tab?.WebView?.CoreWebView2;
            if (core == null || !Uri.TryCreate(tab!.Url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                shieldPanel.ShowPermissions(Array.Empty<SitePermission>());
                return;
            }

            try
            {
                var settings = await core.Profile.GetNonDefaultPermissionSettingsAsync();
                var forSite = settings
                    .Where(s => s.PermissionState != CoreWebView2PermissionState.Default &&
                                SitePermissionTexts.HostOf(s.PermissionOrigin).Equals(uri.Host, StringComparison.OrdinalIgnoreCase))
                    .Select(s => new SitePermission(s.PermissionKind, s.PermissionOrigin, s.PermissionState))
                    .ToList();
                shieldPanel.ShowPermissions(forSite);
            }
            catch (Exception ex)
            {
                Log.Warn("Website-Berechtigungen konnten nicht gelesen werden", ex);
                shieldPanel.ShowPermissions(Array.Empty<SitePermission>());
            }
        }

        private async Task ResetSitePermissionAsync(SitePermission permission)
        {
            var core = ActiveTab?.WebView?.CoreWebView2;
            if (core == null) return;
            try
            {
                await core.Profile.SetPermissionStateAsync(permission.Kind, permission.Origin, CoreWebView2PermissionState.Default);
            }
            catch (Exception ex)
            {
                Log.Warn($"Berechtigung {permission.Kind} für {permission.Origin} konnte nicht zurückgesetzt werden", ex);
            }
            await LoadSitePermissionsAsync();
        }
    }
}
