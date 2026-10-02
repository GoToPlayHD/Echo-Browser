using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EchoBrowser.Services;

namespace EchoBrowser.Views.Popups
{
    /// <summary>Was das Shield-Popup für den aktiven Tab anzeigen soll.</summary>
    public sealed record ShieldViewState(
        string Host,
        bool IsSecure,
        bool GlobalOn,
        bool SiteOn,
        bool JavaScriptOn,
        bool PopupsBlocked,
        int BlockedCount);

    /// <summary>Die Schalter im Shield-Popup nach einer Änderung durch den Nutzer.</summary>
    public sealed record ShieldOptions(bool GlobalOn, bool SiteOn, bool JavaScriptOn, bool PopupsBlocked);

    /// <summary>Inhalt des Echo-Shield-Popups: Status, Schalter, Filterliste, Websitedaten.</summary>
    public partial class ShieldPanel : UserControl
    {
        private bool _isUpdating;

        /// <summary>Der Nutzer hat einen der Schalter umgelegt.</summary>
        public event Action<ShieldOptions>? OptionsChanged;

        /// <summary>"Cookies &amp; Websitedaten leeren" wurde geklickt.</summary>
        public event Action? ClearSiteDataRequested;

        public ShieldPanel()
        {
            InitializeComponent();
        }

        /// <summary>Anzeige aktualisieren, ohne dabei OptionsChanged auszulösen.</summary>
        public void ShowState(ShieldViewState state)
        {
            _isUpdating = true;
            try
            {
                txtShieldHost.Text = string.IsNullOrWhiteSpace(state.Host) ? "Echo-Browser" : state.Host;

                txtShieldStatus.Text = Tr.Get(state.IsSecure ? "Security_Secure" : "Security_NotEncrypted");
                txtShieldStatus.Foreground = ThemeBrush(state.IsSecure ? "StatusSuccessBrush" : "StatusWarningBrush");

                chkGlobalShield.IsChecked = state.GlobalOn;
                chkTrackingProtection.IsChecked = state.SiteOn;
                chkJavaScript.IsChecked = state.JavaScriptOn;
                chkPopups.IsChecked = state.PopupsBlocked;

                bool isProtectionActive = state.GlobalOn && state.SiteOn;
                var statusBrush = ThemeBrush(isProtectionActive ? "StatusSuccessBrush" : "StatusWarningBrush");
                txtShieldActiveState.Text = Tr.Get(isProtectionActive ? "Shield_ActiveStateOn" : "Shield_ActiveStateOff");
                txtShieldActiveState.Foreground = statusBrush;
                pathShieldPopupIcon.Fill = statusBrush;

                txtTrackersBlocked.Text = Tr.Format("Shield_TrackersBlockedFormat", state.BlockedCount);
                txtFilterRuleCount.Text = Tr.Format("Shield_FilterRuleCountFormat", AdBlockerService.Instance.BlockedDomainsCount);
            }
            finally
            {
                _isUpdating = false;
            }
        }

        private Brush ThemeBrush(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;

        private void ShieldOption_Changed(object sender, RoutedEventArgs e)
        {
            if (_isUpdating) return;

            OptionsChanged?.Invoke(new ShieldOptions(
                GlobalOn: chkGlobalShield.IsChecked ?? true,
                SiteOn: chkTrackingProtection.IsChecked ?? true,
                JavaScriptOn: chkJavaScript.IsChecked ?? true,
                PopupsBlocked: chkPopups.IsChecked ?? true));
        }

        private async void BtnUpdateFilterList_Click(object sender, RoutedEventArgs e)
        {
            btnUpdateFilterList.IsEnabled = false;
            btnUpdateFilterList.Content = Tr.Get("Shield_LoadingFilters");

            try
            {
                string url = AppSettingsService.Instance.Settings.AdBlockerFilterUrl;
                await AdBlockerService.Instance.DownloadAndCacheBlocklistAsync(url, force: true);
                txtFilterRuleCount.Text = Tr.Format("Shield_FilterRuleCountFormat", AdBlockerService.Instance.BlockedDomainsCount);
                btnUpdateFilterList.Content = Tr.Get("Shield_Updated");
                await Task.Delay(1800);
            }
            catch (Exception ex)
            {
                ThemedDialogWindow.ShowMessage(Window.GetWindow(this), "Echo Shield", Tr.Format("Shield_UpdateFailed", ex.Message), MessageBoxImage.Warning);
            }
            finally
            {
                btnUpdateFilterList.Content = Tr.Get("Shield_UpdateFilters");
                btnUpdateFilterList.IsEnabled = true;
            }
        }

        private void BtnClearSiteData_Click(object sender, RoutedEventArgs e) => ClearSiteDataRequested?.Invoke();
    }
}
