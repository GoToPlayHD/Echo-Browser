using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Threading;
using EchoBrowser.Models;
using EchoBrowser.Services;

namespace EchoBrowser
{
    /// <summary>Angeheftete Tabs und Tab-Gruppen (Farbe, Name, einklappen) – in der Tab-Leiste und im Kontextmenü.</summary>
    public partial class MainWindow
    {
        #region Angeheftete Tabs & Tab-Gruppen

        /// <summary>Inhalt der Tab-Leiste: Tabs und vor jeder Gruppe ihr Kopf (<see cref="TabOrder.Compose"/>).</summary>
        public ObservableCollection<object> StripItems { get; } = new();

        private bool _isNormalizingTabs;
        private TabGroup? _editedGroup;

        private void InitializeTabGroups()
        {
            Tabs.CollectionChanged += (s, e) =>
            {
                if (!_isNormalizingTabs) RefreshTabStrip();
            };

            groupEditor.Changed += ScheduleSessionSave;
            groupEditor.NewTabRequested += () => { popupGroupEditor.IsOpen = false; if (_editedGroup != null) NewTabInGroup(_editedGroup); };
            groupEditor.UngroupRequested += () => { popupGroupEditor.IsOpen = false; if (_editedGroup != null) Ungroup(_editedGroup); };
            groupEditor.CloseGroupRequested += () => { popupGroupEditor.IsOpen = false; if (_editedGroup != null) CloseGroup(_editedGroup); };
            groupEditor.Done += () => popupGroupEditor.IsOpen = false;
            popupGroupEditor.Closed += (s, e) => _editedGroup = null;
        }

        private IEnumerable<TabGroup> GroupsInUse() => Tabs.Select(t => t.Group).OfType<TabGroup>().Distinct();

        /// <summary>Reihenfolge herstellen (angeheftete vorne, Gruppen zusammen) und die Tab-Leiste abgleichen.</summary>
        private void NormalizeTabs()
        {
            var order = TabOrder.Normalize(Tabs.ToList(), t => t.Group, t => t.IsPinned);
            _isNormalizingTabs = true;
            try
            {
                ListSync.Apply(Tabs, order);
            }
            finally
            {
                _isNormalizingTabs = false;
            }
            RefreshTabStrip();
            ScheduleSessionSave();
        }

        private void RefreshTabStrip()
        {
            foreach (var group in GroupsInUse())
            {
                group.TabCount = Tabs.Count(t => t.Group == group);
            }
            ListSync.Apply(StripItems, TabOrder.Compose(Tabs.ToList(), t => t.Group));
            foreach (var tab in Tabs)
            {
                tab.IsHiddenByGroup = tab.Group?.IsCollapsed == true;
            }
            FindVisualChild<EchoBrowser.Views.Controls.TabStripPanel>(itemsTabs)?.InvalidateMeasure();
        }

        // ---------- Anheften ----------

        /// <summary>Anheften setzt den Tab ans Ende der angehefteten, Loslösen an den Anfang der übrigen (wie Chrome).</summary>
        private void TogglePin(BrowserTab tab)
        {
            tab.IsPinned = !tab.IsPinned;
            if (tab.IsPinned) tab.Group = null;

            int target = Tabs.Count(t => t.IsPinned && t != tab);
            int index = Tabs.IndexOf(tab);
            if (index >= 0 && index != target) Tabs.Move(index, Math.Min(target, Tabs.Count - 1));
            NormalizeTabs();
        }

        // ---------- Gruppen ----------

        private void AddTabToNewGroup(BrowserTab tab)
        {
            var group = new TabGroup { Color = TabGroupPalette.NextColor(GroupsInUse().Select(g => g.Color)) };
            tab.IsPinned = false;
            tab.Group = group;
            NormalizeTabs();

            // Wie Chrome: gleich den Namen eingeben lassen
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => OpenGroupEditor(group, focusName: true));
        }

        private void AddTabToGroup(BrowserTab tab, TabGroup group)
        {
            tab.IsPinned = false;
            tab.Group = group;

            // Ans Ende der Gruppe
            var last = Tabs.LastOrDefault(t => t.Group == group && t != tab);
            if (last != null)
            {
                int from = Tabs.IndexOf(tab);
                int to = Tabs.IndexOf(last);
                Tabs.Move(from, from < to ? to : to + 1);
            }
            NormalizeTabs();
        }

        private void RemoveFromGroup(BrowserTab tab)
        {
            tab.Group = null;
            NormalizeTabs();
        }

        private void Ungroup(TabGroup group)
        {
            foreach (var tab in Tabs.Where(t => t.Group == group)) tab.Group = null;
            group.IsCollapsed = false;
            NormalizeTabs();
        }

