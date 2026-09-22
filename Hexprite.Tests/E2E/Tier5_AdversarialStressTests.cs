using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Resources.Fonts;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests.E2E
{
    /// <summary>
    /// Tier 5: Adversarial Stress Testing & Boundary Verification.
    /// Empirical stress harnesses, fuzzers, and boundary tests covering:
    /// 1. Simulation state machine (high frequency transitions, extreme speed multipliers, zero durations).
    /// 2. Schedule matrix 30x15 heatmap algorithms, deadzone validations, boundary collisions, auto-balancing.
    /// 3. Media slicer aspect ratio calculation, 0x0 / 1x1 image limits, extreme dither amounts.
    /// 4. Serial stream service unexpected disconnections and concurrent cancel commands.
    /// </summary>
    [Trait("Category", "E2E")]
    [Trait("Category", "Unit")]
    public class Tier5_AdversarialStressTests
    {
        public Tier5_AdversarialStressTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        #region 1. Simulation State Machine Adversarial Tests

        [Fact]
        public void Adv_Sim_HighFrequencyTransitions_NoCorruption()
        {
            var sprite = E2ETestHelper.CreateTestSprite(4);
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 2,
                ActiveFrameCount = 2,
                ActiveCycles = 3,
                ActiveCooldown = 4,
                FramesOrder = [0, 1, 2, 3]
            };

            var manifestEntry = new FlipperManifestEntry
            {
                Name = "anim_stress",
                MinLevel = 1,
                MaxLevel = 30,
                MinButthurt = 0,
                MaxButthurt = 14,
                Weight = 5
            };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_stress", sprite, manifestEntry)
            };

            var vm = new FlipperSimulatorViewModel(pack);

            // Execute 5,000 rapid state transitions across all commands
            for (int i = 0; i < 5000; i++)
            {
                switch (i % 6)
                {
                    case 0:
                        vm.AdvanceFrame();
                        break;
                    case 1:
                        vm.StepBack();
                        break;
                    case 2:
                        vm.TriggerActive();
                        break;
                    case 3:
                        vm.AdvanceFrame();
                        break;
                    case 4:
                        vm.ResetAnimationToPassive();
                        break;
                    case 5:
                        vm.RollRng();
                        break;
                }
            }

            // Verify VM remains responsive and in a valid deterministic state
            Assert.NotNull(vm.SelectedCandidate);
            Assert.InRange(vm.SequenceIndex, 0, 10);
            Assert.NotNull(vm.StateBadgeText);
            Assert.NotNull(vm.FrameCounterText);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(-100.0)]
        [InlineData(0.00000001)]
        [InlineData(1000000.0)]
        [InlineData(double.MaxValue)]
        public void Adv_Sim_ExtremeSpeedMultipliers_CalculationsRemainValid(double multiplier)
        {
            var sprite = E2ETestHelper.CreateTestSprite(2);
            var entry = new FlipperManifestEntry { Name = "test", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var vm = new FlipperSimulatorViewModel([("test", sprite, entry)]);

            vm.SpeedMultiplier = multiplier;
            Assert.Equal(multiplier, vm.SpeedMultiplier);

            // Advance frame must not crash regardless of multiplier value
            var ex = Record.Exception(() => vm.AdvanceFrame());
            Assert.Null(ex);
        }

        [Fact]
        public void Adv_Sim_ZeroDurations_ZeroFrames_EmptyCycle_GracefulFallback()
        {
            var emptySprite = new SpriteState(128, 64);
            emptySprite.Frames.Clear();
            emptySprite.FlipperCycle = new FlipperAnimationCycle
            {
                PassiveFrameCount = 0,
                ActiveFrameCount = 0,
                ActiveCycles = 0,
                ActiveCooldown = 0,
                Duration = 0,
                FramesOrder = []
            };

            var entry = new FlipperManifestEntry { Name = "zero_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var vm = new FlipperSimulatorViewModel([("zero_anim", emptySprite, entry)]);

            // Advance and step back with 0 frames must execute cleanly without exceptions
            var exAdvance = Record.Exception(() => vm.AdvanceFrame());
            Assert.Null(exAdvance);

            var exBack = Record.Exception(() => vm.StepBack());
            Assert.Null(exBack);

            var exTrigger = Record.Exception(() => vm.TriggerActive());
            Assert.Null(exTrigger);

            var exReset = Record.Exception(() => vm.ResetAnimationToPassive());
            Assert.Null(exReset);

            var exRender = Record.Exception(() => vm.RenderCurrentFrame());
            Assert.Null(exRender);

            Assert.Equal("Frame 0 / 0", vm.FrameCounterText);
        }

        [Fact]
        public void Adv_Sim_SpeechBubble_ExtremeCoordinatesAndMassiveText()
        {
            var sprite = E2ETestHelper.CreateTestSprite(1);
            var entry = new FlipperManifestEntry { Name = "bubble_test", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var vm = new FlipperSimulatorViewModel([("bubble_test", sprite, entry)]);

            // Test coordinates far beyond bounds - should clamp safely
            vm.BubbleX = -1000;
            Assert.Equal(0, vm.BubbleX);

            vm.BubbleX = 9999;
            Assert.Equal(vm.MaxBubbleX, vm.BubbleX);

            vm.BubbleY = -500;
            Assert.Equal(0, vm.BubbleY);

            vm.BubbleY = 9999;
            Assert.Equal(vm.MaxBubbleY, vm.BubbleY);

            // Test massive text (10,000 characters) and unicode/emoji
            vm.CustomBubbleText = new string('A', 10000) + " 🐬 Special !@#$%^&*()_+ \n\r\t";
            vm.ShowSpeechBubble = true;

            // Render must not throw with massive text
            var ex = Record.Exception(() => vm.RenderCurrentFrame());
            Assert.Null(ex);

            // Test all tail positions
            for (int t = 0; t <= 4; t++)
            {
                vm.BubbleTailIndex = t;
                vm.RenderCurrentFrame();
            }
        }

        [Fact]
        public void Adv_Sim_CandidateWeightRng_ZeroWeightsAndNegativeLevelMood()
        {
            var sprite = E2ETestHelper.CreateTestSprite(1);
            var entryZero = new FlipperManifestEntry { Name = "zero_weight", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 0 };
            var entryHuge = new FlipperManifestEntry { Name = "huge_weight", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1000 };

            var vm = new FlipperSimulatorViewModel([
                ("zero_weight", sprite, entryZero),
                ("huge_weight", sprite, entryHuge)
            ]);

            // Level / mood bounds clamping
            vm.Level = -50;
            Assert.Equal(1, vm.Level);

            vm.Level = 999;
            Assert.Equal(30, vm.Level);

            vm.Mood = -10;
            Assert.Equal(0, vm.Mood);

            vm.Mood = 500;
            Assert.Equal(14, vm.Mood);

            // Roll RNG 100 times - should consistently choose non-zero weight candidate
            for (int i = 0; i < 100; i++)
            {
                vm.RollRng();
                Assert.NotNull(vm.SelectedCandidate);
                Assert.Equal("huge_weight", vm.SelectedCandidate.Name);
            }
        }

        #endregion

        #region 2. Schedule Matrix 30x15 Heatmap & Auto-Balancing Adversarial Tests

        [Fact]
        public void Adv_Schedule_Matrix_ExtremeBounds_ClampingAndIntegrity()
        {
            var entries = new List<FlipperManifestEntry>
            {
                new() { Name = "test", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }
            };

            var matrix = new FlipperScheduleMatrix(entries);

            // Test bounds outside [1..30, 0..14]
            var cellUnder = matrix.GetCell(-100, -50);
            Assert.NotNull(cellUnder);
            Assert.Equal(1, cellUnder.Level);
            Assert.Equal(0, cellUnder.Mood);

            var cellOver = matrix.GetCell(1000, 500);
            Assert.NotNull(cellOver);
            Assert.Equal(30, cellOver.Level);
            Assert.Equal(14, cellOver.Mood);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage);
            Assert.Empty(matrix.GetUncoveredCells());
            Assert.Equal(1, matrix.MaxCollidingAnimations);
        }

        [Fact]
        public void Adv_Schedule_Heatmap_CollisionsAndInvertedRanges()
        {
            // 50 identical entries covering the same slot -> 50 collisions
            var entries = new List<FlipperManifestEntry>();
            for (int i = 0; i < 50; i++)
            {
                entries.Add(new FlipperManifestEntry
                {
                    Name = $"overlap_{i}",
                    MinLevel = 5,
                    MaxLevel = 5,
                    MinButthurt = 2,
                    MaxButthurt = 2,
                    Weight = 1
                });
            }

            // Inverted range entry: Min > Max
            entries.Add(new FlipperManifestEntry
            {
                Name = "inverted",
                MinLevel = 25,
                MaxLevel = 5,
                MinButthurt = 14,
                MaxButthurt = 0,
                Weight = 10
            });

            var matrix = new FlipperScheduleMatrix(entries);

            Assert.Equal(50, matrix.MaxCollidingAnimations);
            Assert.Equal(1, matrix.CoveredCellsCount);
            Assert.Equal(449, matrix.GetUncoveredCells().Count);

            var cell = matrix.GetCell(5, 2);
            Assert.Equal(50, cell.MatchingEntries.Count);
            Assert.Equal(50, cell.TotalWeight);
            Assert.Equal(1.0 / 50.0, cell.GetProbability("overlap_0"), precision: 4);
            Assert.Equal(0.0, cell.GetProbability("non_existent"));
            Assert.Equal(0.0, cell.GetProbability("inverted"));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(25)]
        [InlineData(30)]
        public void Adv_Schedule_AutoBalance_StandardEntryCounts_Guarantees100PercentCoverage(int entryCount)
        {
            var rawEntries = new List<FlipperManifestEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                rawEntries.Add(new FlipperManifestEntry
                {
                    Name = $"anim_{i}",
                    MinLevel = 1,
                    MaxLevel = 1,
                    MinButthurt = 0,
                    MaxButthurt = 0,
                    Weight = (i % 3) + 1
                });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(rawEntries);
            Assert.Equal(entryCount, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced);

            // Must guarantee 100% matrix coverage (450/450 cells) for any standard input count <= 30
            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage, precision: 2);
            Assert.Empty(matrix.GetUncoveredCells());

            // All entries must have valid clamped ranges
            foreach (var e in balanced)
            {
                Assert.InRange(e.MinLevel, 1, 30);
                Assert.InRange(e.MaxLevel, 1, 30);
                Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel}");
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt}");
                Assert.True(e.Weight >= 1, "Weight must be >= 1");
            }
        }

        [Theory]
        [InlineData(35)]
        [InlineData(50)]
        [InlineData(100)]
        [InlineData(150)]
        public void Adv_Schedule_AutoBalance_FuzzAllEntryCounts_Guarantees100PercentCoverage(int entryCount)
        {
            var rawEntries = new List<FlipperManifestEntry>();
            for (int i = 0; i < entryCount; i++)
            {
                rawEntries.Add(new FlipperManifestEntry
                {
                    Name = $"fuzz_anim_{i}",
                    MinLevel = 1,
                    MaxLevel = 1,
                    MinButthurt = 0,
                    MaxButthurt = 0,
                    Weight = (i % 5) + 1
                });
            }

            var balanced = FlipperScheduleMatrix.AutoBalanceEntries(rawEntries);
            Assert.Equal(entryCount, balanced.Count);

            var matrix = new FlipperScheduleMatrix(balanced);

            Assert.Equal(450, matrix.CoveredCellsCount);
            Assert.Equal(100.0, matrix.CoveragePercentage, precision: 2);
            Assert.Empty(matrix.GetUncoveredCells());

            foreach (var e in balanced)
            {
                Assert.InRange(e.MinLevel, 1, 30);
                Assert.InRange(e.MaxLevel, 1, 30);
                Assert.True(e.MinLevel <= e.MaxLevel, $"MinLevel {e.MinLevel} must be <= MaxLevel {e.MaxLevel}");
                Assert.InRange(e.MinButthurt, 0, 14);
                Assert.InRange(e.MaxButthurt, 0, 14);
                Assert.True(e.MinButthurt <= e.MaxButthurt, $"MinButthurt {e.MinButthurt} must be <= MaxButthurt {e.MaxButthurt}");
                Assert.True(e.Weight >= 1, "Weight must be >= 1");
            }
        }

        [Fact]
        public void Adv_Schedule_ViewModel_PropertyUpdate_RecalculationIntegrity()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.NotEmpty(vm.Entries);

            // Change selected entry ranges rapidly
            vm.SelectedMinLevel = -10;
            Assert.Equal(1, vm.SelectedMinLevel);

            vm.SelectedMaxLevel = 100;
            Assert.Equal(30, vm.SelectedMaxLevel);

            vm.SelectedMinButthurt = -5;
            Assert.Equal(0, vm.SelectedMinButthurt);

            vm.SelectedMaxButthurt = 99;
            Assert.Equal(14, vm.SelectedMaxButthurt);

            vm.SelectedWeight = 20;
            Assert.Equal(20, vm.SelectedWeight);
            vm.SelectedWeight = 200;
            Assert.Equal(100, vm.SelectedWeight); // Max weight 100

            // Hover cell boundaries
            vm.HoverCell(-10, -10);
            Assert.Contains("Level 1", vm.CellHoverInfoText);

            vm.HoverCell(50, 50);
            Assert.Contains("Level 30", vm.CellHoverInfoText);

            vm.ClearHover();
            Assert.Contains("Hover over any state", vm.CellHoverInfoText);

            // Trigger auto-balance
            vm.AutoBalanceCommand.Execute(null);
            Assert.Equal("100% Coverage", vm.CoverageBadgeText);
        }

        #endregion

        #region 3. Media Slicer Aspect Ratio & Dithering Adversarial Tests

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 4096)]
        [InlineData(4096, 1)]
        [InlineData(500, 70)]
        [InlineData(32, 32)]
        [InlineData(2048, 128)]
        public void Adv_Slicer_ExtremeDimensions_HandlesSafely(int width, int height)
        {
            var bitmap = E2ETestHelper.CreateTestBitmapSource(width, height);
            var settings = new MediaSliceSettings
            {
                Layout = SpriteSheetLayout.HorizontalStrip,
                FrameWidth = 128,
                FrameHeight = 64,
                MaxFrames = 32,
                DitheringAlgorithm = BitmapDitheringAlgorithm.FloydSteinberg,
                DitherAmount = 100,
                BrightnessThreshold = 128
            };

            var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bitmap, settings);

            Assert.NotNull(sprite);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
            Assert.True(sprite.Frames.Count >= 1, "Must produce at least 1 valid frame");
            Assert.NotEmpty(sprite.Frames[0].LayerPixels);
            Assert.Equal(128 * 64, sprite.Frames[0].LayerPixels[0].GetMonochromeData().Length);
        }

        [Fact]
        public void Adv_Slicer_AllDitheringAlgorithms_WithExtremeParameters()
        {
            var bitmap = E2ETestHelper.CreateTestBitmapSource(256, 128);
            var algorithms = (BitmapDitheringAlgorithm[])Enum.GetValues(typeof(BitmapDitheringAlgorithm));

            int[] ditherAmounts = [-100, 0, 50, 100, 200, 1000];
            int[] thresholds = [-50, 0, 128, 255, 300];

            foreach (var algo in algorithms)
            {
                foreach (int amount in ditherAmounts)
                {
                    foreach (int thresh in thresholds)
                    {
                        var settings = new MediaSliceSettings
                        {
                            Layout = SpriteSheetLayout.HorizontalStrip,
                            FrameWidth = 128,
                            FrameHeight = 64,
                            DitheringAlgorithm = algo,
                            DitherAmount = amount,
                            BrightnessThreshold = thresh,
                            InvertColors = true
                        };

                        var sprite = FlipperMediaSlicerService.SliceToAnimationSprite(bitmap, settings);
                        Assert.NotNull(sprite);
                        Assert.Equal(2, sprite.Frames.Count);
                    }
                }
            }
        }

        [Fact]
        public void Adv_Slicer_ViewModel_StepForwardBackward_BoundaryWrap()
        {
            using var vm = new FlipperMediaSlicerViewModel();

            // Slicer starts with sample 4-frame image
            Assert.Equal(4, vm.TotalFrames);
            Assert.Equal(0, vm.CurrentFrameIndex);

            // Step backward wraps to frame 4 (index 3)
            vm.StepBackward();
            Assert.Equal(3, vm.CurrentFrameIndex);
            Assert.False(vm.IsPlaying);

            // Step forward wraps to frame 1 (index 0)
            vm.StepForward();
            Assert.Equal(0, vm.CurrentFrameIndex);

            // Advance frame under playing state
            vm.IsPlaying = true;
            vm.AdvanceFrame();
            Assert.Equal(1, vm.CurrentFrameIndex);

            // Test with single frame sprite
            var singleSprite = E2ETestHelper.CreateTestSprite(1);
            using var vmSingle = new FlipperMediaSlicerViewModel(initialSprite: singleSprite);
            Assert.Equal(1, vmSingle.TotalFrames);
            Assert.Equal(0, vmSingle.CurrentFrameIndex);

            vmSingle.StepBackward();
            Assert.Equal(0, vmSingle.CurrentFrameIndex);

            vmSingle.StepForward();
            Assert.Equal(0, vmSingle.CurrentFrameIndex);
        }

        #endregion

        #region 4. Serial Stream Service Disconnections & Concurrency Adversarial Tests

        [Fact]
        public void Adv_Serial_Encode1024Buffer_StressAndBitPatterns()
        {
            // Null check
            Assert.Throws<ArgumentNullException>(() => FlipperScreenStreamService.Encode1024Buffer(null!));

            // Undersized array (< 8192) - should not crash, returns 1024 bytes
            bool[] undersized = new bool[100];
            undersized[0] = true;
            byte[] bufUnder = FlipperScreenStreamService.Encode1024Buffer(undersized);
            Assert.Equal(1024, bufUnder.Length);
            Assert.Equal(0x01, bufUnder[0]); // (0,0) -> bit 0

            // Oversized array (> 8192)
            bool[] oversized = new bool[50000];
            oversized[0] = true;
            oversized[8191] = true; // (127, 63) -> Page 7, col 127, bit 7
            byte[] bufOver = FlipperScreenStreamService.Encode1024Buffer(oversized);
            Assert.Equal(1024, bufOver.Length);
            Assert.Equal(0x01, bufOver[0]);
            Assert.Equal(0x80, bufOver[1023]);

            // Checkerboard pattern exact roundtrip decode verification
            bool[] checkerboard = new bool[128 * 64];
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    checkerboard[y * 128 + x] = (x + y) % 2 == 0;
                }
            }

            byte[] encoded = FlipperScreenStreamService.Encode1024Buffer(checkerboard);
            Assert.Equal(1024, encoded.Length);

            bool[] decoded = E2ETestHelper.Decode1024Buffer(encoded);
            Assert.Equal(checkerboard.Length, decoded.Length);
            for (int i = 0; i < checkerboard.Length; i++)
            {
                Assert.Equal(checkerboard[i], decoded[i]);
            }
        }

        [Fact]
        public void Adv_Serial_UnexpectedDisconnections_LifecycleSafety()
        {
            using var service = new FlipperScreenStreamService();

            Assert.False(service.IsConnected);
            Assert.False(service.IsStreaming);
            Assert.Null(service.CurrentPort);
            Assert.Equal(0, service.CurrentFps);
            Assert.Equal(0, service.TotalFramesSent);

            // Sending frames while disconnected returns false safely without throwing
            bool sent = service.SendSingleFrame(new bool[128 * 64]);
            Assert.False(sent);

            bool sentSprite = service.SendSingleFrame(E2ETestHelper.CreateTestSprite(1));
            Assert.False(sentSprite);

            bool sentNull = service.SendSingleFrame((bool[])null!);
            Assert.False(sentNull);

            // Redundant disconnect & stop streaming
            var exDisc = Record.Exception(() => service.Disconnect());
            Assert.Null(exDisc);

            var exStop = Record.Exception(() => service.StopStreaming());
            Assert.Null(exStop);
        }

        [Fact]
        public void Adv_Serial_ConcurrentStreamingAndCancellation_ThreadSafety()
        {
            using var service = new FlipperScreenStreamService();
            const int threadCount = 10;
            const int operationsPerThread = 100;
            var exceptions = new List<Exception>();
            var lockObj = new object();

            var threads = new List<Thread>();

            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                var thread = new Thread(() =>
                {
                    try
                    {
                        var localRng = new Random(threadId * 1000);
                        for (int op = 0; op < operationsPerThread; op++)
                        {
                            int action = localRng.Next(5);
                            switch (action)
                            {
                                case 0:
                                    service.StartStreaming(() => new bool[128 * 64], targetFps: 30);
                                    break;
                                case 1:
                                    service.StopStreaming();
                                    break;
                                case 2:
                                    service.SendSingleFrame(new bool[128 * 64]);
                                    break;
                                case 3:
                                    service.Disconnect();
                                    break;
                                case 4:
                                    _ = service.IsConnected;
                                    _ = service.IsStreaming;
                                    _ = service.CurrentFps;
                                    _ = service.TotalFramesSent;
                                    break;
                            }
                            Thread.Sleep(1);
                        }
                    }
                    catch (Exception ex)
                    {
                        lock (lockObj)
                        {
                            exceptions.Add(ex);
                        }
                    }
                });

                threads.Add(thread);
                thread.Start();
            }

            foreach (var thread in threads)
            {
                bool finished = thread.Join(5000);
                Assert.True(finished, "Thread failed to terminate (deadlock detected)");
            }

            Assert.Empty(exceptions);
            Assert.False(service.IsConnected);
            Assert.False(service.IsStreaming);
        }

        #endregion

        #region 5. Schedule Matrix Undo/Redo & State Mutation Invariant Fuzzing

        [Fact]
        public void Adv_Schedule_RandomizedMutationAndDeepUndoRedo_InvariantsHold()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var initialEntries = vm.Entries.Select(e => new FlipperManifestEntry
            {
                Name = e.Name,
                MinLevel = e.MinLevel,
                MaxLevel = e.MaxLevel,
                MinButthurt = e.MinButthurt,
                MaxButthurt = e.MaxButthurt,
                Weight = e.Weight
            }).ToList();

            var rng = new Random(1337);
            int operationCount = 200;
            int successfulEdits = 0;

            for (int step = 0; step < operationCount; step++)
            {
                int action = rng.Next(7);
                switch (action)
                {
                    case 0: // Drag/Set Selected Entry Bounds
                        if (vm.Entries.Count > 0)
                        {
                            vm.SelectedEntry = vm.Entries[rng.Next(vm.Entries.Count)];
                            int minL = rng.Next(1, vm.MaxAllowedLevel + 1);
                            int maxL = rng.Next(minL, vm.MaxAllowedLevel + 1);
                            int minM = rng.Next(0, 15);
                            int maxM = rng.Next(minM, 15);
                            vm.SetSelectedEntryBounds(minL, maxL, minM, maxM);
                            successfulEdits++;
                        }
                        break;

                    case 1: // Inspect Cell
                        int inspL = rng.Next(-5, 40);
                        int inspM = rng.Next(-5, 20);
                        vm.InspectCell(inspL, inspM);
                        Assert.InRange(vm.SelectedCellLevel, 1, vm.MaxAllowedLevel);
                        Assert.InRange(vm.SelectedCellMood, 0, 14);
                        break;

                    case 2: // Change Weight
                        if (vm.SelectedEntry != null)
                        {
                            vm.SelectedWeight = rng.Next(1, 100);
                            successfulEdits++;
                        }
                        break;

                    case 3: // Add entry
                        if (vm.Entries.Count < 20)
                        {
                            vm.AddEntryCommand.Execute(null);
                            successfulEdits++;
                        }
                        break;

                    case 4: // Auto-balance
                        vm.AutoBalanceCommand.Execute(null);
                        successfulEdits++;
                        Assert.Equal(vm.IsStockMode ? 45 : 450, vm.Matrix.CoveredCellsCount);
                        break;

                    case 5: // Toggle Stock/Extended Mode
                        vm.ToggleModeCommand.Execute(null);
                        successfulEdits++;
                        break;

                    case 6: // Fix diagnostics if any
                        if (vm.HasActionableDiagnostics)
                        {
                            vm.FixAllDiagnosticsCommand.Execute(null);
                            successfulEdits++;
                        }
                        break;
                }

                // Invariant checks at every mutation step
                Assert.InRange(vm.Matrix.CoveredCellsCount, 0, vm.IsStockMode ? 45 : 450);
                Assert.True(vm.MaxAllowedLevel == 3 || vm.MaxAllowedLevel == 30);
                Assert.All(vm.Entries, e =>
                {
                    Assert.InRange(e.MinLevel, 1, vm.MaxAllowedLevel);
                    Assert.InRange(e.MaxLevel, 1, vm.MaxAllowedLevel);
                    Assert.True(e.MinLevel <= e.MaxLevel);
                    Assert.InRange(e.MinButthurt, 0, 14);
                    Assert.InRange(e.MaxButthurt, 0, 14);
                    Assert.True(e.MinButthurt <= e.MaxButthurt);
                    Assert.True(e.Weight >= 1);
                });
            }

            // Deep Undo all operations
            int undoSteps = 0;
            while (vm.CanUndo && undoSteps < 500)
            {
                vm.Undo();
                undoSteps++;

                Assert.InRange(vm.Matrix.CoveredCellsCount, 0, vm.IsStockMode ? 45 : 450);
                Assert.True(vm.MaxAllowedLevel == 3 || vm.MaxAllowedLevel == 30);
            }

            // Verify clean return to initial state
            Assert.Equal(initialEntries.Count, vm.Entries.Count);
            for (int i = 0; i < initialEntries.Count; i++)
            {
                Assert.Equal(initialEntries[i].Name, vm.Entries[i].Name);
                Assert.Equal(initialEntries[i].MinLevel, vm.Entries[i].MinLevel);
                Assert.Equal(initialEntries[i].MaxLevel, vm.Entries[i].MaxLevel);
                Assert.Equal(initialEntries[i].MinButthurt, vm.Entries[i].MinButthurt);
                Assert.Equal(initialEntries[i].MaxButthurt, vm.Entries[i].MaxButthurt);
                Assert.Equal(initialEntries[i].Weight, vm.Entries[i].Weight);
            }

            // Deep Redo all operations
            int redoSteps = 0;
            while (vm.CanRedo && redoSteps < 500)
            {
                vm.Redo();
                redoSteps++;

                Assert.InRange(vm.Matrix.CoveredCellsCount, 0, vm.IsStockMode ? 45 : 450);
                Assert.True(vm.MaxAllowedLevel == 3 || vm.MaxAllowedLevel == 30);
            }

            Assert.Equal(undoSteps, redoSteps);
        }

        [Fact]
        public void Adv_Schedule_CellProbabilityDistribution_SumEqualsOneInvariant()
        {
            var rng = new Random(42);

            // Generate 100 randomized matrix configurations
            for (int config = 0; config < 100; config++)
            {
                int entryCount = rng.Next(1, 15);
                var entries = new List<FlipperManifestEntry>();

                for (int e = 0; e < entryCount; e++)
                {
                    int minL = rng.Next(1, 31);
                    int maxL = rng.Next(minL, 31);
                    int minM = rng.Next(0, 15);
                    int maxM = rng.Next(minM, 15);
                    int weight = rng.Next(1, 100);

                    entries.Add(new FlipperManifestEntry
                    {
                        Name = $"anim_{e}",
                        MinLevel = minL,
                        MaxLevel = maxL,
                        MinButthurt = minM,
                        MaxButthurt = maxM,
                        Weight = weight
                    });
                }

                var matrix = new FlipperScheduleMatrix(entries);

                for (int lvl = 1; lvl <= 30; lvl++)
                {
                    for (int mood = 0; mood <= 14; mood++)
                    {
                        var cell = matrix.GetCell(lvl, mood);
                        if (cell.HasCoverage)
                        {
                            double totalProb = 0;
                            foreach (var matching in cell.MatchingEntries)
                            {
                                double p = cell.GetProbability(matching.Name);
                                Assert.True(p > 0.0 && p <= 1.0, $"Individual prob {p} must be in (0, 1]");
                                totalProb += p;
                            }
                            Assert.True(Math.Abs(totalProb - 1.0) < 1e-6, $"Probability sum for L{lvl} M{mood} was {totalProb}, expected 1.0");
                        }
                        else
                        {
                            Assert.Empty(cell.MatchingEntries);
                            Assert.Equal(0, cell.TotalWeight);
                            Assert.Equal(0.0, cell.GetProbability("any"));
                        }
                    }
                }
            }
        }

        [Fact]
        public void Adv_Schedule_QuickFixDiagnostics_FuzzAndRepair_ReachesZeroErrors()
        {
            var rng = new Random(999);
            var exportService = new FlipperExportService();

            for (int trial = 0; trial < 50; trial++)
            {
                var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
                int count = rng.Next(2, 8);

                for (int i = 0; i < count; i++)
                {
                    // Randomly introduce errors: duplicate names, invalid levels/moods, out-of-range weights
                    string name = rng.Next(3) == 0 ? "duplicate_name" : $"anim_{i}_{trial}";
                    int minL = rng.Next(1, 40);
                    int maxL = rng.Next(1, 40);
                    int minM = rng.Next(0, 20);
                    int maxM = rng.Next(0, 20);
                    int weight = rng.Next(-5, 150);

                    var entry = new FlipperManifestEntry
                    {
                        Name = name,
                        MinLevel = minL,
                        MaxLevel = maxL,
                        MinButthurt = minM,
                        MaxButthurt = maxM,
                        Weight = weight
                    };

                    pack.Add((name, E2ETestHelper.CreateTestSprite(1), entry));
                }

                var vm = new FlipperScheduleMatrixViewModel(pack, exportService: exportService);

                if (vm.HasActionableDiagnostics)
                {
                    vm.FixAllDiagnosticsCommand.Execute(null);

                    // After executing FixAll, all errors must be completely resolved
                    Assert.DoesNotContain(vm.ValidationDiagnostics, d => d.Severity == FlipperValidationSeverity.Error);
                    Assert.All(vm.Entries, e =>
                    {
                        Assert.InRange(e.MinLevel, 1, vm.MaxAllowedLevel);
                        Assert.InRange(e.MaxLevel, 1, vm.MaxAllowedLevel);
                        Assert.True(e.MinLevel <= e.MaxLevel);
                        Assert.InRange(e.MinButthurt, 0, 14);
                        Assert.InRange(e.MaxButthurt, 0, 14);
                        Assert.True(e.MinButthurt <= e.MaxButthurt);
                        Assert.InRange(e.Weight, 1, 100);
                        Assert.False(string.IsNullOrWhiteSpace(e.Name));
                    });
                }
            }
        }

        #endregion
    }
}
