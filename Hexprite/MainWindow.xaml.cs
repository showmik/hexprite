using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Hexprite.ViewModels;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Hexprite
{
    public partial class MainWindow : Window
    {
        // ── Dependencies ──────────────────────────────────────────────────
        private readonly ViewModels.ShellViewModel _shell;
        private ViewModels.MainViewModel? ViewModel => _shell.ActiveDocument as ViewModels.MainViewModel;
        private ISelectionService? Selection => ViewModel?.SelectionService;

        // ── Extracted subsystems ───────────────────────────────────────────
        private readonly ZoomPanController _zoomPan;
        internal ZoomPanController ZoomPan => _zoomPan;
        private readonly SelectionOverlayRenderer _selectionOverlay;
        private readonly BrushCursorManager _brushCursor;
        private readonly CanvasElementProvider _canvasElements;

        // ── Drag tracking (screen-space, belongs in the View) ─────────────
        private Point _dragStartMousePos;
        private int _dragStartFloatingX;
        private int _dragStartFloatingY;

        // ── Splitter drag state (left and right splitters) ───────────────────
        private double _layersColumnWidthBeforeDrag;
        private double _rightSidebarWidthBeforeDrag;
        private double _leftDragAccumulator;
        private double _rightDragAccumulator;

        // ── Panel layout state (restore widths when re-showing) ───────────
        // Seeded from the persisted ShellViewModel widths in the constructor.
        private double _lastLayersColumnWidth;
        private double _lastRightSidebarColumnWidth;
        private readonly double _layersColumnMinWidth;
        private readonly double _rightSidebarColumnMinWidth;
        private readonly double _layersColumnMaxWidth;
        private readonly double _rightSidebarColumnMaxWidth;

        // ── Last hovered pixel (avoids spamming ViewModel on same pixel) ──
        private int _lastHoveredX = -1;
        private int _lastHoveredY = -1;

        // ── Draw mode locked for the duration of a stroke ─────────────────
        private DrawMode _activeDrawMode = DrawMode.None;
        private bool _hasPendingToolMove;
        private PendingToolMove _pendingToolMove;
        private System.Windows.Threading.DispatcherTimer? _toolMoveTimer;
        private DateTime _lastToolMoveDispatchUtc = DateTime.MinValue;
        private static readonly TimeSpan ToolMoveDispatchInterval = TimeSpan.FromMilliseconds(16);

        /// <summary>Floating selection resize via transform handles (preview coordinate deltas).</summary>
        private bool _activeTransformDrag;
        private int _transformDownPixelX;
        private int _transformDownPixelY;

        /// <summary>Floating selection rotation via corner rotation zones.</summary>
        private bool _activeRotationDrag;

        // ── Currently-subscribed document (for event unwiring) ─────────────
        private ViewModels.MainViewModel? _subscribedDoc;
        private bool _hadOpenDocument;

        // ── Win32 hook handle (stored so it can be removed on close) ─────────
        private System.Windows.Interop.HwndSource? _hwndSource;

        private int _minTrackWidth;
        private int _minTrackHeight;

        // ── Constructor ───────────────────────────────────────────────────

        public MainWindow(ViewModels.ShellViewModel shell, Services.IHexpriteShortcutManager? shortcutManager = null)
        {
            InitializeComponent();
            _shell = shell ?? throw new ArgumentNullException(nameof(shell));
            DataContext = _shell;

            // Initialize extracted subsystems
            _zoomPan = new ZoomPanController(Canvas.ZoomSlider, () => Canvas.CanvasImage, () => _shell);

            if (shortcutManager != null)
            {
                Services.EditorCanvasShortcutRegistrar.RegisterDefaultTools(shortcutManager, _shell);

                var zoomInCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => _zoomPan.ApplyZoomCentered(ZoomPanController.ZoomFactor),
                    () => _shell.ActiveDocument is ViewModels.MainViewModel);
                var zoomOutCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => _zoomPan.ApplyZoomCentered(1.0 / ZoomPanController.ZoomFactor),
                    () => _shell.ActiveDocument is ViewModels.MainViewModel);
                var zoomResetCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(
                    () => _zoomPan.ZoomReset(),
                    () => _shell.ActiveDocument is ViewModels.MainViewModel);

                Services.WindowShortcutRegistrar.RegisterDefaultShortcuts(
                    shortcutManager, _shell, zoomInCommand, zoomOutCommand, zoomResetCommand);
            }

            _canvasElements = new CanvasElementProvider(
                () => Canvas.CanvasImage,
                () => Canvas.PixelGridContainer,
                () => Canvas.BrushCursorOverlay,
                () => Canvas.CrosshairH,
                () => Canvas.CrosshairV,
                () => Canvas.MarqueeOverlay,
                () => Canvas.EllipseOverlay,
                () => Canvas.LassoOverlay,
                () => Canvas.TransformHandlesLayer);

            _selectionOverlay = new SelectionOverlayRenderer(
                _canvasElements, () => ViewModel, () => Selection);
            _brushCursor = new BrushCursorManager(
                _canvasElements, () => ViewModel,
                () => GetPixelCoordinates(_brushCursor.LastCanvasMousePos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight));

            // Bug 4: use named handlers so they can be removed in MainWindow_Closed.
            _shell.ActiveTabChanged += Shell_ActiveTabChanged;
            _shell.ThemeChanged += Shell_ThemeChanged;
            _shell.PropertyChanged += Shell_PropertyChanged;
            this.Activated += MainWindow_Activated;

            // Zoom keyboard shortcuts
            CommandBindings.Add(new CommandBinding(NavigationCommands.IncreaseZoom,
                (_, _) => _zoomPan.ApplyZoomCentered(ZoomPanController.ZoomFactor),
                (_, e) => { e.CanExecute = _shell.HasOpenDocument; e.Handled = true; }));
            CommandBindings.Add(new CommandBinding(NavigationCommands.DecreaseZoom,
                (_, _) => _zoomPan.ApplyZoomCentered(1.0 / ZoomPanController.ZoomFactor),
                (_, e) => { e.CanExecute = _shell.HasOpenDocument; e.Handled = true; }));
            CommandBindings.Add(new CommandBinding(NavigationCommands.Zoom,
                (_, _) => _zoomPan.ZoomReset(),
                (_, e) => { e.CanExecute = _shell.HasOpenDocument; e.Handled = true; }));

            // Wire up Canvas events
            Canvas.MainScrollViewer.PreviewMouseWheel += ScrollViewer_PreviewMouseWheel;
            Canvas.MainScrollViewer.PreviewMouseDown += ScrollViewer_PreviewMouseDown;
            Canvas.MainScrollViewer.PreviewMouseMove += ScrollViewer_PreviewMouseMove;
            Canvas.MainScrollViewer.PreviewMouseUp += ScrollViewer_PreviewMouseUp;
            Canvas.MainScrollViewer.MouseLeave += ScrollViewer_MouseLeave;
            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;

            // Capture initial constraints so we can restore after collapsing.
            _layersColumnMinWidth = LayersColumn.MinWidth;
            _rightSidebarColumnMinWidth = RightSidebarColumn.MinWidth;
            _layersColumnMaxWidth = LayersColumn.MaxWidth;
            _rightSidebarColumnMaxWidth = RightSidebarColumn.MaxWidth;

            // Seed from the persisted layout so a hidden panel restores to its last saved width.
            _lastLayersColumnWidth = _shell.LayersPanelWidth;
            _lastRightSidebarColumnWidth = _shell.RightSidebarWidth;

            OnActiveTabChanged();
        }

        private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.ShellViewModel.IsToolSidebarVisible) ||
                e.PropertyName == nameof(ViewModels.ShellViewModel.IsLayersPanelVisible) ||
                e.PropertyName == nameof(ViewModels.ShellViewModel.IsRightSidebarVisible) ||
                e.PropertyName == nameof(ViewModels.ShellViewModel.IsTimelineVisible) ||
                e.PropertyName == nameof(ViewModels.ShellViewModel.IsStatusBarVisible))
            {
                ApplyPanelLayoutFromShell();
            }
            else if (e.PropertyName == nameof(ViewModels.ShellViewModel.CurrentTool))
            {
                // Sync tool sidebar to global tool state
                ToolSidebar.SyncToTool(_shell.CurrentTool);
            }
        }

        private void ApplyPanelLayoutFromShell()
        {
            // Tool sidebar (column 0 is Auto so Visibility collapse is sufficient)
            ToolSidebar.Visibility = _shell.IsToolSidebarVisible ? Visibility.Visible : Visibility.Collapsed;

            // Layers panel (column 3 + splitter column 2)
            if (_shell.IsLayersPanelVisible)
            {
                LayersPanel.Visibility = Visibility.Visible;
                LeftPanelSplitter.Visibility = Visibility.Visible;
                if (FindName("LeftSplitterColumn") is ColumnDefinition leftCol)
                    leftCol.Width = new GridLength(4);

                LayersColumn.MinWidth = _layersColumnMinWidth;
                double widthToRestore = _lastLayersColumnWidth;
                if (widthToRestore < LayersColumn.MinWidth) widthToRestore = LayersColumn.MinWidth;
                if (widthToRestore > _layersColumnMaxWidth) widthToRestore = _layersColumnMaxWidth;
                LayersColumn.Width = new GridLength(widthToRestore);
            }
            else
            {
                if (LayersColumn.ActualWidth > 0)
                {
                    _lastLayersColumnWidth = LayersColumn.ActualWidth;
                    _shell.LayersPanelWidth = _lastLayersColumnWidth;
                }

                LayersPanel.Visibility = Visibility.Collapsed;
                LeftPanelSplitter.Visibility = Visibility.Collapsed;
                if (FindName("LeftSplitterColumn") is ColumnDefinition leftCol)
                    leftCol.Width = new GridLength(0);

                LayersColumn.MinWidth = 0;
                LayersColumn.Width = new GridLength(0);
            }

            // Right sidebar (column 5 + splitter column 4)
            if (_shell.IsRightSidebarVisible)
            {
                RightSidebarPanel.Visibility = Visibility.Visible;
                RightPanelSplitter.Visibility = Visibility.Visible;
                if (FindName("RightSplitterColumn") is ColumnDefinition rightCol)
                    rightCol.Width = new GridLength(4);

                RightSidebarColumn.MinWidth = _rightSidebarColumnMinWidth;
                double widthToRestore = _lastRightSidebarColumnWidth;
                if (widthToRestore < RightSidebarColumn.MinWidth) widthToRestore = RightSidebarColumn.MinWidth;
                if (widthToRestore > _rightSidebarColumnMaxWidth) widthToRestore = _rightSidebarColumnMaxWidth;
                RightSidebarColumn.Width = new GridLength(widthToRestore);
            }
            else
            {
                if (RightSidebarColumn.ActualWidth > 0)
                {
                    _lastRightSidebarColumnWidth = RightSidebarColumn.ActualWidth;
                    _shell.RightSidebarWidth = _lastRightSidebarColumnWidth;
                }

                RightSidebarPanel.Visibility = Visibility.Collapsed;
                RightPanelSplitter.Visibility = Visibility.Collapsed;
                if (FindName("RightSplitterColumn") is ColumnDefinition rightCol)
                    rightCol.Width = new GridLength(0);

                RightSidebarColumn.MinWidth = 0;
                RightSidebarColumn.Width = new GridLength(0);
            }

            // Timeline / Status bar (simple visibility)
            // Use FindName so this stays resilient even if XAML field generation lags in the IDE.
            if (FindName("TimelinePanel") is UIElement timeline)
                timeline.Visibility = _shell.IsTimelineVisible ? Visibility.Visible : Visibility.Collapsed;

            if (FindName("StatusBarPanel") is UIElement statusBar)
            {
                bool isSpriteMode = _shell.ActiveDocument?.Mode == DocumentMode.Sprite;
                statusBar.Visibility = (_shell.IsStatusBarVisible && isSpriteMode) ? Visibility.Visible : Visibility.Collapsed;
            }

            // Ensure Canvas remains fluidly sized.
            if (!CanvasColumn.Width.IsStar)
                CanvasColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (LoggingService.IsFirstRun())
            {
                var firstRunDialog = new Views.FirstRunDialog { Owner = this, DataContext = _shell };
                firstRunDialog.ShowDialog();
                LoggingService.MarkFirstRunComplete();
            }

            // Sync tool sidebar to initial global tool state
            ToolSidebar.SyncToTool(_shell.CurrentTool);
            _shell.CheckAutosaves();
        }

        // Bug 4: named handlers for shell events so they can be -= removed on close.
        private void Shell_ActiveTabChanged(object? sender, EventArgs e) => OnActiveTabChanged();
        private void Shell_ThemeChanged(object? sender, EventArgs e) => _brushCursor.Refresh();
        private void MainWindow_Activated(object? sender, EventArgs e) => _shell.CheckForExternalChanges();

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            this.Activated -= MainWindow_Activated;

            // H1: remove WndProc hook so the Win32 message pump no longer holds
            // a delegate rooted to this window instance.
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;

            // Bug 1: detach ZoomPanController's slider subscription.
            _zoomPan.Detach();

            // Bug 4: unsubscribe all shell events.
            _shell.ActiveTabChanged -= Shell_ActiveTabChanged;
            _shell.ThemeChanged -= Shell_ThemeChanged;
            _shell.PropertyChanged -= Shell_PropertyChanged;

            // Also clean up the currently-subscribed document so nothing roots it.
            if (_subscribedDoc != null)
            {
                _subscribedDoc.HistoryRestored -= OnHistoryRestored;
                _subscribedDoc.ToolChanged -= OnToolChanged;
                _subscribedDoc.PropertyChanged -= OnDocPropertyChanged;
                if (_subscribedDoc.SelectionService != null)
                    _subscribedDoc.SelectionService.SelectionChanged -= OnSelectionChanged;
                _subscribedDoc = null;
            }
        }

        private bool _isClosing;

        protected override async void OnClosing(CancelEventArgs e)
        {
            if (_isClosing)
            {
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;

            bool canClose = await _shell.TryCloseAllTabsAsync();
            if (canClose)
            {
                _isClosing = true;
                await Dispatcher.InvokeAsync(() => Close(), DispatcherPriority.Normal);
            }
        }

        private void PanelSplitter_DragStarted(object sender, DragStartedEventArgs e)
        {
            _layersColumnWidthBeforeDrag = LayersColumn.ActualWidth;
            _rightSidebarWidthBeforeDrag = RightSidebarColumn.ActualWidth;
            _leftDragAccumulator = 0;
            _rightDragAccumulator = 0;
        }

        private void PanelSplitter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender == LeftPanelSplitter)
            {
                // Left splitter is between Canvas (*) and Layers (Pixel).
                // Dragging right (positive change) -> decrease Layers width
                // Dragging left (negative change) -> increase Layers width
                _leftDragAccumulator += e.HorizontalChange;
                double targetLayersWidth = _layersColumnWidthBeforeDrag - _leftDragAccumulator;
                double clampedLayersWidth = Math.Clamp(targetLayersWidth, _layersColumnMinWidth, _layersColumnMaxWidth);

                // Clamp accumulator to avoid dead zones when reversing direction
                _leftDragAccumulator = _layersColumnWidthBeforeDrag - clampedLayersWidth;

                // Apply width to Layers
                LayersColumn.Width = new GridLength(clampedLayersWidth);
            }
            else if (sender == RightPanelSplitter)
            {
                // Right splitter sits between Layers (Pixel) and RightSidebar (Pixel).
                // Dragging left (negative change) -> increase RightSidebar width
                // Dragging right (positive change) -> decrease RightSidebar width
                _rightDragAccumulator += e.HorizontalChange;
                double targetRightWidth = _rightSidebarWidthBeforeDrag - _rightDragAccumulator;
                double clampedRightWidth = Math.Clamp(targetRightWidth, _rightSidebarColumnMinWidth, _rightSidebarColumnMaxWidth);

                // Clamp accumulator to avoid dead zones when reversing direction
                _rightDragAccumulator = _rightSidebarWidthBeforeDrag - clampedRightWidth;

                // Apply width to RightSidebar
                RightSidebarColumn.Width = new GridLength(clampedRightWidth);
            }
        }

        private void PanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender == RightPanelSplitter)
            {
                // L5: persist the final sidebar width so hide→show restores the post-drag size.
                _lastRightSidebarColumnWidth = RightSidebarColumn.ActualWidth;
                _shell.RightSidebarWidth = _lastRightSidebarColumnWidth;
            }
            else if (sender == LeftPanelSplitter)
            {
                // L5: persist the final layers width so hide→show restores the post-drag size.
                _lastLayersColumnWidth = LayersColumn.ActualWidth;
                _shell.LayersPanelWidth = _lastLayersColumnWidth;
            }

            // Ensure Canvas returns to Star sizing so it remains fluid
            if (!CanvasColumn.Width.IsStar)
                CanvasColumn.Width = new GridLength(1, GridUnitType.Star);
        }

        private void OnActiveTabChanged()
        {
            bool wasEmpty = !_hadOpenDocument;
            _hadOpenDocument = _shell.HasOpenDocument;

            if (_subscribedDoc != null)
            {
                // Commit any in-progress text editing before switching away
                _subscribedDoc.StopTextEditing();

                _subscribedDoc.HistoryRestored -= OnHistoryRestored;
                _subscribedDoc.ToolChanged -= OnToolChanged;
                _subscribedDoc.PropertyChanged -= OnDocPropertyChanged;
                if (_subscribedDoc.SelectionService != null)
                    _subscribedDoc.SelectionService.SelectionChanged -= OnSelectionChanged;
            }

            _subscribedDoc = ViewModel;

            if (_subscribedDoc != null)
            {
                _subscribedDoc.HistoryRestored += OnHistoryRestored;
                _subscribedDoc.ToolChanged += OnToolChanged;
                _subscribedDoc.PropertyChanged += OnDocPropertyChanged;
                if (_subscribedDoc.SelectionService != null)
                    _subscribedDoc.SelectionService.SelectionChanged += OnSelectionChanged;
            }

            // ── Reset stale view-layer state ─────────────────────────────
            _activeDrawMode = DrawMode.None;
            _lastHoveredX = -1;
            _lastHoveredY = -1;
            ReleaseDragCapture();
            if (Mouse.Captured != null) Mouse.Capture(element: null);

            // ── Sync visuals to the new document ─────────────────────────
            if (ViewModel != null)
            {
                _selectionOverlay.Update();
                _brushCursor.Hide();
                SyncBrushShapeRadioButtons();
            }

            // Note: Tool state is now global, so no need to sync on tab change.
            // ToolSidebar stays in sync via ShellViewModel.PropertyChanged.

            // Only maximize when transitioning from the Welcome / Quick Start screen (0 open documents)
            if (wasEmpty && _shell.HasOpenDocument && WindowState != WindowState.Maximized)
            {
                WindowState = WindowState.Maximized;
            }

            if (ViewModel != null)
            {
                CenterScrollViewer();
            }

            ApplyPanelLayoutFromShell();
        }

        private void CenterScrollViewer()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                // If we are at the very top-left (initial state), center it
                if (Canvas.MainScrollViewer.HorizontalOffset < 10 && Canvas.MainScrollViewer.VerticalOffset < 10)
                {
                    CenterCanvasToViewport();
                }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void CenterCanvasToViewport()
        {
            if (Canvas.MainScrollViewer == null) return;
            Canvas.MainScrollViewer.UpdateLayout();
            double targetOffsetX = (Canvas.MainScrollViewer.ExtentWidth - Canvas.MainScrollViewer.ViewportWidth) / 2.0;
            double targetOffsetY = (Canvas.MainScrollViewer.ExtentHeight - Canvas.MainScrollViewer.ViewportHeight) / 2.0;

            if (targetOffsetX > 0) Canvas.MainScrollViewer.ScrollToHorizontalOffset(targetOffsetX);
            else Canvas.MainScrollViewer.ScrollToHorizontalOffset(0);

            if (targetOffsetY > 0) Canvas.MainScrollViewer.ScrollToVerticalOffset(targetOffsetY);
            else Canvas.MainScrollViewer.ScrollToVerticalOffset(0);
        }

        private void SyncBrushShapeRadioButtons()
        {
            if (ViewModel == null) return;
            switch (ViewModel.BrushShape)
            {
                case Core.BrushShape.Circle: if (Canvas.RbBrushCircle != null) Canvas.RbBrushCircle.IsChecked = true; break;
                case Core.BrushShape.Square: if (Canvas.RbBrushSquare != null) Canvas.RbBrushSquare.IsChecked = true; break;
                case Core.BrushShape.Line: if (Canvas.RbBrushLine != null) Canvas.RbBrushLine.IsChecked = true; break;
            }
        }

        private void OnHistoryRestored(object? s, EventArgs e)
        {
            _selectionOverlay.Update();
            ReleaseDragCapture();
            _activeDrawMode = DrawMode.None;
            // Reset any in-progress tool drag state that may have survived undo/redo
            // (e.g. _isMoving=true left over from a cancelled move operation).
            ViewModel?.CancelInProgressDrawing();
        }

        private void OnToolChanged(object? s, EventArgs e)
        {
            if (ViewModel != null && Selection != null)
            {
                if (Selection.IsSelecting)
                {
                    Selection.Cancel();
                }

                // When switching to Move tool, keep the floating selection so it can be moved.
                // For other tools, commit the floating selection first.
                if (Selection.IsFloating && ViewModel.CurrentTool != ToolMode.Move)
                {
                    ViewModel.CommitSelectionIfActive();
                }

                _activeTransformDrag = false;
                _activeRotationDrag = false;

                ViewModel.CancelInProgressDrawing();
            }

            // Pure View concern: hide brush cursor when tool isn't a drawing tool
            if (ViewModel != null && !(ViewModel.CurrentTool == ToolMode.Pencil || ViewModel.CurrentTool == ToolMode.Eraser ||
                                       ViewModel.CurrentTool == ToolMode.Dither ||
                                       ViewModel.CurrentTool == ToolMode.Line || ViewModel.CurrentTool == ToolMode.Rectangle ||
                                       ViewModel.CurrentTool == ToolMode.Ellipse || ViewModel.CurrentTool == ToolMode.FilledRectangle ||
                                       ViewModel.CurrentTool == ToolMode.FilledEllipse))
                _brushCursor.Hide();

            _activeDrawMode = DrawMode.None;
            // Do NOT clear the selection overlay here — selection persists across tool switches.
            // Only refresh it so it stays visually current.
            _selectionOverlay.Update();

            // Sync the sidebar radio buttons so shortcut-triggered tool switches
            // are reflected visually (SyncToTool suppresses SelectToolCommand re-entry).
            if (ViewModel != null)
            {
                ToolSidebar.SyncToTool(ViewModel.CurrentTool);

                // Force an immediate cursor update if the mouse is currently over the canvas
                // to prevent the feeling of "slow" or delayed tool switching.
                if (Canvas.CanvasImage.IsMouseOver)
                {
                    Canvas.CanvasImage.Cursor = GetCanvasCursorForTool(ViewModel.CurrentTool);
                    Mouse.OverrideCursor = null; // Clear any override
                }
            }
        }

        private void OnSelectionChanged(object? s, EventArgs e)
        {
            _selectionOverlay.Update();
            // Clear the cursor override when the selection is no longer floating,
            // e.g. after deselection via keyboard (Esc / Ctrl+D) or undo/redo.
            if (Selection == null || !Selection.IsFloating)
                Mouse.OverrideCursor = null;
        }
        private void OnDocPropertyChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModels.MainViewModel.BrushSize) ||
                e.PropertyName == nameof(ViewModels.MainViewModel.BrushShape) ||
                e.PropertyName == nameof(ViewModels.MainViewModel.BrushAngle))
            {
                _brushCursor.Refresh();
                if (e.PropertyName == nameof(ViewModels.MainViewModel.BrushShape))
                    SyncBrushShapeRadioButtons();
            }

            if (e.PropertyName == nameof(ViewModels.MainViewModel.ZoomLevel))
            {
                _selectionOverlay.Update();
                _brushCursor.RefreshPosition();
            }

            if (e.PropertyName == nameof(ViewModels.MainViewModel.IsBrushCursorVisible))
            {
                _brushCursor.RefreshPosition();
            }
        }

        // ── Tab bar event handlers ────────────────────────────────────────

        private void Tab_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ViewModels.IDocumentTab doc)
            {
                if (doc == _shell.ActiveDocument) return; // already active
                _shell.ActiveDocument = doc;
            }
        }

        private void TabClose_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ViewModels.IDocumentTab doc)
                _shell.CloseTabCommand.Execute(doc);
        }

        private void OpenTabsButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Primitives.ToggleButton btn && btn.ContextMenu != null)
            {
                if (btn.IsChecked == true)
                {
                    btn.ContextMenu.PlacementTarget = btn;
                    btn.ContextMenu.IsOpen = true;
                }
            }
        }

        private void ActiveFilesMenu_Closed(object sender, RoutedEventArgs e)
        {
            if (ActiveFilesButton != null)
            {
                ActiveFilesButton.IsChecked = false;
            }
        }

        private void DropdownTabItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ViewModels.IDocumentTab doc)
            {
                if (doc != _shell.ActiveDocument)
                {
                    _shell.ActiveDocument = doc;
                }
            }
        }

        private void DropdownTabClose_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ViewModels.IDocumentTab doc)
            {
                _shell.CloseTabCommand.Execute(doc);
                e.Handled = true;
            }
        }

        // ── Global overrides ──────────────────────────────────────────────

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (ViewModel == null || Selection == null) return;
            if (Selection.IsDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                if (ViewModel.CurrentTool != ToolMode.Move)
                {
                    UpdateDragPosition(e.GetPosition(Canvas.PixelGridContainer));
                }
            }
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseUp(e);
            if (ViewModel == null || Selection == null) return;
            FlushPendingToolMove();

            if (ViewModel.IsDrawingLine || ViewModel.IsDrawingRectangle || ViewModel.IsDrawingEllipse ||
                ViewModel.IsDrawingFilledRectangle || ViewModel.IsDrawingFilledEllipse || ViewModel.IsDrawingGradient)
            {
                ViewModel.ProcessToolInput(-1, -1, ToolAction.Up, DrawMode.None, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
            }
            else if (ViewModel.CurrentTool == ToolMode.Pencil || ViewModel.CurrentTool == ToolMode.Eraser || ViewModel.CurrentTool == ToolMode.Dither)
            {
                ViewModel.ProcessToolInput(-1, -1, ToolAction.Up, DrawMode.None, isShiftDown: false, isAltDown: false);
            }
            else if (ViewModel.CurrentTool == ToolMode.Move)
            {
                ViewModel.ProcessToolInput(-1, -1, ToolAction.Up, DrawMode.None, isShiftDown: false, isAltDown: false);
            }

            _activeDrawMode = DrawMode.None;

            if (e.ChangedButton == MouseButton.Left)
            {
                bool wasDragging = Selection.IsDragging;

                if (_activeTransformDrag || _activeRotationDrag)
                {
                    ViewModel.CommitSelectionTransformIfActive();
                    _activeTransformDrag = false;
                    _activeRotationDrag = false;
                }
                if ((ViewModel.CurrentTool == ToolMode.Lasso || ViewModel.CurrentTool == ToolMode.Marquee || ViewModel.CurrentTool == ToolMode.EllipticalMarquee || ViewModel.CurrentTool == ToolMode.MagicWand) && Selection.IsSelecting)
                    ViewModel.ProcessSelectionInput(-1, -1, ToolAction.Up, isShiftDown: false, isAltDown: false);
                ReleaseDragCapture();
                Selection.EndDrag();

                if (wasDragging)
                    ViewModel.UpdatePreviewSimulation();
            }

            if (Mouse.Captured == Canvas.CanvasImage)
                Mouse.Capture(element: null);

            // Re-evaluate cursor after releasing a mouse button (e.g., after dragging)
            if (Canvas.CanvasImage.IsMouseOver && !_zoomPan.IsPanning)
            {
                var pos = Mouse.GetPosition(Canvas.CanvasImage);
                var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                UpdateCanvasCursor(pos, x, y);
            }
        }

        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            base.OnPreviewTextInput(e);

            if (ViewModel != null && ViewModel.IsTextEditing && !string.IsNullOrEmpty(e.Text))
            {
                if (Keyboard.FocusedElement is TextBox ||
                    Keyboard.FocusedElement is ComboBox ||
                    Keyboard.FocusedElement is ComboBoxItem) return;
                
                if (e.Text == "\r" || e.Text == "\n")
                {
                    ViewModel.InsertText("\n");
                    e.Handled = true;
                    return;
                }

                if (e.Text.Length == 1 && char.IsControl(e.Text[0])) return;

                ViewModel.InsertText(e.Text);
                e.Handled = true;
            }
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (e.Handled) return;
            if (ViewModel == null || Selection == null) return;

            // Update modifier key state for status bar
            UpdateModifierKeyState();

            // The timeline owns selection and destructive keys while it has focus.
            // This guard runs before the canvas-wide Delete/Escape handling below.
            bool timelineHasFocus = TimelinePanel.IsKeyboardFocusWithin && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase and not ComboBox and not ComboBoxItem;
            if (timelineHasFocus && ViewModel.IsAnimationEnabled)
            {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
                {
                    ViewModel.SelectAllFramesCommand.Execute(parameter: null);
                    e.Handled = true;
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.Escape)
                {
                    ViewModel.SelectActiveFrameOnlyCommand.Execute(parameter: null);
                    e.Handled = true;
                    return;
                }
                if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Delete || e.Key == Key.Back))
                {
                    if (ViewModel.DeleteFrameCommand.CanExecute(parameter: null))
                        ViewModel.DeleteFrameCommand.Execute(parameter: null);
                    e.Handled = true;
                    return;
                }
            }

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (actualKey == Key.LeftShift || actualKey == Key.RightShift || actualKey == Key.LeftAlt || actualKey == Key.RightAlt)
            {
                bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                bool isAltDown = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);

                if (ViewModel.IsDrawingLine || ViewModel.IsDrawingRectangle || ViewModel.IsDrawingEllipse ||
                    ViewModel.IsDrawingFilledRectangle || ViewModel.IsDrawingFilledEllipse || ViewModel.IsDrawingGradient)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.ProcessToolInput(x, y, ToolAction.Move, _activeDrawMode, isShiftDown, isAltDown);
                }
                else if (Selection.IsSelecting)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.ProcessSelectionInput(x, y, ToolAction.Move, isShiftDown, isAltDown);
                }
                else if (_activeTransformDrag)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.UpdateSelectionTransform(
                        x - _transformDownPixelX,
                        y - _transformDownPixelY,
                        isShiftDown, isAltDown);
                    _selectionOverlay.Update();
                }
                else if (_activeRotationDrag)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    ViewModel.UpdateSelectionRotation(
                        pos.X, pos.Y, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight,
                        isShiftDown);
                    _selectionOverlay.Update();
                }
            }

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ViewModel.IsTextEditing)
                {
                    if (e.Key == Key.V)
                    {
                        if (Clipboard.ContainsText())
                        {
                            string pasteText = Clipboard.GetText();
                            if (!string.IsNullOrEmpty(pasteText))
                            {
                                ViewModel.InsertText(pasteText);
                            }
                        }
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Left)
                    {
                        ViewModel.MoveCaretLeft(ctrl: true);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Right)
                    {
                        ViewModel.MoveCaretRight(ctrl: true);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Back)
                    {
                        ViewModel.DeleteBackward(word: true);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Delete)
                    {
                        ViewModel.DeleteForward(word: true);
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.A)
                    {
                        e.Handled = true;
                        return;
                    }
                    if (e.Key == Key.Z) return;
                    e.Handled = true;
                    return;
                }

                // Strangler Fig: Ctrl+Arrow grid shift shortcuts (Up, Down, Left, Right)
                // are now registered in HexpriteShortcutManager (ShortcutScope.EditorCanvas) and handled upstream.
                // FIX: Removed redundant Ctrl+D interception. Deselection UI cleanup is 
                // strictly handled by the SelectionChanged event triggered by the KeyBinding command.
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Shift)
            {
                if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                    Keyboard.FocusedElement is ComboBox ||
                    Keyboard.FocusedElement is ComboBoxItem)
                {
                    if (e.Key == Key.Escape && ViewModel.IsTextEditing)
                    {
                        if (Keyboard.FocusedElement is ComboBox cb)
                        {
                            cb.IsDropDownOpen = false;
                        }
                        
                        // Focus the canvas ScrollViewer to pull keyboard focus away
                        // from the ComboBox. Neither Keyboard.ClearFocus() nor
                        // Window.Focus() work here — WPF's focus manager re-snaps
                        // focus back to the ComboBox (last logically focused element),
                        // causing its type-ahead search to swallow keyboard shortcuts.
                        ViewModel.StopTextEditing();
                        Keyboard.Focus(Canvas.MainScrollViewer);
                        e.Handled = true;
                        return;
                    }

                    return;
                }

                if (ViewModel.IsTextEditing)
                {
                    switch (e.Key)
                    {
                        case Key.Left:
                            ViewModel.MoveCaretLeft(ctrl: false);
                            e.Handled = true;
                            return;
                        case Key.Right:
                            ViewModel.MoveCaretRight(ctrl: false);
                            e.Handled = true;
                            return;
                        case Key.Up:
                            ViewModel.MoveCaretUp();
                            e.Handled = true;
                            return;
                        case Key.Down:
                            ViewModel.MoveCaretDown();
                            e.Handled = true;
                            return;
                        case Key.Home:
                            ViewModel.MoveCaretHome();
                            e.Handled = true;
                            return;
                        case Key.End:
                            ViewModel.MoveCaretEnd();
                            e.Handled = true;
                            return;
                        case Key.Back:
                            ViewModel.DeleteBackward(word: false);
                            e.Handled = true;
                            return;
                        case Key.Delete:
                            ViewModel.DeleteForward(word: false);
                            e.Handled = true;
                            return;
                        case Key.Enter:
                            ViewModel.InsertText("\n");
                            e.Handled = true;
                            return;
                        case Key.Escape:
                            ViewModel.StopTextEditing();
                            e.Handled = true;
                            return;
                        default:
                            return; // Ignore other single-key shortcuts while typing text
                    }
                }

                switch (e.Key)
                {
                    case Key.Delete:
                    case Key.Back:
                        if (ViewModel.DeleteSelectionCommand.CanExecute(parameter: null))
                        {
                            ViewModel.DeleteSelectionCommand.Execute(parameter: null);
                            _selectionOverlay.Clear();
                            e.Handled = true;
                        }
                        break;
                    case Key.Escape:
                        if (ViewModel.IsDrawingLine || ViewModel.IsDrawingRectangle || ViewModel.IsDrawingEllipse ||
                            ViewModel.IsDrawingFilledRectangle || ViewModel.IsDrawingFilledEllipse || ViewModel.IsDrawingGradient || ViewModel.IsMoving)
                        {
                            ViewModel.CancelInProgressDrawing();
                            _activeDrawMode = DrawMode.None;
                            ReleaseDragCapture();
                            e.Handled = true;
                        }
                        else if (Selection.IsTransforming)
                        {
                            ViewModel.CancelSelectionTransformIfActive();
                            _activeTransformDrag = false;
                            _activeRotationDrag = false;
                            _selectionOverlay.Update();
                            e.Handled = true;
                        }
                        else if (Selection.IsSelecting)
                        {
                            Selection.Cancel();
                            _selectionOverlay.Clear();
                            e.Handled = true;
                        }
                        else if (Selection.IsDragging)
                        {
                            ViewModel.CancelSelectionDragIfActive();
                            ReleaseDragCapture();
                            _selectionOverlay.Update();
                            e.Handled = true;
                        }
                        else
                        {
                            ViewModel.DeselectCommand.Execute(parameter: null);
                            _selectionOverlay.Clear();
                            e.Handled = true;
                        }
                        break;
                    // Strangler Fig: Tool selection shortcuts (B, E, L, R, C, F, T, V, M, Q, W, D)
                    // are now registered in HexpriteShortcutManager (ShortcutScope.EditorCanvas) and handled upstream.
                    // Strangler Fig: Canvas and animation shortcuts ([, ], Space, ,, ., Home, End, O)
                    // are now registered in HexpriteShortcutManager (ShortcutScope.EditorCanvas) and handled upstream.
                }
            }
            else if (Keyboard.Modifiers == ModifierKeys.Shift)
            {
                // Block Shift+key shortcuts while text editing (Shift is needed for uppercase)
                if (ViewModel.IsTextEditing) return;

                if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                    Keyboard.FocusedElement is ComboBox ||
                    Keyboard.FocusedElement is ComboBoxItem) return;
                // Strangler Fig: Shift+R and Shift+C tool selection shortcuts are now registered
                // in HexpriteShortcutManager (ShortcutScope.EditorCanvas) and handled upstream.
            }
            else if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                if (ViewModel.IsTextEditing) return;

                if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase ||
                    Keyboard.FocusedElement is ComboBox ||
                    Keyboard.FocusedElement is ComboBoxItem) return;
                
                // Strangler Fig: Alt+N (Add Frame) is now registered in HexpriteShortcutManager (ShortcutScope.EditorCanvas) and handled upstream.
            }
        }

        protected override void OnPreviewKeyUp(KeyEventArgs e)
        {
            base.OnPreviewKeyUp(e);
            if (ViewModel == null || Selection == null) return;

            // Update modifier key state for status bar
            UpdateModifierKeyState();

            Key actualKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (actualKey == Key.LeftShift || actualKey == Key.RightShift || actualKey == Key.LeftAlt || actualKey == Key.RightAlt)
            {
                bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
                bool isAltDown = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt);

                if (ViewModel.IsDrawingLine || ViewModel.IsDrawingRectangle || ViewModel.IsDrawingEllipse ||
                    ViewModel.IsDrawingFilledRectangle || ViewModel.IsDrawingFilledEllipse || ViewModel.IsDrawingGradient)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.ProcessToolInput(x, y, ToolAction.Move, _activeDrawMode, isShiftDown, isAltDown);
                }
                else if (Selection.IsSelecting)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.ProcessSelectionInput(x, y, ToolAction.Move, isShiftDown, isAltDown);
                }
                else if (_activeTransformDrag)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    var (x, y) = GetPixelCoordinates(pos, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight);
                    ViewModel.UpdateSelectionTransform(
                        x - _transformDownPixelX,
                        y - _transformDownPixelY,
                        isShiftDown, isAltDown);
                    _selectionOverlay.Update();
                }
                else if (_activeRotationDrag)
                {
                    var pos = Mouse.GetPosition(Canvas.CanvasImage);
                    ViewModel.UpdateSelectionRotation(
                        pos.X, pos.Y, Canvas.CanvasImage.ActualWidth, Canvas.CanvasImage.ActualHeight,
                        isShiftDown);
                    _selectionOverlay.Update();
                }
            }
        }

        private void UpdateModifierKeyState()
        {
            if (ViewModel == null) return;
            ViewModel.IsShiftPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            ViewModel.IsCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
            ViewModel.IsAltPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        }

        // ── Drag position update ──────────────────────────────────────────

        private void UpdateDragPosition(Point currentPos)
        {
            double gw = Canvas.PixelGridContainer.ActualWidth > 0 ? Canvas.PixelGridContainer.ActualWidth : 400.0;
            double gh = Canvas.PixelGridContainer.ActualHeight > 0 ? Canvas.PixelGridContainer.ActualHeight : 400.0;

            int spriteW = ViewModel!.SpriteState.Width;
            int spriteH = ViewModel.SpriteState.Height;

            // Bug 9: guard against zero dimensions — cw/ch would be 0.0, making the
            // division produce ±Infinity which silently overflows int to Min/MaxValue.
            double cw = spriteW > 0 ? gw / spriteW : 1.0;
            double ch = spriteH > 0 ? gh / spriteH : 1.0;

            // M3: use Math.Floor to match GetPixelCoordinates — Math.Round caused the
            // floating selection to snap one pixel early at the start of a drag.
            int newX = _dragStartFloatingX + (int)Math.Floor((currentPos.X - _dragStartMousePos.X) / cw);
            int newY = _dragStartFloatingY + (int)Math.Floor((currentPos.Y - _dragStartMousePos.Y) / ch);

            ViewModel.UpdateSelectionDrag(newX, newY);
        }

        private void ReleaseDragCapture()
        {
            if (Mouse.Captured == Canvas.PixelGridContainer || Mouse.Captured == Canvas.CanvasImage)
                Mouse.Capture(element: null);
        }

        // ── Zoom & pan (delegated to ZoomPanController) ───────────────────

        private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => _zoomPan.ZoomIn();
        private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => _zoomPan.ZoomOut();
        private void BtnZoomReset_Click(object sender, RoutedEventArgs e) => _zoomPan.ZoomReset();

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
            => _zoomPan.HandleMouseWheel((ScrollViewer)sender, e);

        private void TabBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                if (e.Delta > 0)
                    scrollViewer.LineLeft();
                else
                    scrollViewer.LineRight();
                e.Handled = true;
            }
        }

        private void ScrollViewer_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var sv = (ScrollViewer)sender;

            if (_zoomPan.TryStartPan(sv, e, this))
            {
                e.Handled = true;
                return;
            }

            // Ignore scrollbar and symmetry handle clicks
            if (e.OriginalSource is DependencyObject dep)
            {
                if (dep is FrameworkElement fe && fe.Tag is "SymmetryHandle")
                    return; // Let the thumb handle its own drag

                var parent = VisualTreeHelper.GetParent(dep);
                while (parent != null)
                {
                    if (parent is System.Windows.Controls.Primitives.ScrollBar) return;
                    if (parent is FrameworkElement parentFe && parentFe.Tag is "SymmetryHandle") return;
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            if (ViewModel == null || Selection == null) return;

            if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not Image)
            {
                // Only auto-commit a floating selection when clicking outside the canvas.
                // Non-floating selections persist until explicitly deselected (Ctrl+D).
                if (Selection.HasActiveSelection && Selection.IsFloating)
                {
                    // Don't deselect if the click is on a transform handle (handle may be outside canvas).
                    var image2 = Canvas.CanvasImage;
                    var imgPos2 = e.GetPosition(image2);
                    var hitHandle = ViewModel.HitTestSelectionHandle(imgPos2.X, imgPos2.Y, image2.ActualWidth, image2.ActualHeight);
                    if (hitHandle != TransformHandle.None)
                    {
                        // Fall through to transform handle processing below
                    }
                    else
                    {
                        // Also check if the click lands within the floating selection's
                        // bounding box — the user may be trying to drag a selection that
                        // extends outside the canvas bounds.
                        var (px, py) = GetPixelCoordinates(imgPos2, image2.ActualWidth, image2.ActualHeight);
                        if (Selection.IsPointInSelectionBounds(px, py))
                        {
                            // Fall through to drag initiation below
                        }
                        else
                        {
                            ViewModel.DeselectCommand.Execute(parameter: null);
                            _selectionOverlay.Clear();
                        }
                    }
                }
            }

            var image = Canvas.CanvasImage;
            if (e.ChangedButton == MouseButton.Left || e.ChangedButton == MouseButton.Right)
            {
                image.CaptureMouse();
                var imgPos = e.GetPosition(image);
                var (x, y) = GetPixelCoordinates(imgPos, image.ActualWidth, image.ActualHeight);

                // Double-clicking outside a floating selection with the Move tool commits it
                if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && 
                    ViewModel.CurrentTool == ToolMode.Move && Selection.HasActiveSelection && Selection.IsFloating)
                {
                    if (!Selection.IsPointInSelectionBounds(x, y))
                    {
                        ViewModel.DeselectCommand.Execute(parameter: null);
                        _selectionOverlay.Clear();
                        image.ReleaseMouseCapture();
                        return;
                    }
                }

                // Transform handles work for ALL tools (including Move) when there's a floating selection.
                if (e.ChangedButton == MouseButton.Left)
                {
                    var handle = ViewModel.HitTestSelectionHandle(imgPos.X, imgPos.Y, image.ActualWidth, image.ActualHeight);
                    if (handle != TransformHandle.None && ViewModel.TryBeginSelectionTransform(handle))
                    {
                        if (handle == TransformHandle.Rotate)
                        {
                            _activeRotationDrag = true;
                            ViewModel.BeginSelectionRotation(imgPos.X, imgPos.Y, image.ActualWidth, image.ActualHeight);
                        }
                        else
                        {
                            _activeTransformDrag = true;
                            _transformDownPixelX = x;
                            _transformDownPixelY = y;
                        }
                        Mouse.Capture(Canvas.PixelGridContainer);
                        return;
                    }
                }

                if (ViewModel.CurrentTool == ToolMode.Marquee ||
                    ViewModel.CurrentTool == ToolMode.Lasso ||
                    ViewModel.CurrentTool == ToolMode.EllipticalMarquee ||
                    ViewModel.CurrentTool == ToolMode.MagicWand)
                {
                    // Check if we should start a drag instead
                    bool isReplace = !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                    if (isReplace && Selection.HasActiveSelection && Selection.IsPointInSelectionBounds(x, y))
                    {
                        if (ViewModel.TryBeginSelectionDrag(x, y))
                        {
                            _dragStartMousePos = e.GetPosition(Canvas.PixelGridContainer);
                            _dragStartFloatingX = Selection.FloatingX;
                            _dragStartFloatingY = Selection.FloatingY;
                            Mouse.Capture(Canvas.PixelGridContainer);
                            return;
                        }
                    }

                    ViewModel.ProcessSelectionInput(x, y, ToolAction.Down,
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
                        e.ChangedButton == MouseButton.Right);
                    return;
                }

                if (ViewModel.CurrentTool == ToolMode.Eraser)
                {
                    // Eraser only erases on Left-Click. Right-Click does nothing.
                    _activeDrawMode = e.ChangedButton == MouseButton.Left ? DrawMode.Erase : DrawMode.None;
                }
                else
                {
                    // Standard tools: Left-Click = Draw, Right-Click = Erase
                    _activeDrawMode = e.ChangedButton == MouseButton.Left ? DrawMode.Draw
                                   : e.ChangedButton == MouseButton.Right ? DrawMode.Erase
                                   : DrawMode.None;
                }

                if (_activeDrawMode != DrawMode.None)
                {
                    // Update brush cursor immediately on press so the overlay
                    // doesn't lag until the first PreviewMouseMove event fires.
                    _brushCursor.LastCanvasMousePos = imgPos;
                    _brushCursor.IsMouseOverCanvas = true;
                    _brushCursor.Update(x, y, imgPos, image.ActualWidth, image.ActualHeight);

                    _lastHoveredX = x;
                    _lastHoveredY = y;
                    ViewModel.ProcessToolInput(x, y, ToolAction.Down, _activeDrawMode,
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                }
            }
        }

        private static Cursor GetCanvasCursorForTool(ToolMode tool)
        {
            if (tool == ToolMode.Move) return _cursorMovePointer.Value;
            if (tool == ToolMode.Marquee || tool == ToolMode.Lasso || tool == ToolMode.EllipticalMarquee || tool == ToolMode.MagicWand) return _cursorPrecision.Value;
            if (tool == ToolMode.Pencil || tool == ToolMode.Eraser || tool == ToolMode.Dither ||
                tool == ToolMode.Line || tool == ToolMode.Rectangle || tool == ToolMode.Ellipse ||
                tool == ToolMode.FilledRectangle || tool == ToolMode.FilledEllipse) 
                return Cursors.None;
            return Cursors.Arrow;
        }

        private void UpdateCanvasCursor(Point pos, int x, int y)
        {
            if (ViewModel == null || Selection == null) return;
            var image = Canvas.CanvasImage;

            if (!_activeTransformDrag && !_activeRotationDrag && Selection.IsFloating &&
                (ViewModel.CurrentTool == ToolMode.Marquee ||
                 ViewModel.CurrentTool == ToolMode.Lasso ||
                 ViewModel.CurrentTool == ToolMode.EllipticalMarquee ||
                 ViewModel.CurrentTool == ToolMode.MagicWand ||
                 ViewModel.CurrentTool == ToolMode.Move))
            {
                var h = ViewModel.HitTestSelectionHandle(pos.X, pos.Y, image.ActualWidth, image.ActualHeight);
                if (h != TransformHandle.None)
                {
                    var handleCursor = CursorForTransformHandle(h, Selection.RotationAngle);
                    image.Cursor = handleCursor;
                    // When the handle is outside the canvas image bounds, image.Cursor
                    // won't apply because the mouse is physically over the ScrollViewer.
                    // Use Mouse.OverrideCursor so the correct handle cursor shows regardless.
                    bool isOutsideImage = pos.X < 0 || pos.X > image.ActualWidth ||
                                          pos.Y < 0 || pos.Y > image.ActualHeight;
                    Mouse.OverrideCursor = isOutsideImage ? handleCursor : null;
                }
                else if (Selection.IsPointInSelectionBounds(x, y))
                {
                    image.Cursor = _cursorMove.Value;
                    bool isOutsideImage = pos.X < 0 || pos.X > image.ActualWidth ||
                                          pos.Y < 0 || pos.Y > image.ActualHeight;
                    Mouse.OverrideCursor = isOutsideImage ? _cursorMove.Value : null;
                }
                else
                {
                    image.Cursor = GetCanvasCursorForTool(ViewModel.CurrentTool);
                    Mouse.OverrideCursor = null;
                }
            }
            else if (!_zoomPan.IsPanning)
            {
                image.Cursor = GetCanvasCursorForTool(ViewModel.CurrentTool);
                Mouse.OverrideCursor = null;
            }
        }

        private void ScrollViewer_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_zoomPan.IsPanning)
            {
                _zoomPan.HandlePanMove((ScrollViewer)sender, e, this);
                _brushCursor.Hide();
                return;
            }

            var image = Canvas.CanvasImage;
            var pos = e.GetPosition(image);
            var (x, y) = GetPixelCoordinates(pos, image.ActualWidth, image.ActualHeight);

            _brushCursor.LastCanvasMousePos = pos;
            _brushCursor.IsMouseOverCanvas = pos.X >= 0 && pos.X <= image.ActualWidth &&
                                             pos.Y >= 0 && pos.Y <= image.ActualHeight;

            if (ViewModel == null || Selection == null) return;

            // Update brush cursor every mouse move for smooth visual tracking
            _brushCursor.Update(x, y, pos, image.ActualWidth, image.ActualHeight);

            if (_activeRotationDrag && e.LeftButton == MouseButtonState.Pressed)
            {
                ViewModel.UpdateSelectionRotation(
                    pos.X, pos.Y, image.ActualWidth, image.ActualHeight,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
                _selectionOverlay.Update();
                return;
            }

            if (_activeTransformDrag && e.LeftButton == MouseButtonState.Pressed)
            {
                ViewModel.UpdateSelectionTransform(
                    x - _transformDownPixelX,
                    y - _transformDownPixelY,
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                    Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                _selectionOverlay.Update();
                return;
            }

            if (Selection.IsFloating ||
                ViewModel.CurrentTool == ToolMode.Marquee ||
                ViewModel.CurrentTool == ToolMode.Lasso ||
                ViewModel.CurrentTool == ToolMode.EllipticalMarquee ||
                ViewModel.CurrentTool == ToolMode.MagicWand ||
                ViewModel.CurrentTool == ToolMode.Move)
            {
                UpdateCanvasCursor(pos, x, y);
            }

            // Notify the selection controller of any mouse movement during a
            // selection drag so that sub-pixel movement within the same pixel
            // still counts as a drag (distinguishes click from drag).
            if (!_activeTransformDrag &&
                (ViewModel.CurrentTool == ToolMode.Marquee ||
                 ViewModel.CurrentTool == ToolMode.Lasso ||
                 ViewModel.CurrentTool == ToolMode.EllipticalMarquee)
                && e.LeftButton == MouseButtonState.Pressed
                && Selection.IsSelecting)
            {
                ViewModel.NotifySelectionMouseMoved();
            }

            // Only process drawing/selection when the pixel coordinate actually changes
            if (x != _lastHoveredX || y != _lastHoveredY)
            {
                _lastHoveredX = x;
                _lastHoveredY = y;
                ViewModel.CursorX = x;
                ViewModel.CursorY = y;

                if (!_activeTransformDrag &&
                    (ViewModel.CurrentTool == ToolMode.Marquee ||
                     ViewModel.CurrentTool == ToolMode.Lasso ||
                     ViewModel.CurrentTool == ToolMode.EllipticalMarquee ||
                     ViewModel.CurrentTool == ToolMode.MagicWand)
                    && e.LeftButton == MouseButtonState.Pressed)
                {
                    ViewModel.ProcessSelectionInput(x, y, ToolAction.Move,
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
                        Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
                    return;
                }

                var mode = _activeDrawMode;
                if (mode != DrawMode.None || ViewModel.IsDrawingLine || ViewModel.IsDrawingRectangle || ViewModel.IsDrawingEllipse ||
                    ViewModel.IsDrawingFilledRectangle || ViewModel.IsDrawingFilledEllipse || ViewModel.IsDrawingGradient)
                {
                    bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    bool isAlt = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
                    var now = DateTime.UtcNow;
                    if (now - _lastToolMoveDispatchUtc >= ToolMoveDispatchInterval)
                    {
                        _toolMoveTimer?.Stop();
                        _hasPendingToolMove = false;
                        _lastToolMoveDispatchUtc = now;
                        ViewModel.ProcessToolInput(x, y, ToolAction.Move, mode, isShift, isAlt);
                    }
                    else
                    {
                        EnqueueToolMove(x, y, mode, isShift, isAlt);
                    }
                }
            }
        }

        private void ScrollViewer_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _zoomPan.TryEndPan((ScrollViewer)sender, e);
        }

        private void EnqueueToolMove(int x, int y, DrawMode mode, bool isShiftDown, bool isAltDown)
        {
            if (ViewModel == null) return;

            _pendingToolMove = new PendingToolMove(x, y, mode, isShiftDown, isAltDown);
            _hasPendingToolMove = true;

            if (_toolMoveTimer == null)
            {
                _toolMoveTimer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Input, Dispatcher)
                {
                    Interval = ToolMoveDispatchInterval
                };
                _toolMoveTimer.Tick += (_, _) => DispatchPendingToolMove();
            }

            if (!_toolMoveTimer.IsEnabled)
            {
                var elapsed = DateTime.UtcNow - _lastToolMoveDispatchUtc;
                var remaining = ToolMoveDispatchInterval - elapsed;
                _toolMoveTimer.Interval = remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1);
                _toolMoveTimer.Start();
            }
        }

        private void DispatchPendingToolMove()
        {
            _toolMoveTimer?.Stop();
            if (!_hasPendingToolMove || ViewModel == null)
                return;

            var pending = _pendingToolMove;
            _hasPendingToolMove = false;
            _lastToolMoveDispatchUtc = DateTime.UtcNow;
            ViewModel.ProcessToolInput(pending.X, pending.Y, ToolAction.Move, pending.Mode, pending.IsShiftDown, pending.IsAltDown);
        }

        private void FlushPendingToolMove()
        {
            _toolMoveTimer?.Stop();
            if (!_hasPendingToolMove || ViewModel == null)
                return;

            var pending = _pendingToolMove;
            _hasPendingToolMove = false;
            _lastToolMoveDispatchUtc = DateTime.UtcNow;
            ViewModel.ProcessToolInput(pending.X, pending.Y, ToolAction.Move, pending.Mode, pending.IsShiftDown, pending.IsAltDown);
        }

        private readonly record struct PendingToolMove(int X, int Y, DrawMode Mode, bool IsShiftDown, bool IsAltDown);

        // ── Utility ───────────────────────────────────────────────────────

        private static Cursor LoadCursorOrDefault(string filename, Cursor fallback)
        {
            try
            {
                var uri = new Uri($"pack://application:,,,/Assets/{filename}", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info != null)
                {
                    using var ms = new MemoryStream();
                    info.Stream.CopyTo(ms);
                    return LoadCursorWithHotspotFix(ms.ToArray());
                }
            }
            catch (Exception)
            {
                // Fall back to default cursor if custom asset is not found
            }
            return fallback;
        }

        private static Cursor LoadCursorWithHotspotFix(byte[] data)
        {
            // Check if valid .cur file header
            if (data.Length > 16 && data[0] == 0 && data[2] == 2)
            {
                int hx = BitConverter.ToInt16(data, 10);
                int hy = BitConverter.ToInt16(data, 12);

                // If the hotspot is exactly (0,0), it's likely a generic converted image.
                // We'll auto-center it for a better experience with transform handles.
                if (hx == 0 && hy == 0)
                {
                    int width = data[6];
                    int height = data[7];
                    if (width == 0) width = 256;
                    if (height == 0) height = 256;

                    short cx = (short)(width / 2);
                    short cy = (short)(height / 2);

                    byte[] bx = BitConverter.GetBytes(cx);
                    byte[] by = BitConverter.GetBytes(cy);

                    data[10] = bx[0]; data[11] = bx[1];
                    data[12] = by[0]; data[13] = by[1];
                }
            }
            using var ms = new MemoryStream(data);
            return new Cursor(ms);
        }

        private static readonly Lazy<Cursor> _cursorNwse = new(() => LoadCursorOrDefault("resize_nwse.cur", Cursors.SizeNWSE));
        private static readonly Lazy<Cursor> _cursorNesw = new(() => LoadCursorOrDefault("resize_nesw.cur", Cursors.SizeNESW));
        private static readonly Lazy<Cursor> _cursorNs = new(() => LoadCursorOrDefault("resize_ns.cur", Cursors.SizeNS));
        private static readonly Lazy<Cursor> _cursorWe = new(() => LoadCursorOrDefault("resize_we.cur", Cursors.SizeWE));
        private static readonly Lazy<Cursor> _cursorMove = new(() => LoadCursorOrDefault("move.cur", Cursors.SizeAll));
        private static readonly Lazy<Cursor> _cursorMovePointer = new(() => LoadCursorOrDefault("move_pointer.cur", Cursors.SizeAll));
        private static readonly Lazy<Cursor> _cursorPrecision = new(() => LoadCursorOrDefault("precision.cur", Cursors.Cross));

        private static readonly Lazy<Cursor> _rotateCursor = new(CreateRotateCursor);

        /// <summary>
        /// Maps a handle + rotation angle to the appropriate resize cursor.
        /// The handle's base direction is rotated by the selection's current angle,
        /// then snapped to the nearest 45° to select from the 4 available cursors.
        /// </summary>
        private static Cursor CursorForTransformHandle(TransformHandle h, double rotationAngleDeg = 0)
        {
            if (h == TransformHandle.Rotate)
                return _rotateCursor.Value;

            // Base angles for each handle (degrees, 0 = right/east, clockwise)
            double baseAngle = h switch
            {
                TransformHandle.E  => 0,
                TransformHandle.SE => 45,
                TransformHandle.S  => 90,
                TransformHandle.SW => 135,
                TransformHandle.W  => 180,
                TransformHandle.NW => 225,
                TransformHandle.N  => 270,
                TransformHandle.NE => 315,
                _ => 0,
            };

            // Add rotation and normalize to [0, 360)
            double angle = (baseAngle + rotationAngleDeg) % 360;
            if (angle < 0) angle += 360;

            // Snap to nearest 45° and map to one of 4 cursor directions.
            // Opposite handles share the same cursor (0°==180°, 45°==225°, etc.)
            int snapped = (int)Math.Round(angle / 45.0, MidpointRounding.AwayFromZero) % 4;
            return snapped switch
            {
                0 => _cursorWe.Value,    // 0°/180° → horizontal resize
                1 => _cursorNwse.Value,  // 45°/225° → NW-SE diagonal
                2 => _cursorNs.Value,    // 90°/270° → vertical resize
                3 => _cursorNesw.Value,  // 135°/315° → NE-SW diagonal
                _ => Cursors.Arrow,
            };
        }

        /// <summary>
        /// Creates a rotation cursor. Loads from <c>Assets/rotate.cur</c> next to the
        /// executable if it exists, otherwise generates a curved-arrow icon in memory.
        /// Drop your own <c>rotate.cur</c> into the Assets folder to replace it.
        /// </summary>
        private static Cursor CreateRotateCursor()
        {
            try
            {
                var uri = new Uri("pack://application:,,,/Assets/rotate.cur", UriKind.Absolute);
                var info = Application.GetResourceStream(uri);
                if (info != null)
                {
                    using var cursorStream = new MemoryStream();
                    info.Stream.CopyTo(cursorStream);
                    return LoadCursorWithHotspotFix(cursorStream.ToArray());
                }
            }
            catch (Exception)
            {
                /* fall through to generated cursor */
            }
            const int size = 32;
            const int hotspot = 16;

            var dv = new System.Windows.Media.DrawingVisual();
            using (var ctx = dv.RenderOpen())
            {
                double cx = 16, cy = 16, r = 9;

                // Arc from -60° to 240° (300° sweep)
                double startRad = -60 * Math.PI / 180;
                double endRad = 240 * Math.PI / 180;
                var startPt = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
                var endPt = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));

                var arcFig = new System.Windows.Media.PathFigure { StartPoint = startPt, IsClosed = false };
                arcFig.Segments.Add(new System.Windows.Media.ArcSegment(
                    endPt, new Size(r, r), 0, isLargeArc: true, SweepDirection.Clockwise, isStroked: true));
                var arcGeom = new System.Windows.Media.PathGeometry([arcFig]);

                // Arrowhead at the start of the arc (tangent = perpendicular to radius)
                double tangent = startRad - Math.PI / 2;
                double arrowLen = 5.5;
                var a1 = new Point(startPt.X + arrowLen * Math.Cos(tangent + 0.5),
                                   startPt.Y + arrowLen * Math.Sin(tangent + 0.5));
                var a2 = new Point(startPt.X + arrowLen * Math.Cos(tangent - 0.5),
                                   startPt.Y + arrowLen * Math.Sin(tangent - 0.5));
                var arrowFig = new System.Windows.Media.PathFigure { StartPoint = a1, IsClosed = false };
                arrowFig.Segments.Add(new System.Windows.Media.LineSegment(startPt, isStroked: true));
                arrowFig.Segments.Add(new System.Windows.Media.LineSegment(a2, isStroked: true));
                var arrowGeom = new System.Windows.Media.PathGeometry([arrowFig]);

                var outline = new Pen(Brushes.Black, 3.0)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                var main = new Pen(Brushes.White, 1.5)
                { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

                ctx.DrawGeometry(brush: null, outline, arcGeom);
                ctx.DrawGeometry(brush: null, outline, arrowGeom);
                ctx.DrawGeometry(brush: null, main, arcGeom);
                ctx.DrawGeometry(brush: null, main, arrowGeom);
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);

            var pixels = new byte[size * size * 4];
            rtb.CopyPixels(pixels, size * 4, 0);

            // Build a .cur file in memory
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);

            // Header: reserved(2) + type(2, 2=cursor) + count(2)
            bw.Write((short)0); bw.Write((short)2); bw.Write((short)1);

            // Directory entry (16 bytes)
            int andMaskRowBytes = ((size + 31) / 32) * 4;
            int andMaskSize = andMaskRowBytes * size;
            int imgDataSize = 40 + pixels.Length + andMaskSize;
            bw.Write((byte)size); bw.Write((byte)size);
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)hotspot); bw.Write((short)hotspot);
            bw.Write(imgDataSize);
            bw.Write(6 + 16); // offset = header(6) + directory(16)

            // BITMAPINFOHEADER (40 bytes)
            bw.Write(40); bw.Write(size); bw.Write(size * 2);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(pixels.Length + andMaskSize);
            bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);

            // XOR mask (BGRA, bottom-up)
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size + x) * 4;
                    bw.Write(pixels[i]); bw.Write(pixels[i + 1]);
                    bw.Write(pixels[i + 2]); bw.Write(pixels[i + 3]);
                }

            // AND mask (1bpp, all zeros for 32-bit ARGB cursor)
            bw.Write(new byte[andMaskSize]);

            bw.Flush();
            ms.Position = 0;
            return new Cursor(ms);
        }

        private (int x, int y) GetPixelCoordinates(Point pos, double actualWidth, double actualHeight)
        {
            if (ViewModel == null) return (0, 0);

            int w = ViewModel.SpriteState.Width;
            int h = ViewModel.SpriteState.Height;
            // Bug 5: also guard w/h — Math.Clamp(x, 0, w-1) throws when w == 0.
            if (actualWidth == 0 || actualHeight == 0 || w <= 0 || h <= 0) return (0, 0);

            int x = (int)Math.Floor(pos.X / actualWidth * w);
            int y = (int)Math.Floor(pos.Y / actualHeight * h);

            // Aseprite-style: only clamp when the cursor is within the image boundary.
            // Positions outside the canvas edges are intentionally left unclamped so
            // tools that extend beyond the sprite edge continue to work correctly.
            bool activeLayerOverflows = ViewModel.SpriteState?.ActivePixelBuffer is Core.OverflowPixelBuffer;
            if (!activeLayerOverflows)
            {
                if (pos.X >= 0 && pos.X <= actualWidth) x = Math.Clamp(x, 0, w - 1);
                if (pos.Y >= 0 && pos.Y <= actualHeight) y = Math.Clamp(y, 0, h - 1);
            }

            return (x, y);
        }

        // ── Brush cursor (delegated) ──────────────────────────────────────

        private void ScrollViewer_MouseLeave(object sender, MouseEventArgs e) => _brushCursor.OnMouseLeave();

        // ── Custom chrome caption buttons ─────────────────────────────────

        private void CaptionMinimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void CaptionMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            else
                WindowState = WindowState.Maximized;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (MaximizeIcon != null)
            {
                MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            }

            if (_isInSizeOrMove)
            {
                _needsCenterOnExitSizeMove = true;
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CenterCanvasToViewport();
                }), DispatcherPriority.Loaded);
            }
        }

        private void CaptionClose_Click(object sender, RoutedEventArgs e)
            => Close();

        // ── Win32 interop for proper maximize with WindowStyle=None ────────

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        private bool _isInSizeOrMove;
        private bool _needsCenterOnExitSizeMove;

        [LibraryImport("user32.dll", EntryPoint = "MonitorFromWindow")]
        private static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x, y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ENTERSIZEMOVE)
            {
                _isInSizeOrMove = true;
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                _isInSizeOrMove = false;
                if (_needsCenterOnExitSizeMove)
                {
                    _needsCenterOnExitSizeMove = false;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        CenterCanvasToViewport();
                    }), DispatcherPriority.Loaded);
                }
            }
            else if (msg == WM_GETMINMAXINFO)
            {
                var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor != IntPtr.Zero)
                {
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                    GetMonitorInfo(monitor, ref mi);
                    var work = mi.rcWork;
                    var mon = mi.rcMonitor;
                    mmi.ptMaxPosition = new POINT { x = work.left - mon.left, y = work.top - mon.top };
                    mmi.ptMaxSize = new POINT { x = work.right - work.left, y = work.bottom - work.top };
                }

                // ── USE CACHED VALUES FOR ZERO-OVERHEAD RESIZING ──
                if (_minTrackWidth > 0 && _minTrackHeight > 0)
                {
                    mmi.ptMinTrackSize.x = _minTrackWidth;
                    mmi.ptMinTrackSize.y = _minTrackHeight;
                }

                // L4: fDeleteOld must be false — lParam is unmanaged Win32 memory;
                // passing true would cause the marshaler to call DestroyStructure on it,
                // which is undefined behavior for a blittable struct in unmanaged memory.
                Marshal.StructureToPtr(mmi, lParam, fDeleteOld: false);
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var handle = new WindowInteropHelper(this).Handle;
            // H1: store the HwndSource so we can RemoveHook in MainWindow_Closed.
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);

            // Calculate the physical pixels required for MinWidth/MinHeight once
            UpdateMinTrackSize();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            // Recalculate if the user moves the app to a different monitor
            UpdateMinTrackSize();
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    foreach (string file in files)
                    {
                        if (string.IsNullOrWhiteSpace(file)) continue;
                        if (_shell.OpenDocuments.Count >= ShellViewModel.MaxTabs)
                        {
                            break;
                        }

                        string ext = Path.GetExtension(file).ToLowerInvariant();
                        if (ext == ".c" || ext == ".cpp" || ext == ".h" || ext == ".hpp" || ext == ".ino" || ext == ".py")
                        {
                            _shell.ImportFromFile(file);
                            e.Handled = true;
                        }
                        else if (ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp" || ext == ".gif" || ext == ".tif" || ext == ".tiff")
                        {
                            _shell.ImportBitmapFromFile(file);
                            e.Handled = true;
                        }
                        else if (ext == ".zip")
                        {
                            _shell.OpenFile(file);
                            e.Handled = true;
                        }
                        else if (string.Equals(Path.GetFileName(file), "manifest.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            _shell.OpenFile(file);
                            e.Handled = true;
                        }
                        else if (ext == ".bm" || Directory.Exists(file) ||
                                 Path.GetFileName(file).Equals("meta.txt", StringComparison.OrdinalIgnoreCase))
                        {
                            _shell.ImportFlipperFromPath(file);
                            e.Handled = true;
                        }
                        else if (ext == ".xbm")
                        {
                            _shell.ImportXbmFromPath(file);
                            e.Handled = true;
                        }
                        else if (ext is ".hexp" or ".hexfont" or ".hexpfont" or ".hexpack")
                        {
                            _shell.OpenFile(file);
                            e.Handled = true;
                        }
                    }
                }
            }
        }

        private void UpdateMinTrackSize()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                Matrix transform = source.CompositionTarget.TransformToDevice;
                _minTrackWidth = (int)(this.MinWidth * transform.M11);
                _minTrackHeight = (int)(this.MinHeight * transform.M22);
            }
            else
            {
                _minTrackWidth = (int)this.MinWidth;
                _minTrackHeight = (int)this.MinHeight;
            }
        }
    }
}
