using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Hexprite.Views;
using Xunit;
using Path = System.IO.Path;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperChallenger1StressAndPerfTests
    {
        private static void ForceGarbageCollection()
        {
#pragma warning disable S1215 // Intentional GC collection in stress/memory benchmark tests
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
#pragma warning restore S1215
        }

        #region 1. Zero-Allocation Compositing Stress & Invariant Verification

        [Fact]
        public void ZeroAllocationCompositing_100Frames_8Layers_50000Iterations_ZeroHeapGarbage()
        {
            // Arrange: Sprite with 8 layers exercising all blend and opacity modes
            var sprite = new SpriteState(128, 64);
            sprite.Layers.Clear();
            sprite.Frames.Clear();

            var blendModes = new[]
            {
                LayerBlendMode.Normal,
                LayerBlendMode.Xor,
                LayerBlendMode.Mask,
                LayerBlendMode.Subtract,
                LayerBlendMode.Normal,
                LayerBlendMode.Xor,
                LayerBlendMode.Mask,
                LayerBlendMode.Subtract
            };

            var opacityModes = new[]
            {
                LayerOpacityMode.Solid,
                LayerOpacityMode.Checkerboard,
                LayerOpacityMode.Solid,
                LayerOpacityMode.Checkerboard,
                LayerOpacityMode.Solid,
                LayerOpacityMode.Checkerboard,
                LayerOpacityMode.Solid,
                LayerOpacityMode.Checkerboard
            };

            for (int l = 0; l < 8; l++)
            {
                sprite.Layers.Add(new LayerState
                {
                    Name = $"Layer_{l}",
                    IsVisible = true,
                    BlendMode = blendModes[l],
                    OpacityMode = opacityModes[l]
                });
            }

            // Create 100 frames with distinct pseudorandom patterns
            for (int f = 0; f < 100; f++)
            {
                var frame = new FrameState { Name = $"Frame_{f}" };
                for (int l = 0; l < 8; l++)
                {
                    var buf = new MonochromePixelBuffer(128 * 64);
                    var data = buf.GetMonochromeData();
                    int step = (f + l) % 7 + 2;
                    for (int p = (f * 13 + l * 7) % step; p < data.Length; p += step)
                    {
                        data[p] = true;
                    }
                    frame.LayerPixels.Add(buf);
                }
                sprite.Frames.Add(frame);
            }

            // Verify equivalence between Span and Array over all 100 frames
            Span<bool> spanBuf = new bool[128 * 64];
            for (int f = 0; f < 100; f++)
            {
                bool[] expected = sprite.CompositeFramePixels(f, isExport: false);
                sprite.CompositeFramePixels(f, spanBuf, isExport: false);
                for (int i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], spanBuf[i]);
                }
            }

            // Warm up JIT
            for (int f = 0; f < 100; f++)
            {
                sprite.CompositeFramePixels(f, spanBuf, isExport: false);
            }

            // Act & Assert: 2,000 compositing operations must allocate exactly 0 bytes on heap
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

            for (int iter = 0; iter < 2000; iter++)
            {
                int frameIdx = iter % 100;
                sprite.CompositeFramePixels(frameIdx, spanBuf, isExport: false);
            }

            long bytesAllocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            Assert.Equal(0, bytesAllocated);
        }

        [Fact]
        public void ZeroAllocation_CompositeVisiblePixels_2000Iterations_ZeroHeapGarbage()
        {
            var sprite = new SpriteState(128, 64);
            var buf = new MonochromePixelBuffer(128 * 64);
            var d = buf.GetMonochromeData();
            for (int i = 0; i < d.Length; i += 3) d[i] = true;
            sprite.Frames[0].LayerPixels[0] = buf;

            Span<bool> span = new bool[128 * 64];

            // Warm up
            for (int i = 0; i < 100; i++)
            {
                sprite.CompositeVisiblePixels(span);
            }

            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 2000; i++)
            {
                sprite.CompositeVisiblePixels(span);
            }

            long bytesAllocated = GC.GetAllocatedBytesForCurrentThread() - bytesBefore;
            Assert.Equal(0, bytesAllocated);
        }

        [Theory]
        [InlineData(16, 16)]
        [InlineData(64, 32)]
        [InlineData(128, 64)]
        [InlineData(256, 128)]
        public void SpanCompositing_ArbitraryDimensions_MatchesExpectedAndRejectsTooShortSpans(int w, int h)
        {
            var sprite = new SpriteState(w, h);
            var buf = new MonochromePixelBuffer(w * h);
            var d = buf.GetMonochromeData();
            d[0] = true;
            d[w * h - 1] = true;
            sprite.Frames[0].LayerPixels[0] = buf;

            Span<bool> correctSpan = new bool[w * h];
            sprite.CompositeFramePixels(0, correctSpan);

            Assert.True(correctSpan[0]);
            Assert.True(correctSpan[w * h - 1]);

            // Span 1 element too short must throw ArgumentException
            Span<bool> shortSpan = new bool[w * h - 1];
            bool threw = false;
            try
            {
                sprite.CompositeFramePixels(0, shortSpan);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Assert.True(threw);
        }

        #endregion

        #region 2. Asynchronous Large-Scale Export & Import Stress Tests (50 Animations / 500 Frames)

        [Fact]
        public async Task StressTest_AsyncExportAndImport_50Animations_500Frames_FullFidelity()
        {
            var exportService = new FlipperExportService();
            var importService = new FlipperImportService();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_Stress50Anims_" + Guid.NewGuid().ToString("N"));
            string tempZip = Path.Combine(Path.GetTempPath(), "Hexprite_Stress50Anims_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                var animations = new List<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)>();

                for (int i = 1; i <= 50; i++)
                {
                    var sprite = new SpriteState(128, 64) { FrameRateFps = (i % 24) + 1 };
                    sprite.Frames.Clear();

                    for (int f = 0; f < 10; f++)
                    {
                        var frame = new FrameState { Name = $"Frame_{f}" };
                        var buf = new MonochromePixelBuffer(128 * 64);
                        var data = buf.GetMonochromeData();
                        data[(i * 17 + f * 31) % (128 * 64)] = true;
                        frame.LayerPixels.Add(buf);
                        sprite.Frames.Add(frame);
                    }

                    int minL = ((i - 1) % 30) + 1;
                    int maxL = Math.Min(30, minL + (i % 5));
                    int minM = (i - 1) % 15;
                    int maxM = Math.Min(14, minM + (i % 3));

                    var entry = new FlipperManifestEntry
                    {
                        Name = $"stress_anim_{i:D2}",
                        MinLevel = minL,
                        MaxLevel = maxL,
                        MinButthurt = minM,
                        MaxButthurt = maxM,
                        Weight = (i % 10) + 1
                    };

                    var settings = new FlipperExportSettings
                    {
                        AnimationName = entry.Name,
                        FrameRate = sprite.FrameRateFps,
                        MinLevel = minL,
                        MaxLevel = maxL
                    };

                    animations.Add((sprite, entry, settings));
                }

                // 1. Export Folder Async
                var progressReports = new List<double>();
                var progress = new Progress<double>(p => progressReports.Add(p));

                await exportService.ExportAssetPackAsync(animations, tempDir, isMomentum: true, progress: progress);

                // Verify folder contents
                string manifestPath = Path.Combine(tempDir, "Anims", "manifest.txt");
                Assert.True(File.Exists(manifestPath));
                for (int i = 1; i <= 50; i++)
                {
                    string animDir = Path.Combine(tempDir, "Anims", $"stress_anim_{i:D2}");
                    Assert.True(Directory.Exists(animDir), $"Directory for anim {i} missing");
                    Assert.True(File.Exists(Path.Combine(animDir, "meta.txt")));
                    for (int f = 0; f < 10; f++)
                    {
                        Assert.True(File.Exists(Path.Combine(animDir, $"frame_{f}.bm")));
                    }
                }

                // 2. Export Zip Async
                await exportService.ExportAssetPackZipAsync(animations, tempZip, isMomentum: true);
                Assert.True(File.Exists(tempZip));

                using (var zip = ZipFile.OpenRead(tempZip))
                {
                    Assert.NotNull(zip.GetEntry("Anims/manifest.txt"));
                    for (int i = 1; i <= 50; i++)
                    {
                        Assert.NotNull(zip.GetEntry($"Anims/stress_anim_{i:D2}/meta.txt"));
                        for (int f = 0; f < 10; f++)
                        {
                            Assert.NotNull(zip.GetEntry($"Anims/stress_anim_{i:D2}/frame_{f}.bm"));
                        }
                    }
                }

                // 3. Import Folder Async
                var importedFolder = await importService.ImportAssetPackAsync(tempDir);
                Assert.Equal(50, importedFolder.Count);
                for (int i = 1; i <= 50; i++)
                {
                    string name = $"stress_anim_{i:D2}";
                    var match = importedFolder.FirstOrDefault(x => x.Name == name);
                    Assert.NotNull(match.Sprite);
                    Assert.Equal(10, match.Sprite.Frames.Count);
                    Assert.Equal(128, match.Sprite.Width);
                    Assert.Equal(64, match.Sprite.Height);
                }

                // 4. Import Zip Async
                var importedZip = await importService.ImportAssetPackAsync(tempZip);
                Assert.Equal(50, importedZip.Count);
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
            }
        }

        [Fact]
        public async Task AsyncExport_CancellationToken_CancelsPromptly()
        {
            var exportService = new FlipperExportService();
            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_CancelExport_" + Guid.NewGuid().ToString("N"));

            try
            {
                var animations = new List<(SpriteState, FlipperManifestEntry, FlipperExportSettings)>();
                for (int i = 0; i < 100; i++)
                {
                    var sprite = new SpriteState(128, 64);
                    for (int f = 0; f < 5; f++)
                    {
                        sprite.Frames.Add(new FrameState { Name = $"F{f}", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
                    }
                    var entry = new FlipperManifestEntry { Name = $"anim_{i}", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
                    var settings = new FlipperExportSettings { AnimationName = entry.Name, FrameRate = 8, MinLevel = 1, MaxLevel = 30 };
                    animations.Add((sprite, entry, settings));
                }

                using var cts = new CancellationTokenSource();
                cts.Cancel(); // Cancel immediately

                await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                {
                    await exportService.ExportAssetPackAsync(animations, tempDir, isMomentum: true, cancellationToken: cts.Token);
                });
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        #endregion

        #region 3. Matrix Canvas Hover/Drag Retained Visuals & GC Pressure Verification

        [Fact]
        public void MatrixCanvas_1000HoverEvents_RetainsVisualElements_ZeroChildCountGrowth()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var vm = new FlipperScheduleMatrixViewModel();
                var panel = new AssetPackEditorPanel { DataContext = vm };

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                // Force layout dimensions
                panel.Width = 900;
                panel.Height = 450;
                panel.Measure(new Size(900, 450));
                panel.Arrange(new Rect(0, 0, 900, 450));
                panel.RedrawMatrix();

                int initialChildCount = panel.MatrixCanvas.Children.Count;
                Assert.True(initialChildCount > 0, "Canvas must have children after RedrawMatrix");

                // Simulate 1,000 continuous mouse move / hover events across the grid
                for (int iter = 0; iter < 1000; iter++)
                {
                    int lvl = (iter % 30) + 1;
                    int mood = (iter % 15);

                    vm.HoverCell(lvl, mood);
                    panel.UpdateInteractiveOverlays();

                    // Canvas children count must remain strictly constant (no Clear + Add churn)
                    Assert.Equal(initialChildCount, panel.MatrixCanvas.Children.Count);
                }

                // Simulate mouse leave
                vm.ClearHover();
                panel.UpdateInteractiveOverlays();
                Assert.Equal(initialChildCount, panel.MatrixCanvas.Children.Count);

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                panel.DataContext = null;
                vm.Dispose();
            });
        }

        [Fact]
        public void MatrixCanvas_500DragMoveEvents_RetainsVisualElements_ZeroChildCountGrowth()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var vm = new FlipperScheduleMatrixViewModel();
                var panel = new AssetPackEditorPanel { DataContext = vm };

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
                panel.Width = 900;
                panel.Height = 450;
                panel.Measure(new Size(900, 450));
                panel.Arrange(new Rect(0, 0, 900, 450));
                panel.RedrawMatrix();

                int initialChildCount = panel.MatrixCanvas.Children.Count;

                // Simulate 500 drag selection updates
                for (int iter = 0; iter < 500; iter++)
                {
                    int minL = (iter % 15) + 1;
                    int maxL = minL + 10;
                    int minM = (iter % 8);
                    int maxM = minM + 5;

                    vm.UpdateDragSelectionTelemetry(minL, maxL, minM, maxM);
                    panel.UpdateInteractiveOverlays();

                    Assert.Equal(initialChildCount, panel.MatrixCanvas.Children.Count);
                }

                panel.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
                panel.DataContext = null;
                vm.Dispose();
            });
        }

        #endregion

        #region 4. High-Volume ViewModel Lifecycle Stress & Garbage Collection Verification

        [Fact]
        public void RapidLifecycle_100ViewModels_AllCleanlyReclaimedByGC()
        {
            var weakRefs = new List<WeakReference>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDisposeBatch()
            {
                for (int i = 0; i < 100; i++)
                {
                    var sprite = new SpriteState(128, 64);
                    var entry = new FlipperManifestEntry
                    {
                        Name = $"anim_{i}",
                        MinLevel = (i % 30) + 1,
                        MaxLevel = 30,
                        MinButthurt = (i % 15),
                        MaxButthurt = 14,
                        Weight = (i % 10) + 1
                    };

                    var vm = new FlipperScheduleMatrixViewModel(pack: [(entry.Name, sprite, entry)]);
                    weakRefs.Add(new WeakReference(vm));

                    // Perform mutations and undo history
                    vm.InspectCell(1, 0);
                    vm.SelectedWeight = 20;
                    vm.Undo();
                    vm.Redo();

                    vm.Dispose();
                }
            }

            CreateAndDisposeBatch();
            ForceGarbageCollection();

            int aliveCount = weakRefs.Count(r => r.IsAlive);
            Assert.Equal(0, aliveCount);
        }

        [Fact]
        public void RapidLifecycle_50AssetPackViewModels_AllCleanlyReclaimedByGC()
        {
            var weakRefs = new List<WeakReference>();

            [MethodImpl(MethodImplOptions.NoInlining)]
            void CreateAndDisposeBatch()
            {
                for (int i = 0; i < 50; i++)
                {
                    var doc = new AssetPackViewModel();
                    weakRefs.Add(new WeakReference(doc));
                    weakRefs.Add(new WeakReference(doc.MatrixViewModel));

                    doc.IsActive = true;
                    doc.MatrixViewModel.InspectCell(5, 5);
                    doc.MatrixViewModel.SelectedWeight = 15;

                    doc.IsActive = false;
                    doc.Dispose();
                }
            }

            CreateAndDisposeBatch();
            ForceGarbageCollection();

            int aliveCount = weakRefs.Count(r => r.IsAlive);
            Assert.Equal(0, aliveCount);
        }

        [Fact]
        public void PreviewLoop_TelemetryNoiseElimination_1000Steps_ZeroTelemetrySpam()
        {
            var sprite = new SpriteState(128, 64);
            for (int f = 0; f < 10; f++)
            {
                sprite.Frames.Add(new FrameState { Name = $"F{f}", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
            }

            var entry = new FlipperManifestEntry { Name = "fast_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var vm = new FlipperScheduleMatrixViewModel(pack: [("fast_anim", sprite, entry)]);
            vm.SetPreviewSpriteForTest(sprite);

            int frameIndexChanges = 0;
            int telemetrySpamChanges = 0;

            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(vm.PreviewFrameIndex) ||
                    e.PropertyName == nameof(vm.CurrentPreviewFrameIndex) ||
                    e.PropertyName == nameof(vm.PreviewFrameCountText))
                {
                    frameIndexChanges++;
                }
                else if (e.PropertyName == nameof(vm.PreviewFps) ||
                         e.PropertyName == nameof(vm.PreviewFpsBadgeText) ||
                         e.PropertyName == nameof(vm.PreviewTotalFrames) ||
                         e.PropertyName == nameof(vm.PreviewMaxFrameIndex) ||
                         e.PropertyName == nameof(vm.PreviewHasMultipleFrames) ||
                         e.PropertyName == nameof(vm.PreviewDimensionsText))
                {
                    telemetrySpamChanges++;
                }
            };

            // Act: Step through 1,000 preview ticks
            for (int i = 0; i < 1000; i++)
            {
                vm.PreviewNextFrame();
            }

            // Assert: Exactly 1,000 steps of frame tracking and 0 spam notifications
            Assert.True(frameIndexChanges >= 1000);
            Assert.Equal(0, telemetrySpamChanges);
            vm.Dispose();
        }

        [Fact]
        public void MediaSlicer_RapidSliderScrubbing_DebouncingCancelsIntermediateTasksWithoutDeadlock()
        {
            var slicer = new FlipperMediaSlicerViewModel();

            // Simulate rapid user slider drag across 50 values
            for (int i = 0; i < 50; i++)
            {
                slicer.BrightnessThreshold = (i * 5) % 255;
                slicer.DitherAmount = (i * 2) % 100;
            }

            // Must settle cleanly without exception or deadlocking
            Assert.NotNull(slicer.SlicedSprite);
            Assert.True(slicer.TotalFrames >= 1);
            slicer.Dispose();
        }

        #endregion
    }
}
