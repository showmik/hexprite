using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Hexprite.ViewModels;
using Hexprite.Core;

namespace Hexprite.Views
{
    public partial class FontEditorPanel : UserControl
    {
        private bool _isDrawing;
        private bool _isErasing;
        private int _lastCellX = -1;
        private int _lastCellY = -1;

        public FontEditorPanel()
        {
            InitializeComponent();
            Loaded += FontEditorPanel_Loaded;
            Unloaded += FontEditorPanel_Unloaded;
            PreviewKeyDown += FontEditorPanel_PreviewKeyDown;
            PreviewKeyUp += FontEditorPanel_PreviewKeyUp;
            DataContextChanged += FontEditorPanel_DataContextChanged;
        }

        private void FontEditorPanel_Loaded(object sender, RoutedEventArgs e)
        {
            Focusable = true;
            if (DataContext is FontViewModel vm)
            {
                vm.DocumentLoaded -= Vm_DocumentLoaded;
                vm.DocumentLoaded += Vm_DocumentLoaded;
            }
        }

        private void FontEditorPanel_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is FontViewModel vm)
            {
                vm.DocumentLoaded -= Vm_DocumentLoaded;
            }
        }

        private void FontEditorPanel_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue is FontViewModel oldVm)
            {
                oldVm.DocumentLoaded -= Vm_DocumentLoaded;
            }
            if (e.NewValue is FontViewModel newVm)
            {
                newVm.DocumentLoaded -= Vm_DocumentLoaded; // Safety check
                newVm.DocumentLoaded += Vm_DocumentLoaded;
                
                if (newVm.IsNewlyCreated)
                {
                    newVm.IsNewlyCreated = false;
                    FitZoomToScreen();
                }
            }
        }

        private void Vm_DocumentLoaded(object? sender, EventArgs e)
        {
            FitZoomToScreen();
        }

        private void FitZoomToScreen()
        {
            if (ViewModel == null) return;

            void doFit()
            {
                if (ViewModel == null || ViewModel.ActiveGlyph == null) return;

                double sw = MainScrollViewer.ActualWidth;
                double sh = MainScrollViewer.ActualHeight;
                if (sw == 0 || sh == 0) return;

                int gw = ViewModel.ActiveGlyph.Width;
                int gh = ViewModel.ActiveGlyph.Height;
                if (gw == 0 || gh == 0) return;

                double factorX = (sw - 64) / gw;
                double factorY = (sh - 64) / gh;

                int cell = (int)Math.Min(factorX, factorY);
                ViewModel.CellSize = Math.Clamp(cell, 4, 128);
            }

            if (MainScrollViewer.ActualWidth == 0)
                Dispatcher.InvokeAsync(doFit, System.Windows.Threading.DispatcherPriority.Loaded);
            else
                doFit();
        }

        private void BtnResetZoom_Click(object sender, RoutedEventArgs e)
        {
            FitZoomToScreen();
        }

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.CellSize = Math.Min(128, ViewModel.CellSize + 2);
            }
        }

        private void BtnZoomOut_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.CellSize = Math.Max(4, ViewModel.CellSize - 2);
            }
        }

        private bool _isPanning;
        private Point _panStartPoint;
        private double _panHorizontalOffset;
        private double _panVerticalOffset;

        private void Canvas_ScrollViewer_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.MiddleButton == MouseButtonState.Pressed ||
                (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(this);
                _panHorizontalOffset = MainScrollViewer.HorizontalOffset;
                _panVerticalOffset = MainScrollViewer.VerticalOffset;
                MainScrollViewer.CaptureMouse();
                Cursor = Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void Canvas_ScrollViewer_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point current = e.GetPosition(this);
                double dx = current.X - _panStartPoint.X;
                double dy = current.Y - _panStartPoint.Y;
                MainScrollViewer.ScrollToHorizontalOffset(_panHorizontalOffset - dx);
                MainScrollViewer.ScrollToVerticalOffset(_panVerticalOffset - dy);
                e.Handled = true;
            }
        }

        private void Canvas_ScrollViewer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning && (e.MiddleButton == MouseButtonState.Released || e.LeftButton == MouseButtonState.Released))
            {
                _isPanning = false;
                MainScrollViewer.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
                e.Handled = true;
            }
        }

        private void BtnNudgeLeft_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShiftActiveGlyph(-1, 0);
        }

        private void BtnNudgeRight_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShiftActiveGlyph(1, 0);
        }

        private void BtnNudgeUp_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShiftActiveGlyph(0, -1);
        }

        private void BtnNudgeDown_Click(object sender, RoutedEventArgs e)
        {
            ViewModel?.ShiftActiveGlyph(0, 1);
        }

        private FontViewModel? ViewModel => DataContext as FontViewModel;

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null || ViewModel.ActiveGlyph == null) return;

            if (sender is not Image image) return;

            image.CaptureMouse();

            if (e.ChangedButton == MouseButton.Left)
            {
                _isDrawing = true;
                _isErasing = false;
            }
            else if (e.ChangedButton == MouseButton.Right)
            {
                _isErasing = true;
                _isDrawing = false;
            }

            _lastCellX = -1;
            _lastCellY = -1;

            ViewModel.BeginDrawing();
            ProcessMouse(e, image);
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (sender is not Image image) return;
            
            ProcessMouse(e, image);
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image image)
            {
                image.ReleaseMouseCapture();
            }

            _isDrawing = false;
            _isErasing = false;
            _lastCellX = -1;
            _lastCellY = -1;
            
            ViewModel?.EndDrawing();
        }

        private void Canvas_MouseLeave(object sender, MouseEventArgs e)
        {
            _lastCellX = -1;
            _lastCellY = -1;
        }

        private void Canvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            _isDrawing = false;
            _isErasing = false;
            _lastCellX = -1;
            _lastCellY = -1;
            ViewModel?.EndDrawing();
        }

        private void FontEditorPanel_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;
            
            // Allow arrow keys to nudge the glyph pixels
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                bool isNudgeKey = e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right;
                if (isNudgeKey && !e.IsRepeat)
                {
                    ViewModel.KeyboardScrubStartedCommand.Execute(parameter: null);
                }

                switch (e.Key)
                {
                    case Key.Z:
                        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                            Keyboard.Focus(this);
                        ViewModel.Undo();
                        e.Handled = true;
                        return;
                    case Key.Y:
                        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
                            Keyboard.Focus(this);
                        ViewModel.Redo();
                        e.Handled = true;
                        return;
                    case Key.Up:
                        ViewModel.ShiftActiveGlyph(0, -1);
                        e.Handled = true;
                        break;
                    case Key.Down:
                        ViewModel.ShiftActiveGlyph(0, 1);
                        e.Handled = true;
                        break;
                    case Key.Left:
                        ViewModel.ShiftActiveGlyph(-1, 0);
                        e.Handled = true;
                        break;
                    case Key.Right:
                        ViewModel.ShiftActiveGlyph(1, 0);
                        e.Handled = true;
                        break;
                    case Key.OemPlus:
                    case Key.Add:
                        ViewModel.CellSize = Math.Min(128, ViewModel.CellSize + 2);
                        e.Handled = true;
                        return;
                    case Key.OemMinus:
                    case Key.Subtract:
                        ViewModel.CellSize = Math.Max(4, ViewModel.CellSize - 2);
                        e.Handled = true;
                        return;
                }

                if (e.Handled)
                {
                    ViewModel.IsDirty = true;
                    // Note: UpdatePreviewBitmap and SaveStateForUndo are already handled by the ViewModel setters!
                }
            }

            if (e.Handled) return;

            if (Keyboard.FocusedElement is TextBoxBase || Keyboard.FocusedElement is ComboBox)
            {
                return;
            }

            // Unmodified navigation keys
            switch (e.Key)
            {
                case Key.Left:
                    ViewModel.PreviousGlyph();
                    e.Handled = true;
                    break;
                case Key.Right:
                    ViewModel.NextGlyph();
                    e.Handled = true;
                    break;
            }
        }

        private void FontEditorPanel_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            if (ViewModel == null) return;
            
            if (e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Left || e.Key == Key.Right)
            {
                ViewModel.KeyboardScrubEndedCommand.Execute(parameter: null);
            }
        }

        private void Canvas_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (ViewModel == null) return;
            if (sender is not ScrollViewer sv) return;
            if (sv.Content is not FrameworkElement contentGrid) return;

            int current = ViewModel.CellSize;
            double factor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
            int newCellSize = (int)Math.Round(current * factor, MidpointRounding.AwayFromZero);
            
            if (newCellSize == current)
            {
                newCellSize += e.Delta > 0 ? 2 : -2;
            }

            newCellSize = Math.Clamp(newCellSize, 4, 128);

            if (newCellSize == ViewModel.CellSize) 
            {
                e.Handled = true;
                return;
            }

            double ratio = (double)newCellSize / ViewModel.CellSize;
            
            var mouseInSv = e.GetPosition(sv);
            var mouseInContent = e.GetPosition(contentGrid);

            ViewModel.CellSize = newCellSize;

            sv.UpdateLayout();

            sv.ScrollToHorizontalOffset((mouseInContent.X * ratio) - mouseInSv.X);
            sv.ScrollToVerticalOffset((mouseInContent.Y * ratio) - mouseInSv.Y);
            
            e.Handled = true;
        }

        private void ProcessMouse(MouseEventArgs e, Image image)
        {
            if (ViewModel == null || ViewModel.ActiveGlyph == null) return;

            Point pos = e.GetPosition(image);

            if (image.Source is not System.Windows.Media.Imaging.WriteableBitmap bitmap) return;

            double actualWidth = image.ActualWidth;
            double actualHeight = image.ActualHeight;
            
            double pixelWidth = bitmap.PixelWidth;
            double pixelHeight = bitmap.PixelHeight;

            double scaleX = actualWidth / pixelWidth;
            double scaleY = actualHeight / pixelHeight;
            double scale = System.Math.Min(scaleX, scaleY);
            
            if (scale <= 0) return;

            double renderedWidth = pixelWidth * scale;
            double renderedHeight = pixelHeight * scale;

            double offsetX = (actualWidth - renderedWidth) / 2.0;
            double offsetY = (actualHeight - renderedHeight) / 2.0;

            double localX = pos.X - offsetX;
            double localY = pos.Y - offsetY;

            if (localX < 0 || localX >= renderedWidth || localY < 0 || localY >= renderedHeight) return;

            int pixelX = (int)(localX / scale);
            int pixelY = (int)(localY / scale);
            
            int cellX = pixelX / ViewModel.CellSize;
            int cellY = pixelY / ViewModel.CellSize;

            ViewModel.CursorX = cellX;
            ViewModel.CursorY = cellY;

            if (_isDrawing || _isErasing)
            {
                if (cellX >= 0 && cellX < ViewModel.ActiveGlyph.Width &&
                    cellY >= 0 && cellY < ViewModel.ActiveGlyph.Height)
                {
                    if (_lastCellX >= 0 && _lastCellY >= 0 && (_lastCellX != cellX || _lastCellY != cellY))
                    {
                        // Bresenham's line algorithm between (_lastCellX, _lastCellY) and (cellX, cellY)
                        int x0 = _lastCellX;
                        int y0 = _lastCellY;
                        int x1 = cellX;
                        int y1 = cellY;

                        int dx = Math.Abs(x1 - x0);
                        int dy = -Math.Abs(y1 - y0);
                        int sx = x0 < x1 ? 1 : -1;
                        int sy = y0 < y1 ? 1 : -1;
                        int err = dx + dy;

                        while (true)
                        {
                            ViewModel.DrawPixel(x0, y0, _isErasing);
                            if (x0 == x1 && y0 == y1) break;
                            int e2 = 2 * err;
                            if (e2 >= dy)
                            {
                                err += dy;
                                x0 += sx;
                            }
                            if (e2 <= dx)
                            {
                                err += dx;
                                y0 += sy;
                            }
                        }
                    }
                    else
                    {
                        ViewModel.DrawPixel(cellX, cellY, _isErasing);
                    }

                    _lastCellX = cellX;
                    _lastCellY = cellY;
                }
                else
                {
                    _lastCellX = -1;
                    _lastCellY = -1;
                }
            }
        }
    }
}
