using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Hexprite.Core;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.Views
{
    public partial class AssetPackEditorPanel : UserControl
    {
        public FlipperScheduleMatrixViewModel? ViewModel =>
            (DataContext as AssetPackViewModel)?.MatrixViewModel ?? DataContext as FlipperScheduleMatrixViewModel;

        private FlipperScheduleMatrixViewModel? _subscribedVm;
        private bool _isDragging;
        private Point _dragStartPoint;
        private int _dragStartLvl;
        private int _dragStartMood;
        private int _dragCurrentLvl;
        private int _dragCurrentMood;

        private int _hoverLvl = -1;
        private int _hoverMood = -1;

        private static readonly SolidColorBrush SoloBrush = CreateFrozenBrush(Color.FromArgb(240, 0, 229, 255));
        private static readonly SolidColorBrush DuoBrush = CreateFrozenBrush(Color.FromArgb(240, 57, 255, 20));
        private static readonly SolidColorBrush TrioBrush = CreateFrozenBrush(Color.FromArgb(240, 255, 179, 0));
        private static readonly SolidColorBrush ContendedBrush = CreateFrozenBrush(Color.FromArgb(240, 255, 51, 102));
        private static readonly SolidColorBrush CellBorderBrush = CreateFrozenBrush(Color.FromArgb(90, 0, 0, 0));
        private static readonly SolidColorBrush SelectedCellBorderBrush = CreateFrozenBrush(Color.FromRgb(255, 160, 0)); // #FFA000
        private static readonly SolidColorBrush SelectedBadgeTextBrush = CreateFrozenBrush(Color.FromRgb(255, 248, 225)); // #FFF8E1
        private static readonly SolidColorBrush SelectedBoxBorderBrush = CreateFrozenBrush(Color.FromRgb(255, 130, 0)); // #FF8200
        private static readonly SolidColorBrush SelectedBoxFillBrush = CreateFrozenBrush(Color.FromArgb(20, 255, 130, 0)); // #14FF8200 (8% tint)
        private static readonly SolidColorBrush DefaultGapFillBrush = CreateFrozenBrush(Color.FromRgb(42, 21, 27));
        private static readonly SolidColorBrush DefaultGapStrokeBrush = CreateFrozenBrush(Color.FromRgb(255, 123, 114));
        private static readonly SolidColorBrush DefaultBadgeTextBrush = CreateFrozenBrush(Color.FromRgb(11, 13, 20));
        private static readonly SolidColorBrush DefaultBabyBrush = CreateFrozenBrush(Color.FromRgb(0, 229, 255));
        private static readonly SolidColorBrush DefaultTeenBrush = CreateFrozenBrush(Color.FromRgb(255, 179, 0));
        private static readonly SolidColorBrush DefaultAdultBrush = CreateFrozenBrush(Color.FromRgb(57, 255, 20));
        private static readonly SolidColorBrush DefaultHappyBrush = CreateFrozenBrush(Color.FromRgb(16, 185, 129));
        private static readonly SolidColorBrush DefaultNeutralBrush = CreateFrozenBrush(Color.FromRgb(255, 179, 0));
        private static readonly SolidColorBrush DefaultAngryBrush = CreateFrozenBrush(Color.FromRgb(255, 51, 102));
        private static readonly SolidColorBrush DefaultReticleStrokeBrush = CreateFrozenBrush(Color.FromRgb(255, 255, 255));
        private static readonly SolidColorBrush DefaultReticleFillBrush = CreateFrozenBrush(Color.FromArgb(64, 255, 255, 255));
        private static readonly SolidColorBrush DefaultHoverStrokeBrush = CreateFrozenBrush(Color.FromArgb(180, 255, 255, 255));
        private static readonly SolidColorBrush DefaultHoverFillBrush = CreateFrozenBrush(Color.FromArgb(32, 255, 255, 255));
        private static readonly SolidColorBrush DefaultVDividerBrush = CreateFrozenBrush(Color.FromArgb(200, 255, 130, 0));
        private static readonly SolidColorBrush DefaultHDividerBrush = CreateFrozenBrush(Color.FromArgb(160, 255, 179, 0));
        private static readonly SolidColorBrush DefaultDragStrokeBrush = CreateFrozenBrush(Color.FromRgb(179, 136, 255));
        private static readonly SolidColorBrush DefaultDragFillBrush = CreateFrozenBrush(Color.FromArgb(64, 179, 136, 255));

        private double _lastDrawnWidth = -1;
        private double _lastDrawnHeight = -1;
        private int _lastMaxLvl = -1;

        private Brush _cachedGapFillBrush = DefaultGapFillBrush;
        private Brush _cachedGapStrokeBrush = DefaultGapStrokeBrush;
        private Brush _cachedBadgeTextBrush = DefaultBadgeTextBrush;
        private Brush _cachedVDividerBrush = DefaultVDividerBrush;
        private Brush _cachedHDividerBrush = DefaultHDividerBrush;
        private Brush _cachedHoverStrokeBrush = DefaultHoverStrokeBrush;
        private Brush _cachedHoverFillBrush = DefaultHoverFillBrush;
        private Brush _cachedDragStrokeBrush = DefaultDragStrokeBrush;
        private Brush _cachedDragFillBrush = DefaultDragFillBrush;
        private Brush _cachedReticleStrokeBrush = DefaultReticleStrokeBrush;
        private Brush _cachedReticleFillBrush = DefaultReticleFillBrush;
        private Brush _cachedBabyBrush = DefaultBabyBrush;
        private Brush _cachedTeenBrush = DefaultTeenBrush;
        private Brush _cachedAdultBrush = DefaultAdultBrush;
        private Brush _cachedHappyBrush = DefaultHappyBrush;
        private Brush _cachedNeutralBrush = DefaultNeutralBrush;
        private Brush _cachedAngryBrush = DefaultAngryBrush;
        private Brush _cachedPrimaryTextBrush = Brushes.White;

        private void RefreshCachedBrushes()
        {
            _cachedGapFillBrush = TryFindResource("Brush.Matrix.GapFill") as Brush ?? DefaultGapFillBrush;
            _cachedGapStrokeBrush = TryFindResource("Brush.Matrix.GapStroke") as Brush ?? DefaultGapStrokeBrush;
            _cachedBadgeTextBrush = TryFindResource("Brush.Matrix.BadgeText") as Brush ?? DefaultBadgeTextBrush;
            _cachedVDividerBrush = TryFindResource("Brush.Matrix.DividerTier") as Brush ?? DefaultVDividerBrush;
            _cachedHDividerBrush = TryFindResource("Brush.Matrix.DividerMood") as Brush ?? DefaultHDividerBrush;
            _cachedHoverStrokeBrush = TryFindResource("Brush.Matrix.HoverStroke") as Brush ?? DefaultHoverStrokeBrush;
            _cachedHoverFillBrush = TryFindResource("Brush.Matrix.HoverFill") as Brush ?? DefaultHoverFillBrush;
            _cachedDragStrokeBrush = TryFindResource("Brush.Matrix.DragStroke") as Brush ?? DefaultDragStrokeBrush;
            _cachedDragFillBrush = TryFindResource("Brush.Matrix.DragFill") as Brush ?? DefaultDragFillBrush;
            _cachedReticleStrokeBrush = TryFindResource("Brush.Matrix.ReticleStroke") as Brush ?? DefaultReticleStrokeBrush;
            _cachedReticleFillBrush = TryFindResource("Brush.Matrix.ReticleFill") as Brush ?? DefaultReticleFillBrush;
            _cachedBabyBrush = TryFindResource("Brush.Stage.Baby") as Brush ?? DefaultBabyBrush;
            _cachedTeenBrush = TryFindResource("Brush.Stage.Teen") as Brush ?? DefaultTeenBrush;
            _cachedAdultBrush = TryFindResource("Brush.Stage.Adult") as Brush ?? DefaultAdultBrush;
            _cachedHappyBrush = TryFindResource("Brush.Mood.Happy") as Brush ?? DefaultHappyBrush;
            _cachedNeutralBrush = TryFindResource("Brush.Mood.Neutral") as Brush ?? DefaultNeutralBrush;
            _cachedAngryBrush = TryFindResource("Brush.Mood.Angry") as Brush ?? DefaultAngryBrush;
            _cachedPrimaryTextBrush = TryFindResource("Brush.Text.Primary") as Brush ?? Brushes.White;
        }

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // Retained 2D matrix cell elements to avoid 500-950 UIElement re-instantiations on every redraw/bound change
        private readonly Rectangle[,] _cellRects = new Rectangle[31, 15];
        private readonly TextBlock[,] _cellCountLabels = new TextBlock[31, 15];
        private readonly TextBlock[] _levelAxisLabels = new TextBlock[31];
        private readonly TextBlock[] _moodAxisLabels = new TextBlock[15];
        private bool _gridElementsInitialized;

        // Retained visual elements to avoid UIElement re-instantiations on mouse hover / drag
        private readonly Rectangle _hoverRect = new()
        {
            RadiusX = 2,
            RadiusY = 2,
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly Rectangle _dragOverlay = new()
        {
            RadiusX = 4,
            RadiusY = 4,
            StrokeThickness = 2.5,
            StrokeDashArray = [4, 2],
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly Rectangle _regionOverlay = new()
        {
            RadiusX = 4,
            RadiusY = 4,
            StrokeThickness = 2.0,
            StrokeDashArray = [4, 4],
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly Rectangle _selOverlay = new()
        {
            RadiusX = 4,
            RadiusY = 4,
            StrokeThickness = 2.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly Rectangle _inspectedRect = new()
        {
            RadiusX = 2,
            RadiusY = 2,
            StrokeThickness = 2.5,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        private readonly Line _vLine1 = new() { StrokeThickness = 2.0, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        private readonly Line _vLine2 = new() { StrokeThickness = 2.0, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        private readonly Line _hLine1 = new() { StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };
        private readonly Line _hLine2 = new() { StrokeThickness = 1.5, StrokeDashArray = [4, 3], IsHitTestVisible = false };

        private bool _isRedrawPending;

        public AssetPackEditorPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) =>
            {
                RefreshCachedBrushes();
                AttachViewModel();
                RequestRedrawMatrix();
            };
            Unloaded += OnUnloaded;
            SizeChanged += (s, e) => RequestRedrawMatrix();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            DetachViewModel();
        }

        private void DetachViewModel()
        {
            if (_subscribedVm != null)
            {
                _subscribedVm.MatrixRedrawRequested -= OnMatrixRedrawRequested;
                _subscribedVm.PropertyChanged -= OnViewModelPropertyChanged;
                _subscribedVm = null;
            }
        }

        private void AttachViewModel()
        {
            if (_subscribedVm != null) return;
            var vm = ViewModel;
            if (vm != null)
            {
                _subscribedVm = vm;
                vm.MatrixRedrawRequested += OnMatrixRedrawRequested;
                vm.PropertyChanged += OnViewModelPropertyChanged;
                if (vm.SelectedEntry != null && ListEntries != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ViewModel?.SelectedEntry != null && ListEntries != null)
                        {
                            ListEntries.ScrollIntoView(ViewModel.SelectedEntry);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlipperScheduleMatrixViewModel.SelectedEntry))
            {
                if (ViewModel?.SelectedEntry != null && ListEntries != null)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (ViewModel?.SelectedEntry != null && ListEntries != null)
                        {
                            ListEntries.ScrollIntoView(ViewModel.SelectedEntry);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModel();
            AttachViewModel();
            RefreshCachedBrushes();
            RequestRedrawMatrix();
        }

        private void OnMatrixRedrawRequested(object? sender, EventArgs e)
        {
            RequestRedrawMatrix();
        }

        public void RequestRedrawMatrix()
        {
            if (_isRedrawPending) return;
            _isRedrawPending = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isRedrawPending = false;
                RedrawMatrix();
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            // Allow TextBox to handle its own undo/redo (Ctrl+Z, Ctrl+Y)
            if (e.OriginalSource is TextBox) return;

            if (vm.IsDeviceSimulatorView && vm.SimulatorViewModel != null)
            {
                var sim = vm.SimulatorViewModel;
                switch (e.Key)
                {
                    case Key.Space:
                        sim.TogglePlayPause();
                        e.Handled = true;
                        return;
                    case Key.Up:
                        sim.HandleDpadPress("UP");
                        e.Handled = true;
                        return;
                    case Key.Down:
                        sim.HandleDpadPress("DOWN");
                        e.Handled = true;
                        return;
                    case Key.Left:
                        sim.HandleDpadPress("LEFT");
                        e.Handled = true;
                        return;
                    case Key.Right:
                        sim.HandleDpadPress("RIGHT");
                        e.Handled = true;
                        return;
                    case Key.A:
                    case Key.Enter:
                        sim.HandleDpadPress("OK");
                        e.Handled = true;
                        return;
                    case Key.Back:
                    case Key.B:
                        sim.HandleDpadPress("BACK");
                        e.Handled = true;
                        return;
                    case Key.R:
                        sim.ResetAnimationToPassive();
                        e.Handled = true;
                        return;
                    case Key.D:
                        sim.ShowDesktopHud = !sim.ShowDesktopHud;
                        e.Handled = true;
                        return;
                    case Key.G:
                        sim.ShowLcdGrid = !sim.ShowLcdGrid;
                        e.Handled = true;
                        return;
                }
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (e.Key == Key.Z)
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        if (vm.CanRedo) { vm.Redo(); e.Handled = true; }
                    }
                    else
                    {
                        if (vm.CanUndo) { vm.Undo(); e.Handled = true; }
                    }
                }
                else if (e.Key == Key.Y)
                {
                    if (vm.CanRedo) { vm.Redo(); e.Handled = true; }
                }
                else if (e.Key == Key.Up)
                {
                    vm.SelectPreviousEntry();
                    e.Handled = true;
                }
                else if (e.Key == Key.Down)
                {
                    vm.SelectNextEntry();
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    vm.PreviewPrevFrame();
                    e.Handled = true;
                }
                else if (e.Key == Key.Right)
                {
                    vm.PreviewNextFrame();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Space)
            {
                vm.TogglePreviewPlay();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    MatrixCanvas.ReleaseMouseCapture();
                    vm.ClearRegionSelection();
                    RedrawMatrix();
                    e.Handled = true;
                }
                else if (vm.HasSelectedRegion)
                {
                    vm.ClearRegionSelection();
                    e.Handled = true;
                }
                else if (vm.SelectedEntry != null)
                {
                    vm.DeselectEntry();
                    e.Handled = true;
                }
            }
        }

        private void EnsureGridElements()
        {
            if (_gridElementsInitialized || MatrixCanvas == null) return;

            MatrixCanvas.Children.Clear();

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var rect = new Rectangle
                    {
                        RadiusX = 2,
                        RadiusY = 2,
                        IsHitTestVisible = true,
                    };
                    _cellRects[lvl, mood] = rect;
                    MatrixCanvas.Children.Add(rect);

                    var countLabel = new TextBlock
                    {
                        FontWeight = FontWeights.Bold,
                        IsHitTestVisible = false,
                        TextAlignment = TextAlignment.Center,
                        Visibility = Visibility.Collapsed,
                    };
                    _cellCountLabels[lvl, mood] = countLabel;
                    MatrixCanvas.Children.Add(countLabel);
                }
            }

            // Dividers & interactive overlays on top
            MatrixCanvas.Children.Add(_vLine1);
            MatrixCanvas.Children.Add(_vLine2);
            MatrixCanvas.Children.Add(_hLine1);
            MatrixCanvas.Children.Add(_hLine2);
            MatrixCanvas.Children.Add(_regionOverlay);
            MatrixCanvas.Children.Add(_selOverlay);
            MatrixCanvas.Children.Add(_dragOverlay);
            MatrixCanvas.Children.Add(_inspectedRect);
            MatrixCanvas.Children.Add(_hoverRect);

            _gridElementsInitialized = true;
        }

        private void EnsureAxisElements()
        {
            if (LevelAxisCanvas != null && LevelAxisCanvas.Children.Count == 0)
            {
                for (int lvl = 1; lvl <= 30; lvl++)
                {
                    var tb = new TextBlock
                    {
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        IsHitTestVisible = false,
                    };
                    _levelAxisLabels[lvl] = tb;
                    LevelAxisCanvas.Children.Add(tb);
                }
            }

            if (MoodAxisCanvas != null && MoodAxisCanvas.Children.Count == 0)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var tb = new TextBlock
                    {
                        FontWeight = FontWeights.SemiBold,
                        TextAlignment = TextAlignment.Center,
                        IsHitTestVisible = false,
                    };
                    _moodAxisLabels[mood] = tb;
                    MoodAxisCanvas.Children.Add(tb);
                }
            }
        }

        public void RedrawMatrix()
        {
            var vm = ViewModel;
            if (vm == null || MatrixCanvas == null || MatrixCanvas.ActualWidth < 10 || MatrixCanvas.ActualHeight < 10)
                return;

            int maxLvl = vm.MaxAllowedLevel;
            double width = MatrixCanvas.ActualWidth;
            double height = MatrixCanvas.ActualHeight;

            bool sizeChanged = Math.Abs(_lastDrawnWidth - width) > 0.01 ||
                               Math.Abs(_lastDrawnHeight - height) > 0.01 ||
                               _lastMaxLvl != maxLvl;

            if (sizeChanged)
            {
                _lastDrawnWidth = width;
                _lastDrawnHeight = height;
                _lastMaxLvl = maxLvl;
            }

            EnsureGridElements();
            EnsureAxisElements();
            DrawLevelAxis(sizeChanged);
            DrawMoodAxis(sizeChanged);

            double cellW = width / (double)maxLvl;
            double cellH = height / 15.0;

            // Update stage headers and column width ratios
            if (ColStageBaby != null && ColStageTeen != null && ColStageAdult != null)
            {
                if (vm.IsStockMode)
                {
                    ColStageBaby.Width = new GridLength(1, GridUnitType.Star);
                    ColStageTeen.Width = new GridLength(1, GridUnitType.Star);
                    ColStageAdult.Width = new GridLength(1, GridUnitType.Star);
                }
                else
                {
                    ColStageBaby.Width = new GridLength(9, GridUnitType.Star);
                    ColStageTeen.Width = new GridLength(10, GridUnitType.Star);
                    ColStageAdult.Width = new GridLength(11, GridUnitType.Star);
                }
            }

            if (TxtStageBaby != null && TxtStageTeen != null && TxtStageAdult != null)
            {
                if (vm.IsStockMode)
                {
                    TxtStageBaby.Text = "👶 Baby (L1)";
                    TxtStageTeen.Text = "👦 Teen (L2)";
                    TxtStageAdult.Text = "🐬 Adult (L3)";
                }
                else
                {
                    TxtStageBaby.Text = "👶 Baby (L1-9)";
                    TxtStageTeen.Text = "👦 Teen (L10-19)";
                    TxtStageAdult.Text = "🐬 Adult (L20-30)";
                }
            }

            if (TxtCoverageBadge != null)
            {
                double pct = vm.Matrix.CoveragePercentage;
                if (pct >= 100.0)
                {
                    TxtCoverageBadge.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Success");
                }
                else if (pct > 0)
                {
                    TxtCoverageBadge.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Warning");
                }
                else
                {
                    TxtCoverageBadge.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Status.Danger");
                }
            }

            var matrix = vm.Matrix;

            var brushGapFill = _cachedGapFillBrush;
            var brushGapStroke = _cachedGapStrokeBrush;
            var badgeTextBrush = _cachedBadgeTextBrush;

            double rectW = Math.Max(1, cellW - 1);
            double rectH = Math.Max(1, cellH - 1);
            double fontSize = Math.Max(7, Math.Min(9, cellH * 0.45));
            double countLabelTopOffset = Math.Max(0, (cellH - 12) / 2);

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    var rect = _cellRects[lvl, mood];
                    var countLabel = _cellCountLabels[lvl, mood];

                    if (lvl > maxLvl)
                    {
                        rect.Visibility = Visibility.Collapsed;
                        countLabel.Visibility = Visibility.Collapsed;
                        continue;
                    }

                    var cell = matrix.GetCell(lvl, mood);
                    bool isSelectedPresent = vm.SelectedEntry != null && cell.MatchingEntries.Exists(e => e.Name.Equals(vm.SelectedEntry.Name, StringComparison.OrdinalIgnoreCase));

                    double cellOpacity;
                    if (vm.SelectedEntry == null)
                    {
                        cellOpacity = 1.0;
                    }
                    else if (isSelectedPresent)
                    {
                        cellOpacity = 1.0;
                    }
                    else if (vm.IsSoloModeActive)
                    {
                        cellOpacity = 0.12;
                    }
                    else
                    {
                        cellOpacity = 0.45;
                    }

                    if (sizeChanged)
                    {
                        rect.Width = rectW;
                        rect.Height = rectH;
                        double left = (lvl - 1) * cellW;
                        double top = mood * cellH;
                        Canvas.SetLeft(rect, left);
                        Canvas.SetTop(rect, top);
                    }

                    rect.Opacity = cellOpacity;
                    rect.Visibility = Visibility.Visible;

                    if (!cell.HasCoverage)
                    {
                        rect.Fill = brushGapFill;
                        rect.Stroke = isSelectedPresent ? SelectedCellBorderBrush : brushGapStroke;
                        rect.StrokeThickness = isSelectedPresent ? 1.5 : 1.0;
                    }
                    else
                    {
                        if (cell.MatchingEntries.Count == 1)
                        {
                            rect.Fill = SoloBrush;
                        }
                        else if (cell.MatchingEntries.Count == 2)
                        {
                            rect.Fill = DuoBrush;
                        }
                        else if (cell.MatchingEntries.Count == 3)
                        {
                            rect.Fill = TrioBrush;
                        }
                        else
                        {
                            rect.Fill = ContendedBrush;
                        }

                        rect.Stroke = isSelectedPresent ? SelectedCellBorderBrush : CellBorderBrush;
                        rect.StrokeThickness = isSelectedPresent ? 1.5 : 0.5;
                    }

                    if (cell.MatchingEntries.Count >= 2)
                    {
                        if (rectW < 18)
                        {
                            countLabel.Text = isSelectedPresent ? "★" : (cell.MatchingEntries.Count >= 10 ? "9+" : $"{cell.MatchingEntries.Count}");
                            countLabel.FontSize = Math.Max(6, fontSize - 1);
                        }
                        else
                        {
                            countLabel.Text = isSelectedPresent
                                ? (cell.MatchingEntries.Count >= 10 ? $"★{cell.MatchingEntries.Count}" : $"★×{cell.MatchingEntries.Count}")
                                : (cell.MatchingEntries.Count >= 10 ? $"{cell.MatchingEntries.Count}" : $"×{cell.MatchingEntries.Count}");
                            countLabel.FontSize = fontSize;
                        }

                        countLabel.Foreground = isSelectedPresent ? SelectedBadgeTextBrush : badgeTextBrush;
                        countLabel.Opacity = cellOpacity;
                        countLabel.Visibility = Visibility.Visible;

                        if (sizeChanged)
                        {
                            countLabel.Width = rectW;
                            Canvas.SetLeft(countLabel, (lvl - 1) * cellW);
                            Canvas.SetTop(countLabel, mood * cellH + countLabelTopOffset);
                        }
                    }
                    else if (cell.MatchingEntries.Count == 1 && isSelectedPresent)
                    {
                        countLabel.Text = "★";
                        countLabel.Foreground = SelectedBadgeTextBrush;
                        countLabel.Opacity = cellOpacity;
                        countLabel.Visibility = Visibility.Visible;

                        if (sizeChanged)
                        {
                            countLabel.FontSize = fontSize;
                            countLabel.Width = rectW;
                            Canvas.SetLeft(countLabel, (lvl - 1) * cellW);
                            Canvas.SetTop(countLabel, mood * cellH + countLabelTopOffset);
                        }
                    }
                    else
                    {
                        countLabel.Visibility = Visibility.Collapsed;
                    }
                }
            }

            // Update overlay visuals and dividers in-place
            UpdateInteractiveOverlays();
        }

        public void UpdateInteractiveOverlays()
        {
            var vm = ViewModel;
            if (vm == null || MatrixCanvas == null || MatrixCanvas.ActualWidth < 10 || MatrixCanvas.ActualHeight < 10)
                return;

            int maxLvl = vm.MaxAllowedLevel;
            double width = MatrixCanvas.ActualWidth;
            double height = MatrixCanvas.ActualHeight;

            double cellW = width / (double)maxLvl;
            double cellH = height / 15.0;

            // 1. Dividers
            double div1 = vm.IsStockMode ? 1.0 * cellW : 9.0 * cellW;
            double div2 = vm.IsStockMode ? 2.0 * cellW : 19.0 * cellW;
            var vDividerBrush = _cachedVDividerBrush;
            _vLine1.X1 = div1; _vLine1.Y1 = 0; _vLine1.X2 = div1; _vLine1.Y2 = height;
            _vLine1.Stroke = vDividerBrush;
            _vLine2.X1 = div2; _vLine2.Y1 = 0; _vLine2.X2 = div2; _vLine2.Y2 = height;
            _vLine2.Stroke = vDividerBrush;

            double hdiv1 = 5.0 * cellH;
            double hdiv2 = 10.0 * cellH;
            var hDividerBrush = _cachedHDividerBrush;
            _hLine1.X1 = 0; _hLine1.Y1 = hdiv1; _hLine1.X2 = width; _hLine1.Y2 = hdiv1;
            _hLine1.Stroke = hDividerBrush;
            _hLine2.X1 = 0; _hLine2.Y1 = hdiv2; _hLine2.X2 = width; _hLine2.Y2 = hdiv2;
            _hLine2.Stroke = hDividerBrush;

            // 2. Hover Reticle & Rich Tooltip
            if (!_isDragging && _hoverLvl >= 1 && _hoverLvl <= maxLvl && _hoverMood >= 0 && _hoverMood <= 14)
            {
                var hoverStroke = _cachedHoverStrokeBrush;
                var hoverFill = _cachedHoverFillBrush;
                _hoverRect.Width = Math.Max(1, cellW - 1);
                _hoverRect.Height = Math.Max(1, cellH - 1);
                _hoverRect.Stroke = hoverStroke;
                _hoverRect.Fill = hoverFill;
                Canvas.SetLeft(_hoverRect, (_hoverLvl - 1) * cellW);
                Canvas.SetTop(_hoverRect, _hoverMood * cellH);

                var cell = vm.Matrix.GetCell(_hoverLvl, _hoverMood);
                bool isSelectedPresent = vm.SelectedEntry != null && cell.MatchingEntries.Exists(e => e.Name.Equals(vm.SelectedEntry.Name, StringComparison.OrdinalIgnoreCase));
                if (!cell.HasCoverage)
                {
                    _hoverRect.ToolTip = string.Create(CultureInfo.InvariantCulture, $"Level {_hoverLvl} • Mood {_hoverMood}\n⚠️ Deadzone Gap (0% Coverage)");
                }
                else if (isSelectedPresent && vm.SelectedEntry != null)
                {
                    double selProb = cell.GetProbability(vm.SelectedEntry.Name) * 100.0;
                    var otherEntries = cell.MatchingEntries.Where(e => !e.Name.Equals(vm.SelectedEntry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                    string othersText = otherEntries.Count > 0
                        ? $"\nOther contenders in this state:\n{string.Join('\n', otherEntries.Select(e => string.Create(CultureInfo.InvariantCulture, $"  • {e.Name} (Weight: {e.Weight}, Share: {cell.GetProbability(e.Name) * 100:F0}%)")))}"
                        : "\n(100% Dedicated Playback)";
                    _hoverRect.ToolTip = string.Create(CultureInfo.InvariantCulture, $"Level {_hoverLvl} • Mood {_hoverMood}\n🎯 SELECTED: {vm.SelectedEntry.Name} (Weight: {vm.SelectedEntry.Weight}, Share: {selProb:F0}%){othersText}");
                }
                else
                {
                    _hoverRect.ToolTip = string.Create(CultureInfo.InvariantCulture, $"Level {_hoverLvl} • Mood {_hoverMood}\n{string.Join('\n', cell.MatchingEntries.Select(e => $"• {e.Name} (Weight: {e.Weight}, Share: {cell.GetProbability(e.Name) * 100:F0}%)"))}");
                }

                _hoverRect.Visibility = Visibility.Visible;
            }
            else
            {
                _hoverRect.ToolTip = null;
                _hoverRect.Visibility = Visibility.Collapsed;
            }

            // 3. Drag Overlay, Region Overlay & Selection Overlay
            if (_isDragging)
            {
                int dMinL = Math.Min(_dragStartLvl, _dragCurrentLvl);
                int dMaxL = Math.Max(_dragStartLvl, _dragCurrentLvl);
                int dMinM = Math.Min(_dragStartMood, _dragCurrentMood);
                int dMaxM = Math.Max(_dragStartMood, _dragCurrentMood);

                double boxX = (dMinL - 1) * cellW;
                double boxY = dMinM * cellH;
                double boxW = Math.Max(1, (dMaxL - dMinL + 1) * cellW - 1);
                double boxH = Math.Max(1, (dMaxM - dMinM + 1) * cellH - 1);

                var dragStrokeBrush = _cachedDragStrokeBrush;
                var dragFillBrush = _cachedDragFillBrush;

                _dragOverlay.Width = boxW;
                _dragOverlay.Height = boxH;
                _dragOverlay.Stroke = dragStrokeBrush;
                _dragOverlay.Fill = dragFillBrush;
                Canvas.SetLeft(_dragOverlay, boxX);
                Canvas.SetTop(_dragOverlay, boxY);

                int cellCount = (dMaxL - dMinL + 1) * (dMaxM - dMinM + 1);
                if (vm.IsAssignDragMode && vm.SelectedEntry != null)
                {
                    _dragOverlay.ToolTip = string.Create(CultureInfo.InvariantCulture, $"🎯 Assigning '{vm.SelectedEntry.Name}' bounds\nLevel {dMinL}–{dMaxL} • Mood {dMinM}–{dMaxM} ({cellCount} cell{(cellCount == 1 ? "" : "s")})\n(Press Esc to cancel)");
                }
                else
                {
                    _dragOverlay.ToolTip = string.Create(CultureInfo.InvariantCulture, $"🔍 Inspect Region\nLevel {dMinL}–{dMaxL} • Mood {dMinM}–{dMaxM} ({cellCount} cell{(cellCount == 1 ? "" : "s")})\n(Release to inspect region, Esc to cancel)");
                }

                _dragOverlay.Visibility = Visibility.Visible;
                _selOverlay.Visibility = Visibility.Collapsed;
                _regionOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                _dragOverlay.Visibility = Visibility.Collapsed;

                if (vm.HasSelectedRegion)
                {
                    int rMinL = Math.Clamp(vm.SelectedRegionMinLevel, 1, maxLvl);
                    int rMaxL = Math.Clamp(vm.SelectedRegionMaxLevel, 1, maxLvl);
                    int rMinM = Math.Clamp(vm.SelectedRegionMinMood, 0, 14);
                    int rMaxM = Math.Clamp(vm.SelectedRegionMaxMood, 0, 14);

                    double rBoxX = (rMinL - 1) * cellW;
                    double rBoxY = rMinM * cellH;
                    double rBoxW = Math.Max(1, (rMaxL - rMinL + 1) * cellW - 1);
                    double rBoxH = Math.Max(1, (rMaxM - rMinM + 1) * cellH - 1);

                    _regionOverlay.Width = rBoxW;
                    _regionOverlay.Height = rBoxH;
                    _regionOverlay.Stroke = _cachedBabyBrush;
                    _regionOverlay.Fill = CreateFrozenBrush(Color.FromArgb(32, 0, 229, 255));
                    Canvas.SetLeft(_regionOverlay, rBoxX);
                    Canvas.SetTop(_regionOverlay, rBoxY);
                    _regionOverlay.ToolTip = vm.SelectedRegionSummaryText;
                    _regionOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    _regionOverlay.Visibility = Visibility.Collapsed;
                }

                var selected = vm.SelectedEntry;
                if (selected != null && selected.MinLevel <= maxLvl && selected.MaxLevel >= 1 && selected.MinButthurt <= 14 && selected.MaxButthurt >= 0)
                {
                    int selMinL = Math.Clamp(selected.MinLevel, 1, maxLvl);
                    int selMaxL = Math.Clamp(selected.MaxLevel, 1, maxLvl);
                    int selMinM = Math.Clamp(selected.MinButthurt, 0, 14);
                    int selMaxM = Math.Clamp(selected.MaxButthurt, 0, 14);

                    if (selMinL <= selMaxL && selMinM <= selMaxM)
                    {
                        double boxX = (selMinL - 1) * cellW;
                        double boxY = selMinM * cellH;
                        double boxW = Math.Max(1, (selMaxL - selMinL + 1) * cellW - 1);
                        double boxH = Math.Max(1, (selMaxM - selMinM + 1) * cellH - 1);

                        _selOverlay.Width = boxW;
                        _selOverlay.Height = boxH;
                        _selOverlay.Stroke = SelectedBoxBorderBrush;
                        _selOverlay.StrokeThickness = 2.0;
                        _selOverlay.StrokeDashArray = [6, 3];
                        _selOverlay.Fill = SelectedBoxFillBrush;
                        Canvas.SetLeft(_selOverlay, boxX);
                        Canvas.SetTop(_selOverlay, boxY);
                        _selOverlay.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _selOverlay.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    _selOverlay.Visibility = Visibility.Collapsed;
                }
            }

            // 4. Inspected Cell Target Reticle
            int inspectLvl = Math.Clamp(vm.SelectedCellLevel, 1, maxLvl);
            int inspectMood = Math.Clamp(vm.SelectedCellMood, 0, 14);
            var reticleStroke = _cachedReticleStrokeBrush;
            var reticleFill = _cachedReticleFillBrush;
            _inspectedRect.Width = Math.Max(1, cellW - 1);
            _inspectedRect.Height = Math.Max(1, cellH - 1);
            _inspectedRect.Stroke = reticleStroke;
            _inspectedRect.Fill = reticleFill;
            Canvas.SetLeft(_inspectedRect, (inspectLvl - 1) * cellW);
            Canvas.SetTop(_inspectedRect, inspectMood * cellH);
            _inspectedRect.Visibility = Visibility.Visible;
        }

        private void DrawLevelAxis(bool sizeChanged)
        {
            var vm = ViewModel;
            if (vm == null || LevelAxisCanvas == null || MatrixCanvas == null || MatrixCanvas.ActualWidth < 10)
                return;

            EnsureAxisElements();

            int maxLvl = vm.MaxAllowedLevel;
            double width = MatrixCanvas.ActualWidth;
            double cellW = width / (double)maxLvl;

            var babyBrush = _cachedBabyBrush;
            var teenBrush = _cachedTeenBrush;
            var adultBrush = _cachedAdultBrush;
            var primaryBrush = _cachedPrimaryTextBrush;

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                var tb = _levelAxisLabels[lvl];
                if (tb == null) continue;

                if (lvl > maxLvl)
                {
                    tb.Visibility = Visibility.Collapsed;
                    continue;
                }

                bool isInspected = vm.SelectedCellLevel == lvl;
                bool isHovered = _hoverLvl == lvl;

                Brush fgBrush;
                if (vm.IsStockMode)
                {
                    fgBrush = lvl switch
                    {
                        1 => babyBrush,
                        2 => teenBrush,
                        _ => adultBrush,
                    };
                }
                else
                {
                    fgBrush = lvl switch
                    {
                        <= 9 => babyBrush,
                        <= 19 => teenBrush,
                        _ => adultBrush,
                    };
                }

                tb.Text = vm.IsStockMode ? string.Create(CultureInfo.InvariantCulture, $"L{lvl}") : lvl.ToString(System.Globalization.CultureInfo.InvariantCulture);
                tb.FontSize = vm.IsStockMode ? 10 : (isInspected || isHovered ? 9.0 : 8.0);
                tb.FontWeight = isInspected || isHovered ? FontWeights.Bold : FontWeights.SemiBold;
                tb.Foreground = isInspected ? primaryBrush : fgBrush;
                tb.Opacity = isInspected || isHovered ? 1.0 : 0.85;
                tb.Visibility = Visibility.Visible;

                if (sizeChanged)
                {
                    tb.Width = Math.Max(1, cellW);
                    Canvas.SetLeft(tb, (lvl - 1) * cellW);
                    Canvas.SetTop(tb, 1);
                }
            }
        }

        private void DrawMoodAxis(bool sizeChanged)
        {
            var vm = ViewModel;
            if (vm == null || MoodAxisCanvas == null || MatrixCanvas == null || MatrixCanvas.ActualHeight < 10)
                return;

            EnsureAxisElements();

            double cellH = MatrixCanvas.ActualHeight / 15.0;

            var happyBrush = _cachedHappyBrush;
            var neutralBrush = _cachedNeutralBrush;
            var angryBrush = _cachedAngryBrush;
            var primaryBrush = _cachedPrimaryTextBrush;
            double moodAxisW = Math.Max(1, MoodAxisCanvas.ActualWidth > 0 ? MoodAxisCanvas.ActualWidth : 18);
            double topOffset = Math.Max(0, (cellH - 12) / 2);

            for (int mood = 0; mood <= 14; mood++)
            {
                var tb = _moodAxisLabels[mood];
                if (tb == null) continue;

                bool isInspected = vm.SelectedCellMood == mood;
                bool isHovered = _hoverMood == mood;

                Brush fgBrush = mood switch
                {
                    <= 4 => happyBrush,
                    <= 9 => neutralBrush,
                    _ => angryBrush,
                };

                tb.Text = mood.ToString(System.Globalization.CultureInfo.InvariantCulture);
                tb.FontSize = isInspected || isHovered ? 9.0 : 8.0;
                tb.FontWeight = isInspected || isHovered ? FontWeights.Bold : FontWeights.SemiBold;
                tb.Foreground = isInspected ? primaryBrush : fgBrush;
                tb.Opacity = isInspected || isHovered ? 1.0 : 0.85;
                tb.Visibility = Visibility.Visible;

                if (sizeChanged)
                {
                    tb.Width = moodAxisW;
                    Canvas.SetLeft(tb, 0);
                    Canvas.SetTop(tb, mood * cellH + topOffset);
                }
            }
        }

        private void MatrixCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null || MatrixCanvas.ActualWidth <= 0 || MatrixCanvas.ActualHeight <= 0) return;

            var pos = e.GetPosition(MatrixCanvas);
            int maxLvl = vm.MaxAllowedLevel;
            double cellW = MatrixCanvas.ActualWidth / (double)maxLvl;
            double cellH = MatrixCanvas.ActualHeight / 15.0;

            int lvl = Math.Clamp((int)Math.Floor(pos.X / cellW) + 1, 1, maxLvl);
            int mood = Math.Clamp((int)Math.Floor(pos.Y / cellH), 0, 14);

            if (_isDragging)
            {
                if (lvl != _dragCurrentLvl || mood != _dragCurrentMood)
                {
                    _dragCurrentLvl = lvl;
                    _dragCurrentMood = mood;
                    int minL = Math.Min(_dragStartLvl, _dragCurrentLvl);
                    int maxL = Math.Max(_dragStartLvl, _dragCurrentLvl);
                    int minM = Math.Min(_dragStartMood, _dragCurrentMood);
                    int maxM = Math.Max(_dragStartMood, _dragCurrentMood);
                    vm.UpdateDragSelectionTelemetry(minL, maxL, minM, maxM);
                    UpdateInteractiveOverlays();
                }
            }
            else
            {
                if (lvl != _hoverLvl || mood != _hoverMood)
                {
                    _hoverLvl = lvl;
                    _hoverMood = mood;
                    vm.HoverCell(lvl, mood);
                    UpdateInteractiveOverlays();
                }
            }
        }

        private void MatrixCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                _hoverLvl = -1;
                _hoverMood = -1;
                ViewModel?.ClearHover();
                UpdateInteractiveOverlays();
            }
        }

        private void MatrixCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null || MatrixCanvas.ActualWidth <= 0 || MatrixCanvas.ActualHeight <= 0) return;

            MatrixCanvas.Focus();

            var pos = e.GetPosition(MatrixCanvas);
            int maxLvl = vm.MaxAllowedLevel;
            double cellW = MatrixCanvas.ActualWidth / (double)maxLvl;
            double cellH = MatrixCanvas.ActualHeight / 15.0;

            int lvl = Math.Clamp((int)Math.Floor(pos.X / cellW) + 1, 1, maxLvl);
            int mood = Math.Clamp((int)Math.Floor(pos.Y / cellH), 0, 14);

            if (e.ClickCount >= 2)
            {
                var cell = vm.Matrix.GetCell(lvl, mood);
                if (cell.HasCoverage && cell.MatchingEntries.Count > 0)
                {
                    var firstMatch = cell.MatchingEntries[0];
                    vm.NavigateToEntryByName(firstMatch.Name);
                }
                else
                {
                    vm.InspectCell(lvl, mood);
                    vm.CreateTabForGap();
                }
                e.Handled = true;
                return;
            }

            MatrixCanvas.CaptureMouse();

            _isDragging = true;
            _dragStartPoint = pos;
            _dragStartLvl = lvl;
            _dragStartMood = mood;
            _dragCurrentLvl = lvl;
            _dragCurrentMood = mood;

            if (vm.HasSelectedRegion)
            {
                vm.ClearRegionSelection();
            }

            vm.InspectCell(lvl, mood);
            UpdateInteractiveOverlays();
        }

        private void MatrixCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                MatrixCanvas.ReleaseMouseCapture();

                int minL = Math.Min(_dragStartLvl, _dragCurrentLvl);
                int maxL = Math.Max(_dragStartLvl, _dragCurrentLvl);
                int minM = Math.Min(_dragStartMood, _dragCurrentMood);
                int maxM = Math.Max(_dragStartMood, _dragCurrentMood);

                var vm = ViewModel;
                bool hasDragged = (_dragStartLvl != _dragCurrentLvl || _dragStartMood != _dragCurrentMood);
                if (hasDragged && vm != null)
                {
                    if (vm.IsAssignDragMode && vm.SelectedEntry != null)
                    {
                        vm.SetSelectedEntryBounds(minL, maxL, minM, maxM);
                    }
                    else
                    {
                        vm.SelectRegion(minL, maxL, minM, maxM);
                    }
                }

                if (vm != null)
                {
                    vm.HoverCell(_dragCurrentLvl, _dragCurrentMood);
                }

                RedrawMatrix();
            }
        }

        private void MatrixCanvas_LostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                RedrawMatrix();
            }
        }

        private void MatrixCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null || MatrixCanvas.ActualWidth <= 0 || MatrixCanvas.ActualHeight <= 0) return;

            var pos = e.GetPosition(MatrixCanvas);
            int maxLvl = vm.MaxAllowedLevel;
            double cellW = MatrixCanvas.ActualWidth / (double)maxLvl;
            double cellH = MatrixCanvas.ActualHeight / 15.0;

            int lvl = Math.Clamp((int)Math.Floor(pos.X / cellW) + 1, 1, maxLvl);
            int mood = Math.Clamp((int)Math.Floor(pos.Y / cellH), 0, 14);

            vm.InspectCell(lvl, mood);
            RedrawMatrix();
        }

        private void ListEntries_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var vm = ViewModel;
            if (vm?.SelectedEntry != null)
            {
                vm.NavigateToEntryTab(vm.SelectedEntry);
            }
        }

        private void SelectedCellProbability_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FlipperCellProbability prob)
            {
                var vm = ViewModel;
                if (vm != null)
                {
                    var match = vm.Entries.FirstOrDefault(entry => entry.Name.Equals(prob.Name, StringComparison.OrdinalIgnoreCase));
                    if (match != null)
                    {
                        vm.SelectedEntry = match;
                    }

                    if (e.ClickCount >= 2)
                    {
                        vm.NavigateToEntryByName(prob.Name);
                        e.Handled = true;
                    }
                }
            }
        }

        private void MatrixCanvas_KeyDown(object sender, KeyEventArgs e)
        {
            var vm = ViewModel;
            if (vm == null) return;

            if (e.Key == Key.Escape)
            {
                if (_isDragging)
                {
                    _isDragging = false;
                    MatrixCanvas.ReleaseMouseCapture();
                    vm.ClearRegionSelection();
                    RedrawMatrix();
                    e.Handled = true;
                    return;
                }

                if (vm.HasSelectedRegion)
                {
                    vm.ClearRegionSelection();
                    e.Handled = true;
                    return;
                }

                if (vm.SelectedEntry != null)
                {
                    vm.DeselectEntry();
                    e.Handled = true;
                    return;
                }
            }

            int maxLvl = vm.MaxAllowedLevel;
            int curLvl = vm.SelectedCellLevel;
            int curMood = vm.SelectedCellMood;
            bool handled = true;

            bool isShift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
            bool isCtrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            if (isShift)
            {
                if (vm.SelectedEntry != null)
                {
                    switch (e.Key)
                    {
                        case Key.Right:
                            if (isCtrl) vm.ShrinkSelectedEntryMinLevel();
                            else vm.ExpandSelectedEntryMaxLevel();
                            break;
                        case Key.Left:
                            if (isCtrl) vm.ExpandSelectedEntryMinLevel();
                            else vm.ShrinkSelectedEntryMaxLevel();
                            break;
                        case Key.Down:
                            if (isCtrl) vm.ShrinkSelectedEntryMinMood();
                            else vm.ExpandSelectedEntryMaxMood();
                            break;
                        case Key.Up:
                            if (isCtrl) vm.ExpandSelectedEntryMinMood();
                            else vm.ShrinkSelectedEntryMaxMood();
                            break;
                        default:
                            handled = false;
                            break;
                    }
                }
                else
                {
                    handled = false;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Key.Left:
                        vm.InspectCell(Math.Max(1, curLvl - 1), curMood);
                        break;
                    case Key.Right:
                        vm.InspectCell(Math.Min(maxLvl, curLvl + 1), curMood);
                        break;
                    case Key.Up:
                        vm.InspectCell(curLvl, Math.Max(0, curMood - 1));
                        break;
                    case Key.Down:
                        vm.InspectCell(curLvl, Math.Min(14, curMood + 1));
                        break;
                    case Key.Home:
                        vm.InspectCell(1, curMood);
                        break;
                    case Key.End:
                        vm.InspectCell(maxLvl, curMood);
                        break;
                    case Key.PageUp:
                        vm.InspectCell(curLvl, 0);
                        break;
                    case Key.PageDown:
                        vm.InspectCell(curLvl, 14);
                        break;
                    case Key.Space:
                    case Key.Enter:
                        var cell = vm.Matrix.GetCell(curLvl, curMood);
                        if (cell.HasCoverage && cell.MatchingEntries.Count > 0)
                        {
                            var firstMatch = vm.Entries.FirstOrDefault(entry => entry.Name.Equals(cell.MatchingEntries[0].Name, StringComparison.OrdinalIgnoreCase));
                            if (firstMatch != null)
                            {
                                vm.SelectedEntry = firstMatch;
                            }
                        }
                        break;
                    default:
                        handled = false;
                        break;
                }
            }

            if (handled)
            {
                e.Handled = true;
            }
        }

        private void ListBoxItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item)
            {
                item.IsSelected = true;
                item.Focus();
            }
            else if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry)
            {
                if (ViewModel != null)
                {
                    ViewModel.SelectedEntry = entry;
                }
            }
        }

        private void DataTableRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var parent = dep;
                while (parent != null && parent != sender)
                {
                    if (parent is TextBox || parent is Button || parent is CheckBox || parent is ComboBox)
                    {
                        return;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
            }

            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry)
            {
                if (ViewModel != null)
                {
                    ViewModel.SelectedEntry = entry;
                }
            }
        }

        private void DataTableRow_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry)
                {
                    ViewModel?.NavigateToEntryTab(entry);
                    e.Handled = true;
                }
            }
        }

        private void NameTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox tb)
                {
                    var binding = tb.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                    tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape)
            {
                if (sender is TextBox tb && tb.DataContext is FlipperScheduleEntryViewModel entry)
                {
                    tb.Text = entry.PreviousName.Length > 0 ? entry.PreviousName : entry.Name;
                    tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
            }
        }

        private void NumericTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is TextBox tb && int.TryParse(tb.Text, out int val))
            {
                int delta = e.Delta > 0 ? 1 : -1;
                int newVal = val + delta;

                var binding = tb.GetBindingExpression(TextBox.TextProperty);
                string propName = binding?.ParentBinding?.Path?.Path ?? string.Empty;

                if (propName.Contains("Level", StringComparison.OrdinalIgnoreCase))
                {
                    int maxL = ViewModel?.MaxAllowedLevel ?? 30;
                    newVal = Math.Clamp(newVal, 1, maxL);
                }
                else if (propName.Contains("Butthurt", StringComparison.OrdinalIgnoreCase) || propName.Contains("Mood", StringComparison.OrdinalIgnoreCase))
                {
                    newVal = Math.Clamp(newVal, 0, 14);
                }
                else if (propName.Contains("Weight", StringComparison.OrdinalIgnoreCase))
                {
                    newVal = Math.Clamp(newVal, 1, 100);
                }

                tb.Text = newVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                binding?.UpdateSource();
                e.Handled = true;
            }
        }

        private void NumericTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Up || e.Key == Key.Down)
            {
                if (sender is TextBox tb && int.TryParse(tb.Text, out int val))
                {
                    int delta = e.Key == Key.Up ? 1 : -1;
                    int newVal = val + delta;

                    var binding = tb.GetBindingExpression(TextBox.TextProperty);
                    string propName = binding?.ParentBinding?.Path?.Path ?? string.Empty;

                    if (propName.Contains("Level", StringComparison.OrdinalIgnoreCase))
                    {
                        int maxL = ViewModel?.MaxAllowedLevel ?? 30;
                        newVal = Math.Clamp(newVal, 1, maxL);
                    }
                    else if (propName.Contains("Butthurt", StringComparison.OrdinalIgnoreCase) || propName.Contains("Mood", StringComparison.OrdinalIgnoreCase))
                    {
                        newVal = Math.Clamp(newVal, 0, 14);
                    }
                    else if (propName.Contains("Weight", StringComparison.OrdinalIgnoreCase))
                    {
                        newVal = Math.Clamp(newVal, 1, 100);
                    }

                    tb.Text = newVal.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    binding?.UpdateSource();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (sender is TextBox tb)
                {
                    var binding = tb.GetBindingExpression(TextBox.TextProperty);
                    binding?.UpdateSource();
                    tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                    e.Handled = true;
                }
            }
        }

        private void DataTableRow_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry && ViewModel != null)
            {
                if (e.Key == Key.Space)
                {
                    entry.IsSelected = !entry.IsSelected;
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    ViewModel.DeleteEntryCommand.Execute(entry);
                    e.Handled = true;
                }
            }
        }

        private void StageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void MoodButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.ContextMenu != null)
            {
                btn.ContextMenu.PlacementTarget = btn;
                btn.ContextMenu.IsOpen = true;
            }
        }

        private void StageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string stage && mi.DataContext is FlipperScheduleEntryViewModel entry && ViewModel != null)
            {
                entry.SetStage(stage, ViewModel.IsStockMode);
                ViewModel.RecalculateMatrix();
            }
        }

        private void MoodMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string mood && mi.DataContext is FlipperScheduleEntryViewModel entry && ViewModel != null)
            {
                entry.SetMoodPreset(mood);
                ViewModel.RecalculateMatrix();
            }
        }

        private void GalleryCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry && ViewModel != null)
            {
                if (e.OriginalSource is DependencyObject dep)
                {
                    var parentButton = FindParent<Button>(dep);
                    var parentCheckBox = FindParent<CheckBox>(dep);
                    var parentTextBox = FindParent<TextBox>(dep);
                    if (parentButton != null || parentCheckBox != null || parentTextBox != null)
                    {
                        return;
                    }
                }

                if (e.ClickCount == 2)
                {
                    ViewModel.NavigateToEntryTabCommand.Execute(entry);
                    e.Handled = true;
                    return;
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    entry.IsSelected = !entry.IsSelected;
                }
                else
                {
                    ViewModel.SelectedEntry = entry;
                }
            }
        }

        private void GalleryCard_KeyDown(object sender, KeyEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry && ViewModel != null)
            {
                if (e.Key == Key.Space)
                {
                    entry.IsSelected = !entry.IsSelected;
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    ViewModel.NavigateToEntryTabCommand.Execute(entry);
                    e.Handled = true;
                }
                else if (e.Key == Key.Delete)
                {
                    ViewModel.DeleteEntryCommand.Execute(entry);
                    e.Handled = true;
                }
            }
        }

        private void GalleryCard_MouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry)
            {
                entry.IsPreviewPlaying = true;
            }
        }

        private void GalleryCard_MouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is FlipperScheduleEntryViewModel entry)
            {
                entry.ResetPreviewFrame();
            }
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not T)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private bool _isDraggingEmbeddedBubble;

        private void ImgEmbeddedFlipperScreen_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel != null && ViewModel.IsDeviceSimulatorView && ViewModel.SimulatorViewModel.ShowSpeechBubble && sender is Image img)
            {
                _isDraggingEmbeddedBubble = true;
                img.CaptureMouse();
                var pos = e.GetPosition(img);
                ViewModel.SimulatorViewModel.UpdateBubblePositionFromPoint(pos.X, pos.Y, img.ActualWidth, img.ActualHeight);
            }
        }

        private void ImgEmbeddedFlipperScreen_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingEmbeddedBubble && ViewModel != null && sender is Image img)
            {
                var pos = e.GetPosition(img);
                ViewModel.SimulatorViewModel.UpdateBubblePositionFromPoint(pos.X, pos.Y, img.ActualWidth, img.ActualHeight);
            }
        }

        private void ImgEmbeddedFlipperScreen_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingEmbeddedBubble && sender is Image img)
            {
                _isDraggingEmbeddedBubble = false;
                img.ReleaseMouseCapture();
            }
        }

        private void EmbeddedSpeedChip_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.IsChecked == true && ViewModel != null && ViewModel.IsDeviceSimulatorView)
            {
                double mult = rb.Content?.ToString() switch
                {
                    "0.5x" => 0.5,
                    "2x" => 2.0,
                    _ => 1.0
                };
                ViewModel.SimulatorViewModel.SetSpeedMultiplier(mult);
            }
        }
    }
}
