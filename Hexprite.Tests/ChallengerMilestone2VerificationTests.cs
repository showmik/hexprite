using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ChallengerMilestone2VerificationTests
    {
        private class TestTabService : IWorkspaceTabService
        {
            public List<(string Title, SpriteState Sprite)> OpenSprites { get; } = [];
            public List<string> ActivatedTabs { get; } = [];
            public List<(string OldTitle, string NewTitle)> RenamedTabs { get; } = [];
            public string? LastOpenedTabName { get; private set; }
            public SpriteState? LastOpenedSprite { get; private set; }

            public void OpenSpritesInTabs(IEnumerable<SpriteState> sprites, string tabNamePrefix = "Imported")
            {
                int idx = 1;
                foreach (var s in sprites)
                {
                    OpenSpriteInTab(s, $"{tabNamePrefix} {idx++}");
                }
            }

            public void OpenSpritesInTabs(IEnumerable<(string Name, SpriteState Sprite)> sprites)
            {
                foreach (var (name, sprite) in sprites)
                {
                    OpenSpriteInTab(sprite, name);
                }
            }

            public void OpenSpriteInTab(SpriteState sprite, string title)
            {
                LastOpenedTabName = title;
                LastOpenedSprite = sprite;
                OpenSprites.Add((title, sprite));
            }

            public SpriteState? GetActiveSpriteState() => OpenSprites.LastOrDefault().Sprite;

            public (string Title, SpriteState Sprite)? GetActiveSprite()
            {
                if (OpenSprites.Count == 0) return null;
                var last = OpenSprites.Last();
                return (last.Title, last.Sprite);
            }

            public bool[]? GetActiveFramePixels(bool animated = false) => null;

            public IReadOnlyList<(string Title, SpriteState Sprite)> GetAllOpenSprites() => OpenSprites;

            public bool ActivateTabByTitle(string title)
            {
                if (string.IsNullOrWhiteSpace(title)) return false;
                string cleanTarget = title.Trim().TrimStart('*').Trim();
                var match = OpenSprites.FirstOrDefault(s =>
                    string.Equals(s.Title.Trim().TrimStart('*').Trim(), cleanTarget, StringComparison.OrdinalIgnoreCase));
                if (match.Sprite != null)
                {
                    ActivatedTabs.Add(title);
                    return true;
                }
                return false;
            }

            public bool RenameTab(string oldTitle, string newTitle)
            {
                RenamedTabs.Add((oldTitle, newTitle));
                string cleanOld = oldTitle.Trim().TrimStart('*').Trim();
                for (int i = 0; i < OpenSprites.Count; i++)
                {
                    if (string.Equals(OpenSprites[i].Title.Trim().TrimStart('*').Trim(), cleanOld, StringComparison.OrdinalIgnoreCase))
                    {
                        OpenSprites[i] = (newTitle, OpenSprites[i].Sprite);
                        return true;
                    }
                }
                return false;
            }

            public void OpenAssetPackInTab(
                IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
                string packName = "Flipper Asset Pack")
            {
            }
        }

        private static ShellViewModel CreateShellViewModel()
        {
            var codeGen = new CodeGeneratorService();
            var drawingMock = new Mock<IDrawingService>();
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

            return new ShellViewModel(
                codeGen,
                drawingMock.Object,
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
        }

        [Fact]
        public void Verify_NonExistingAnimation_Creates128x64MonochromeSingleFrameTab()
        {
            var tabService = new TestTabService();
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);

            // Select an entry that does not exist in open tabs
            var entry = vm.Entries.First(e => e.Name == "anim_adult");
            vm.NavigateToEntryTab(entry);

            Assert.Equal("anim_adult", tabService.LastOpenedTabName);
            Assert.NotNull(tabService.LastOpenedSprite);
            Assert.Equal(128, tabService.LastOpenedSprite.Width);
            Assert.Equal(64, tabService.LastOpenedSprite.Height);
            Assert.Equal(ColorMode.Monochrome, tabService.LastOpenedSprite.ColorMode);
            Assert.Equal(10, tabService.LastOpenedSprite.FrameRateFps);
            Assert.Single(tabService.LastOpenedSprite.Frames);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_adult"));
            Assert.Same(tabService.LastOpenedSprite, vm.AnimationSprites["anim_adult"]);
        }

        [Fact]
        public void Verify_NavigateToEntryTab_WhenTabAlreadyOpen_ActivatesInsteadOfDuplicate()
        {
            var tabService = new TestTabService();
            var existingSprite = new SpriteState(128, 64);
            tabService.OpenSprites.Add(("anim_baby", existingSprite));

            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);
            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");

            int initialOpenedCount = tabService.OpenSprites.Count;
            vm.NavigateToEntryTab(babyEntry);

            // Should have activated the tab, not opened a new one
            Assert.Contains("anim_baby", tabService.ActivatedTabs);
            Assert.Equal(initialOpenedCount, tabService.OpenSprites.Count);
        }

        [Fact]
        public void Verify_DeadzoneGapTabCreation_AtMatrixBoundaries_L1M0_And_L30M14()
        {
            var tabService = new TestTabService();
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);
            vm.Entries.Clear();
            vm.RecalculateMatrix();

            // 1. Boundary 1: Level 1, Mood 0
            vm.InspectCell(1, 0);
            Assert.True(vm.HasSelectedCellGap);
            vm.CreateTabForGapCommand.Execute(null);

            Assert.Equal("anim_L1_M0", tabService.LastOpenedTabName);
            Assert.NotNull(tabService.LastOpenedSprite);
            Assert.Equal(128, tabService.LastOpenedSprite.Width);
            Assert.Equal(64, tabService.LastOpenedSprite.Height);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_L1_M0"));

            var createdEntry1 = vm.Entries.First(e => e.Name == "anim_L1_M0");
            Assert.Equal(1, createdEntry1.MinLevel);
            Assert.Equal(1, createdEntry1.MaxLevel);
            Assert.Equal(0, createdEntry1.MinButthurt);
            Assert.Equal(0, createdEntry1.MaxButthurt);

            // 2. Boundary 2: Level 30, Mood 14
            vm.InspectCell(30, 14);
            Assert.True(vm.HasSelectedCellGap);
            vm.CreateTabForGapCommand.Execute(null);

            Assert.Equal("anim_L30_M14", tabService.LastOpenedTabName);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_L30_M14"));

            var createdEntry2 = vm.Entries.First(e => e.Name == "anim_L30_M14");
            Assert.Equal(30, createdEntry2.MinLevel);
            Assert.Equal(30, createdEntry2.MaxLevel);
            Assert.Equal(14, createdEntry2.MinButthurt);
            Assert.Equal(14, createdEntry2.MaxButthurt);
        }

        [Fact]
        public void Verify_DeadzoneGapTabCreation_SequentialUniqueNames_And_CoverageInspection()
        {
            var tabService = new TestTabService();
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);
            vm.Entries.Clear();
            vm.RecalculateMatrix();

            vm.InspectCell(10, 5);
            vm.CreateTabForGapCommand.Execute(null);
            Assert.Equal("anim_L10_M5", tabService.LastOpenedTabName);

            // Calling gap creation again at same cell creates disambiguated names
            vm.InspectCell(10, 5);
            vm.CreateTabForGapCommand.Execute(null);
            Assert.Equal("anim_L10_M5_1", tabService.LastOpenedTabName);

            vm.InspectCell(10, 5);
            vm.CreateTabForGapCommand.Execute(null);
            Assert.Equal("anim_L10_M5_2", tabService.LastOpenedTabName);

            Assert.Equal(3, vm.Entries.Count);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_L10_M5"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_L10_M5_1"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_L10_M5_2"));
        }

        [Fact]
        public void Verify_AnimationSprites_PreservesEditedPixelsAndMultiFrames_AcrossTabCloseAndReopen()
        {
            var tabService = new TestTabService();
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);

            // Step 1: Open tab
            var teenEntry = vm.Entries.First(e => e.Name == "anim_teen");
            vm.NavigateToEntryTab(teenEntry);

            Assert.True(vm.AnimationSprites.ContainsKey("anim_teen"));
            var sprite = vm.AnimationSprites["anim_teen"];

            // Step 2: Simulate Canvas User Edits (Set pixel, add frames, change FPS)
            sprite.Frames[0].LayerPixels[0].GetMonochromeData()[42] = true;
            sprite.Frames.Add(new FrameState { Name = "Frame 2" });
            sprite.Frames.Add(new FrameState { Name = "Frame 3" });
            sprite.FrameRateFps = 15;

            // Step 3: Simulate Workspace Tab Closed by User
            tabService.OpenSprites.Clear();
            Assert.Empty(tabService.OpenSprites);

            // Step 4: Re-open the tab from Matrix View
            vm.NavigateToEntryTab(teenEntry);

            // Verify the re-opened sprite is the preserved instance with all edits intact
            Assert.NotNull(tabService.LastOpenedSprite);
            Assert.Equal(3, tabService.LastOpenedSprite.Frames.Count);
            Assert.Equal(15, tabService.LastOpenedSprite.FrameRateFps);
            Assert.True(tabService.LastOpenedSprite.Frames[0].LayerPixels[0].GetMonochromeData()[42]);
        }

        [Fact]
        public void Verify_SimulatorIntegration_PullsPreservedModifiedSprites_AfterTabClosed()
        {
            var tabService = new TestTabService();
            var mockWindowManager = new Mock<IFlipperWindowManager>();
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? capturedAnims = null;

            mockWindowManager
                .Setup(wm => wm.ShowSimulator(It.IsAny<IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>>(), It.IsAny<string>()))
                .Callback<IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>, string>((anims, packName) =>
                {
                    capturedAnims = anims;
                });

            var vm = new FlipperScheduleMatrixViewModel(
                windowManager: mockWindowManager.Object,
                tabService: tabService);

            // Open tab and modify
            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.NavigateToEntryTab(babyEntry);
            var sprite = vm.AnimationSprites["anim_baby"];
            sprite.Frames.Add(new FrameState { Name = "Frame 2" });

            // Close tab
            tabService.OpenSprites.Clear();

            // Run simulator
            vm.OpenSimulatorCommand.Execute(null);

            Assert.True(vm.IsDeviceSimulatorView);
            var babyAnim = vm.SimulatorViewModel.Animations.FirstOrDefault(a => a.Name == "anim_baby");
            Assert.NotNull(babyAnim.Sprite);
            Assert.Equal(2, babyAnim.Sprite.Frames.Count);
        }

        [Fact]
        public void Verify_ShellViewModel_OpenSpritesInTabs_RebuildsFrameViewModels_ForMultiFrameAnimation()
        {
            var shell = CreateShellViewModel();
            var multiFrameSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 12 };
            multiFrameSprite.Frames.Clear();
            multiFrameSprite.Frames.Add(new FrameState { Name = "F0" });
            multiFrameSprite.Frames.Add(new FrameState { Name = "F1" });
            multiFrameSprite.Frames.Add(new FrameState { Name = "F2" });
            multiFrameSprite.Frames.Add(new FrameState { Name = "F3" });

            shell.OpenSpriteInTab(multiFrameSprite, "FlipperMultiFrame");

            Assert.NotNull(shell.ActiveDocument);
            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            Assert.Equal("FlipperMultiFrame", doc.SpriteName);
            Assert.Equal(4, doc.SpriteState.Frames.Count);
            Assert.Equal(4, doc.Frames.Count);
            Assert.True(doc.SpriteState.IsAnimationEnabled);
            Assert.Equal(12, doc.SpriteState.FrameRateFps);
        }

        [Fact]
        public void Verify_BidirectionalRename_SyncsAnimationSpritesKey_And_ShellTabTitle()
        {
            var shell = CreateShellViewModel();
            var tabService = new WorkspaceTabService(shell);
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);

            // Open tab for anim_baby
            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.NavigateToEntryTab(babyEntry);

            Assert.Single(shell.OpenDocuments);
            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.Equal("anim_baby", doc.SpriteName);
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));

            // Rename in Matrix ViewModel
            vm.SelectedEntry = babyEntry;
            vm.SelectedName = "anim_baby_renamed";

            // Verify cache key updated
            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby_renamed"));

            // Verify Shell tab title updated
            Assert.Equal("anim_baby_renamed", doc.SpriteName);
            Assert.Equal("anim_baby_renamed", babyEntry.Name);
        }

        [Fact]
        public void Verify_PreviewPlayback_NonLinearFlipperCycleOrder_StepsAndWrapsCorrectly()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0" });
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 2, 1, 2, 0] // 5 steps
            };

            var entry = new FlipperManifestEntry { Name = "cycle_test", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("cycle_test", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            Assert.Equal("Frame 1 / 5", vm.PreviewFrameCountText);
            Assert.Equal(0, vm.PreviewFrameIndex);

            // Step Forward
            vm.PreviewNextFrameCommand.Execute(null); // step 1 (order = 2)
            Assert.Equal("Frame 2 / 5", vm.PreviewFrameCountText);
            Assert.Equal(1, vm.PreviewFrameIndex);

            vm.PreviewNextFrameCommand.Execute(null); // step 2 (order = 1)
            Assert.Equal("Frame 3 / 5", vm.PreviewFrameCountText);
            Assert.Equal(2, vm.PreviewFrameIndex);

            vm.PreviewNextFrameCommand.Execute(null); // step 3 (order = 2)
            Assert.Equal("Frame 4 / 5", vm.PreviewFrameCountText);
            Assert.Equal(3, vm.PreviewFrameIndex);

            vm.PreviewNextFrameCommand.Execute(null); // step 4 (order = 0)
            Assert.Equal("Frame 5 / 5", vm.PreviewFrameCountText);
            Assert.Equal(4, vm.PreviewFrameIndex);

            // Step Forward Wrap
            vm.PreviewNextFrameCommand.Execute(null); // step 0
            Assert.Equal("Frame 1 / 5", vm.PreviewFrameCountText);
            Assert.Equal(0, vm.PreviewFrameIndex);

            // Step Backward Wrap
            vm.PreviewPrevFrameCommand.Execute(null); // step 4
            Assert.Equal("Frame 5 / 5", vm.PreviewFrameCountText);
            Assert.Equal(4, vm.PreviewFrameIndex);
        }

        [Fact]
        public void Verify_PreviewPlayback_CorruptedOrOutOfBoundsFramesOrder_ClampsSafely()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F0" });
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.FlipperCycle = new FlipperAnimationCycle
            {
                FramesOrder = [0, 999, -5, 1] // Out-of-bounds frame indices
            };

            var entry = new FlipperManifestEntry { Name = "corrupted_cycle", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("corrupted_cycle", sprite, entry)
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            // Should not throw when stepping through out-of-bounds indices and rendering
            for (int i = 0; i < 6; i++)
            {
                vm.PreviewNextFrameCommand.Execute(null);
                vm.RenderPreviewFrame();
            }

            Assert.True(true);
        }

        [Fact]
        public void Verify_AssetPackViewModel_IsActiveSwitch_RefreshesPreviewSprite()
        {
            var tabService = new TestTabService();
            var editedSprite = new SpriteState(128, 64);
            editedSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            tabService.OpenSprites.Add(("anim_baby", editedSprite));

            var vm = new AssetPackViewModel(tabService: tabService);
            Assert.NotNull(vm.MatrixViewModel.SelectedEntry);

            // Setting IsActive = true simulates switching back to Asset Pack tab
            vm.IsActive = true;

            Assert.Equal("Frame 1 / 2", vm.MatrixViewModel.PreviewFrameCountText);
            Assert.Equal(2, vm.MatrixViewModel.AnimationSprites["anim_baby"].Frames.Count);
        }

        [Fact]
        public void Verify_AdversarialInputs_SelectedName_And_NavigateToEntry()
        {
            var tabService = new TestTabService();
            var sprite = new SpriteState(128, 64);
            tabService.OpenSprites.Add(("anim_baby", sprite));
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);

            // 1. Setting SelectedName to new valid name updates cache key
            vm.SelectedName = "test_renamed";
            Assert.Equal("test_renamed", vm.SelectedName);
            Assert.True(vm.AnimationSprites.ContainsKey("test_renamed"));

            // 2. NavigateToEntryByName with null / empty / whitespace
            vm.NavigateToEntryByNameCommand.Execute(null);
            vm.NavigateToEntryByNameCommand.Execute("");
            vm.NavigateToEntryByNameCommand.Execute("   ");

            // 3. NavigateToEntryTab with null entry when SelectedEntry is null
            vm.Entries.Clear();
            vm.SelectedEntry = null;
            vm.NavigateToEntryTab(null); // Must not throw NRE

            Assert.True(true);
        }

        [Fact]
        public void Verify_ExtremeCoordinateClamping_And_InvertedBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.NotNull(vm.SelectedEntry);

            // Test extreme out-of-range inspect coordinates
            vm.InspectCell(-999, -999);
            Assert.Equal(1, vm.SelectedCellLevel);
            Assert.Equal(0, vm.SelectedCellMood);

            vm.InspectCell(999, 999);
            Assert.Equal(30, vm.SelectedCellLevel);
            Assert.Equal(14, vm.SelectedCellMood);

            // Test setting inverted bounds (min > max) -> safely swapped and clamped
            vm.SetSelectedEntryBounds(28, 4, 13, 2);
            Assert.Equal(4, vm.SelectedEntry.MinLevel);
            Assert.Equal(28, vm.SelectedEntry.MaxLevel);
            Assert.Equal(2, vm.SelectedEntry.MinButthurt);
            Assert.Equal(13, vm.SelectedEntry.MaxButthurt);
        }

        [Fact]
        public void Verify_SyncFromWorkspace_WithShellViewModel_SanitizesAndDedupes()
        {
            var shell = CreateShellViewModel();
            shell.OpenSpriteInTab(new SpriteState(128, 64), "my_anim");
            shell.OpenSpriteInTab(new SpriteState(128, 64), "other_anim");

            var tabService = new WorkspaceTabService(shell);
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);
            vm.Entries.Clear();
            vm.RecalculateMatrix();

            vm.SyncFromWorkspaceCommand.Execute(null);

            Assert.Equal(2, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name == "my_anim");
            Assert.Contains(vm.Entries, e => e.Name == "other_anim");
            Assert.True(vm.AnimationSprites.ContainsKey("my_anim"));
            Assert.True(vm.AnimationSprites.ContainsKey("other_anim"));
        }

        [Fact]
        public void Verify_RenderPreview_NonStandardDimensions_ClippedWithoutBufferOverflow()
        {
            var spriteSmall = new SpriteState(32, 16);
            var spriteLarge = new SpriteState(256, 128);

            var pack = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("small", spriteSmall, new FlipperManifestEntry { Name = "small", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("large", spriteLarge, new FlipperManifestEntry { Name = "large", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var vm = new FlipperScheduleMatrixViewModel(pack);

            vm.SelectedEntry = vm.Entries[0];
            vm.RefreshCurrentPreviewSprite();
            vm.RenderPreviewFrame();
            Assert.Equal("32 × 16", vm.PreviewDimensionsText);

            vm.SelectedEntry = vm.Entries[1];
            vm.RefreshCurrentPreviewSprite();
            vm.RenderPreviewFrame();
            Assert.Equal("256 × 128", vm.PreviewDimensionsText);

            // Blank preview rendering with special characters
            vm.RenderBlankPreview("<Unicode: 🐬 • ★ • \n \t>");
            Assert.True(true);
        }

        [Fact]
        public void Verify_UndoRedo_CyclesThroughTabAndGapOperations()
        {
            var tabService = new TestTabService();
            var vm = new FlipperScheduleMatrixViewModel(tabService: tabService);

            int initialCount = vm.Entries.Count; // 3

            // Perform gap creation
            vm.InspectCell(15, 7);
            vm.CreateTabForGapCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.True(vm.CanUndo);

            // Undo gap creation
            vm.UndoCommand.Execute(null);
            Assert.Equal(initialCount, vm.Entries.Count);
            Assert.True(vm.CanRedo);

            // Redo gap creation
            vm.RedoCommand.Execute(null);
            Assert.Equal(initialCount + 1, vm.Entries.Count);
            Assert.Contains(vm.Entries, e => e.Name.StartsWith("anim_L15_M7"));
        }
    }
}
