using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperMatrixStressTests
    {
        private class MockTabServiceForStress : IWorkspaceTabService
        {
            public List<(string Title, SpriteState Sprite)> OpenSprites { get; } = [];
            public string? LastActivatedTab { get; private set; }
            public string? LastOpenedTab { get; private set; }
            public string? LastRenamedOldTitle { get; private set; }
            public string? LastRenamedNewTitle { get; private set; }
            public SpriteState? ActiveSprite { get; set; }
            public string? ActiveSpriteTitle { get; set; }

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported") { }
            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites) { }
            public void OpenSpriteInTab(SpriteState sprite, string title)
            {
                LastOpenedTab = title;
                OpenSprites.Add((title, sprite));
            }
            public SpriteState? GetActiveSpriteState() => ActiveSprite;
            public (string Title, SpriteState Sprite)? GetActiveSprite() => ActiveSprite != null ? (ActiveSpriteTitle ?? "ActiveTab", ActiveSprite) : null;
            public bool[]? GetActiveFramePixels(bool animated = false) => null;
            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => OpenSprites;
            public bool ActivateTabByTitle(string title)
            {
                LastActivatedTab = title;
                return OpenSprites.Any(s => string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
            }
            public bool RenameTab(string oldTitle, string newTitle)
            {
                LastRenamedOldTitle = oldTitle;
                LastRenamedNewTitle = newTitle;
                int idx = OpenSprites.FindIndex(s => string.Equals(s.Title, oldTitle, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    var sprite = OpenSprites[idx].Sprite;
                    OpenSprites[idx] = (newTitle, sprite);
                    return true;
                }
                return false;
            }
            public void OpenAssetPackInTab(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack") { }
        }

        #region 1. FlipperCycle.FramesOrder Sequence Playback Stress Tests

        [Fact]
        public void FramesOrder_ComplexLoopSequence_NavigatesAndRendersCorrectly()
        {
            // 4 physical frames: F0, F1, F2, F3
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            for (int i = 0; i < 4; i++)
            {
                sprite.Frames.Add(new FrameState { Name = $"F{i}" });
            }
            sprite.EnsureLayers();
            for (int i = 0; i < 4; i++)
            {
                sprite.Frames[i].LayerPixels[0].GetMonochromeData()[i * 128 + i] = true;
            }

            // Complex non-linear sequence: 0, 1, 2, 1, 0, 3, 2, 0, 1 (length 9)
            int[] complexOrder = [0, 1, 2, 1, 0, 3, 2, 0, 1];
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = complexOrder,
                PassiveFrameCount = 5,
                ActiveFrameCount = 4
            };

            var entry = new FlipperManifestEntry { Name = "complex_cycle", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("complex_cycle", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            Assert.Equal("Frame 1 / 9", vm.PreviewFrameCountText);
            Assert.Equal(0, vm.PreviewFrameIndex);

            // Step forward through all 9 steps
            for (int step = 0; step < complexOrder.Length; step++)
            {
                Assert.Equal(step, vm.PreviewFrameIndex);
                Assert.Equal($"Frame {step + 1} / 9", vm.PreviewFrameCountText);

                // Advance
                if (step < complexOrder.Length - 1)
                {
                    vm.PreviewNextFrameCommand.Execute(null);
                }
            }

            // Wrap forward around to step 0
            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(0, vm.PreviewFrameIndex);
            Assert.Equal("Frame 1 / 9", vm.PreviewFrameCountText);

            // Wrap backwards around to step 8
            vm.PreviewPrevFrameCommand.Execute(null);
            Assert.Equal(8, vm.PreviewFrameIndex);
            Assert.Equal("Frame 9 / 9", vm.PreviewFrameCountText);

            // Step backwards through the sequence
            for (int step = complexOrder.Length - 1; step >= 0; step--)
            {
                Assert.Equal(step, vm.PreviewFrameIndex);
                if (step > 0)
                {
                    vm.PreviewPrevFrameCommand.Execute(null);
                }
            }
            Assert.Equal(0, vm.PreviewFrameIndex);
        }

        [Fact]
        public void FramesOrder_OutOfBoundsIndices_ClampsSafelyWithoutCrashing()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0" });
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });

            // Severely out-of-bounds indices: negative, huge positive, int.MinValue, int.MaxValue
            int[] badOrder = [-999, 1000, int.MinValue, int.MaxValue, 1, 0, -1, 5];
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = badOrder
            };

            var entry = new FlipperManifestEntry { Name = "oob_cycle", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("oob_cycle", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal("Frame 1 / 8", vm.PreviewFrameCountText);

            // Step through all out of bounds steps and ensure no exceptions
            for (int i = 0; i < badOrder.Length * 2; i++)
            {
                var ex = Record.Exception(() =>
                {
                    vm.RenderPreviewFrame();
                    vm.PreviewNextFrameCommand.Execute(null);
                });
                Assert.Null(ex);
            }

            // Step backward through all out of bounds steps
            for (int i = 0; i < badOrder.Length * 2; i++)
            {
                var ex = Record.Exception(() =>
                {
                    vm.RenderPreviewFrame();
                    vm.PreviewPrevFrameCommand.Execute(null);
                });
                Assert.Null(ex);
            }
        }

        [Fact]
        public void FramesOrder_EmptyAndNullArrays_FallsBackSafely()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0" });
            sprite.Frames.Add(new FrameState { Name = "F1" });

            // 1. Empty array
            sprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [] };
            var entry = new FlipperManifestEntry { Name = "empty_order", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> { ("empty_order", sprite, entry) };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal("Frame 1 / 2", vm.PreviewFrameCountText);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal("Frame 2 / 2", vm.PreviewFrameCountText);
            Assert.Equal(1, vm.PreviewFrameIndex);

            // 2. Null FramesOrder
            sprite.FlipperCycle.FramesOrder = null!;
            vm.RefreshCurrentPreviewSprite();
            Assert.Equal("Frame 1 / 2", vm.PreviewFrameCountText);

            vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal("Frame 2 / 2", vm.PreviewFrameCountText);

            // 3. Null FlipperCycle
            sprite.FlipperCycle = null;
            vm.RefreshCurrentPreviewSprite();
            Assert.Equal("Frame 1 / 2", vm.PreviewFrameCountText);
        }

        [Fact]
        public void FramesOrder_ZeroFramesInSprite_RendersBlankPlaceholderSafely()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 1, 2] };

            var entry = new FlipperManifestEntry { Name = "zero_frames", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> { ("zero_frames", sprite, entry) };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            Assert.Equal("1 Frame (Placeholder)", vm.PreviewFrameCountText);

            var ex1 = Record.Exception(() => vm.PreviewNextFrameCommand.Execute(null));
            var ex2 = Record.Exception(() => vm.PreviewPrevFrameCommand.Execute(null));
            var ex3 = Record.Exception(() => vm.RenderPreviewFrame());

            Assert.Null(ex1);
            Assert.Null(ex2);
            Assert.Null(ex3);
        }

        [Fact]
        public void FramesOrder_SwitchingSelectionResetsFrameIndexAcrossDifferentLengths()
        {
            var spriteLong = new SpriteState(128, 64);
            spriteLong.Frames.Clear();
            for (int i = 0; i < 10; i++) spriteLong.Frames.Add(new FrameState { Name = $"F{i}" });
            spriteLong.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9] };

            var spriteShort = new SpriteState(128, 64);
            spriteShort.Frames.Clear();
            spriteShort.Frames.Add(new FrameState { Name = "F0" });
            spriteShort.Frames.Add(new FrameState { Name = "F1" });
            spriteShort.FlipperCycle = new FlipperAnimationCycle { FramesOrder = [0, 1] };

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_long", spriteLong, new FlipperManifestEntry { Name = "anim_long", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("anim_short", spriteShort, new FlipperManifestEntry { Name = "anim_short", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);
            vm.SelectedEntry = vm.Entries[0]; // anim_long
            Assert.Equal("Frame 1 / 10", vm.PreviewFrameCountText);

            // Advance to step 8
            for (int i = 0; i < 8; i++) vm.PreviewNextFrameCommand.Execute(null);
            Assert.Equal(8, vm.PreviewFrameIndex);
            Assert.Equal("Frame 9 / 10", vm.PreviewFrameCountText);

            // Switch to anim_short (which only has 2 steps)
            vm.SelectedEntry = vm.Entries[1]; // anim_short

            // Verify preview frame index was cleanly reset to 0 instead of staying at 8 (which would be out of bounds)
            Assert.Equal(0, vm.PreviewFrameIndex);
            Assert.Equal("Frame 1 / 2", vm.PreviewFrameCountText);

            var ex = Record.Exception(() => vm.RenderPreviewFrame());
            Assert.Null(ex);
        }

        [Fact]
        public void FramesOrder_FuzzTesting_RandomSequences_NeverThrows()
        {
            var rand = new Random(42);
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            for (int i = 0; i < 5; i++) sprite.Frames.Add(new FrameState { Name = $"F{i}" });

            for (int trial = 0; trial < 50; trial++)
            {
                int orderLength = rand.Next(0, 30);
                int[] randomOrder = new int[orderLength];
                for (int i = 0; i < orderLength; i++)
                {
                    randomOrder[i] = rand.Next(-50, 50);
                }

                sprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = randomOrder };
                var entry = new FlipperManifestEntry { Name = $"fuzz_{trial}", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
                var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> { ($"fuzz_{trial}", sprite, entry) };

                var vm = new FlipperScheduleMatrixViewModel(pack);

                for (int step = 0; step < 20; step++)
                {
                    if (rand.NextDouble() > 0.5) vm.PreviewNextFrameCommand.Execute(null);
                    else vm.PreviewPrevFrameCommand.Execute(null);

                    var ex = Record.Exception(() => vm.RenderPreviewFrame());
                    Assert.Null(ex);
                }
            }
        }

        #endregion

        #region 2. Bidirectional Tab Renaming & Sync Stress Tests

        [Fact]
        public void TabRenaming_WithWhitespaceAndAsterisks_SanitizesAndUpdatesTabService()
        {
            var mockTabService = new MockTabServiceForStress();
            var sprite = new SpriteState(128, 64);
            mockTabService.OpenSprites.Add(("walk_anim", sprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);
            vm.SelectedName = "walk_anim";

            // Rename with leading asterisk and whitespace
            vm.SelectedName = "  *walk_anim_v2  ";

            Assert.Equal("walk_anim_v2", mockTabService.LastRenamedNewTitle);
            Assert.Equal("walk_anim", mockTabService.LastRenamedOldTitle);
            Assert.True(vm.AnimationSprites.ContainsKey("walk_anim_v2"));
            Assert.False(vm.AnimationSprites.ContainsKey("walk_anim"));
        }

        [Fact]
        public void TabRenaming_NonExistentOldName_HandlesSafely()
        {
            var mockTabService = new MockTabServiceForStress();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            // Rename entry that was never in tab service
            var ex = Record.Exception(() => vm.SelectedName = "new_standalone_name");
            Assert.Null(ex);
            Assert.Equal("new_standalone_name", vm.SelectedName);
        }

        [Fact]
        public void TabRenaming_EmptyOrNullNewName_SetsValidationStatusWithoutCrashing()
        {
            var mockTabService = new MockTabServiceForStress();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            vm.SelectedName = "";
            Assert.True(vm.HasValidationIssues);
            Assert.Contains("Error", vm.ValidationStatusText);

            vm.SelectedName = "   ";
            Assert.True(vm.HasValidationIssues);
        }

        [Fact]
        public void TabRenaming_RenamingToDuplicateName_TriggersValidationDiagnostics()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.Equal(3, vm.Entries.Count);
            string name1 = vm.Entries[0].Name; // anim_baby
            string name2 = vm.Entries[1].Name; // anim_teen

            vm.SelectedEntry = vm.Entries[1];
            vm.SelectedName = name1; // duplicate name

            Assert.True(vm.HasValidationIssues);
            Assert.Contains("Duplicate", vm.ValidationStatusText);
            Assert.Contains(vm.ValidationDiagnostics, d => d.Message.Contains("Duplicate animation name"));
        }

        #endregion

        #region 3. Real-Time LCD Preview & IsActive Sync Stress Tests

        [Fact]
        public void IsActive_SwitchingToggles_RefreshesLiveCanvasEdits()
        {
            var mockTabService = new MockTabServiceForStress();
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F1" });

            mockTabService.OpenSprites.Add(("anim_baby", sprite));

            var entry = new FlipperManifestEntry { Name = "anim_baby", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> { ("anim_baby", sprite, entry) };

            var docVm = new AssetPackViewModel(pack: pack, tabService: mockTabService);
            Assert.Equal("Frame 1 / 1", docVm.MatrixViewModel.PreviewFrameCountText);

            // User edits canvas tab, adding 2 more frames
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.Frames.Add(new FrameState { Name = "F3" });

            // Switch to AssetPack tab (IsActive = true)
            docVm.IsActive = true;

            // Preview count should immediately reflect the 3 frames
            Assert.Equal("Frame 1 / 3", docVm.MatrixViewModel.PreviewFrameCountText);
        }

        [Fact]
        public void IsActive_WhenTabWasClosed_RetainsEditsFromInternalCache()
        {
            var mockTabService = new MockTabServiceForStress();
            var vm = new FlipperScheduleMatrixViewModel(tabService: mockTabService);

            var entry = vm.Entries[0];
            vm.NavigateToEntryTab(entry);

            // Simulate editing sprite in cache
            var cached = vm.AnimationSprites[entry.Name];
            cached.Frames.Add(new FrameState { Name = "F2" });
            cached.Frames.Add(new FrameState { Name = "F3" });

            // Simulate tab close (empty OpenSprites)
            mockTabService.OpenSprites.Clear();

            var docVm = new AssetPackViewModel(vm);
            docVm.IsActive = true;

            Assert.Equal("Frame 1 / 3", docVm.MatrixViewModel.PreviewFrameCountText);
            Assert.Equal(3, vm.AnimationSprites[entry.Name].Frames.Count);
        }

        [Fact]
        public void IsActive_RapidToggling_DoesNotThrow()
        {
            var mockTabService = new MockTabServiceForStress();
            var docVm = new AssetPackViewModel(tabService: mockTabService);

            for (int i = 0; i < 50; i++)
            {
                docVm.IsActive = true;
                docVm.IsActive = false;
            }

            Assert.False(docVm.IsActive);
        }
        #endregion

        #region 5. Milestone 4 Undo/Redo & State Clamping Stress Tests

        [Fact]
        public void UndoRedo_Rapid50StepSequence_MaintainsExactStateFidelity()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var snapshots = new List<(string Name, int MinL, int MaxL, int MinM, int MaxM, int Weight, bool Stock, string Pack)>();

            // Capture initial state
            snapshots.Add((
                vm.Entries[0].Name,
                vm.Entries[0].MinLevel,
                vm.Entries[0].MaxLevel,
                vm.Entries[0].MinButthurt,
                vm.Entries[0].MaxButthurt,
                vm.Entries[0].Weight,
                vm.IsStockMode,
                vm.PackName
            ));

            // Execute 50 sequential mutating operations
            for (int i = 1; i <= 50; i++)
            {
                vm.SelectedEntry = vm.Entries[0];
                switch (i % 6)
                {
                    case 1:
                        vm.SelectedMinLevel = vm.SelectedMinLevel == 1 ? 2 : 1;
                        break;
                    case 2:
                        vm.SelectedMaxLevel = vm.SelectedMaxLevel == 30 ? 25 : 30;
                        break;
                    case 3:
                        vm.SelectedMinButthurt = vm.SelectedMinButthurt == 0 ? 3 : 0;
                        break;
                    case 4:
                        vm.SelectedMaxButthurt = vm.SelectedMaxButthurt == 14 ? 10 : 14;
                        break;
                    case 5:
                        vm.SelectedWeight = vm.SelectedWeight == 1 ? 15 : 1;
                        break;
                    case 0:
                        vm.PackName = $"Pack_Iteration_{i}";
                        break;
                }

                snapshots.Add((
                    vm.Entries[0].Name,
                    vm.Entries[0].MinLevel,
                    vm.Entries[0].MaxLevel,
                    vm.Entries[0].MinButthurt,
                    vm.Entries[0].MaxButthurt,
                    vm.Entries[0].Weight,
                    vm.IsStockMode,
                    vm.PackName
                ));
            }

            Assert.True(vm.CanUndo);

            // Execute 50 sequential Undo operations in reverse and verify exact state match
            for (int i = 50; i >= 1; i--)
            {
                vm.Undo();
                var expected = snapshots[i - 1];
                Assert.Equal(expected.Name, vm.Entries[0].Name);
                Assert.Equal(expected.MinL, vm.Entries[0].MinLevel);
                Assert.Equal(expected.MaxL, vm.Entries[0].MaxLevel);
                Assert.Equal(expected.MinM, vm.Entries[0].MinButthurt);
                Assert.Equal(expected.MaxM, vm.Entries[0].MaxButthurt);
                Assert.Equal(expected.Weight, vm.Entries[0].Weight);
                Assert.Equal(expected.Stock, vm.IsStockMode);
                Assert.Equal(expected.Pack, vm.PackName);
            }

            Assert.False(vm.CanUndo);
            Assert.True(vm.CanRedo);

            // Execute 50 sequential Redo operations forward and verify exact state match
            for (int i = 1; i <= 50; i++)
            {
                vm.Redo();
                var expected = snapshots[i];
                Assert.Equal(expected.Name, vm.Entries[0].Name);
                Assert.Equal(expected.MinL, vm.Entries[0].MinLevel);
                Assert.Equal(expected.MaxL, vm.Entries[0].MaxLevel);
                Assert.Equal(expected.MinM, vm.Entries[0].MinButthurt);
                Assert.Equal(expected.MaxM, vm.Entries[0].MaxButthurt);
                Assert.Equal(expected.Weight, vm.Entries[0].Weight);
                Assert.Equal(expected.Stock, vm.IsStockMode);
                Assert.Equal(expected.Pack, vm.PackName);
            }

            Assert.False(vm.CanRedo);
        }

        [Fact]
        public void StateClamping_FuzzMatrixBoundsAndWeights_NeverProducesCorruptedGrid()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var rand = new Random(42);

            for (int trial = 0; trial < 100; trial++)
            {
                int rawMinL = rand.Next(-100, 200);
                int rawMaxL = rand.Next(-100, 200);
                int rawMinM = rand.Next(-100, 100);
                int rawMaxM = rand.Next(-100, 100);
                int rawWeight = rand.Next(-50, 500);

                vm.SetSelectedEntryBounds(rawMinL, rawMaxL, rawMinM, rawMaxM);
                vm.SelectedWeight = rawWeight;

                // Validate invariants
                Assert.InRange(vm.SelectedEntry!.MinLevel, 1, vm.MaxAllowedLevel);
                Assert.InRange(vm.SelectedEntry.MaxLevel, 1, vm.MaxAllowedLevel);
                Assert.True(vm.SelectedEntry.MinLevel <= vm.SelectedEntry.MaxLevel);
                Assert.InRange(vm.SelectedEntry.MinButthurt, 0, 14);
                Assert.InRange(vm.SelectedEntry.MaxButthurt, 0, 14);
                Assert.True(vm.SelectedEntry.MinButthurt <= vm.SelectedEntry.MaxButthurt);
                Assert.InRange(vm.SelectedEntry.Weight, 1, 100);

                // Matrix total cells and coverage percentage must never throw or be NaN
                Assert.False(double.IsNaN(vm.Matrix.CoveragePercentage));
                Assert.InRange(vm.Matrix.CoveragePercentage, 0.0, 100.0);
            }
        }

        [Fact]
        public void StockMode_ToggleStress_RepeatedTransitions_NoDiagnosticLeaks()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.Entries.Clear();
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "b1", MinLevel = 1, MaxLevel = 9, MinButthurt = 0, MaxButthurt = 4, Weight = 1 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "t1", MinLevel = 10, MaxLevel = 19, MinButthurt = 5, MaxButthurt = 8, Weight = 2 }));
            vm.Entries.Add(new FlipperScheduleEntryViewModel(new FlipperManifestEntry { Name = "a1", MinLevel = 20, MaxLevel = 30, MinButthurt = 9, MaxButthurt = 14, Weight = 3 }));
            vm.RecalculateMatrix();

            for (int cycle = 0; cycle < 20; cycle++)
            {
                // Toggle to Stock
                vm.ToggleMode();
                Assert.True(vm.IsStockMode);
                Assert.Equal(3, vm.MaxAllowedLevel);
                Assert.All(vm.Entries, e => Assert.InRange(e.MaxLevel, 1, 3));
                Assert.DoesNotContain(vm.ValidationDiagnostics, d => d.Code == "FZ003");

                // Toggle back to Extended
                vm.ToggleMode();
                Assert.False(vm.IsStockMode);
                Assert.Equal(30, vm.MaxAllowedLevel);
                Assert.DoesNotContain(vm.ValidationDiagnostics, d => d.Code == "FZ003");
            }
        }

        #endregion
    }
}
