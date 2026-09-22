using System;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public class SelectionHardeningTests
{
    public SelectionHardeningTests()
    {
        WpfTestHelper.EnsureApplication();
    }

    private static MainViewModel CreateTestViewModel()
    {
        var codeGenMock = new Mock<ICodeGeneratorService>();
        var drawingMock = new Mock<IDrawingService>();
        var clipboardMock = new Mock<IClipboardService>();
        var pixelClipboardService = new PixelClipboardService();
        var dialogMock = new Mock<IDialogService>();
        var themeMock = new Mock<IThemeService>();
        var bugReportMock = new Mock<IBugReportService>();
        var feedbackMock = new Mock<IUserFeedbackService>();
        var controllerFactory = new ControllerFactory();
        var exportMock = new Mock<IExportService>();
        var importExportMock = new Mock<IFileImportExportService>();
        var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
        var autosaveMock = new Mock<IAutosaveService>();
        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

        var shell = new ShellViewModel(
            codeGenMock.Object,
            drawingMock.Object,
            clipboardMock.Object,
            pixelClipboardService,
            dialogMock.Object,
            themeMock.Object,
            bugReportMock.Object,
            feedbackMock.Object,
            controllerFactory,
            exportMock.Object,
            importExportMock.Object,
            hardwarePreviewMock.Object,
            serviceProviderMock.Object);

        shell.NewDocumentCommand.Execute("64x64");
        return (MainViewModel)shell.ActiveDocument!;
    }

    // ── 1. Exact Orthogonal Rotation ────────────────────────────────────────

    [Fact]
    public void RotatePixels2D_0And360Degrees_ReturnsExactCloneWithoutDistortion()
    {
        var src = new bool[3, 4];
        src[0, 0] = true;
        src[1, 2] = true;
        src[2, 3] = true;

        var (dst0, w0, h0) = SelectionService.RotatePixels2D(src, 3, 4, 0);
        Assert.Equal(3, w0);
        Assert.Equal(4, h0);
        Assert.False(ReferenceEquals(src, dst0));
        Assert.True(dst0[0, 0]);
        Assert.True(dst0[1, 2]);
        Assert.True(dst0[2, 3]);
        Assert.False(dst0[0, 1]);

        var (dst360, w360, h360) = SelectionService.RotatePixels2D(src, 3, 4, 360);
        Assert.Equal(3, w360);
        Assert.Equal(4, h360);
        Assert.True(dst360[0, 0]);
        Assert.True(dst360[1, 2]);
        Assert.True(dst360[2, 3]);
    }

    [Fact]
    public void RotatePixels2D_90Degrees_TransposesDimensionsAndRotatesClockwise()
    {
        // 3 wide x 2 high grid
        // Row 0: [T, F, F]
        // Row 1: [F, F, T]
        var src = new bool[3, 2];
        src[0, 0] = true;
        src[2, 1] = true;

        var (dst90, w, h) = SelectionService.RotatePixels2D(src, 3, 2, 90);
        // After 90 deg clockwise rotation, dimensions should be 2 wide x 3 high
        Assert.Equal(2, w);
        Assert.Equal(3, h);

        // (0,0) moves to (sh - 1 - y, x) = (2 - 1 - 0, 0) = (1, 0)
        Assert.True(dst90[1, 0]);
        // (2,1) moves to (sh - 1 - y, x) = (2 - 1 - 1, 2) = (0, 2)
        Assert.True(dst90[0, 2]);
        // Other pixels remain false
        Assert.False(dst90[0, 0]);
        Assert.False(dst90[1, 1]);
    }

    [Fact]
    public void RotatePixels2D_180Degrees_InvertsCoordinatesExactly()
    {
        var src = new bool[3, 2];
        src[0, 0] = true;
        src[2, 1] = true;

        var (dst180, w, h) = SelectionService.RotatePixels2D(src, 3, 2, 180);
        Assert.Equal(3, w);
        Assert.Equal(2, h);

        // (0,0) -> (3-1-0, 2-1-0) = (2, 1)
        Assert.True(dst180[2, 1]);
        // (2,1) -> (3-1-2, 2-1-1) = (0, 0)
        Assert.True(dst180[0, 0]);
    }

    [Fact]
    public void RotatePixels2D_270Degrees_TransposesDimensionsCounterClockwise()
    {
        var src = new bool[3, 2];
        src[0, 0] = true;
        src[2, 1] = true;

        var (dst270, w, h) = SelectionService.RotatePixels2D(src, 3, 2, 270);
        Assert.Equal(2, w);
        Assert.Equal(3, h);

        // (0,0) -> (y, sw - 1 - x) = (0, 3-1-0) = (0, 2)
        Assert.True(dst270[0, 2]);
        // (2,1) -> (y, sw - 1 - x) = (1, 3-1-2) = (1, 0)
        Assert.True(dst270[1, 0]);
    }

    [Fact]
    public void RotatePixels2D_Four90DegreeRotations_RestoresOriginalPattern()
    {
        var original = new bool[4, 5];
        original[0, 1] = true;
        original[2, 2] = true;
        original[3, 4] = true;

        var (r1, w1, h1) = SelectionService.RotatePixels2D(original, 4, 5, 90);
        var (r2, w2, h2) = SelectionService.RotatePixels2D(r1, w1, h1, 90);
        var (r3, w3, h3) = SelectionService.RotatePixels2D(r2, w2, h2, 90);
        var (r4, w4, h4) = SelectionService.RotatePixels2D(r3, w3, h3, 90);

        Assert.Equal(4, w4);
        Assert.Equal(5, h4);
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(original[x, y], r4[x, y]);
            }
        }
    }

    // ── 2. 1:1 Resampling Fast Path ────────────────────────────────────────

    [Fact]
    public void ResamplePixels_SameDimensions_ReturnsIdenticalCloneWithoutDegradation()
    {
        var src = new bool[5, 5];
        src[0, 0] = true;
        src[2, 2] = true;
        src[4, 4] = true;

        var dst = SelectionService.ResamplePixels(src, 5, 5, 5, 5);

        Assert.False(ReferenceEquals(src, dst));
        Assert.Equal(5, dst.GetLength(0));
        Assert.Equal(5, dst.GetLength(1));
        for (int y = 0; y < 5; y++)
        {
            for (int x = 0; x < 5; x++)
            {
                Assert.Equal(src[x, y], dst[x, y]);
            }
        }
    }

    // ── 3. Safety Clamping (4096 Max Dimension) ────────────────────────────

    [Fact]
    public void ResamplePixels_ExtremeDimensions_ClampedTo4096()
    {
        var src = new bool[2, 2];
        src[0, 0] = true;

        // Attempting massive dimension that would cause OOM if not clamped
        var dst = SelectionService.ResamplePixels(src, 2, 2, 100_000, 100_000);

        Assert.NotNull(dst);
        Assert.Equal(4096, dst.GetLength(0));
        Assert.Equal(4096, dst.GetLength(1));
    }

    [Fact]
    public void UpdateTransform_ExtremeDimensions_ClampedTo4096()
    {
        var svc = new SelectionService();
        var state = new SpriteState(16, 16);
        state.Pixels[0] = true;

        svc.BeginRectangleSelection(0, 0);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();
        svc.LiftSelection(state);

        svc.BeginTransform(TransformHandle.SE);
        svc.UpdateTransform(0, 0, 50_000, 50_000, flipX: false, flipY: false);

        Assert.Equal(4096, svc.FloatingWidth);
        Assert.Equal(4096, svc.FloatingHeight);
    }

    // ── 4. Zoom-Independent Handle Hit-Testing ──────────────────────────────

    [Fact]
    public void HitTestHandle_AtHighZoom_DetectsNWHandleAccurately()
    {
        var vm = CreateTestViewModel();
        var svc = (SelectionService)vm.SelectionService;

        // Selection at pixel (10, 10) with size (10, 10)
        svc.BeginRectangleSelection(10, 10);
        svc.UpdateRectangleSelection(19, 19);
        svc.FinalizeSelection();
        svc.LiftSelection(vm.SpriteState);
        svc.BeginTransform(TransformHandle.None);

        // High zoom: canvas 64x64 displayed at 1024x1024 (16 pixels per canvas cell)
        double actualW = 1024;
        double actualH = 1024;
        // The NW handle visual center is at pixel (10, 10) -> (160px, 160px) in image space
        double mouseX = 160;
        double mouseY = 160;

        var handle = vm.HitTestSelectionHandle(mouseX, mouseY, actualW, actualH);
        Assert.Equal(TransformHandle.NW, handle);
    }

    [Fact]
    public void HitTestHandle_AtLowZoom_DetectsNWHandleAccurately()
    {
        var vm = CreateTestViewModel();
        var svc = (SelectionService)vm.SelectionService;

        // Selection at pixel (10, 10) with size (10, 10)
        svc.BeginRectangleSelection(10, 10);
        svc.UpdateRectangleSelection(19, 19);
        svc.FinalizeSelection();
        svc.LiftSelection(vm.SpriteState);
        svc.BeginTransform(TransformHandle.None);

        // Low zoom: canvas 64x64 displayed at 32x32 (0.5 pixels per canvas cell)
        double actualW = 32;
        double actualH = 32;
        // NW handle visual center is at pixel (10, 10) -> (5px, 5px) in image space
        double mouseX = 5.0;
        double mouseY = 5.0;

        var handle = vm.HitTestSelectionHandle(mouseX, mouseY, actualW, actualH);
        Assert.Equal(TransformHandle.NW, handle);
    }

    // ── 5. Stationary Click & Modifier Drag Restoration ────────────────────

    [Fact]
    public void CancelSelectionDrag_AddMode_RestoresBaseSelection()
    {
        var svc = new SelectionService();
        // Create base selection at (0, 0) - (3, 3)
        svc.BeginRectangleSelection(0, 0);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(3, svc.MaxX);

        // Start an Add operation elsewhere, then cancel the drag
        svc.BeginRectangleSelection(10, 10, SelectionMode.Add);
        svc.UpdateRectangleSelection(15, 15);
        Assert.True(svc.IsSelecting);

        svc.CancelSelectionDrag();

        Assert.False(svc.IsSelecting);
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(3, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(3, svc.MaxY);
    }

    [Fact]
    public void CancelSelectionDrag_SubtractMode_RestoresBaseSelection()
    {
        var svc = new SelectionService();
        // Create base selection at (0, 0) - (5, 5)
        svc.BeginRectangleSelection(0, 0);
        svc.UpdateRectangleSelection(5, 5);
        svc.FinalizeSelection();

        // Start a Subtract operation, then cancel
        svc.BeginRectangleSelection(2, 2, SelectionMode.Subtract);
        svc.UpdateRectangleSelection(4, 4);

        svc.CancelSelectionDrag();

        Assert.False(svc.IsSelecting);
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(5, svc.MaxX);
    }

    [Fact]
    public void CancelSelectionDrag_ReplaceMode_CancelsCompletely()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(5, 5);

        svc.CancelSelectionDrag();

        Assert.False(svc.IsSelecting);
        Assert.False(svc.HasActiveSelection);
    }

    [Fact]
    public void SelectionInputController_StationaryClickWithShift_PreservesBaseSelection()
    {
        var vm = CreateTestViewModel();
        var drawingMock = new Mock<IDrawingService>();
        var svc = new SelectionService();
        var controller = new SelectionInputController(vm, svc, drawingMock.Object);

        // Set up base selection
        svc.BeginRectangleSelection(0, 0);
        svc.UpdateRectangleSelection(4, 4);
        svc.FinalizeSelection();
        Assert.True(svc.HasActiveSelection);

        // Stationary click with Shift (Down then immediately Up at the same point without moving)
        controller.ProcessInput(10, 10, ToolAction.Down, isShiftDown: true, isAltDown: false);
        controller.ProcessInput(10, 10, ToolAction.Up, isShiftDown: true, isAltDown: false);

        // The base selection must remain active and preserved
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(4, svc.MaxX);
    }

    // ── 6. IsPixelInSelection Robustness & Rotated Pixel Hit-Testing ────────

    [Fact]
    public void IsPixelInSelection_OutOfBounds_DoesNotThrowAndReturnsFalse()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(5, 5);
        svc.UpdateRectangleSelection(10, 10);
        svc.FinalizeSelection();

        Assert.False(svc.IsPixelInSelection(-100, -100));
        Assert.False(svc.IsPixelInSelection(1000, 1000));
        Assert.False(svc.IsPixelInSelection(int.MinValue, int.MaxValue));
        Assert.True(svc.IsPixelInSelection(7, 7));
    }

    [Fact]
    public void IsPixelInSelection_RotatedFloatingSelection_HitTestsRotatedPixels()
    {
        var svc = new SelectionService();
        var state = new SpriteState(16, 16);
        // Put a single pixel at (5, 5)
        state.Pixels[5 * 16 + 5] = true;

        svc.BeginRectangleSelection(5, 5);
        svc.UpdateRectangleSelection(7, 7); // 3x3 selection from (5,5) to (7,7)
        svc.FinalizeSelection();
        svc.LiftSelection(state);

        // Before rotation, (5, 5) is selected
        Assert.True(svc.IsPixelInSelection(5, 5));
        Assert.False(svc.IsPixelInSelection(7, 5));

        // Rotate 90 degrees clockwise
        svc.BeginTransform(TransformHandle.Rotate);
        svc.UpdateRotation(90.0);

        // With center at (6, 6), rotating (5, 5) by 90 deg maps it to (7, 5)
        Assert.True(svc.IsPixelInSelection(7, 5));
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    // ── 7. Lasso Points Lifecycle Cleanup ──────────────────────────────────

    [Fact]
    public void LassoPoints_ClearedAfterFinalize()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(5, 0);
        svc.AddLassoPoint(5, 5);
        svc.AddLassoPoint(0, 5);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        // Start a rectangle selection next: lasso vertices should NOT bleed into it
        svc.BeginRectangleSelection(10, 10);
        svc.UpdateRectangleSelection(12, 12);
        svc.FinalizeSelection();

        Assert.Equal(10, svc.MinX);
        Assert.Equal(12, svc.MaxX);
        Assert.Null(svc.Mask); // Pure rectangle marquee should have null mask
    }

    [Fact]
    public void LassoPoints_ClearedAfterApplyMask()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(5, 5);

        var mask = new bool[3, 3];
        mask[1, 1] = true;
        svc.ApplyMask(mask, 10, 10, 12, 12, SelectionMode.Replace);

        Assert.True(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
    }

    // ── 8. Public API Defensive Null & Negative Guards ─────────────────────

    [Fact]
    public void PublicMethods_NullOrNegativeParameters_DoNotThrow()
    {
        var svc = new SelectionService();

        // Null sprite state guards
        svc.LiftSelection(null!);
        svc.CommitSelection(null!);
        svc.DeleteSelection(null!);

        // Null clipboard data guards
        svc.PasteAsFloating(null!, 16, 16);
        svc.PasteAsFloating(new PixelClipboardData(null!, 0, 0), 16, 16);
        svc.PasteAsFloatingAt(null!, 0, 0);
        svc.PasteAsFloatingAt(new PixelClipboardData(new bool[1, 1], -1, -1), 0, 0);

        // ReplaceFloatingData invalid parameters
        svc.ReplaceFloatingData(null!, 0, 0, 10, 10);
        svc.ReplaceFloatingData(new bool[2, 2], 0, 0, -5, -5);

        // RestoreSnapshot null guard
        svc.RestoreSnapshot(null!);

        // Controller reset
        var vm = CreateTestViewModel();
        var drawingMock = new Mock<IDrawingService>();
        var controller = new SelectionInputController(vm, svc, drawingMock.Object);
        controller.ResetControllerState();
    }

    // ── 9. Robust Edge Cases and Phase 1-3 Hardening ─────────────────────

    [Fact]
    public void RotatePixels2D_NaNOrInfinity_DefaultsToZeroDegrees()
    {
        var src = new bool[3, 3];
        src[0, 0] = true;

        var (dstNaN, wNaN, hNaN) = SelectionService.RotatePixels2D(src, 3, 3, double.NaN);
        Assert.Equal(3, wNaN);
        Assert.Equal(3, hNaN);
        Assert.True(dstNaN[0, 0]);

        var (dstInf, wInf, hInf) = SelectionService.RotatePixels2D(src, 3, 3, double.PositiveInfinity);
        Assert.Equal(3, wInf);
        Assert.Equal(3, hInf);
        Assert.True(dstInf[0, 0]);
    }

    [Fact]
    public void CopySelection_ZeroOrNegativeDimensions_ReturnsNull()
    {
        var svc = new SelectionService();
        var emptyState = new SpriteState(0, 0);
        Assert.Null(svc.CopySelection(emptyState));
    }

    [Fact]
    public void DeleteSelection_ZeroOrNegativeDimensions_DoesNotThrow()
    {
        var svc = new SelectionService();
        var emptyState = new SpriteState(0, 0);
        svc.DeleteSelection(emptyState);
    }

    [Fact]
    public void UpdateTransform_WithMaskAndFlipping_CorrectlyMirrorsMask()
    {
        var svc = new SelectionService();
        var mask = new bool[4, 4];
        mask[0, 0] = true; // Top-left
        svc.ApplyMask(mask, 0, 0, 3, 3, SelectionMode.Replace);

        var state = new SpriteState(10, 10);
        state.Pixels[0] = true;
        svc.LiftSelection(state);

        svc.BeginTransform(TransformHandle.SE);
        svc.UpdateTransform(0, 0, 4, 4, flipX: true, flipY: false);

        Assert.NotNull(svc.Mask);
        Assert.Equal(4, svc.Mask.GetLength(0));
        Assert.Equal(4, svc.Mask.GetLength(1));
        // Top-left (0,0) mirrored in X becomes top-right (3,0)
        Assert.True(svc.Mask[3, 0]);
        Assert.False(svc.Mask[0, 0]);

        svc.CommitTransform();
        Assert.NotNull(svc.Mask);
        Assert.True(svc.Mask[3, 0]);
    }

    [Fact]
    public void CancelTransform_RestoresOriginalMask()
    {
        var svc = new SelectionService();
        var mask = new bool[4, 4];
        mask[0, 0] = true;
        svc.ApplyMask(mask, 0, 0, 3, 3, SelectionMode.Replace);

        var state = new SpriteState(10, 10);
        state.Pixels[0] = true;
        svc.LiftSelection(state);

        svc.BeginTransform(TransformHandle.SE);
        svc.UpdateTransform(0, 0, 8, 8, flipX: true, flipY: false);
        Assert.Equal(8, svc.Mask!.GetLength(0));

        svc.CancelTransform();
        Assert.NotNull(svc.Mask);
        Assert.Equal(4, svc.Mask.GetLength(0));
        Assert.Equal(4, svc.Mask.GetLength(1));
        Assert.True(svc.Mask[0, 0]);
    }

    [Fact]
    public void SelectionInputController_ProcessingActive_IgnoresInput()
    {
        var vm = CreateTestViewModel();
        var svc = new SelectionService();
        var drawingMock = new Mock<IDrawingService>();
        var controller = new SelectionInputController(vm, svc, drawingMock.Object);

        vm.IsProcessing = true;
        controller.ProcessInput(5, 5, ToolAction.Down, isShiftDown: false, isAltDown: false);
        Assert.False(svc.IsSelecting);
        Assert.False(svc.IsDragging);

        Assert.False(controller.TryBeginDrag(5, 5));
        Assert.False(controller.TryBeginTransform(TransformHandle.SE));
    }

    [Fact]
    public void PrepareForToolChange_FloatingSelection_PreservesCommittedStateWithoutResidualMaskDistortion()
    {
        var vm = CreateTestViewModel();
        vm.CurrentTool = ToolMode.Marquee;
        vm.SelectionService.BeginRectangleSelection(5, 5, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(10, 10);
        vm.SelectionService.FinalizeSelection();
        vm.SelectionService.LiftSelection(vm.SpriteState);
        Assert.True(vm.SelectionService.IsFloating);

        vm.PrepareForToolChange(ToolMode.Pencil);

        Assert.False(vm.SelectionService.IsFloating);
        Assert.True(vm.SelectionService.HasActiveSelection);
        Assert.Equal(5, vm.SelectionService.MinX);
        Assert.Equal(10, vm.SelectionService.MaxX);
    }

    [Fact]
    public void ToolInputController_MoveToolCancellation_DoesNotLosePixels()
    {
        var vm = CreateTestViewModel();
        vm.SpriteState.Pixels[0] = true;
        vm.CurrentTool = ToolMode.Move;

        // Start Move drag: Down then Move to trigger deferred start
        vm.ProcessToolInput(0, 0, ToolAction.Down, DrawMode.Draw, isShiftDown: false, isAltDown: false);
        vm.ProcessToolInput(5, 5, ToolAction.Move, DrawMode.Draw, isShiftDown: false, isAltDown: false);
        Assert.True(vm.SelectionService.IsFloating);

        vm.CancelInProgressDrawing();

        Assert.False(vm.SelectionService.IsFloating);
        Assert.True(vm.SpriteState.Pixels[0]);
    }

    // ── 10. Deselect on New Layer & Empty Selection Guard Tests ───────────

    [Fact]
    public void AddLayer_WithActiveSelection_DeselectsAndClearsSelectionPreview()
    {
        var vm = CreateTestViewModel();
        vm.CurrentTool = ToolMode.Marquee;
        vm.SelectionService.BeginRectangleSelection(5, 5, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(15, 15);
        vm.SelectionService.FinalizeSelection();
        Assert.True(vm.SelectionService.HasActiveSelection);

        vm.AddLayerCommand.Execute(null);

        // Selection must be cleared so the newly created layer has no lingering selection preview
        Assert.False(vm.SelectionService.HasActiveSelection);
        Assert.False(vm.SelectionService.IsFloating);
        Assert.Equal(2, vm.SpriteState.Layers.Count);
    }

    [Fact]
    public void AddLayer_WithFloatingSelection_CommitsToPreviousLayerAndDeselects()
    {
        var vm = CreateTestViewModel();
        vm.SpriteState.Pixels[5 * 64 + 5] = true;
        vm.CurrentTool = ToolMode.Marquee;
        vm.SelectionService.BeginRectangleSelection(5, 5, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(10, 10);
        vm.SelectionService.FinalizeSelection();
        vm.SelectionService.LiftSelection(vm.SpriteState);
        Assert.True(vm.SelectionService.IsFloating);

        vm.AddLayerCommand.Execute(null);

        Assert.False(vm.SelectionService.IsFloating);
        Assert.False(vm.SelectionService.HasActiveSelection);
        // The pixel on the previous layer (Layer 1, now index 1) was committed
        var layer1Pixels = vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData();
        Assert.True(layer1Pixels[5 * 64 + 5]);
    }

    [Fact]
    public void TryBeginDrag_OnEmptyLayerSelection_ShowsWarningAndDoesNotLift()
    {
        var vm = CreateTestViewModel();
        // Layer is completely blank (all false)
        vm.SelectionService.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(20, 20);
        vm.SelectionService.FinalizeSelection();
        Assert.True(vm.SelectionService.HasActiveSelection);

        var drawingMock = new Mock<IDrawingService>();
        var controller = new SelectionInputController(vm, vm.SelectionService, drawingMock.Object);

        bool began = controller.TryBeginDrag(15, 15);

        Assert.False(began);
        Assert.False(vm.SelectionService.IsFloating);
        Assert.Equal("Selected area on active layer is empty.", vm.StatusMessage);
    }

    [Fact]
    public void EnterTransformMode_OnEmptyLayerSelection_ShowsWarningAndDoesNotTransform()
    {
        var vm = CreateTestViewModel();
        // Layer is blank
        vm.SelectionService.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(20, 20);
        vm.SelectionService.FinalizeSelection();

        var drawingMock = new Mock<IDrawingService>();
        var controller = new SelectionInputController(vm, vm.SelectionService, drawingMock.Object);

        controller.EnterTransformMode();

        Assert.False(vm.SelectionService.IsFloating);
        Assert.Equal("Selected area on active layer is empty.", vm.StatusMessage);
    }

    [Fact]
    public void MoveTool_DragOnEmptyLayerSelection_ShowsWarningAndDoesNotLift()
    {
        var vm = CreateTestViewModel();
        // Layer is blank
        vm.SelectionService.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(20, 20);
        vm.SelectionService.FinalizeSelection();

        vm.CurrentTool = ToolMode.Move;
        vm.ProcessToolInput(15, 15, ToolAction.Down, DrawMode.Draw, isShiftDown: false, isAltDown: false);
        vm.ProcessToolInput(18, 18, ToolAction.Move, DrawMode.Draw, isShiftDown: false, isAltDown: false);

        Assert.False(vm.SelectionService.IsFloating);
        Assert.Equal("Selected area on active layer is empty.", vm.StatusMessage);
    }

    [Fact]
    public void DeleteAndCopySelection_OnEmptyLayerSelection_ClearsSelectionAndCopies()
    {
        var vm = CreateTestViewModel();
        // Layer is blank
        vm.SelectionService.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(20, 20);
        vm.SelectionService.FinalizeSelection();

        // Copy works on empty selection (stores empty clipboard region)
        vm.CopySelectionCommand.Execute(null);
        Assert.True(vm.PasteCommand.CanExecute(null));

        // DeleteSelection clears the selection marquee
        vm.DeleteSelectionCommand.Execute(null);
        Assert.False(vm.SelectionService.HasActiveSelection);
    }

    [Fact]
    public void HasAnyPixelInSelection_CorrectlyIdentifiesEmptyVsPopulated()
    {
        var svc = new SelectionService();
        var state = new SpriteState(10, 10);
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(6, 6);
        svc.FinalizeSelection();

        // Initially blank
        Assert.False(svc.HasAnyPixelInSelection(state));

        // Set pixel outside selection
        state.Pixels[0] = true;
        Assert.False(svc.HasAnyPixelInSelection(state));

        // Set pixel inside selection
        state.Pixels[3 * 10 + 3] = true;
        Assert.True(svc.HasAnyPixelInSelection(state));
    }

    [Fact]
    public void Reselect_AfterCancelOrDeselect_RestoresSelectionBoundsAndMask()
    {
        var svc = new SelectionService();
        Assert.False(svc.CanReselect);

        svc.BeginRectangleSelection(4, 5, SelectionMode.Replace);
        svc.UpdateRectangleSelection(14, 15);
        svc.FinalizeSelection();
        Assert.True(svc.HasActiveSelection);

        // Cancel selection
        svc.Cancel();
        Assert.False(svc.HasActiveSelection);
        Assert.True(svc.CanReselect);

        // Reselect
        svc.Reselect();
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(4, svc.MinX);
        Assert.Equal(14, svc.MaxX);
        Assert.Equal(5, svc.MinY);
        Assert.Equal(15, svc.MaxY);
    }

    [Fact]
    public void AddLayer_FollowedByReselect_RestoresSelectionOnNewEmptyLayerWithEmptyTelemetry()
    {
        var vm = CreateTestViewModel();
        // Draw pixel on layer 0
        vm.SpriteState.Pixels[5 * 64 + 5] = true;

        vm.SelectionService.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(10, 10);
        vm.SelectionService.FinalizeSelection();

        Assert.True(vm.SelectionService.HasActiveSelection);
        Assert.Contains("1 px", vm.SelectionInfo);

        // Add Layer deselects
        vm.AddLayerCommand.Execute(null);
        Assert.False(vm.SelectionService.HasActiveSelection);
        Assert.Empty(vm.SelectionInfo);
        Assert.True(vm.ReselectCommand.CanExecute(null));

        // Reselect on the new layer
        vm.ReselectCommand.Execute(null);
        Assert.True(vm.SelectionService.HasActiveSelection);
        Assert.Contains("Empty", vm.SelectionInfo);
        Assert.Contains("Layer 2", vm.SelectionInfo);
    }

    [Fact]
    public void NewLayerFromSelection_CreatesLayerWithOnlySelectedPixels()
    {
        var vm = CreateTestViewModel();
        // Draw a 4x4 block
        for (int y = 10; y <= 13; y++)
            for (int x = 10; x <= 13; x++)
                vm.SpriteState.Pixels[y * 64 + x] = true;

        // Select the 4x4 block
        vm.SelectionService.BeginRectangleSelection(10, 10, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(13, 13);
        vm.SelectionService.FinalizeSelection();

        Assert.True(vm.NewLayerFromSelectionCommand.CanExecute(null));
        int initialLayerCount = vm.SpriteState.Layers.Count;

        vm.NewLayerFromSelectionCommand.Execute(null);

        Assert.Equal(initialLayerCount + 1, vm.SpriteState.Layers.Count);
        Assert.Equal(0, vm.SpriteState.ActiveLayerIndex); // Newly inserted layer is active
        Assert.Equal("Layer 1 Selection", vm.SpriteState.Layers[0].Name);

        // Verify the new layer has the 16 pixels at (10..13, 10..13) and nowhere else
        var activePixels = vm.SpriteState.ActiveLayerPixels;
        int count = 0;
        for (int i = 0; i < activePixels.Length; i++)
        {
            if (activePixels[i])
            {
                count++;
                int x = i % 64;
                int y = i / 64;
                Assert.InRange(x, 10, 13);
                Assert.InRange(y, 10, 13);
            }
        }
        Assert.Equal(16, count);
    }

    [Fact]
    public void NudgeSelection_MovesMaskWithoutModifyingCanvasPixels()
    {
        var vm = CreateTestViewModel();
        vm.SpriteState.Pixels[5 * 64 + 5] = true;

        vm.SelectionService.BeginRectangleSelection(5, 5, SelectionMode.Replace);
        vm.SelectionService.UpdateRectangleSelection(15, 15);
        vm.SelectionService.FinalizeSelection();

        Assert.Equal(5, vm.SelectionService.MinX);
        Assert.Equal(5, vm.SelectionService.MinY);

        // Nudge right and down
        vm.NudgeSelection(2, 3);

        Assert.Equal(7, vm.SelectionService.MinX);
        Assert.Equal(8, vm.SelectionService.MinY);
        Assert.Equal(17, vm.SelectionService.MaxX);
        Assert.Equal(18, vm.SelectionService.MaxY);

        // Canvas pixel at (5, 5) is still intact
        Assert.True(vm.SpriteState.Pixels[5 * 64 + 5]);
    }
}
