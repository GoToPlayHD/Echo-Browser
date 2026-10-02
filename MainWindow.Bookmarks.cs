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
    /// <summary>Lesezeichenleiste, Lesezeichen-Gruppen und deren Drag & Drop.</summary>
    public partial class MainWindow
    {
        #region Bookmarks Bar Reordering & Interactions

        private void CheckBookmarkStatus()
        {
            if (ActiveTab == null || IsStartPage(ActiveTab.Url))
            {
                pathBookmarkStar.Data = Geometry.Parse("M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z");
                pathBookmarkStar.Fill = FindResource("AccentSilverDimBrush") as Brush ?? Brushes.Gray;
                btnBookmark.ToolTip = "Startseite kann nicht als Lesezeichen gespeichert werden";
                return;
            }

            bool isBookmarked = _bookmarkService.IsBookmarked(ActiveTab.Url);
            if (isBookmarked)
            {
                pathBookmarkStar.Data = Geometry.Parse("M12 17.27L18.18 21l-1.64-7.03L22 9.24l-7.19-.61L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21z");
                pathBookmarkStar.Fill = FindResource("StatusWarningBrush") as Brush ?? Brushes.Gold;
                btnBookmark.ToolTip = "Lesezeichen bearbeiten oder entfernen";
            }
            else
            {
                pathBookmarkStar.Data = Geometry.Parse("M22 9.24l-7.19-.62L12 2 9.19 8.63 2 9.24l5.46 4.73L5.82 21 12 17.27 18.18 21l-1.63-7.03L22 9.24zM12 15.4l-3.76 2.27 1-4.28-3.32-2.88 4.38-.38L12 6.1l1.71 4.04 4.38.38-3.32 2.88 1 4.28L12 15.4z");
                pathBookmarkStar.Fill = FindResource("AccentSilverDimBrush") as Brush ?? Brushes.Gray;
                btnBookmark.ToolTip = "Diese Seite als Lesezeichen speichern (Ctrl+D)";
            }
        }

        private void BtnBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (ActiveTab == null || string.IsNullOrWhiteSpace(ActiveTab.Url) || IsStartPage(ActiveTab.Url)) return;

            string title = string.IsNullOrWhiteSpace(ActiveTab.Title) ? ActiveTab.Url : ActiveTab.Title;
            _bookmarkService.ToggleBookmark(title, ActiveTab.Url);
            CheckBookmarkStatus();
        }

        private void BookmarkChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string url)
            {
                NavigateToInput(url);
            }
        }

        private void MenuItemOpenBookmarkNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string url)
            {
                AddNewTab(url);
            }
        }

        private void MenuItemDeleteBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm)
            {
                _bookmarkService.RemoveBookmark(bm);
                CheckBookmarkStatus();
            }
        }

        // Bookmark Drag-and-Drop Reordering with Live Feedback
        private void BookmarkChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                _bmDragStartPoint = e.GetPosition(null);
                _draggedBookmark = bm;
            }
        }

        private void BookmarkChip_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedBookmark != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    if (sender is FrameworkElement fe)
                    {
                        var data = new DataObject("EchoBookmark", _draggedBookmark);
                        DragDrop.DoDragDrop(fe, data, DragDropEffects.Move);
                        _draggedBookmark = null;
                    }
                }
            }
        }

        private void BookmarkChip_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _draggedBookmark = null;
        }

        private void BookmarkChip_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                // Live reorder only between non-group bookmarks that are already top-level
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm)
                {
                    if (sourceBm != targetBm && !targetBm.IsGroup && !sourceBm.IsGroup &&
                        Bookmarks.Contains(sourceBm) && Bookmarks.Contains(targetBm))
                    {
                        int oldIndex = Bookmarks.IndexOf(sourceBm);
                        int newIndex = Bookmarks.IndexOf(targetBm);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            Bookmarks.Move(oldIndex, newIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
        }

        private void BookmarkChip_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm)
            {
                e.Handled = true;
                popupBookmarkGroup.IsOpen = false;

                if (sourceBm != targetBm)
                {
                    // If sourceBm was in a group, remove from group and insert next to targetBm in top-level Bookmarks
                    Bookmark? parentGroup = null;
                    if (e.Data.GetDataPresent("EchoBookmarkSourceGroup") &&
                        e.Data.GetData("EchoBookmarkSourceGroup") is Bookmark sg)
                    {
                        parentGroup = sg;
                    }
                    else
                    {
                        parentGroup = Bookmarks.FirstOrDefault(b => b.IsGroup && b.Children.Contains(sourceBm));
                    }

                    if (parentGroup != null)
                    {
                        parentGroup.Children.Remove(sourceBm);

                        int targetIndex = Bookmarks.IndexOf(targetBm);
                        if (targetIndex >= 0)
                        {
                            Bookmarks.Insert(targetIndex, sourceBm);
                        }
                        else
                        {
                            Bookmarks.Add(sourceBm);
                        }

                        _bookmarkService.SaveBookmarks();
                        CheckBookmarkStatus();
                    }
                    else if (Bookmarks.Contains(sourceBm) && Bookmarks.Contains(targetBm))
                    {
                        int oldIndex = Bookmarks.IndexOf(sourceBm);
                        int targetIndex = Bookmarks.IndexOf(targetBm);
                        if (oldIndex >= 0 && targetIndex >= 0 && oldIndex != targetIndex)
                        {
                            Bookmarks.Move(oldIndex, targetIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
            else
            {
                e.Handled = true;
            }
        }

        private void BookmarksBar_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void BookmarksBar_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm)
            {
                e.Handled = true;
                popupBookmarkGroup.IsOpen = false;

                // Check if bookmark was in a group
                Bookmark? parentGroup = null;
                if (e.Data.GetDataPresent("EchoBookmarkSourceGroup") &&
                    e.Data.GetData("EchoBookmarkSourceGroup") is Bookmark sg)
                {
                    parentGroup = sg;
                }
                else
                {
                    parentGroup = Bookmarks.FirstOrDefault(b => b.IsGroup && b.Children.Contains(sourceBm));
                }

                if (parentGroup != null)
                {
                    parentGroup.Children.Remove(sourceBm);
                }
                else if (Bookmarks.Contains(sourceBm))
                {
                    Bookmarks.Remove(sourceBm);
                }

                // Determine insertion index based on mouse position relative to itemsBookmarksBar
                Point dropPos = e.GetPosition(itemsBookmarksBar);
                int dropIndex = GetBookmarkDropIndexAtPoint(dropPos);
                if (dropIndex >= 0 && dropIndex <= Bookmarks.Count)
                {
                    Bookmarks.Insert(dropIndex, sourceBm);
                }
                else
                {
                    Bookmarks.Add(sourceBm);
                }

                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();
            }
        }

        private int GetBookmarkDropIndexAtPoint(Point pointInItemsControl)
        {
            if (itemsBookmarksBar == null || Bookmarks.Count == 0) return Bookmarks.Count;

            for (int i = 0; i < itemsBookmarksBar.Items.Count; i++)
            {
                var container = itemsBookmarksBar.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                if (container != null && container.IsLoaded)
                {
                    try
                    {
                        GeneralTransform transform = container.TransformToAncestor(itemsBookmarksBar);
                        Point containerPos = transform.Transform(new Point(0, 0));
                        double containerMidX = containerPos.X + (container.ActualWidth / 2.0);

                        if (pointInItemsControl.X < containerMidX)
                        {
                            return i;
                        }
                    }
                    catch
                    {
                        // Fallback if transform fails
                    }
                }
            }

            return Bookmarks.Count;
        }

        private void GroupChip_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm && !sourceBm.IsGroup)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;

                    if (sender is FrameworkElement fe)
                    {
                        var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                        if (border != null)
                        {
                            border.BorderBrush = FindResource("AccentSilverBrightBrush") as Brush;
                            border.Background = FindResource("BookmarkItemHoverBrush") as Brush;
                        }
                    }
                }
            }
        }

        private void GroupChip_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe)
            {
                var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                if (border != null)
                {
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BackgroundProperty);
                }
            }
        }

        private void GroupChip_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark targetGroup && targetGroup.IsGroup)
            {
                var border = FindVisualChild<Border>(fe, b => b.Name == "groupChipBorder") ?? FindVisualChild<Border>(fe);
                if (border != null)
                {
                    border.ClearValue(Border.BorderBrushProperty);
                    border.ClearValue(Border.BackgroundProperty);
                }

                if (e.Data.GetDataPresent("EchoBookmark") &&
                    e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    !sourceBm.IsGroup &&
                    sourceBm != targetGroup)
                {
                    popupBookmarkGroup.IsOpen = false;

                    // Remove from top-level Bookmarks or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    // Add to target group
                    targetGroup.Children.Add(sourceBm);
                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    e.Handled = true;
                }
            }
        }

        #region Bookmarks Bar Empty Space & Group Handling

        private Bookmark? _targetGroupForAdd;

        private void MenuAddBookmark_Click(object sender, RoutedEventArgs e)
        {
            OpenAddBookmarkDialog(null);
        }

        private void MenuAddGroup_Click(object sender, RoutedEventArgs e)
        {
            txtAddGroupName.Text = "";
            popupAddGroup.IsOpen = true;
            txtAddGroupName.Focus();
        }

        private void OpenAddBookmarkDialog(Bookmark? preselectedGroup)
        {
            _targetGroupForAdd = preselectedGroup;

            if (ActiveTab != null && ActiveTab.Url != StartPageService.StartPageUrl)
            {
                txtAddBmTitle.Text = ActiveTab.Title;
                txtAddBmUrl.Text = ActiveTab.Url;
            }
            else
            {
                txtAddBmTitle.Text = "";
                txtAddBmUrl.Text = "https://";
            }

            cmbAddBmGroup.Items.Clear();
            var mainItem = new ComboBoxItem { Content = "(Hauptleiste)", Tag = null };
            cmbAddBmGroup.Items.Add(mainItem);
            cmbAddBmGroup.SelectedItem = mainItem;

            foreach (var b in Bookmarks.Where(b => b.IsGroup))
            {
                var item = new ComboBoxItem { Content = "📁 " + b.Title, Tag = b };
                cmbAddBmGroup.Items.Add(item);
                if (preselectedGroup == b)
                {
                    cmbAddBmGroup.SelectedItem = item;
                }
            }

            popupAddBookmark.IsOpen = true;
            txtAddBmTitle.Focus();
        }

        private void BtnCancelAddBm_Click(object sender, RoutedEventArgs e)
        {
            popupAddBookmark.IsOpen = false;
        }

        private void BtnSaveAddBm_Click(object sender, RoutedEventArgs e)
        {
            string title = txtAddBmTitle.Text.Trim();
            string url = txtAddBmUrl.Text.Trim();

            if (string.IsNullOrWhiteSpace(url)) return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                url = "https://" + url;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = url.Replace("https://", "").Replace("http://", "").Split('/')[0];
            }

            Bookmark? targetGroup = null;
            if (cmbAddBmGroup.SelectedItem is ComboBoxItem cItem && cItem.Tag is Bookmark grp)
            {
                targetGroup = grp;
            }

            _bookmarkService.AddBookmark(title, url, targetGroup);
            CheckBookmarkStatus();
            popupAddBookmark.IsOpen = false;
        }

        private void BtnCancelAddGroup_Click(object sender, RoutedEventArgs e)
        {
            popupAddGroup.IsOpen = false;
        }

        private void BtnSaveAddGroup_Click(object sender, RoutedEventArgs e)
        {
            string name = txtAddGroupName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;

            _bookmarkService.AddGroup(name);
            popupAddGroup.IsOpen = false;
        }

        private void GroupChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && ((fe.Tag as Bookmark) ?? (fe.DataContext as Bookmark)) is Bookmark group && group.IsGroup)
            {
                _activeGroupForPopup = group;
                txtBookmarkGroupName.Text = group.Title;
                txtBookmarkGroupCount.Text = group.Children.Count.ToString();
                txtGroupEmptyState.Visibility = group.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                itemsGroupBookmarks.ItemsSource = group.Children;

                popupBookmarkGroup.PlacementTarget = fe;
                popupBookmarkGroup.IsOpen = true;
            }
        }

        private void BtnCloseGroupPopup_Click(object sender, RoutedEventArgs e)
        {
            popupBookmarkGroup.IsOpen = false;
        }

        private void BtnAddBookmarkToCurrentGroup_Click(object sender, RoutedEventArgs e)
        {
            var group = _activeGroupForPopup;
            popupBookmarkGroup.IsOpen = false;
            OpenAddBookmarkDialog(group);
        }

        private void BtnDeleteCurrentGroup_Click(object sender, RoutedEventArgs e)
        {
            if (_activeGroupForPopup != null)
            {
                var group = _activeGroupForPopup;
                popupBookmarkGroup.IsOpen = false;
                _bookmarkService.RemoveBookmark(group);
                CheckBookmarkStatus();
            }
        }

        private void GroupBookmarkItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                _bmDragStartPoint = e.GetPosition(null);
                _draggedBookmark = bm;
            }
        }

        private void GroupBookmarkItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedBookmark != null)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var bm = _draggedBookmark;
                    var sourceGroup = _activeGroupForPopup;

                    var data = new DataObject("EchoBookmark", bm);
                    if (sourceGroup != null)
                    {
                        data.SetData("EchoBookmarkSourceGroup", sourceGroup);
                    }

                    try
                    {
                        popupBookmarkGroup.StaysOpen = true;
                        DragDrop.DoDragDrop(this, data, DragDropEffects.Move);
                    }
                    finally
                    {
                        popupBookmarkGroup.StaysOpen = false;
                        _draggedBookmark = null;
                    }
                }
            }
        }

        private void GroupBookmarkItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedBookmark != null && sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                Point currentPos = e.GetPosition(null);
                Vector diff = _bmDragStartPoint - currentPos;
                if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                {
                    _draggedBookmark = null;
                    popupBookmarkGroup.IsOpen = false;
                    NavigateToInput(bm.Url);
                }
            }
            _draggedBookmark = null;
        }

        private void GroupBookmarkItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement fe && fe.DataContext is Bookmark bm)
            {
                popupBookmarkGroup.IsOpen = false;
                AddNewTab(bm.Url);
            }
        }

        private void MenuGroupBookmarkOpenNewTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm)
            {
                popupBookmarkGroup.IsOpen = false;
                AddNewTab(bm.Url);
            }
        }

        private void MenuGroupBookmarkMoveToBar_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm && _activeGroupForPopup != null)
            {
                _activeGroupForPopup.Children.Remove(bm);
                Bookmarks.Add(bm);
                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();

                txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MenuGroupBookmarkDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark bm && _activeGroupForPopup != null)
            {
                _activeGroupForPopup.Children.Remove(bm);
                _bookmarkService.SaveBookmarks();
                CheckBookmarkStatus();

                txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void GroupBookmarkItem_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;

                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                    sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm &&
                    _activeGroupForPopup != null)
                {
                    if (sourceBm != targetBm && _activeGroupForPopup.Children.Contains(sourceBm) && _activeGroupForPopup.Children.Contains(targetBm))
                    {
                        int oldIndex = _activeGroupForPopup.Children.IndexOf(sourceBm);
                        int newIndex = _activeGroupForPopup.Children.IndexOf(targetBm);
                        if (oldIndex >= 0 && newIndex >= 0)
                        {
                            _activeGroupForPopup.Children.Move(oldIndex, newIndex);
                            _bookmarkService.SaveBookmarks();
                        }
                    }
                }
            }
        }

        private void GroupBookmarkItem_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                sender is FrameworkElement fe && fe.DataContext is Bookmark targetBm &&
                _activeGroupForPopup != null)
            {
                e.Handled = true;

                if (!_activeGroupForPopup.Children.Contains(sourceBm) && !sourceBm.IsGroup)
                {
                    // Remove from top-level or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    int targetIndex = _activeGroupForPopup.Children.IndexOf(targetBm);
                    if (targetIndex >= 0)
                    {
                        _activeGroupForPopup.Children.Insert(targetIndex, sourceBm);
                    }
                    else
                    {
                        _activeGroupForPopup.Children.Add(sourceBm);
                    }

                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                    txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void GroupFlyout_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark"))
            {
                if (e.Data.GetData("EchoBookmark") is Bookmark sourceBm && !sourceBm.IsGroup)
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                }
            }
        }

        private void GroupFlyout_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("EchoBookmark") &&
                e.Data.GetData("EchoBookmark") is Bookmark sourceBm &&
                !sourceBm.IsGroup &&
                _activeGroupForPopup != null)
            {
                e.Handled = true;

                if (!_activeGroupForPopup.Children.Contains(sourceBm))
                {
                    // Remove from top-level or from another group
                    if (Bookmarks.Contains(sourceBm))
                    {
                        Bookmarks.Remove(sourceBm);
                    }
                    else
                    {
                        foreach (var g in Bookmarks.Where(b => b.IsGroup))
                        {
                            if (g.Children.Contains(sourceBm))
                            {
                                g.Children.Remove(sourceBm);
                                break;
                            }
                        }
                    }

                    _activeGroupForPopup.Children.Add(sourceBm);
                    _bookmarkService.SaveBookmarks();
                    CheckBookmarkStatus();
                    txtBookmarkGroupCount.Text = _activeGroupForPopup.Children.Count.ToString();
                    txtGroupEmptyState.Visibility = _activeGroupForPopup.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void MenuItemAddBookmarkToGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark group)
            {
                OpenAddBookmarkDialog(group);
            }
        }

        private void MenuItemDeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is Bookmark group)
            {
                _bookmarkService.RemoveBookmark(group);
                CheckBookmarkStatus();
            }
        }

        #endregion

        #endregion
    }
}
