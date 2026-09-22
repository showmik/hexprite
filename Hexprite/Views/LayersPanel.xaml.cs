using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public partial class LayersPanel : UserControl
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        private Point _layerDragArmStart;
        private LayerItemViewModel? _armedLayerDragItem;

        internal const string LayerDragDataFormat = "Hexprite.LayerItemViewModel";

        internal static LayerItemViewModel? TryReadLayerDragPayload(IDataObject d)
        {
            if (d.GetData(LayerDragDataFormat) is LayerItemViewModel keyed)
                return keyed;
            if (d is DataObject dbo && dbo.GetData(typeof(LayerItemViewModel)) is LayerItemViewModel typed)
                return typed;
            return null;
        }

        internal static bool LayerDragPayloadPresent(IDataObject d)
        {
            return d.GetDataPresent(LayerDragDataFormat)
                   || (d is DataObject dbo && dbo.GetDataPresent(typeof(LayerItemViewModel)));
        }

        internal static DataObject WrapLayerDragPayload(LayerItemViewModel item)
        {
            var obj = new DataObject();
            obj.SetData(typeof(LayerItemViewModel), item);
            obj.SetData(LayerDragDataFormat, item);
            return obj;
        }

        private ListBoxItem? _opaqueDragRow;
        private double _opaqueStored = 1.0;

        public LayersPanel()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                // DynamicResource resolves late; fallback if brush missing.
                LayerInsertionGuide.Background ??= LayerList.TryFindResource("Brush.Canvas.Drawing") as Brush
                        ?? LayerList.TryFindResource("Brush.Border.Separator") as Brush
                        ?? Brushes.DeepSkyBlue;
            };
        }

        private void HideInsertionGuide() => LayerInsertionGuide.Visibility = Visibility.Collapsed;

        /// <summary>Positions the drop-indicator line in ListBox viewport coordinates.</summary>
        private void ShowInsertionGuideAt(double centerYInLayerListViewport)
        {
            const double h = 3;
            const double inset = 6;
            double hostH = LayerListClipHost.ActualHeight;
            if (hostH <= 0 || double.IsNaN(hostH))
                hostH = LayerList.ActualHeight;
            double maxY = Math.Max(0, hostH - h);
            double y = Math.Clamp(centerYInLayerListViewport - h / 2.0, 0, maxY);
            LayerInsertionGuide.Margin = new Thickness(inset, y, inset, 0);
            LayerInsertionGuide.Visibility = Visibility.Visible;
        }

        private int ResolveLayerItemIndex(FrameworkElement? fe)
        {
            if (fe == null) return -1;
            if (fe.DataContext is LayerItemViewModel item)
            {
                int itemIndex = LayerList.Items.IndexOf(item);
                if (itemIndex >= 0) return itemIndex;
            }
            if (LayerList.ContainerFromElement(fe) is DependencyObject container)
            {
                return LayerList.ItemContainerGenerator.IndexFromContainer(container);
            }
            return -1;
        }

        private void LayerVisible_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;
            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;
            bool isVisible = (fe as ToggleButton)?.IsChecked ?? true;
            ViewModel.SetLayerVisibility(index, isVisible);
        }

        private void LayerLocked_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;
            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;
            bool isLocked = (fe as ToggleButton)?.IsChecked ?? false;
            ViewModel.SetLayerLocked(index, isLocked);
        }

        private void LayerGlobal_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not MenuItem mi) return;
            int index = ResolveLayerItemIndex(mi);
            if (index < 0) return;
            if (mi.DataContext is not LayerItemViewModel item) return;
            bool targetValue = !item.IsGlobal;
            bool accepted = ViewModel.TrySetLayerGlobal(index, targetValue);
            if (!accepted)
            {
                mi.IsChecked = item.IsGlobal;
            }
        }

        private void LayerExclude_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not MenuItem mi) return;
            int index = ResolveLayerItemIndex(mi);
            if (index < 0) return;
            if (mi.DataContext is not LayerItemViewModel item) return;
            ViewModel.SetLayerExcludeFromExport(index, !item.ExcludeFromExport);
        }

        private void LayerOverflow_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not MenuItem mi) return;
            int index = ResolveLayerItemIndex(mi);
            if (index < 0) return;
            if (mi.DataContext is not LayerItemViewModel item) return;
            ViewModel.SetLayerOverflow(index, !item.PreserveOverflow);
        }

        private void LayerBlendMode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not MenuItem mi) return;
            int index = ResolveLayerItemIndex(mi);
            if (index < 0) return;
            string? tag = mi.Tag as string;
            if (!string.IsNullOrEmpty(tag) && Enum.TryParse<Core.LayerBlendMode>(tag, out var mode))
            {
                ViewModel.SetLayerBlendMode(index, mode);
            }
        }

        private void LayerOpacityMode_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not MenuItem mi) return;
            int index = ResolveLayerItemIndex(mi);
            if (index < 0) return;
            string? tag = mi.Tag as string;
            if (!string.IsNullOrEmpty(tag) && Enum.TryParse<Core.LayerOpacityMode>(tag, out var mode))
            {
                ViewModel.SetLayerOpacityMode(index, mode);
            }
        }

        private static void EndRenamingUi(LayerItemViewModel item) => item.IsRenaming = false;

        private void LayerName_LostFocus(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox tb) return;
            int index = ResolveLayerItemIndex(tb);
            if (index >= 0)
                ViewModel.UpdateLayerName(index, tb.Text);

            if (tb.DataContext is LayerItemViewModel item)
                EndRenamingUi(item);
        }

        private void LayerName_KeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null || sender is not TextBox tb) return;
            int index = ResolveLayerItemIndex(tb);
            if (index < 0) return;
            if (tb.DataContext is not LayerItemViewModel item) return;

            if (e.Key == Key.Enter)
            {
                ViewModel.UpdateLayerName(index, tb.Text);
                EndRenamingUi(item);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                tb.Text = item.Name;
                EndRenamingUi(item);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void LayerName_TextBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not TextBox tb || !tb.IsVisible) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                tb.Focus();
                tb.SelectAll();
            }), DispatcherPriority.Loaded);
        }

        private void LayerDisplayName_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount != 2 || ViewModel == null) return;
            int index = ResolveLayerItemIndex(sender as FrameworkElement);
            if (index < 0) return;
            ViewModel.BeginLayerRename(index);
            e.Handled = true;
        }

        private void LayerContextMenuRename_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;
            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;
            ViewModel.BeginLayerRename(index);
        }

        private void LayerItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;
            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;

            // If right-clicked layer is not already in selection, select it
            if (!ViewModel.SelectedLayerIndices.Contains(index))
            {
                ViewModel.SetSingleLayerSelection(index);
            }
        }

        private void LayerItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;

            // Ignore clicks on visibility/lock toggle buttons so they don't change selection
            var current = e.OriginalSource as DependencyObject;
            while (current != null && current != fe)
            {
                if (current is ToggleButton) return;
                current = current is Visual || current is System.Windows.Media.Media3D.Visual3D 
                    ? VisualTreeHelper.GetParent(current) 
                    : LogicalTreeHelper.GetParent(current);
            }

            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;

            // Additional validation: ensure the click is actually within the bounds of a layer item
            // This prevents selection when clicking between layers or on edges
            if (LayerList.ItemContainerGenerator.ContainerFromIndex(index) is not ListBoxItem container) return;

            // Get the position relative to the container
            var position = e.GetPosition(container);
            if (position.X < 0 || position.Y < 0 || 
                position.X > container.ActualWidth || position.Y > container.ActualHeight)
            {
                // Click is outside the valid bounds of this layer item
                return;
            }

            bool isCtrlDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            bool isShiftDown = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

            // Handle CTRL+Click to select all content of the layer (Photoshop-style)
            if (isCtrlDown && !isShiftDown)
            {
                ViewModel.SelectLayerContent(index);
                e.Handled = true;
                return;
            }

            // Handle multi-selection (Shift or Ctrl both toggle individual layers)
            if (isShiftDown || isCtrlDown)
            {
                // Shift+click or Ctrl+click: toggle individual layer selection
                ViewModel.ToggleLayerSelection(index);
                // Don't set LayerList.SelectedIndex - we manage multi-selection ourselves
                e.Handled = true;
            }
            else
            {
                // Normal click: clear multi-selection and select single layer
                ViewModel.SetSingleLayerSelection(index);
                // Don't mark as handled - let ListBox update its visual selection
                e.Handled = false;
            }
        }

        private void LayerList_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null || e.Handled) return;

            // If focus/source is inside a text input (such as layer rename TextBox), do not intercept keys
            if (Hexprite.Services.HexpriteShortcutManager.IsControlTextInput(e.OriginalSource as IInputElement))
            {
                return;
            }

            // Strangler Fig: F2 (Rename Layer) is registered in HexpriteShortcutManager (ShortcutScope.Window)
            // and handled upstream via RenameLayerCommand. The block below serves as a safety fallback.
            if (e.Key == Key.F2)
            {
                int idx = LayerList.SelectedIndex;
                if (idx >= 0)
                {
                    ViewModel.BeginLayerRename(idx);
                    e.Handled = true;
                }
            }
        }

        private static LayerItemViewModel? GetLayerRowDataContext(DependencyObject? leaf)
        {
            var cur = leaf;
            while (cur != null && cur is not ListBoxItem)
                cur = VisualTreeHelper.GetParent(cur);
            return (cur as FrameworkElement)?.DataContext as LayerItemViewModel;
        }

        private void LayerGrip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Capture the whole list so PreviewMouseMove still fires once the cursor leaves the narrow grip.
            _armedLayerDragItem = GetLayerRowDataContext(e.OriginalSource as DependencyObject)
                                  ?? ((sender as FrameworkElement)?.DataContext as LayerItemViewModel);
            _layerDragArmStart = e.GetPosition(relativeTo: null);
            Mouse.Capture(LayerList);
        }

        private void LayerList_PreviewMouseMove_ArmDrag(object sender, MouseEventArgs e)
        {
            if (_armedLayerDragItem == null)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _armedLayerDragItem = null;
                return;
            }

            var pos = e.GetPosition(relativeTo: null);
            if (Math.Abs(pos.X - _layerDragArmStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _layerDragArmStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var dragged = _armedLayerDragItem;
            _armedLayerDragItem = null;
            Mouse.Capture(element: null);

            if (dragged == null || ViewModel == null)
                return;

            ApplyDragSourceGhost(dragged);
            try
            {
                DragDrop.DoDragDrop(LayerList, WrapLayerDragPayload(dragged), DragDropEffects.Move);
            }
            finally
            {
                ClearLayerDragFx();
            }

            e.Handled = true;
        }

        private void LayerList_PreviewMouseLeftButtonUp_ArmDrag(object sender, MouseButtonEventArgs e)
        {
            if (_armedLayerDragItem != null || Mouse.Captured == LayerList)
            {
                if (Mouse.Captured == LayerList)
                    Mouse.Capture(element: null);
                _armedLayerDragItem = null;
            }
        }

        private void ApplyDragSourceGhost(LayerItemViewModel payload)
        {
            int i = LayerList.Items.IndexOf(payload);
            if (i < 0) return;
            if (LayerList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row) return;
            _opaqueDragRow = row;
            _opaqueStored = row.Opacity;
            row.Opacity = 0.45;
        }

        private void ClearLayerDragFx()
        {
            if (_opaqueDragRow != null)
                _opaqueDragRow.Opacity = _opaqueStored;
            _opaqueDragRow = null;
            _opaqueStored = 1.0;
            HideInsertionGuide();
        }

        private void LayerList_PreviewDragOver(object sender, DragEventArgs e)
        {
            if (!LayerDragPayloadPresent(e.Data))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var payload = TryReadLayerDragPayload(e.Data);
            int from = payload != null ? LayerList.Items.IndexOf(payload) : -1;

            var pHost = e.GetPosition(LayerListClipHost);
            if (LayerList.Items.Count == 0)
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
                HideInsertionGuide();
                return;
            }

            if (!TryComputeInsertionGap(pHost, out int slotVisual, out double lineY))
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                HideInsertionGuide();
                return;
            }

            int adjustedPreview = AdjustInsertIndex(slotVisual, from);
            ShowInsertionGuideAt(lineY);

            if (adjustedPreview == from)
            {
                e.Effects = DragDropEffects.None;
                HideInsertionGuide();
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private static int AdjustInsertIndex(int gapBeforeRow, int from)
        {
            if (from >= 0 && from < gapBeforeRow)
                return gapBeforeRow - 1;
            return gapBeforeRow;
        }

        /// <returns>False if computation failed (fallback to callers).</returns>
        private bool TryComputeInsertionGap(Point mouseInClipHost, out int slotBeforeRowOrCount, out double lineYPixelsInClipHost)
        {
            slotBeforeRowOrCount = 0;
            lineYPixelsInClipHost = 0;
            Rect? lastBounds = null;
            int lastRealizedIndex = -1;

            int n = LayerList.Items.Count;
            for (int i = 0; i < n; i++)
            {
                if (LayerList.ItemContainerGenerator.ContainerFromIndex(i) is not ListBoxItem row)
                    continue;

                lastRealizedIndex = i;

                double w = row.ActualWidth > 0 ? row.ActualWidth : row.RenderSize.Width;
                double h = row.ActualHeight > 0 ? row.ActualHeight : row.RenderSize.Height;
                var t = row.TransformToAncestor(LayerListClipHost);
                var bounds = t.TransformBounds(new Rect(0, 0, w, h));
                double mid = bounds.Top + bounds.Height / 2.0;

                if (mouseInClipHost.Y < mid)
                {
                    slotBeforeRowOrCount = i;
                    lineYPixelsInClipHost = bounds.Top;
                    return true;
                }

                lastBounds = bounds;
            }

            slotBeforeRowOrCount = lastRealizedIndex != -1 ? lastRealizedIndex + 1 : n;
            if (lastBounds.HasValue)
            {
                lineYPixelsInClipHost = lastBounds.Value.Bottom;
                return true;
            }

            lineYPixelsInClipHost = 0;
            return true;
        }

        private void LayerList_DragLeave(object sender, DragEventArgs e)
        {
            if (!LayerDragPayloadPresent(e.Data)) return;
            HideInsertionGuide();
        }

        private void LayerList_Drop(object sender, DragEventArgs e)
        {
            if (ViewModel == null)
            {
                ClearLayerDragFx();
                return;
            }

            if (!LayerDragPayloadPresent(e.Data))
            {
                ClearLayerDragFx();
                return;
            }

            var sourceItem = TryReadLayerDragPayload(e.Data);
            if (sourceItem == null)
            {
                ClearLayerDragFx();
                return;
            }

            int from = LayerList.Items.IndexOf(sourceItem);
            if (from < 0)
            {
                ClearLayerDragFx();
                return;
            }

            if (LayerList.Items.Count == 0 || !TryComputeInsertionGap(e.GetPosition(LayerListClipHost), out int slotVisual, out _))
            {
                ClearLayerDragFx();
                return;
            }

            int to = AdjustInsertIndex(slotVisual, from);
            if (to != from)
                ViewModel.MoveLayer(from, to);

            ClearLayerDragFx();
            e.Handled = true;
        }

        // ── Status bar: Layer hover info ─────────────────────────────────

        private void LayerItem_MouseEnter(object sender, MouseEventArgs e)
        {
            if (ViewModel == null || sender is not FrameworkElement fe) return;
            int index = ResolveLayerItemIndex(fe);
            if (index < 0) return;
            if (fe.DataContext is not LayerItemViewModel item) return;

            var indicators = new System.Collections.Generic.List<string>();
            if (!item.IsVisible) indicators.Add("[Hidden]");
            if (item.IsLocked) indicators.Add("[Locked]");

            string indicatorStr = indicators.Count > 0 ? " " + string.Join(' ', indicators) : "";
            ViewModel.LayerHoverInfo = string.Create(CultureInfo.InvariantCulture, $"Layer {index + 1}: {item.Name}{indicatorStr}");
        }

        private void LayerItem_MouseLeave(object sender, MouseEventArgs e)
        {
            ViewModel?.ClearLayerHoverInfo();
        }
    }
}
