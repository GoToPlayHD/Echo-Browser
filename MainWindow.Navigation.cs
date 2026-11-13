using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using EchoBrowser.Models;
using EchoBrowser.Services;
using EchoBrowser.Views;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace EchoBrowser
{
    /// <summary>Navigation und Omnibox (Adressleiste, Zurück/Vor/Neu laden/Home).</summary>
    public partial class MainWindow
    {
        #region Navigation & Omnibox

        /// <summary>
        /// Einzige Quelle für den Zustand von Zurück/Vor/Neu laden (MainWindow hat kein INotifyPropertyChanged –
        /// eine XAML-Bindung an ActiveTab würde einen Tabwechsel nicht bemerken).
        /// </summary>
        private void UpdateNavigationControls()
        {
            if (ActiveTab == null) return;

            // Verworfener/schlafender Tab: Zustand aus der WebView lesen, sobald es eine gibt
            if (ActiveTab.WebView is { CoreWebView2: not null } webView)
            {
                ActiveTab.CanGoBack = webView.CanGoBack;
                ActiveTab.CanGoForward = webView.CanGoForward;
            }
            btnBack.IsEnabled = ActiveTab.CanGoBack;
            btnForward.IsEnabled = ActiveTab.CanGoForward;

            if (ActiveTab.IsLoading)
            {
                pathReload.Visibility = Visibility.Collapsed;
                pathStop.Visibility = Visibility.Visible;
                btnReload.ToolTip = Tr.Get("Nav_StopLoading");
                progressLoading.Visibility = Visibility.Visible;
                progressLoading.IsIndeterminate = true;
            }
            else
            {
                pathReload.Visibility = Visibility.Visible;
                pathStop.Visibility = Visibility.Collapsed;
                btnReload.ToolTip = Tr.Get("Nav_Reload");
                // Unsichtbar, behält aber seine Höhe; Animation anhalten, solange nichts lädt
                progressLoading.Visibility = Visibility.Hidden;
                progressLoading.IsIndeterminate = false;
            }
        }

        public static bool IsStartPage(string? url) => AddressInput.IsStartPage(url);

        /// <summary>
        /// Verbindung wieder da: Tabs mit Offline-/DNS-Fehlerseite automatisch neu laden (wie Chrome).
        /// NetworkChange ist ein statisches Ereignis – beim Schließen des Fensters abmelden, sonst bleibt es im Speicher.
        /// </summary>
        private void InitializeNetworkRecovery()
        {
            System.Net.NetworkInformation.NetworkAvailabilityChangedEventHandler handler = (s, e) =>
            {
                if (!e.IsAvailable) return;
                // Ereignis kommt auf einem Thread-Pool-Thread; kurz warten, bis DNS/DHCP wirklich bereit sind
                Dispatcher.BeginInvoke(async () =>
                {
                    await Task.Delay(1500);
                    RetryFailedTabsAfterReconnect();
                });
            };
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += handler;
            Closed += (s, e) => System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged -= handler;
        }

        private void RetryFailedTabsAfterReconnect()
        {
            foreach (var tab in Tabs.ToList())
            {
                if (!tab.LoadFailed || tab.LastNetError is not { } error) continue;
                if (error.Kind is not (NetErrorPageService.ErrorKind.Offline or NetErrorPageService.ErrorKind.Dns)) continue;
                if (tab.WebView?.CoreWebView2 is not { } core) continue;
                try
                {
                    tab.ExpectBrowserNavigation = true;
                    core.Navigate(error.Url);
                }
                catch (Exception ex)
                {
                    Log.Warn($"Automatisches Neuladen von {error.Url} fehlgeschlagen", ex);
                }
            }
        }

        /// <summary>Eingabe aus Adressleiste oder Startseite: Adresse öffnen oder mit der gewählten Suchmaschine suchen.</summary>
        private void NavigateToInput(string input, BrowserTab? tab = null)
        {
            tab ??= ActiveTab;
            if (string.IsNullOrWhiteSpace(input) || tab == null) return;

            // Bedeutung der Eingabe: Services/AddressInput.cs
            var target = AddressInput.Resolve(input);

            switch (target.Kind)
            {
                case AddressTargetKind.StartPage:
                    tab.Title = Tr.Get(_isIncognito ? "Tab_NewTabIncognito" : "Tab_NewTab");
                    break;
                case AddressTargetKind.Settings:
                    tab.Title = Tr.Get("Tab_Settings");
                    break;
            }

            var core = tab.WebView?.CoreWebView2;
            if (core == null)
            {
                // WebView startet noch (oder der Tab ist verworfen): Ziel merken statt die Eingabe zu verlieren.
                // AttachWebViewEvents navigiert nach der Initialisierung dorthin, ein verworfener Tab lädt es beim Aktivieren.
                if (tab.IsDiscarded)
                {
                    tab.Url = target.Url;
                }
                else
                {
                    tab.PendingNavigationUrl = target.Url;
                    tab.Url = target.Url;
                }
                ShowAddress(tab);
                return;
            }

            try
            {
                tab.ExpectBrowserNavigation = true;
                core.Navigate(target.Url);
            }
            catch (Exception ex)
            {
                Log.Warn($"Navigation zu '{target.Url}' fehlgeschlagen", ex);
            }
        }

        // Enter, Esc und Pfeiltasten der Adressleiste: TxtUrl_PreviewKeyDown in MainWindow.Omnibox.cs

        private void TxtUrl_GotFocus(object sender, RoutedEventArgs e)
        {
            _typedAddressText = txtUrl.Text;
            UpdateAddressDisplay();
            txtUrl.SelectAll();
        }

        /// <summary>Erster Klick in die Adressleiste markiert alles (wie in Chrome), weitere Klicks setzen den Cursor.</summary>
        private void TxtUrl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!txtUrl.IsKeyboardFocusWithin)
            {
                txtUrl.Focus();
                e.Handled = true;
            }
        }

        private void TxtUrl_LostFocus(object sender, RoutedEventArgs e)
        {
            CloseOmniboxPopup();
            if (ActiveTab != null && string.IsNullOrWhiteSpace(txtUrl.Text))
            {
                _isEditingAddress = false;
                ShowAddress(ActiveTab);
            }
            UpdateAddressDisplay();
        }

        private void BtnBack_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoBack)
            {
                ActiveTab.ExpectBrowserNavigation = true;
                ActiveTab.WebView.GoBack();
            }
        }

        private void BtnForward_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView != null && ActiveTab.WebView.CanGoForward)
            {
                ActiveTab.ExpectBrowserNavigation = true;
                ActiveTab.WebView.GoForward();
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab?.WebView == null) return;

            if (ActiveTab.IsLoading)
            {
                ActiveTab.WebView.Stop();
            }
            else if (InternalPages.Is(ActiveTab.WebView.CoreWebView2?.Source, InternalPages.Crashed))
            {
                // Auf der Absturzseite heißt "Neu laden": die ursprüngliche Seite erneut öffnen
                NavigateToInput(ActiveTab.Url);
            }
            else
            {
                ActiveTab.WebView.Reload();
            }
        }

        private void BtnHome_Click(object sender, RoutedEventArgs e)
        {
            NavigateToInput(StartPageService.StartPageUrl);
        }

        #endregion
    }
}
