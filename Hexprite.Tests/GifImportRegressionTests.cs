using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Tests.E2E;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class GifImportRegressionTests
    {
        private static string CreateTestGif(int width, int height, int frameCount = 3)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"test_gif_import_{Guid.NewGuid():N}.gif");
            var encoder = new GifBitmapEncoder();

            for (int f = 0; f < frameCount; f++)
            {
                var wb = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                byte[] pixels = new byte[width * height * 4];
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte val = (byte)(f % 2 == 0 ? 0 : 255);
                    pixels[i] = val;
                    pixels[i + 1] = val;
                    pixels[i + 2] = val;
                    pixels[i + 3] = 255;
                }
                wb.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, width * 4, 0);
                encoder.Frames.Add(BitmapFrame.Create(wb));
            }

            using (var fs = File.OpenWrite(tempPath))
            {
                encoder.Save(fs);
            }

            return tempPath;
        }

        [Fact]
        public async Task ImportBitmapFromFileAsync_AnimatedGif_DoesNotThrowIndexOutOfRange()
        {
            string gifPath = CreateTestGif(16, 16, frameCount: 3);
            try
            {
                var mockDialog = new Mock<IDialogService>();
                mockDialog.Setup(d => d.ShowImportAnimationDialog(It.IsAny<string>(), It.IsAny<AnimationImportSettings>()))
                    .Returns(new AnimationImportSettings
                    {
                        MaxDimension = 16,
                        TargetFps = 10,
                        MaxFrames = 5,
                        UniformSampling = false,
                        Threshold = 128
                    });

                var shell = E2ETestHelper.CreateTestShellViewModel(dialogService: mockDialog.Object);

                // Act - must not throw ArgumentOutOfRangeException
                await shell.ImportBitmapFromFileAsync(gifPath);

                // Assert
                Assert.NotNull(shell.ActiveDocument);
                var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
                Assert.True(doc.IsAnimationEnabled);
                Assert.Equal(3, doc.SpriteState.Frames.Count);
                Assert.Single(doc.SpriteState.Layers);

                for (int i = 0; i < doc.SpriteState.Frames.Count; i++)
                {
                    var frame = doc.SpriteState.Frames[i];
                    Assert.NotNull(frame.LayerPixels);
                    Assert.Single(frame.LayerPixels);
                    Assert.NotNull(frame.LayerPixels[0]);
                }

                // Verify redraw grid and sync active layer execute cleanly
                doc.RedrawGridFromMemory();
                doc.SpriteState.SyncActiveLayer();
            }
            finally
            {
                if (File.Exists(gifPath)) File.Delete(gifPath);
            }
        }

        [Fact]
        public async Task ImportBitmapFromFileAsync_SingleFrameGif_ImportsCorrectly()
        {
            string gifPath = CreateTestGif(8, 8, frameCount: 1);
            try
            {
                var mockDialog = new Mock<IDialogService>();
                mockDialog.Setup(d => d.ShowImportAnimationDialog(It.IsAny<string>(), It.IsAny<AnimationImportSettings>()))
                    .Returns(new AnimationImportSettings
                    {
                        MaxDimension = 8,
                        TargetFps = 10,
                        MaxFrames = 5,
                        UniformSampling = true,
                        Threshold = 128
                    });

                var shell = E2ETestHelper.CreateTestShellViewModel(dialogService: mockDialog.Object);

                await shell.ImportBitmapFromFileAsync(gifPath);

                Assert.NotNull(shell.ActiveDocument);
                var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
                Assert.True(doc.IsAnimationEnabled);
                Assert.Single(doc.SpriteState.Frames);
                Assert.Single(doc.SpriteState.Layers);
                Assert.NotNull(doc.SpriteState.Frames[0].LayerPixels);
                Assert.Single(doc.SpriteState.Frames[0].LayerPixels);
            }
            finally
            {
                if (File.Exists(gifPath)) File.Delete(gifPath);
            }
        }

        [Fact]
        public void SpriteState_SyncActiveLayer_WithEmptyFramesOrLayers_DoesNotThrow()
        {
            var state = new SpriteState(16, 16);
            state.Frames.Clear();
            state.Layers.Clear();

            // None of these should throw ArgumentOutOfRangeException
            state.SyncActiveLayer();
            Assert.Null(state.ActivePixelBuffer);

            // Frame with empty LayerPixels
            state.Frames.Add(new FrameState { LayerPixels = [] });
            state.Layers.Add(new LayerState { Name = "Layer 1" });
            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;

            state.SyncActiveLayer();
            Assert.Null(state.ActivePixelBuffer);

            // ActiveFrameIndex out of bounds
            state.ActiveFrameIndex = 5;
            state.SyncActiveLayer();
            Assert.Null(state.ActivePixelBuffer);

            // ActiveLayerIndex out of bounds
            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 5;
            state.SyncActiveLayer();
            Assert.Null(state.ActivePixelBuffer);
        }

        [Fact]
        public void GifToMonochromeConverter_SelectFrames_EmptyComposited_ReturnsEmpty()
        {
            var empty = new List<BitmapSource>();
            var rawFrames = new System.Collections.ObjectModel.ReadOnlyCollection<BitmapFrame>([]);
            
            // Invoke via private reflection or test through public conversion
            var result = GifToMonochromeConverter.CompositeGifFrames(new List<BitmapFrame>());
            Assert.Empty(result);
        }

        [Fact]
        public void SpriteState_ActiveLayerPixels_WithEmptyOrOutOfBounds_ReturnsFallbackWithoutThrowing()
        {
            var state = new SpriteState(16, 16);
            state.Frames.Clear();
            state.Layers.Clear();

            // Frames empty
            var px1 = state.ActiveLayerPixels;
            Assert.NotNull(px1);

            // LayerPixels empty
            state.Frames.Add(new FrameState { LayerPixels = [] });
            state.Layers.Add(new LayerState { Name = "Layer 1" });
            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            var px2 = state.ActiveLayerPixels;
            Assert.NotNull(px2);

            // Out-of-bounds indices
            state.ActiveFrameIndex = 10;
            state.ActiveLayerIndex = 10;
            var px3 = state.ActiveLayerPixels;
            Assert.NotNull(px3);
        }

        [Fact]
        public void SpriteState_SetActiveLayer_And_SetActiveFrame_OutOfBounds_DoesNotThrow()
        {
            var state = new SpriteState(16, 16);
            state.SetActiveLayer(99);
            state.SetActiveFrame(99);
            state.SetActiveLayer(-5);
            state.SetActiveFrame(-5);

            state.Frames[0].LayerPixels.Clear();
            state.SetActiveLayer(0);
            state.SetActiveFrame(0);
        }

        [Fact]
        public void SpriteState_GlobalizeLayer_WithMismatchedLayerPixels_DoesNotThrow()
        {
            var state = new SpriteState(16, 16);
            state.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [] });
            state.GlobalizeLayer(0, 0);
        }

        [Fact]
        public void FrameState_Clone_WithNullLayerPixels_DoesNotThrow()
        {
            var frame = new FrameState { LayerPixels = null! };
            var clone = frame.Clone();
            Assert.NotNull(clone.LayerPixels);
            Assert.Empty(clone.LayerPixels);
        }

        [Fact]
        public void MainViewModel_SelectLayerContent_And_TrySetLayerGlobal_OutOfBounds_DoesNotThrow()
        {
            var shell = E2ETestHelper.CreateTestShellViewModel();
            shell.OpenSpriteInTab(E2ETestHelper.CreateTestSprite(1, 16, 16), "Test");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            // Out of bounds layer selection
            mvm.SelectLayerContent(99);
            mvm.SelectLayerContent(-1);

            // Out of bounds global layer
            bool result1 = mvm.TrySetLayerGlobal(99, true);
            Assert.False(result1);
            bool result2 = mvm.TrySetLayerGlobal(-1, true);
            Assert.False(result2);

            // UpdatePreviewSimulation with negative and excessive indices
            mvm.UpdatePreviewSimulationForFrame(-1);
            mvm.UpdatePreviewSimulationForFrame(99);
        }
    }
}
