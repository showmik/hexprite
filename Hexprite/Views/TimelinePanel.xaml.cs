using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public partial class TimelinePanel : UserControl
    {
        [GeneratedRegex(@"^[0-9]+$", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex DigitsOnlyRegex { get; }

        private Point _dragArmStart;
        private FrameItemViewModel? _armedDragItem;
        private const string DragDataFormat = "Hexprite.FrameItemViewModel";
        private ListBoxItem? _opaqueDragRow;
        private double _opaqueStored = 1.0;

        public TimelinePanel()
        {
            InitializeComponent();
        }

        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
        }

        private void FpsTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && DataContext is MainViewModel vm)
            {
                if (int.TryParse(tb.Text, out int val))
                    vm.FrameRateFps = Math.Clamp(val, 1, 60);
                tb.Text = vm.FrameRateFps.ToString(CultureInfo.InvariantCulture);
            }
        }

        private void FpsTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                FpsTextBox_LostFocus(sender, e);
                var scope = FocusManager.GetFocusScope(tb);
                FocusManager.SetFocusedElement(scope, null);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void FrameList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var fe = e.OriginalSource as FrameworkElement;
            var cur = (DependencyObject?)fe;
            while (cur != null && cur is not ListBoxItem)
                cur = VisualTreeHelper.GetParent(cur);

            if (cur is ListBoxItem item && item.DataContext is FrameItemViewModel frameItem)
            {
                if (!frameItem.IsSelected && DataContext is MainViewModel vm)
                {
                    vm.SynchronizeFrameSelection([frameItem], frameItem);
                }
            }
        }

        private void FrameList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var fe = e.OriginalSource as FrameworkElement;
            var cur = (DependencyObject?)fe;
            while (cur != null && cur is not ListBoxItem)
                cur = VisualTreeHelper.GetParent(cur);

            if (cur is FrameworkElement row && row.DataContext is FrameItemViewModel item)
            {
                _armedDragItem = item;
                _dragArmStart = e.GetPosition(relativeTo: null);
                // Mouse.Capture(FrameList) removed to allow normal frame selection clicks
            }
        }

        private void FrameList_PreviewMouseMove_ArmDrag(object sender, MouseEventArgs e)
        {
            if (_armedDragItem == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _armedDragItem = null;
                return;
            }
            var pos = e.GetPosition(relativeTo: null);
            if (Math.Abs(pos.X - _dragArmStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragArmStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            var dragged = _armedDragItem;
            _armedDragItem = null;

            if (dragged == null) return;

            var draggedItems = new System.Collections.Generic.List<FrameItemViewModel>();
            if (dragged.IsSelected && DataContext is MainViewModel vm)
            {
                foreach (var frame in vm.Frames)
                {
                    if (frame.IsSelected) draggedItems.Add(frame);
                }
            }
            else
            {
                draggedItems.Add(dragged);
            }

            // Visual feedback for primary item
            int idx = FrameList.Items.IndexOf(dragged);
            if (idx >= 0 && FrameList.ItemContainerGenerator.ContainerFromIndex(idx) is ListBoxItem row)
            {
                _opaqueDragRow = row;
                _opaqueStored = row.Opacity;
                row.Opacity = 0.45;
            }

            try
            {
                var obj = new DataObject();
                obj.SetData(typeof(System.Collections.Generic.List<FrameItemViewModel>), draggedItems);
                obj.SetData(DragDataFormat, draggedItems);
                DragDrop.DoDragDrop(FrameList, obj, DragDropEffects.Move);
            }
            finally
            {
                if (_opaqueDragRow != null) _opaqueDragRow.Opacity = _opaqueStored;
                _opaqueDragRow = null;
                FrameInsertionGuide.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        private void FrameList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _armedDragItem = null;
        }

        private void FrameList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.SynchronizeFrameSelection(
                    FrameList.SelectedItems.OfType<FrameItemViewModel>(),
                    FrameList.SelectedItem as FrameItemViewModel);
            }

            if (FrameList.SelectedItem is FrameItemViewModel activeItem)
            {
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    FrameList.ScrollIntoView(activeItem);
                });
            }
        }

        private void FrameList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                vm.SelectAllFramesCommand.Execute(parameter: null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
            {
                vm.SelectActiveFrameOnlyCommand.Execute(parameter: null);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Delete || e.Key == Key.Back))
            {
                if (vm.DeleteFrameCommand.CanExecute(parameter: null))
                    vm.DeleteFrameCommand.Execute(parameter: null);
                e.Handled = true;
            }
        }

        internal static void HandleBatchButtonClick(ToggleButton btn)
        {
            if (btn.ContextMenu == null)
                return;

            if (btn.IsChecked == true)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
            else
            {
                btn.ContextMenu.IsOpen = false;
            }
        }

        internal static void HandleBatchContextMenuClosed(ToggleButton? btn)
        {
            if (btn != null)
            {
                btn.IsChecked = false;
            }
        }

        internal void BatchContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            var btn = (sender as ContextMenu)?.PlacementTarget as ToggleButton ?? BatchButton;
            HandleBatchContextMenuClosed(btn);
        }

        internal void BatchButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton btn)
            {
                HandleBatchButtonClick(btn);
            }
        }

        private void FrameList_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DragDataFormat))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                FrameInsertionGuide.Visibility = Visibility.Collapsed;
                return;
            }

            // Get position relative to the static host to account for ScrollViewer offset
            var pHost = e.GetPosition(FrameListClipHost);
            if (TryComputeHorizontalGap(pHost, out _, out double lineX))
            {
                double hostW = FrameListClipHost.ActualWidth > 0 ? FrameListClipHost.ActualWidth : FrameList.ActualWidth;
                double x = Math.Clamp(lineX - 1.5, 0, Math.Max(0, hostW - 3));
                FrameInsertionGuide.Margin = new Thickness(x, 6, 0, 6);
                FrameInsertionGuide.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Move;
            }
            else
            {
                e.Effects = DragDropEffects.None;
                FrameInsertionGuide.Visibility = Visibility.Collapsed;
            }
            e.Handled = true;
        }

        private void FrameList_DragLeave(object sender, DragEventArgs e)
        {
            FrameInsertionGuide.Visibility = Visibility.Collapsed;
        }

        private void FrameList_Drop(object sender, DragEventArgs e)
        {
            FrameInsertionGuide.Visibility = Visibility.Collapsed;
            if (DataContext is not MainViewModel vm || !e.Data.GetDataPresent(DragDataFormat)) return;
            if (e.Data.GetData(DragDataFormat) is not List<FrameItemViewModel> payload || payload.Count == 0) return;

            if (TryComputeHorizontalGap(e.GetPosition(FrameListClipHost), out int slotVisual, out _))
            {
                vm.MoveFrames(payload, slotVisual);
            }
            e.Handled = true;
        }

        private bool TryComputeHorizontalGap(Point mouseInHost, out int slot, out double lineX)
        {
            slot = 0; lineX = 0;
            Rect? lastBounds = null;
            int lastRealizedIndex = -1;
            int n = FrameList.Items.Count;
            for (int i = 0; i < n; i++)
            {
                if (FrameList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row) continue;
                lastRealizedIndex = i;

                double w = row.ActualWidth > 0 ? row.ActualWidth : row.RenderSize.Width;
                double h = row.ActualHeight > 0 ? row.ActualHeight : row.RenderSize.Height;

                // Transform to Host instead of ListBox to fix positioning when scrolled
                var t = row.TransformToAncestor(FrameListClipHost);
                var bounds = t.TransformBounds(new Rect(0, 0, w, h));
                double mid = bounds.Left + bounds.Width / 2.0;

                if (mouseInHost.X < mid)
                {
                    slot = i; lineX = bounds.Left;
                    return true;
                }
                lastBounds = bounds;
            }
            slot = lastRealizedIndex != -1 ? lastRealizedIndex + 1 : n;
            if (lastBounds.HasValue) { lineX = lastBounds.Value.Right; return true; }
            return false;
        }

        internal static void HandleFrameListPreviewMouseWheel(ScrollViewer scrollViewer, MouseWheelEventArgs e)
        {
            if (scrollViewer.ScrollableWidth > 0)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void FrameListScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                HandleFrameListPreviewMouseWheel(scrollViewer, e);
            }
        }
    }
}
