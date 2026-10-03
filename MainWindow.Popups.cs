using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>Popup-Blocker: Symbol in der Adressleiste und Menü zum Öffnen oder dauerhaften Erlauben.</summary>
    public partial class MainWindow
    {
        private void UpdatePopupBlockedIndicator()
        {
            int count = ActiveTab?.BlockedPopupUrls.Count ?? 0;
            btnPopupBlocked.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
            btnPopupBlocked.ToolTip = Tr.Format("Popup_BlockedTooltip", count);
        }

        private void BtnPopupBlocked_Click(object sender, RoutedEventArgs e)
        {
            var tab = ActiveTab;
            if (tab == null || tab.BlockedPopupUrls.Count == 0) return;

            var menu = new ContextMenu
            {
                Background = (Brush)FindResource("SurfaceBrush"),
                BorderBrush = (Brush)FindResource("BorderBrush"),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                PlacementTarget = btnPopupBlocked,
                Placement = PlacementMode.Bottom
            };

            menu.Items.Add(new MenuItem
            {
                Header = Tr.Format("Popup_BlockedHeader", tab.BlockedPopupUrls.Count),
                FontWeight = FontWeights.Bold,
                IsEnabled = false
            });
            menu.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });

            foreach (string url in tab.BlockedPopupUrls.ToList())
            {
                // TextBlock statt String: Unterstriche in Adressen wären sonst Zugriffstasten
                var item = new MenuItem
                {
                    Header = new TextBlock { Text = ShortenForMenu(url), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 420 },
                    ToolTip = url
                };
                item.Click += (s, args) =>
                {
                    tab.BlockedPopupUrls.Remove(url);
                    AddNewTab(url);
                    UpdatePopupBlockedIndicator();
                };
                menu.Items.Add(item);
            }

            if (Uri.TryCreate(tab.Url, UriKind.Absolute, out var siteUri) && siteUri.Host.Length > 0)
            {
                menu.Items.Add(new Separator { Background = (Brush)FindResource("BorderSubtleBrush") });
                var allow = new MenuItem { Header = new TextBlock { Text = Tr.Format("Popup_AlwaysAllow", siteUri.Host) } };
                allow.Click += (s, args) => AllowPopupsForSite(siteUri.Host);
                menu.Items.Add(allow);
            }

            menu.IsOpen = true;
        }

        /// <summary>Website dauerhaft erlauben und die bisher blockierten Popups öffnen.</summary>
        private void AllowPopupsForSite(string host)
        {
            AppSettingsService.Instance.Settings.PopupAllowedDomains.Add(host);
            AppSettingsService.Instance.Save();

            foreach (var tab in Tabs.Where(t => Uri.TryCreate(t.Url, UriKind.Absolute, out var u) && u.Host == host))
            {
                tab.PopupsBlocked = false;
            }

            var active = ActiveTab;
            if (active != null)
            {
                var blocked = active.BlockedPopupUrls.ToList();
                active.BlockedPopupUrls.Clear();
                foreach (string url in blocked)
                {
                    AddNewTab(url);
                }
            }

            UpdatePopupBlockedIndicator();
        }

        private static string ShortenForMenu(string url) => url.Length <= 80 ? url : url[..77] + "…";
    }
}