        private void CloseGroup(TabGroup group)
        {
            foreach (var tab in Tabs.Where(t => t.Group == group).ToList()) CloseTab(tab);
        }

        private void NewTabInGroup(TabGroup group)
        {
            var last = Tabs.LastOrDefault(t => t.Group == group);
            if (last == null) return;
            if (group.IsCollapsed) ToggleGroupCollapsed(group);
            AddNewTabAt(Tabs.IndexOf(last) + 1, StartPageService.StartPageUrl);
        }

        /// <summary>Einklappen blendet die Tabs der Gruppe aus; war der aktive Tab dabei, wird ein anderer aktiv (wie Chrome).</summary>
        private void ToggleGroupCollapsed(TabGroup group)
        {
            group.IsCollapsed = !group.IsCollapsed;

            if (group.IsCollapsed && ActiveTab?.Group == group)
            {
                int index = Tabs.IndexOf(ActiveTab);
                var next = Tabs.Skip(index + 1).FirstOrDefault(t => t.Group?.IsCollapsed != true && t.Group != group)
                    ?? Tabs.Take(index).LastOrDefault(t => t.Group?.IsCollapsed != true && t.Group != group);
                if (next != null)
                {
                    SelectTab(next);
                }
                else
                {
                    AddNewTab(StartPageService.StartPageUrl);
                }
            }

            RefreshTabStrip();
            ScheduleSessionSave();
        }

        /// <summary>Ein Tab einer eingeklappten Gruppe wird aktiv (Strg+Tab, Befehlspalette …): Gruppe aufklappen.</summary>
        private void ExpandGroupOf(BrowserTab tab)
        {
            if (tab.Group is not { IsCollapsed: true } group) return;
            group.IsCollapsed = false;
            RefreshTabStrip();
            ScheduleSessionSave();
        }

        private void GroupHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TabGroup group })
            {
                ToggleGroupCollapsed(group);
                e.Handled = true;
            }
        }

        private void GroupHeader_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement { DataContext: TabGroup group } header)
            {
                OpenGroupEditor(group, focusName: false, header);
                e.Handled = true;
            }
        }

        /// <summary>Name, Farbe und Aktionen einer Gruppe – unter ihrem Kopf.</summary>
        private void OpenGroupEditor(TabGroup group, bool focusName, FrameworkElement? header = null)
        {
            header ??= VisibleTabItems.ItemContainerGenerator.ContainerFromItem(group) as FrameworkElement;
            if (header == null) return;

            _editedGroup = group;
            groupEditor.Show(group);
            popupGroupEditor.PlacementTarget = header;
            popupGroupEditor.IsOpen = true;
            if (focusName) FocusWpfInput(groupEditor.NameBox);
        }

        /// <summary>Einträge des Tab-Kontextmenüs für Anheften und Gruppen.</summary>
        private void AddPinAndGroupMenuItems(ContextMenu menu, BrowserTab tab)
        {
            menu.Items.Add(MenuEntry(Tr.Get(tab.IsPinned ? "Tab_Unpin" : "Tab_Pin"), () => TogglePin(tab)));
            menu.Items.Add(MenuEntry(Tr.Get("Tab_AddToNewGroup"), () => AddTabToNewGroup(tab)));

            var otherGroups = GroupsInUse().Where(g => g != tab.Group).ToList();
            if (otherGroups.Count > 0)
            {
                var submenu = new MenuItem { Header = new TextBlock { Text = Tr.Get("Tab_AddToGroup") } };
                foreach (var group in otherGroups)
                {
                    var item = MenuEntry(group.HasName ? group.Name : Tr.Get("Group_Unnamed"), () => AddTabToGroup(tab, group));
                    item.Icon = new Ellipse { Width = 10, Height = 10, Fill = group.ColorBrush };
                    submenu.Items.Add(item);
                }
                menu.Items.Add(submenu);
            }

            if (tab.Group != null)
            {
                menu.Items.Add(MenuEntry(Tr.Get("Tab_RemoveFromGroup"), () => RemoveFromGroup(tab)));
            }
        }

        /// <summary>Nach Drag &amp; Drop: mitten in einer Gruppe beitreten, außerhalb ihrer Gruppe austreten.</summary>
        private void FinishTabDrag(BrowserTab tab)
        {
            int index = Tabs.IndexOf(tab);
            if (index < 0) return;

            if (!tab.IsPinned)
            {
                var left = index > 0 ? Tabs[index - 1] : null;
                var right = index < Tabs.Count - 1 ? Tabs[index + 1] : null;
                tab.Group = TabOrder.GroupAfterMove(left?.Group, right?.Group, tab.Group);
            }
            NormalizeTabs();
        }

        #endregion
    }
}
