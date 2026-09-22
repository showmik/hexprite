using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Views
{
    public partial class SpriteSheetSlicerWindow : Window
    {
        public SpriteSheetSlicerViewModel ViewModel => (SpriteSheetSlicerViewModel)DataContext;

        public SpriteSheetSlicerWindow(SpriteSheetSlicerViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            viewModel.RequestClose = Close;
            Closed += (s, e) => viewModel.Dispose();
        }

        public SpriteSheetSlicerWindow(
            IWorkspaceTabService? tabService = null,
            SpriteState? initialSprite = null,
            SpriteSheetSliceSettings? initialSettings = null)
            : this(new SpriteSheetSlicerViewModel(
                tabService: tabService,
                initialSprite: initialSprite,
                initialSettings: initialSettings))
        {
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void CaptionMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
            }
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                    // Ignore DragMove exceptions if mouse is captured or button released
                }
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (TxtCaptionMaxGlyph != null)
            {
                // &#xE922; is Maximize icon, &#xE923; is Restore icon
                TxtCaptionMaxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }
        }

        private void BtnShortcuts_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnExportMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                if (DragOverlay != null)
                {
                    DragOverlay.Visibility = Visibility.Visible;
                }
                e.Handled = true;
            }
        }

        private void Window_DragLeave(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    ViewModel.HandleFileDrop(files);
                    e.Handled = true;
                }
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Delta > 0)
                {
                    ViewModel.ZoomIn();
                }
                else if (e.Delta < 0)
                {
                    ViewModel.ZoomOut();
                }
                e.Handled = true;
            }
        }

        private Point _atlasPanStartPoint;
        private double _atlasScrollHorizontalStart;
        private double _atlasScrollVerticalStart;
        private bool _isPanningAtlas;

        private void AtlasScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            bool isMiddle = e.ChangedButton == MouseButton.Middle;
            bool isSpaceLeft = e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space);

            if ((isMiddle || isSpaceLeft) && AtlasScrollViewer != null)
            {
                _isPanningAtlas = true;
                _atlasPanStartPoint = e.GetPosition(this);
                _atlasScrollHorizontalStart = AtlasScrollViewer.HorizontalOffset;
                _atlasScrollVerticalStart = AtlasScrollViewer.VerticalOffset;
                AtlasScrollViewer.CaptureMouse();
                AtlasScrollViewer.Cursor = Cursors.Hand;
                e.Handled = true;
            }
        }

        private void AtlasScrollViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningAtlas && AtlasScrollViewer != null)
            {
                Point currentPos = e.GetPosition(this);
                Vector delta = currentPos - _atlasPanStartPoint;
                AtlasScrollViewer.ScrollToHorizontalOffset(_atlasScrollHorizontalStart - delta.X);
                AtlasScrollViewer.ScrollToVerticalOffset(_atlasScrollVerticalStart - delta.Y);
                e.Handled = true;
            }
        }

        private void AtlasScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanningAtlas && AtlasScrollViewer != null)
            {
                _isPanningAtlas = false;
                AtlasScrollViewer.ReleaseMouseCapture();
                AtlasScrollViewer.Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void AtlasScrollViewer_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isPanningAtlas = false;
            if (AtlasScrollViewer != null)
            {
                AtlasScrollViewer.Cursor = Cursors.Arrow;
            }
        }

        private Point _previewPanStartPoint;
        private bool _isPanningPreview;

        private void PreviewBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            ViewModel?.ZoomPreview(e.Delta);
        }

        private void PreviewBox_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ViewModel?.ResetPreviewPanAndZoom();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
            {
                if (sender is UIElement el)
                {
                    _isPanningPreview = true;
                    _previewPanStartPoint = e.GetPosition(el);
                    el.CaptureMouse();
                    e.Handled = true;
                }
            }
        }

        private void PreviewBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanningPreview && sender is UIElement el && ViewModel != null)
            {
                var currentPos = e.GetPosition(el);
                var delta = currentPos - _previewPanStartPoint;
                _previewPanStartPoint = currentPos;

                ViewModel.PreviewPanX += delta.X;
                ViewModel.PreviewPanY += delta.Y;
                e.Handled = true;
            }
        }

        private void PreviewBox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanningPreview && sender is UIElement el)
            {
                _isPanningPreview = false;
                el.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void PreviewBox_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isPanningPreview = false;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Do not steal key strokes if user is typing in a TextBox
            if (e.OriginalSource is System.Windows.Controls.TextBox) return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                switch (key)
                {
                    case Key.O:
                        ViewModel.OpenImage();
                        e.Handled = true;
                        return;
                    case Key.D0:
                    case Key.NumPad0:
                        ViewModel.ResetZoom();
                        e.Handled = true;
                        return;
                    case Key.OemPlus:
                    case Key.Add:
                        ViewModel.ZoomIn();
                        e.Handled = true;
                        return;
                    case Key.OemMinus:
                    case Key.Subtract:
                        ViewModel.ZoomOut();
                        e.Handled = true;
                        return;
                }
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                int delta = isShift ? 10 : 1;
                switch (key)
                {
                    case Key.Up:
                        ViewModel.OffsetY = Math.Max(0, ViewModel.OffsetY - delta);
                        e.Handled = true;
                        return;
                    case Key.Down:
                        ViewModel.OffsetY += delta;
                        e.Handled = true;
                        return;
                    case Key.Left:
                        ViewModel.OffsetX = Math.Max(0, ViewModel.OffsetX - delta);
                        e.Handled = true;
                        return;
                    case Key.Right:
                        ViewModel.OffsetX += delta;
                        e.Handled = true;
                        return;
                }
            }

            switch (key)
            {
                case Key.Space:
                    ViewModel.TogglePlayPause();
                    e.Handled = true;
                    break;
                case Key.Left:
                    ViewModel.PrevFrame();
                    e.Handled = true;
                    break;
                case Key.Right:
                    ViewModel.NextFrame();
                    e.Handled = true;
                    break;
                case Key.Home:
                    ViewModel.FirstFrame();
                    e.Handled = true;
                    break;
                case Key.End:
                    ViewModel.LastFrame();
                    e.Handled = true;
                    break;
                case Key.F1:
                    if (BtnShortcuts?.ContextMenu != null)
                    {
                        BtnShortcuts.ContextMenu.PlacementTarget = BtnShortcuts;
                        BtnShortcuts.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        BtnShortcuts.ContextMenu.IsOpen = true;
                        e.Handled = true;
                    }
                    break;
                case Key.Escape:
                    Close();
                    e.Handled = true;
                    break;
            }
        }
    }
}
