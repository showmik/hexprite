using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Collection("WpfTest")]
    [Trait("Category", "Unit")]
    public class OverflowPixelBufferBugProofs
    {
        public OverflowPixelBufferBugProofs()
        {
            WpfTestHelper.EnsureApplication();
        }

        private MainViewModel CreateMainViewModel(int width = 10, int height = 10)
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
            var drawingService = new DrawingService();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
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
                drawingService,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                importExportMock.Object,
                hardwarePreviewMock.Object,
                serviceProviderMock.Object);

            shell.NewDocumentCommand.Execute($"{width}x{height}");
            return (MainViewModel)shell.ActiveDocument!;
        }

        /// <summary>
        /// Bug 1: SerializedPixelLayer.ToPixelBuffer() computes invalid canvas size when buffer is expanded asymmetrically.
        /// Root cause: ToPixelBuffer() calculates canvasWidth = ExtendedWidth - 2 * MarginX. When a pixel is drawn off-canvas right,
        /// MarginX remains unchanged while ExtendedWidth increases, making MarginX != (ExtendedWidth - canvasWidth) / 2.
        /// Deserialization calculates an incorrect, corrupted canvas size.
        /// </summary>
        [Fact]
        public void Proof_Bug1_DeserializationCanvasDimensionCorruption()
        {
            int canvasW = 10;
            int canvasH = 10;
            int initialMargin = 10;
            var ovf = new OverflowPixelBuffer(canvasW, canvasH, initialMargin);
            ovf.SetPixel(2, 2, true);

            // Write off-canvas to the right (x = 25).
            // This expands ExtendedWidth asymmetrically while MarginX remains 10.
            ovf.SetPixel(25, 0, true);

            // Serialize layer state and deserialize back
            var serialized = SerializedPixelLayer.From(ovf);
            IPixelBuffer restoredBuffer = serialized.ToPixelBuffer();

            // Expected: Restored buffer should retain original canvas dimensions (10x10 = 100 pixels).
            // Actual on current code: ToPixelBuffer() calculates canvasWidth = ExtendedWidth - 2 * MarginX = 163 - 20 = 143,
            // producing a 143x10 (1430 pixels) buffer instead of 10x10.
            Assert.Equal(canvasW * canvasH, restoredBuffer.GetMonochromeData().Length);
        }

        private static void RunOnStaThread(Func<Task> asyncAction)
        {
            WpfTestHelper.RunOnSta(asyncAction);
        }

        /// <summary>
        /// Bug 2: Canvas Rotation Margin Miscalculation.
        /// Root cause: RotateCanvasAsync hardcodes newMarginX = oldOvf.MarginY for 90° CW rotation.
        /// When vertical margin is expanded asymmetrically (e.g. set pixel at y = 25),
        /// 90° CW rotation moves the canvas top-left origin to ExtendedHeight - MarginY - oldH,
        /// but assigning oldOvf.MarginY displaces the canvas viewport into blank overflow padding.
        /// </summary>
        [Fact]
        public void Proof_Bug2_CanvasRotationMarginMiscalculation()
        {
            bool[]? rotatedData = null;

            RunOnStaThread(async () =>
            {
                var vm = CreateMainViewModel(10, 10);
                int w = 10, h = 10;
                var ovf = new OverflowPixelBuffer(w, h, 10);

                // Set canvas pixel at top-left (0, 0)
                ovf.SetPixel(0, 0, true);
                // Expand bottom margin asymmetrically by setting off-canvas pixel at y = 25
                ovf.SetPixel(0, 25, true);

                vm.SpriteState.Frames[0].LayerPixels[0] = ovf;
                vm.SpriteState.Layers[0].PreserveOverflow = true;

                // Rotate canvas 90° CW
                await vm.RotateCanvasAsync(RotationDirection.Clockwise90);

                rotatedData = vm.SpriteState.ActivePixelBuffer.GetMonochromeData();
            });

            // Canvas coordinate (0, 0) rotated 90° CW on 10x10 canvas maps to canvas coordinate (9, 0) [index 9].
            // Expected: Canvas top-right pixel (index 9) is true.
            // Actual on current code: newMarginX is set to 10 instead of 143, reading empty padding where index 9 is false.
            Assert.True(rotatedData != null && rotatedData[9], "Rotated canvas top-right pixel (9,0) should be true after 90° CW rotation.");
        }

        /// <summary>
        /// Bug 3: Canvas Flip Margin Miscalculation.
        /// Root cause: FlipCanvasAsync reuses oldOvf.MarginX on horizontal flip instead of ExtendedWidth - MarginX - w.
        /// On asymmetric buffers (e.g. right side expanded), horizontal flip shifts canvas content away from origin.
        /// </summary>
        [Fact]
        public void Proof_Bug3_CanvasFlipMarginMiscalculation()
        {
            bool[]? flippedData = null;

            RunOnStaThread(async () =>
            {
                var vm = CreateMainViewModel(10, 10);
                int w = 10, h = 10;
                var ovf = new OverflowPixelBuffer(w, h, 10);

                // Set top-left canvas pixel (0, 0)
                ovf.SetPixel(0, 0, true);
                // Expand right margin asymmetrically by setting off-canvas pixel at x = 25
                ovf.SetPixel(25, 0, true);

                vm.SpriteState.Frames[0].LayerPixels[0] = ovf;
                vm.SpriteState.Layers[0].PreserveOverflow = true;

                // Flip canvas horizontally
                await vm.FlipCanvasAsync(FlipDirection.Horizontal);

                flippedData = vm.SpriteState.ActivePixelBuffer.GetMonochromeData();
            });

            // Canvas pixel (0, 0) flipped horizontally maps to (9, 0) [index 9].
            // Expected: Canvas pixel at (9, 0) [index 9] is true.
            // Actual on current code: oldOvf.MarginX (10) was reused instead of 143, so viewport reads empty padding.
            Assert.True(flippedData != null && flippedData[9], "Flipped canvas pixel at (9,0) should be true after horizontal flip.");
        }

        /// <summary>
        /// Bug 4: Invert Grid / Outline Layer View Cache Desync.
        /// Root cause: InvertGrid and OutlineLayer mutate _viewCache directly without updating _extendedPixels.
        /// When _viewCache is invalidated (e.g. via InvalidateViewCache or SetPixel), changes revert to un-inverted state.
        /// </summary>
        [Fact]
        public void Proof_Bug4_InvertGrid_OutlineLayer_ViewCacheDesync()
        {
            var state = new SpriteState(10, 10);
            var ovf = new OverflowPixelBuffer(10, 10, 10);
            state.Frames[0].LayerPixels[0] = ovf;
            state.Layers[0].PreserveOverflow = true;
            state.EnsureLayers();

            // Ensure canvas pixel (0, 0) is false initially
            var initialData = ovf.GetMonochromeData();
            Assert.False(initialData[0]);

            var drawingService = new DrawingService();
            drawingService.InvertGrid(state);

            // Directly after InvertGrid, _viewCache[0] is true.
            // Now invalidate the view cache!
            ovf.InvalidateViewCache();

            // Re-query GetMonochromeData()
            var postInvalidateData = ovf.GetMonochromeData();

            // Expected: _extendedPixels was updated, so inverted pixel (0,0) remains true.
            // Actual on current code: InvertGrid only mutated _viewCache, so post-invalidation data reverts to false.
            Assert.True(postInvalidateData[0], "Inverted grid modification should persist after view cache invalidation.");
        }

        /// <summary>
        /// Bug 5: Layer Merge Data Loss.
        /// Root cause: MergeLayers() composites canvas pixels into MonochromePixelBuffer, discarding off-canvas overflow pixels.
        /// </summary>
        [Fact]
        public void Proof_Bug5_LayerMergeDataLoss()
        {
            var vm = CreateMainViewModel(10, 10);
            var state = vm.SpriteState;

            // Add a second layer so we have 2 layers to merge
            vm.AddLayerCommand.Execute(null);
            Assert.Equal(2, state.Layers.Count);

            // Set bottom layer (index 1) to OverflowPixelBuffer with an off-canvas pixel
            var ovf = new OverflowPixelBuffer(10, 10, 10);
            ovf.SetPixel(-5, -5, true);
            state.Frames[0].LayerPixels[1] = ovf;
            state.Layers[1].PreserveOverflow = true;

            // Select both layers for merging
            vm.SelectedLayerIndices.Clear();
            vm.SelectedLayerIndices.Add(0);
            vm.SelectedLayerIndices.Add(1);

            // Merge layers
            vm.MergeLayerCommand.Execute(null);

            // After merging 2 layers into 1, only 1 layer remains at index 0
            Assert.Single(state.Layers);
            var mergedBuffer = state.Frames[0].LayerPixels[0];
            var mergedLayer = state.Layers[0];

            // Expected: Merged layer should preserve overflow capability and off-canvas pixel data.
            // Actual on current code: Line 978 replaces LayerPixels[0] with MonochromePixelBuffer and line 993 sets PreserveOverflow = false.
            Assert.True(mergedLayer.PreserveOverflow, "Merged layer should retain PreserveOverflow = true.");
            Assert.IsType<OverflowPixelBuffer>(mergedBuffer);
        }

        /// <summary>
        /// Bug 6: Invert Grid Omits Off-Canvas Overflow Pixels.
        /// Root cause: InvertGrid loops over state.Pixels.Length (canvas pixels only), leaving off-canvas overflow pixels un-inverted.
        /// </summary>
        [Fact]
        public void Proof_Bug6_InvertGridOmitsOffCanvasOverflowPixels()
        {
            var state = new SpriteState(10, 10);
            var ovf = new OverflowPixelBuffer(10, 10, 10);
            ovf.SetPixel(-5, -5, true); // Off-canvas pixel is true

            state.Frames[0].LayerPixels[0] = ovf;
            state.Layers[0].PreserveOverflow = true;
            state.EnsureLayers();

            int extX = -5 + ovf.MarginX;
            int extY = -5 + ovf.MarginY;
            int extIdx = extY * ovf.ExtendedWidth + extX;

            // Verify initial off-canvas pixel is true
            Assert.True(ovf.GetExtendedData()[extIdx]);

            var drawingService = new DrawingService();
            drawingService.InvertGrid(state);
            state.SyncActiveLayer();

            // Expected: Inverting the grid on an overflow layer inverts off-canvas pixels as well (true -> false).
            // Actual on current code: InvertGrid only loops 0..99 (canvas bounds), leaving extended coordinate (-5, -5) as true.
            Assert.False(ovf.GetExtendedData()[extIdx], "Off-canvas pixel at (-5,-5) should be inverted to false.");
        }

        /// <summary>
        /// Bug 7: ClearFrame Resets Asymmetric Margins.
        /// Root cause: ClearFrame constructs OverflowPixelBuffer with single margin parameter (ovf.MarginX), resetting MarginY = MarginX.
        /// </summary>
        [Fact]
        public void Proof_Bug7_ClearFrameResetsAsymmetricMargins()
        {
            var vm = CreateMainViewModel(10, 10);
            var ovf = new OverflowPixelBuffer(10, 10, 10);
            // Expand vertical margin asymmetrically (y = -40)
            ovf.SetPixel(0, -40, true);

            int oldMarginX = ovf.MarginX; // 10
            int oldMarginY = ovf.MarginY; // e.g. 50

            Assert.NotEqual(oldMarginX, oldMarginY);

            vm.SpriteState.Frames[0].LayerPixels[0] = ovf;
            vm.SpriteState.Layers[0].PreserveOverflow = true;

            // Clear frame
            vm.ClearFrameCommand.Execute(null);

            var clearedOvf = (OverflowPixelBuffer)vm.SpriteState.Frames[0].LayerPixels[0];

            // Expected: Cleared OverflowPixelBuffer retains original MarginY (50).
            // Actual on current code: Line 2173 uses single-margin constructor OverflowPixelBuffer(w, h, ovf.MarginX), resetting MarginY to MarginX (10).
            Assert.Equal(oldMarginY, clearedOvf.MarginY);
        }

        /// <summary>
        /// Bug 8: ComputeHash Ignores Overflow Pixels.
        /// Root cause: SpriteState.ComputeHash() hashes canvas pixels only (GetMonochromeData()), missing overflow edits in dirty/state tracking.
        /// </summary>
        [Fact]
        public void Proof_Bug8_ComputeHashIgnoresOverflowPixels()
        {
            var state = new SpriteState(10, 10);
            var ovf = new OverflowPixelBuffer(10, 10, 10);
            state.Frames[0].LayerPixels[0] = ovf;
            state.Layers[0].PreserveOverflow = true;
            state.EnsureLayers();

            string hashBefore = state.ComputeHash();

            // Edit an off-canvas overflow pixel
            ovf.SetPixel(-5, -5, true);

            string hashAfter = state.ComputeHash();

            // Expected: Document hash changes when off-canvas overflow pixels are modified.
            // Actual on current code: ComputeHash() hashes canvas pixels only (GetMonochromeData()), so hashBefore == hashAfter.
            Assert.NotEqual(hashBefore, hashAfter);
        }

        /// <summary>
        /// Bug 9: Non-Contiguous Flood Fill Clamped to Canvas.
        /// Root cause: ApplyFloodFill with isContiguous = false loops over canvas bounds only (0..Width, 0..Height), ignoring off-canvas overflow pixels.
        /// </summary>
        [Fact]
        public void Proof_Bug9_NonContiguousFloodFillClampedToCanvas()
        {
            var state = new SpriteState(10, 10);
            var ovf = new OverflowPixelBuffer(10, 10, 10);

            // Set canvas pixel (2, 2) to true
            ovf.SetPixel(2, 2, true);
            // Set off-canvas pixel (-5, -5) to true
            ovf.SetPixel(-5, -5, true);

            state.Frames[0].LayerPixels[0] = ovf;
            state.Layers[0].PreserveOverflow = true;
            state.EnsureLayers();

            int extX = -5 + ovf.MarginX;
            int extY = -5 + ovf.MarginY;
            int extIdx = extY * ovf.ExtendedWidth + extX;

            // Verify off-canvas pixel is true initially
            Assert.True(ovf.GetExtendedData()[extIdx]);

            var drawingService = new DrawingService();
            // Non-contiguous flood fill replacing all 'true' pixels with 'false'
            drawingService.ApplyFloodFill(state, startX: 2, startY: 2, newState: false, isContiguous: false);

            // Expected: Global flood fill replaces all matching color pixels across the layer, including off-canvas pixels (-5, -5).
            // Actual on current code: ApplyFloodFill loops only 0..Width and 0..Height, leaving off-canvas pixel (-5, -5) as true.
            Assert.False(ovf.GetExtendedData()[extIdx], "Off-canvas matching pixel at (-5, -5) should be filled to false.");
        }
    }
}
