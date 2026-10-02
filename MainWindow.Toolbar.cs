using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>Symbolleiste: ein-/ausblendbare Schaltflächen und das Rechtsklick-Menü "Symbolleiste anpassen".</summary>
    public partial class MainWindow
    {
        /// <summary>Eine ausblendbare Schaltfläche der Symbolleiste.</summary>
        private sealed record ToolbarButtonInfo(
            string Tag,
            string LocKey,
            Func<MainWindow, FrameworkElement> Element,
            Func<AppSettings, bool> IsVisible,
            Action<AppSettings, bool> SetVisible);

        /// <summary>Alle ausblendbaren Schaltflächen – einzige Stelle, an der neue Schaltflächen ergänzt werden müssen.</summary>
        private static readonly ToolbarButtonInfo[] ToolbarButtons =
        {
            new("Sidebar", "Toolbar_Sidebar", w => w.btnToggleSidebar, s => s.ShowSidebarButton, (s, v) => s.ShowSidebarButton = v),
            new("Back", "Toolbar_Back", w => w.btnBack, s => s.ShowBackButton, (s, v) => s.ShowBackButton = v),
            new("Forward", "Toolbar_Forward", w => w.btnForward, s => s.ShowForwardButton, (s, v) => s.ShowForwardButton = v),
            new("Reload", "Toolbar_Reload", w => w.btnReload, s => s.ShowReloadButton, (s, v) => s.ShowReloadButton = v),
            new("Home", "Toolbar_Home", w => w.btnHome, s => s.ShowHomeButton, (s, v) => s.ShowHomeButton = v),
            new("SearchEngine", "Toolbar_SearchEngine", w => w.cmbSearchEngine, s => s.ShowSearchEngineSelector, (s, v) => s.ShowSearchEngineSelector = v),
            new("Extensions", "Ext_Title", w => w.btnExtensions, s => s.ShowExtensionsButton, (s, v) => s.ShowExtensionsButton = v),
            new("Downloads", "Nav_Downloads", w => w.btnDownloads, s => s.ShowDownloadsButton, (s, v) => s.ShowDownloadsButton = v),
        };

        /// <summary>Nach dieser Schaltfläche folgt im Menü ein Trenner (Navigation | Werkzeuge).</summary>
        private const string LastNavigationButtonTag = "Home";

        private static ToolbarButtonInfo? FindToolbarButton(object? tag) =>
            tag is string t ? ToolbarButtons.FirstOrDefault(b => b.Tag == t) : null;

        /// <summary>Hängt das Rechtsklick-Menü an die Symbolleiste und jede Schaltfläche.</summary>
        private void InitializeToolbarContextMenus()
        {
            borderToolbar.ContextMenu = CreateToolbarContextMenu(hideTag: null);
            btnMenu.ContextMenu = CreateToolbarContextMenu(hideTag: null);

            foreach (var button in ToolbarButtons)
            {
                button.Element(this).ContextMenu = CreateToolbarContextMenu(button.Tag);
            }
        }

        /// <param name="hideTag">Schaltfläche, für die "Schaltfläche ausblenden" angeboten wird (null = keine).</param>
        private ContextMenu CreateToolbarContextMenu(string? hideTag)
        {
            var menu = new ContextMenu();
            menu.SetResourceReference(BackgroundProperty, "SurfaceBrush");
            menu.SetResourceReference(BorderBrushProperty, "BorderBrush");
            menu.SetResourceReference(ForegroundProperty, "TextPrimaryBrush");

            if (hideTag != null)
            {
                menu.Items.Add(CreateLocMenuItem("Toolbar_HideButton", hideTag, MenuHideToolbarButton_Click));
                menu.Items.Add(CreateMenuSeparator());
            }

            var customize = CreateLocMenuItem("Toolbar_Customize");
            customize.SubmenuOpened += MenuToolbarSubmenu_Opened;
            foreach (var button in ToolbarButtons)
            {
                var item = CreateLocMenuItem(button.LocKey, button.Tag, MenuToolbarButton_Toggle);
                item.IsCheckable = true;
                customize.Items.Add(item);

                if (button.Tag == LastNavigationButtonTag)
                {
                    customize.Items.Add(CreateMenuSeparator());
                }
            }
            menu.Items.Add(customize);

            menu.Items.Add(CreateLocMenuItem("Toolbar_ShowAll", null, MenuShowAllToolbarButtons_Click));
            menu.Items.Add(CreateMenuSeparator());
            menu.Items.Add(CreateLocMenuItem("Toolbar_OpenSettings", null, MenuOpenToolbarSettings_Click));
            return menu;
        }

        /// <summary>Menüeintrag mit übersetztem Text, der sich bei Sprachwechsel mit aktualisiert.</summary>
        private static MenuItem CreateLocMenuItem(string locKey, string? tag = null, RoutedEventHandler? click = null)
        {
            var item = new MenuItem { Tag = tag };
            item.SetBinding(HeaderedItemsControl.HeaderProperty, new Binding($"[{locKey}]")
            {
                Source = LocalizationService.Instance,
                Mode = BindingMode.OneWay
            });
            if (click != null)
            {
                item.Click += click;
            }
            return item;
        }

        private static Separator CreateMenuSeparator()
        {
            var separator = new Separator();
            separator.SetResourceReference(BackgroundProperty, "BorderSubtleBrush");
            return separator;
        }

        public void ApplyToolbarButtonVisibilities()
        {
            var s = AppSettingsService.Instance.Settings;
            foreach (var button in ToolbarButtons)
            {
                button.Element(this).Visibility = button.IsVisible(s) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void SetToolbarButtonVisible(ToolbarButtonInfo button, bool visible)
        {
            button.SetVisible(AppSettingsService.Instance.Settings, visible);
            AppSettingsService.Instance.Save();
            ApplyToolbarButtonVisibilities();
        }

        private void MenuHideToolbarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && FindToolbarButton(item.Tag) is { } button)
            {
                SetToolbarButtonVisible(button, false);
            }
        }

        private void MenuToolbarSubmenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem parent) return;

            var s = AppSettingsService.Instance.Settings;
            foreach (var child in parent.Items.OfType<MenuItem>())
            {
                if (FindToolbarButton(child.Tag) is { } button)
                {
                    child.IsChecked = button.IsVisible(s);
                }
            }
        }

        private void MenuToolbarButton_Toggle(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && FindToolbarButton(item.Tag) is { } button)
            {
                SetToolbarButtonVisible(button, item.IsChecked);
            }
        }

        private void MenuShowAllToolbarButtons_Click(object sender, RoutedEventArgs e)
        {
            var s = AppSettingsService.Instance.Settings;
            foreach (var button in ToolbarButtons)
            {
                button.SetVisible(s, true);
            }
            AppSettingsService.Instance.Save();
            ApplyToolbarButtonVisibilities();
        }

        private void MenuOpenToolbarSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsTab();
        }
    }
}
