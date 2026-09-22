using System.IO;
using Hexprite.Core;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class AssetPackViewModelTests
    {
        [Fact]
        public void Constructor_DefaultDocument_HasValidState()
        {
            var vm = new AssetPackViewModel();

            Assert.Equal(DocumentMode.AssetPack, vm.Mode);
            Assert.Equal("Flipper Asset Pack (Pack)", vm.Title);
            Assert.False(vm.HasUnsavedChanges);
            Assert.False(vm.IsDirty);
            Assert.False(vm.IsLinked);
            Assert.Null(vm.FilePath);
            Assert.NotNull(vm.MatrixViewModel);
            Assert.Equal(3, vm.MatrixViewModel.Entries.Count);
        }

        [Fact]
        public void Title_ReflectsPackName()
        {
            var doc = AssetPackDocument.CreateNew("Cyber Dolphin");
            var vm = new AssetPackViewModel(doc);

            Assert.Equal("Cyber Dolphin (Pack)", vm.Title);
        }

        [Fact]
        public void ModifyingMatrix_SetsDirtyFlagAndUpdatesTitle()
        {
            var vm = new AssetPackViewModel();
            Assert.False(vm.IsDirty);

            // Add an entry to trigger matrix redraw
            vm.MatrixViewModel.AddEntry();

            Assert.True(vm.IsDirty);
            Assert.True(vm.HasUnsavedChanges);
            Assert.StartsWith("*", vm.Title);
        }

        [Fact]
        public void MarkAsClean_ClearsDirtyFlag()
        {
            var vm = new AssetPackViewModel();
            vm.MatrixViewModel.AddEntry();
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();

            Assert.False(vm.IsDirty);
            Assert.False(vm.HasUnsavedChanges);
            Assert.DoesNotContain("*", vm.Title);
        }

        [Fact]
        public void SaveToPath_And_ToDocument_RoundTrips()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"test_pack_{System.Guid.NewGuid():N}.hexpack");
            try
            {
                var doc = AssetPackDocument.CreateNew("SaveTestPack");
                var vm = new AssetPackViewModel(doc);
                vm.MatrixViewModel.AddEntry();

                vm.SaveToPath(tempFile);

                Assert.Equal(tempFile, vm.FilePath);
                Assert.False(vm.IsDirty);
                Assert.True(File.Exists(tempFile));

                var roundtripDoc = vm.ToDocument();
                Assert.Equal("SaveTestPack", roundtripDoc.PackName);
                Assert.Equal(4, roundtripDoc.Entries.Count);
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        [Fact]
        public void SaveAs_EnforcesHexpackExtension()
        {
            string tempBase = Path.Combine(Path.GetTempPath(), $"test_pack_{System.Guid.NewGuid():N}");
            string expectedPath = tempBase + ".hexpack";
            try
            {
                var vm = new AssetPackViewModel();
                vm.SaveAs(tempBase);

                Assert.Equal(expectedPath, vm.FilePath);
                Assert.True(File.Exists(expectedPath));
            }
            finally
            {
                if (File.Exists(expectedPath)) File.Delete(expectedPath);
            }
        }

        [Fact]
        public void LoadDocument_PopulatesViewModelAndMarksClean()
        {
            var vm = new AssetPackViewModel();
            var newDoc = new AssetPackDocument
            {
                PackName = "NewLoadedPack",
                IsStockMode = true,
                Entries =
                [
                    new FlipperManifestEntry { Name = "loaded_1", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 14, Weight = 2 }
                ]
            };

            vm.LoadDocument(newDoc);

            Assert.Equal("NewLoadedPack (Pack)", vm.Title);
            Assert.False(vm.IsDirty);
            Assert.True(vm.MatrixViewModel.IsStockMode);
            Assert.Single(vm.MatrixViewModel.Entries);
            Assert.Equal("loaded_1", vm.MatrixViewModel.Entries[0].Name);
        }

        [Fact]
        public void Constructor_WithImportedPack_PreservesLoadedSpriteFrames()
        {
            // Arrange - Create 2 animations with multi-frame sprites
            var swimSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 6 };
            swimSprite.Frames.Clear();
            swimSprite.Frames.Add(new FrameState { Name = "Frame 1" });
            swimSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            swimSprite.Frames.Add(new FrameState { Name = "Frame 3" });
            var swimEntry = new FlipperManifestEntry { Name = "DolphinSwim", MinLevel = 1, MaxLevel = 5, MinButthurt = 0, MaxButthurt = 3, Weight = 2 };

            var jumpSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
            jumpSprite.Frames.Clear();
            jumpSprite.Frames.Add(new FrameState { Name = "Frame 1" });
            jumpSprite.Frames.Add(new FrameState { Name = "Frame 2" });
            var jumpEntry = new FlipperManifestEntry { Name = "DolphinJump", MinLevel = 6, MaxLevel = 30, MinButthurt = 4, MaxButthurt = 14, Weight = 5 };

            var packList = new System.Collections.Generic.List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("DolphinSwim", swimSprite, swimEntry),
                ("DolphinJump", jumpSprite, jumpEntry)
            };

            // Act
            var vm = new AssetPackViewModel(pack: packList);

            // Assert
            Assert.Equal(2, vm.MatrixViewModel.Entries.Count);
            Assert.NotNull(vm.MatrixViewModel.ImportedPack);
            Assert.Equal(2, vm.MatrixViewModel.ImportedPack.Count);

            // Select Swim entry and check preview frame count
            vm.MatrixViewModel.SelectedEntry = vm.MatrixViewModel.Entries[0];
            Assert.Equal("DolphinSwim", vm.MatrixViewModel.SelectedName);
            Assert.Equal("Frame 1 / 3", vm.MatrixViewModel.PreviewFrameCountText);

            // Select Jump entry and check preview frame count
            vm.MatrixViewModel.SelectedEntry = vm.MatrixViewModel.Entries[1];
            Assert.Equal("DolphinJump", vm.MatrixViewModel.SelectedName);
            Assert.Equal("Frame 1 / 2", vm.MatrixViewModel.PreviewFrameCountText);
        }

        [Fact]
        public void Constructor_Overload_WithPackAndPackName_InitializesCorrectly()
        {
            var animSprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 5 };
            var entry = new FlipperManifestEntry { Name = "Wave", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var packList = new System.Collections.Generic.List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("Wave", animSprite, entry)
            };

            var vm = new AssetPackViewModel(packList, "Dolphin Wave Pack");

            Assert.Equal("Dolphin Wave Pack (Pack)", vm.Title);
            Assert.Equal("Dolphin Wave Pack", vm.MatrixViewModel.PackName);
            Assert.Single(vm.MatrixViewModel.Entries);
            Assert.Equal("Wave", vm.MatrixViewModel.Entries[0].Name);
        }

        [Fact]
        public void IsActive_WhenSetToTrue_RefreshesCurrentPreviewSprite()
        {
            var mockTabService = new Moq.Mock<Hexprite.Services.IWorkspaceTabService>();
            mockTabService.Setup(t => t.GetAllOpenSprites()).Returns(new List<(string Title, SpriteState Sprite)>());

            var sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            sprite.Frames.Clear();
            sprite.Frames.Add(new FrameState { Name = "F1" });
            sprite.Frames.Add(new FrameState { Name = "F2" });
            var entry = new FlipperManifestEntry { Name = "ActiveAnim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
            var pack = new System.Collections.Generic.List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("ActiveAnim", sprite, entry)
            };

            var vm = new AssetPackViewModel(pack: pack, tabService: mockTabService.Object);
            Assert.False(vm.IsActive);

            // Open tab modifies sprite with third frame
            sprite.Frames.Add(new FrameState { Name = "F3" });
            mockTabService.Setup(t => t.GetAllOpenSprites()).Returns([("ActiveAnim", sprite)]);

            // Set IsActive to true
            vm.IsActive = true;

            Assert.True(vm.IsActive);
            Assert.Equal("Frame 1 / 3", vm.MatrixViewModel.PreviewFrameCountText);
        }

        [Fact]
        public void SelectionChange_DoesNotMarkDocumentAsDirty()
        {
            var vm = new AssetPackViewModel();
            Assert.False(vm.IsDirty);
            Assert.True(vm.MatrixViewModel.Entries.Count >= 2);

            // Change selected entry (click another animation)
            vm.MatrixViewModel.SelectedEntry = vm.MatrixViewModel.Entries[1];

            Assert.False(vm.IsDirty);
            Assert.False(vm.HasUnsavedChanges);
            Assert.DoesNotContain("*", vm.Title);

            // Select multiple entries
            vm.MatrixViewModel.Entries[0].IsSelected = true;
            vm.MatrixViewModel.Entries[1].IsSelected = true;

            Assert.False(vm.IsDirty);
            Assert.False(vm.HasUnsavedChanges);
        }

        [Fact]
        public void ViewModeChange_DoesNotMarkDocumentAsDirty()
        {
            var vm = new AssetPackViewModel();
            Assert.False(vm.IsDirty);

            // Switch to Sprite Gallery
            vm.MatrixViewModel.CurrentViewMode = Hexprite.ViewModels.Flipper.AssetPackViewMode.SpriteGallery;
            Assert.False(vm.IsDirty);

            // Switch to DataGrid Roster
            vm.MatrixViewModel.CurrentViewMode = Hexprite.ViewModels.Flipper.AssetPackViewMode.DataGridRoster;
            Assert.False(vm.IsDirty);

            // Switch back to Matrix Studio
            vm.MatrixViewModel.CurrentViewMode = Hexprite.ViewModels.Flipper.AssetPackViewMode.MatrixStudio;
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void CellInspection_DoesNotMarkDocumentAsDirty()
        {
            var vm = new AssetPackViewModel();
            Assert.False(vm.IsDirty);

            // Inspect cell to inspect probabilities
            vm.MatrixViewModel.InspectCell(2, 5);
            Assert.False(vm.IsDirty);

            // Select region to inspect metrics
            vm.MatrixViewModel.SelectRegion(1, 3, 0, 5);
            Assert.False(vm.IsDirty);

            // Clear region selection
            vm.MatrixViewModel.ClearRegionSelection();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void DataModifications_CorrectlyMarkDocumentAsDirty()
        {
            var vm = new AssetPackViewModel();
            Assert.False(vm.IsDirty);

            // 1. Modifying MinLevel
            vm.MatrixViewModel.SelectedMinLevel = 2;
            Assert.True(vm.IsDirty);
            Assert.StartsWith("*", vm.Title);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // 2. Modifying Weight
            vm.MatrixViewModel.SelectedWeight = 5;
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // 3. Modifying Animation Name
            vm.MatrixViewModel.SelectedName = "renamed_anim";
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // 4. Modifying PackName
            vm.MatrixViewModel.PackName = "New Pack Name";
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // 5. AutoBalance
            vm.MatrixViewModel.AutoBalance();
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // 6. Undo/Redo
            vm.MatrixViewModel.Undo();
            Assert.True(vm.IsDirty);
        }
    }
}
