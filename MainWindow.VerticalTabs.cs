using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Shell;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>
    /// Vertikale Tabs (wie Edge): eine Spalte links mit allen Tabs und Gruppen, schmal nur mit Symbolen.
    /// Beide Ansichten zeigen dieselbe Liste (<see cref="StripItems"/>), die Titelzeile wird dann zur schmalen Leiste.
    /// </summary>
    public partial class MainWindow
    {
        #region Vertikale Tabs

        public static readonly DependencyProperty IsVerticalTabsNarrowProperty = DependencyProperty.Register(
            nameof(IsVerticalTabsNarrow), typeof(bool), typeof(MainWindow), new PropertyMetadata(false));

        /// <summary>Schmale Spalte: die Tab-Vorlagen blenden Titel und Knöpfe aus.</summary>
        public bool IsVerticalTabsNarrow
        {
            get => (bool)GetValue(IsVerticalTabsNarrowProperty);
            set => SetValue(IsVerticalTabsNarrowProperty, value);
        }

        private const double VerticalTabsWidth = 240;
        private const double VerticalTabsNarrowWidth = 52;

        private static bool IsVerticalTabs => AppSettingsService.Instance.Settings.VerticalTabs;

        /// <summary>Höhe der Titelzeile: mit Tabs links nur noch eine schmale Leiste.</summary>
        private static double TitleBarHeight => IsVerticalTabs ? 34 : 42;

        private void InitializeVerticalTabs()
        {
            ApplyTabLayout();
            LocalizationService.Instance.LanguageChanged += ApplyTabLayout;
            Closed += (s, e) => LocalizationService.Instance.LanguageChanged -= ApplyTabLayout;
        }

        /// <summary>Tabs oben oder links – nach der Einstellung (gilt für alle Fenster).</summary>
        public void ApplyTabLayout()
        {
            var settings = AppSettingsService.Instance.Settings;
            bool vertical = settings.VerticalTabs;
            bool narrow = settings.VerticalTabsNarrow;
            IsVerticalTabsNarrow = narrow;

            borderVerticalTabs.Visibility = vertical && !_isFullscreen ? Visibility.Visible : Visibility.Collapsed;
            borderVerticalTabs.Width = narrow ? VerticalTabsNarrowWidth : VerticalTabsWidth;
            txtVerticalNewTab.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            btnVerticalNewTab.HorizontalContentAlignment = narrow ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            DockPanel.SetDock(btnVerticalNarrow, narrow ? Dock.Top : Dock.Right);
            btnVerticalNarrow.Margin = narrow ? new Thickness(0, 0, 0, 4) : new Thickness(4, 0, 0, 0);
            rotateVerticalNarrow.Angle = narrow ? 180 : 0;
            SetToolTipAndName(btnVerticalNarrow, Tr.Get(narrow ? "VTabs_Expand" : "VTabs_Collapse"));

            gridHorizontalTabs.Visibility = vertical ? Visibility.Collapsed : Visibility.Visible;
            txtWindowTitle.Visibility = vertical ? Visibility.Visible : Visibility.Collapsed;
            SetToolTipAndName(btnTabLayout, Tr.Get(vertical ? "VTabs_TurnOff" : "VTabs_TurnOn"));

            if (!_isFullscreen)
            {
                rowTabStrip.Height = new GridLength(TitleBarHeight);
                if (WindowChrome.GetWindowChrome(this) is WindowChrome chrome) chrome.CaptionHeight = TitleBarHeight;
            }

            UpdateVerticalTabsForActiveTab();
        }

        private static void SetToolTipAndName(FrameworkElement element, string text)
        {
            element.ToolTip = text;
            AutomationProperties.SetName(element, text);
        }

        private static void ApplyTabLayoutToAllWindows()
        {
            foreach (var window in Application.Current.Windows.OfType<MainWindow>()) window.ApplyTabLayout();
        }

        private void ToggleVerticalTabs()
        {
            var settings = AppSettingsService.Instance.Settings;
            settings.VerticalTabs = !settings.VerticalTabs;
            AppSettingsService.Instance.Save();
            ApplyTabLayoutToAllWindows();
        }

        private void BtnTabLayout_Click(object sender, RoutedEventArgs e) => ToggleVerticalTabs();

        private void BtnVerticalNarrow_Click(object sender, RoutedEventArgs e)
        {
            var settings = AppSettingsService.Instance.Settings;
            settings.VerticalTabsNarrow = !settings.VerticalTabsNarrow;
            AppSettingsService.Instance.Save();
            ApplyTabLayoutToAllWindows();
        }

        /// <summary>Titel des aktiven Tabs in der Titelzeile; in der Liste zum aktiven Tab scrollen.</summary>
        private void UpdateVerticalTabsForActiveTab()
        {
            if (ActiveTab == null || !IsVerticalTabs) return;

            txtWindowTitle.SetBinding(TextBlock.TextProperty, new Binding(nameof(BrowserTab.DisplayTitle)) { Source = ActiveTab });

            var tab = ActiveTab;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                (itemsVerticalTabs.ItemContainerGenerator.ContainerFromItem(tab) as FrameworkElement)?.BringIntoView());
        }

        /// <summary>Die sichtbare Tab-Leiste (oben oder links) – z.B. um ein Popup an einem Gruppenkopf auszurichten.</summary>
        private ItemsControl VisibleTabItems => IsVerticalTabs ? itemsVerticalTabs : itemsTabs;

        #endregion
    }
}
