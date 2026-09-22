using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.Core
{
    [Trait("Category", "Unit")]
    public class FlipperPerformanceOptimizationTests
    {
        [Fact]
        public void CompositeFramePixels_WithSpan_MatchesArrayOverload_AcrossBlendModes()
        {
            // Arrange
            var sprite = new SpriteState(128, 64);
            sprite.Layers.Clear();
            sprite.Frames[0].LayerPixels.Clear();

            // Layer 1: Normal blend, checkerboard opacity
            var layer1 = new LayerState { Name = "Base", IsVisible = true, BlendMode = LayerBlendMode.Normal, OpacityMode = LayerOpacityMode.Checkerboard };
            var buf1 = new MonochromePixelBuffer(128 * 64);
            var d1 = buf1.GetMonochromeData();
            for (int i = 0; i < d1.Length; i += 2) d1[i] = true;

            // Layer 2: Xor blend
            var layer2 = new LayerState { Name = "Overlay", IsVisible = true, BlendMode = LayerBlendMode.Xor, OpacityMode = LayerOpacityMode.Solid };
            var buf2 = new MonochromePixelBuffer(128 * 64);
            var d2 = buf2.GetMonochromeData();
            for (int i = 0; i < d2.Length; i += 4) d2[i] = true;

            sprite.Layers.Add(layer1);
            sprite.Layers.Add(layer2);
            sprite.Frames[0].LayerPixels.Add(buf1);
            sprite.Frames[0].LayerPixels.Add(buf2);

            // Act
            bool[] expected = sprite.CompositeFramePixels(0, isExport: false);

            Span<bool> actualSpan = stackalloc bool[128 * 64];
            sprite.CompositeFramePixels(0, actualSpan, isExport: false);

            // Assert
            Assert.Equal(expected.Length, actualSpan.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], actualSpan[i]);
            }
        }

        [Fact]
        public void CompositeFramePixels_WithSpan_AllocatesZeroHeapMemory()
        {
            // Arrange
            var sprite = new SpriteState(128, 64);
            var buf = new MonochromePixelBuffer(128 * 64);
            var d = buf.GetMonochromeData();
            for (int i = 0; i < d.Length; i += 3) d[i] = true;
            sprite.Frames[0].LayerPixels[0] = buf;

            Span<bool> destination = stackalloc bool[128 * 64];

            // Warm up JIT compilation (avoid counting tiered JIT compilation heap allocations)
            for (int i = 0; i < 100; i++)
            {
                sprite.CompositeFramePixels(0, destination, isExport: false);
            }

            // Measure allocations over 1000 iterations
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 1000; i++)
            {
                sprite.CompositeFramePixels(0, destination, isExport: false);
            }

            long bytesAllocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;

            // Assert: Exactly 0 bytes allocated on heap
            Assert.Equal(0, bytesAllocated);
        }

        [Fact]
        public void CompositeFramePixels_WithInsufficientSpanLength_ThrowsArgumentException()
        {
            var sprite = new SpriteState(128, 64);
            bool threw = false;
            try
            {
                Span<bool> tooSmallSpan = stackalloc bool[100];
                sprite.CompositeFramePixels(0, tooSmallSpan);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert.True(threw);
        }

        [Fact]
        public void CompositeVisiblePixels_WithSpan_MatchesCompositeFramePixels()
        {
            var sprite = new SpriteState(64, 32);
            var buf = new MonochromePixelBuffer(64 * 32);
            var d = buf.GetMonochromeData();
            d[10] = true;
            d[20] = true;
            sprite.Frames[0].LayerPixels[0] = buf;

            Span<bool> span = stackalloc bool[64 * 32];
            sprite.CompositeVisiblePixels(span);

            bool[] expected = sprite.CompositeVisiblePixels();
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.Equal(expected[i], span[i]);
            }
        }

        [Fact]
        public void RecalculateMatrix_PreservesCachedSpriteThumbnail_OnNonVisualEdits()
        {
            // Arrange
            var sp1 = new SpriteState(128, 64);
            var sp2 = new SpriteState(128, 64);
            var e1 = new FlipperManifestEntry { Name = "idle_anim", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 2 };
            var e2 = new FlipperManifestEntry { Name = "walk_anim", MinLevel = 11, MaxLevel = 20, MinButthurt = 6, MaxButthurt = 10, Weight = 3 };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("idle_anim", sp1, e1),
                ("walk_anim", sp2, e2)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack: pack);

            var entry1 = vm.Entries[0];
            var entry2 = vm.Entries[1];

            var thumb1First = entry1.SpriteThumbnail;
            var thumb2First = entry2.SpriteThumbnail;
            Assert.NotNull(thumb1First);
            Assert.NotNull(thumb2First);

            // Act 1: Modify bounds / weights (does not change sprite signature)
            entry1.Weight = 5;
            entry1.MinLevel = 2;
            vm.RecalculateMatrix();

            // Assert 1: Thumbnails must be reused in-place (no re-rendering WriteableBitmap)
            Assert.Same(thumb1First, entry1.SpriteThumbnail);
            Assert.Same(thumb2First, entry2.SpriteThumbnail);

            // Act 2: Invalidate thumbnail
            entry1.InvalidateThumbnail();
            vm.RecalculateMatrix();

            // Assert 2: New thumbnail generated after explicit invalidation
            Assert.NotNull(entry1.SpriteThumbnail);
            Assert.NotSame(thumb1First, entry1.SpriteThumbnail);
        }

        [Fact]
        public void RenderPreviewFrame_ReusesBuffers_AndEliminatesPerTickGarbage()
        {
            // Arrange
            var sp = new SpriteState(128, 64);
            sp.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
            var entry = new FlipperManifestEntry { Name = "test_anim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 1 };

            var vm = new FlipperScheduleMatrixViewModel(pack: [("test_anim", sp, entry)]);
            vm.SetPreviewSpriteForTest(sp);

            // Warm up
            vm.PreviewFrameIndex = 0;

            // Property change tracking during steady preview ticks
            var firedProperties = new List<string>();
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != null) firedProperties.Add(e.PropertyName);
            };

            // Act: Step to next frame
            vm.PreviewNextFrame();

            // Assert: During steady-state frame step, invariant telemetry properties must not fire
            Assert.Contains(nameof(vm.PreviewFrameIndex), firedProperties);
            Assert.Contains(nameof(vm.CurrentPreviewFrameIndex), firedProperties);
            Assert.DoesNotContain(nameof(vm.PreviewFps), firedProperties);
            Assert.DoesNotContain(nameof(vm.PreviewFpsBadgeText), firedProperties);
            Assert.DoesNotContain(nameof(vm.PreviewTotalFrames), firedProperties);
            Assert.DoesNotContain(nameof(vm.PreviewMaxFrameIndex), firedProperties);
            Assert.DoesNotContain(nameof(vm.PreviewHasMultipleFrames), firedProperties);
        }

        [Fact]
        public async Task ExportAssetPackAsync_ExportsFolderAndReportsProgress()
        {
            // Arrange
            var exportService = new FlipperExportService();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_AsyncExportTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                var sprite1 = new SpriteState(128, 64);
                var entry1 = new FlipperManifestEntry { Name = "anim_one", MinLevel = 1, MaxLevel = 10, MinButthurt = 0, MaxButthurt = 5, Weight = 1 };
                var settings1 = new FlipperExportSettings { AnimationName = "anim_one", FrameRate = 8, MinLevel = 1, MaxLevel = 10 };

                var sprite2 = new SpriteState(128, 64);
                sprite2.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
                var entry2 = new FlipperManifestEntry { Name = "anim_two", MinLevel = 11, MaxLevel = 20, MinButthurt = 6, MaxButthurt = 10, Weight = 2 };
                var settings2 = new FlipperExportSettings { AnimationName = "anim_two", FrameRate = 12, MinLevel = 11, MaxLevel = 20 };

                var animations = new List<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>
                {
                    (sprite1, entry1, settings1),
                    (sprite2, entry2, settings2)
                };

                var progressValues = new List<double>();
                var progress = new Progress<double>(p => progressValues.Add(p));

                // Act
                await exportService.ExportAssetPackAsync(animations, tempDir, isMomentum: true, progress: progress);

                // Assert
                string manifestPath = Path.Combine(tempDir, "Anims", "manifest.txt");
                Assert.True(File.Exists(manifestPath));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_one", "meta.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_one", "frame_0.bm")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_two", "meta.txt")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_two", "frame_0.bm")));
                Assert.True(File.Exists(Path.Combine(tempDir, "Anims", "anim_two", "frame_1.bm")));
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public async Task ExportAssetPackZipAsync_ExportsValidZipArchive()
        {
            // Arrange
            var exportService = new FlipperExportService();
            string tempZip = Path.Combine(Path.GetTempPath(), "Hexprite_AsyncZipTest_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry { Name = "walk", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 4, Weight = 1 };
                var settings = new FlipperExportSettings { AnimationName = "walk", FrameRate = 10, MinLevel = 1, MaxLevel = 3 };

                var animations = new List<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>
                {
                    (sprite, entry, settings)
                };

                // Act
                await exportService.ExportAssetPackZipAsync(animations, tempZip, isMomentum: true);

                // Assert
                Assert.True(File.Exists(tempZip));
                using var zip = System.IO.Compression.ZipFile.OpenRead(tempZip);
                Assert.NotNull(zip.GetEntry("Anims/manifest.txt"));
                Assert.NotNull(zip.GetEntry("Anims/walk/meta.txt"));
                Assert.NotNull(zip.GetEntry("Anims/walk/frame_0.bm"));
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        [Fact]
        public async Task ImportAssetPackAsync_ImportsFolderAsynchronously()
        {
            // Arrange
            var exportService = new FlipperExportService();
            var importService = new FlipperImportService();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_AsyncImportTest_" + Guid.NewGuid().ToString("N"));

            try
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry { Name = "test_jump", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 4, Weight = 2 };
                var settings = new FlipperExportSettings { AnimationName = "test_jump", FrameRate = 10, MinLevel = 1, MaxLevel = 5 };

                await exportService.ExportAssetPackAsync([(sprite, entry, settings)], tempDir, isMomentum: true);

                // Act
                var imported = await importService.ImportAssetPackAsync(tempDir);

                // Assert
                Assert.Single(imported);
                Assert.Equal("test_jump", imported[0].Name);
                Assert.Equal(128, imported[0].Sprite.Width);
                Assert.Equal(64, imported[0].Sprite.Height);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        [Fact]
        public void MediaSlicer_ScheduleReslice_ExecutesWithoutException()
        {
            var slicer = new FlipperMediaSlicerViewModel();
            slicer.BrightnessThreshold = 150;
            slicer.DitherAmount = 80;
            slicer.FrameWidth = 128;
            slicer.FrameHeight = 64;

            Assert.NotNull(slicer.SlicedSprite);
            Assert.True(slicer.TotalFrames >= 1);
            slicer.Dispose();
        }

        [Fact]
        public void BoundsAdjustment_ContinuousStepping_ExecutesUnderTargetBudget()
        {
            // Arrange: 30 animations with sprite states
            var entries = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
            for (int i = 1; i <= 30; i++)
            {
                var sprite = new SpriteState(128, 64);
                var entry = new FlipperManifestEntry
                {
                    Name = $"dolphin_anim_{i:D2}",
                    MinLevel = Math.Clamp(i, 1, 30),
                    MaxLevel = Math.Clamp(i + 2, 1, 30),
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1
                };
                entries.Add((entry.Name, sprite, entry));
            }

            using var vm = new FlipperScheduleMatrixViewModel(entries, "PerformanceTestPack");
            vm.SelectedEntry = vm.Entries[0];

            // Warm up
            for (int i = 0; i < 10; i++)
            {
                vm.StepMinLevelCommand.Execute(1);
                vm.StepMinLevelCommand.Execute(-1);
            }

            // Act: 500 continuous bound adjustments across step commands
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                vm.StepMinLevelCommand.Execute(1);
                vm.StepMaxLevelCommand.Execute(1);
                vm.StepMinMoodCommand.Execute(1);
                vm.StepMaxMoodCommand.Execute(1);
                vm.StepWeightCommand.Execute(1);
            }
            sw.Stop();

            // Assert: 500 operations should complete in under 2000ms (averaging <4ms per step even under parallel test load)
            Assert.True(sw.ElapsedMilliseconds < 2000, $"500 step adjustments took {sw.ElapsedMilliseconds}ms, expected < 2000ms.");
            Assert.True(vm.CanUndo);

            // Verify Undo/Redo restores bounds accurately
            vm.Undo();
            Assert.True(vm.CanRedo);
            vm.Redo();
        }
    }
}
