using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class FlipperAssetPackHexpIntegrationTests
    {
        [Fact]
        public void AssetPackDocument_CreateNew_InitializesSchemaVersionAndAnimations()
        {
            var doc = AssetPackDocument.CreateNew("TestPack");

            Assert.Equal("TestPack", doc.PackName);
            Assert.Equal(2, doc.SchemaVersion);
            Assert.Equal(3, doc.Entries.Count);
            Assert.Equal(3, doc.Animations.Count);
            Assert.True(doc.Animations.ContainsKey("anim_baby"));
            Assert.True(doc.Animations.ContainsKey("anim_teen"));
            Assert.True(doc.Animations.ContainsKey("anim_adult"));

            var baby = doc.Animations["anim_baby"];
            Assert.Equal(128, baby.Width);
            Assert.Equal(64, baby.Height);
            Assert.Equal(10, baby.FrameRateFps);
        }

        [Fact]
        public void AssetPackViewModel_ToDocument_And_LoadDocument_RoundTrips_EmbeddedAnimations()
        {
            var doc = AssetPackDocument.CreateNew("MyCustomPack");

            // Customize anim_baby with 2 frames
            var customBaby = new SpriteState(128, 64) { FrameRateFps = 15 };
            customBaby.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
            doc.Animations["anim_baby"] = customBaby;

            var vm = new AssetPackViewModel(doc);

            // Export to document
            var exportedDoc = vm.ToDocument();
            Assert.Equal("MyCustomPack", exportedDoc.PackName);
            Assert.Equal(2, exportedDoc.SchemaVersion);
            Assert.True(exportedDoc.Animations.ContainsKey("anim_baby"));
            Assert.Equal(2, exportedDoc.Animations["anim_baby"].Frames.Count);
            Assert.Equal(15, exportedDoc.Animations["anim_baby"].FrameRateFps);

            // Serialize to JSON and deserialize back
            string json = JsonSerializer.Serialize(exportedDoc);
            var reloadedDoc = JsonSerializer.Deserialize<AssetPackDocument>(json);
            Assert.NotNull(reloadedDoc);

            var newVm = new AssetPackViewModel(reloadedDoc);
            Assert.True(newVm.MatrixViewModel.AnimationSprites.ContainsKey("anim_baby"));
            var loadedBaby = newVm.MatrixViewModel.AnimationSprites["anim_baby"];
            Assert.Equal(2, loadedBaby.Frames.Count);
            Assert.Equal(15, loadedBaby.FrameRateFps);
        }

        [Fact]
        public void AssetPackDocument_LegacyJsonWithoutAnimations_DeserializesGracefully()
        {
            string legacyJson = """
            {
                "PackName": "Legacy Pack",
                "IsStockMode": true,
                "Entries": [
                    { "Name": "legacy_anim", "MinLevel": 1, "MaxLevel": 3, "MinButthurt": 0, "MaxButthurt": 14, "Weight": 1 }
                ]
            }
            """;

            var doc = JsonSerializer.Deserialize<AssetPackDocument>(legacyJson);
            Assert.NotNull(doc);
            Assert.Equal("Legacy Pack", doc.PackName);
            Assert.True(doc.IsStockMode);
            Assert.Single(doc.Entries);
            Assert.Empty(doc.Animations);

            // Passing legacy doc to AssetPackViewModel initializes fallback sprites
            var vm = new AssetPackViewModel(doc);
            Assert.True(vm.MatrixViewModel.AnimationSprites.ContainsKey("legacy_anim"));
            var sprite = vm.MatrixViewModel.AnimationSprites["legacy_anim"];
            Assert.NotNull(sprite);
            Assert.Equal(128, sprite.Width);
            Assert.Equal(64, sprite.Height);
        }

        [Fact]
        public void FlipperScheduleMatrixViewModel_ImportHexpAnimations_ImportsFilesCorrectly()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hexp_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string hexpPath = Path.Combine(tempDir, "anim_jump.hexp");
                var jumpSprite = new SpriteState(128, 64) { FrameRateFps = 12 };
                jumpSprite.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });
                File.WriteAllText(hexpPath, JsonSerializer.Serialize(jumpSprite));

                var vm = new FlipperScheduleMatrixViewModel();
                vm.ImportHexpAnimations([hexpPath]);

                Assert.True(vm.AnimationSprites.ContainsKey("anim_jump"));
                Assert.Equal(2, vm.AnimationSprites["anim_jump"].Frames.Count);
                Assert.Contains(vm.Entries, e => e.Name.Equals("anim_jump", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void FlipperScheduleMatrixViewModel_ExportSelectedToHexp_WritesValidFile()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hexp_export_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var vm = new FlipperScheduleMatrixViewModel();
                string outPath = Path.Combine(tempDir, "baby_exported.hexp");

                vm.ExportSelectedToHexp(outPath);
                Assert.True(File.Exists(outPath));

                string json = File.ReadAllText(outPath);
                var loaded = JsonSerializer.Deserialize<SpriteState>(json);
                Assert.NotNull(loaded);
                Assert.Equal(128, loaded.Width);
                Assert.Equal(64, loaded.Height);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void FlipperScheduleMatrixViewModel_ExportAllToHexp_ExportsAllPackEntries()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hexp_export_all_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var vm = new FlipperScheduleMatrixViewModel();
                vm.ExportAllToHexp(tempDir);

                string babyFile = Path.Combine(tempDir, "anim_baby.hexp");
                string teenFile = Path.Combine(tempDir, "anim_teen.hexp");
                string adultFile = Path.Combine(tempDir, "anim_adult.hexp");

                Assert.True(File.Exists(babyFile));
                Assert.True(File.Exists(teenFile));
                Assert.True(File.Exists(adultFile));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void AssetPackViewModel_IsActive_SyncsWithOpenWorkspaceTabs()
        {
            var tabServiceMock = new Mock<IWorkspaceTabService>();

            var activeCanvasSprite = new SpriteState(128, 64) { FrameRateFps = 20 };
            tabServiceMock.Setup(t => t.GetAllOpenSprites()).Returns([("anim_baby", activeCanvasSprite)]);

            var vm = new AssetPackViewModel(tabService: tabServiceMock.Object);

            // Trigger IsActive = true
            vm.IsActive = true;

            Assert.True(vm.MatrixViewModel.AnimationSprites.ContainsKey("anim_baby"));
            Assert.Equal(20, vm.MatrixViewModel.AnimationSprites["anim_baby"].FrameRateFps);
        }

        [Fact]
        public void Bug1_AssetPackDocument_DeserializedAnimations_AreCaseInsensitive()
        {
            string json = """
            {
                "SchemaVersion": 2,
                "PackName": "CasePack",
                "IsStockMode": false,
                "Entries": [
                    { "Name": "ANIM_BABY", "MinLevel": 1, "MaxLevel": 10, "MinButthurt": 0, "MaxButthurt": 14, "Weight": 1 }
                ],
                "Animations": {
                    "ANIM_BABY": { "Width": 128, "Height": 64, "FrameRateFps": 24 }
                }
            }
            """;

            var doc = JsonSerializer.Deserialize<AssetPackDocument>(json);
            Assert.NotNull(doc);
            doc.EnsureCaseInsensitiveAnimations();

            // Verify case-insensitive lookup works seamlessly with lower/upper/mixed case
            Assert.True(doc.Animations.ContainsKey("anim_baby"));
            Assert.True(doc.Animations.ContainsKey("ANIM_BABY"));
            Assert.True(doc.Animations.ContainsKey("Anim_Baby"));
            Assert.Equal(24, doc.Animations["anim_baby"].FrameRateFps);
        }

        [Fact]
        public void Bug2_FlipperScheduleMatrixViewModel_RenameEntry_MigratesAnimationSpriteKey()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));

            // Select and rename entry
            var entry = vm.Entries.First(e => e.Name == "anim_baby");
            entry.Name = "anim_toddler";

            // Verify old key is removed and new key is mapped
            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.True(vm.AnimationSprites.ContainsKey("anim_toddler"));
        }

        [Fact]
        public void Bug3_FlipperScheduleMatrixViewModel_DeleteEntry_CleansUpAnimationSprite()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));

            var entry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.DeleteEntry(entry);

            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.DoesNotContain(vm.Entries, e => e.Name == "anim_baby");
        }

        [Fact]
        public void Bug4_AssetPackViewModel_ToDocument_ExcludesOrphanAnimationsAndDoesNotMutateLiveSprites()
        {
            var doc = AssetPackDocument.CreateNew("OrphanTestPack");
            var vm = new AssetPackViewModel(doc);

            // Add an orphan sprite directly to ViewModel dictionary that is NOT in Entries
            var orphanSprite = new SpriteState(128, 64) { FrameRateFps = 30 };
            vm.MatrixViewModel.SetAnimationSprite("orphan_ghost_anim", orphanSprite);

            var exportedDoc = vm.ToDocument();

            // Orphan sprite should NOT be serialized into doc.Animations
            Assert.False(exportedDoc.Animations.ContainsKey("orphan_ghost_anim"));
            Assert.True(exportedDoc.Animations.ContainsKey("anim_baby"));
            Assert.True(exportedDoc.Animations.ContainsKey("anim_teen"));
            Assert.True(exportedDoc.Animations.ContainsKey("anim_adult"));
        }

        [Fact]
        public void Bug5_AssetPackViewModel_ToDocument_DoesNotMarkDocumentAsDirty()
        {
            var doc = AssetPackDocument.CreateNew("CleanTestPack");
            var vm = new AssetPackViewModel(doc);
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Calling ToDocument() should not leave the document in a dirty state
            var exportedDoc = vm.ToDocument();
            Assert.NotNull(exportedDoc);
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void Bug6_FlipperScheduleMatrixViewModel_UndoRedo_RestoresAnimationSprites()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            int initialCount = vm.AnimationSprites.Count;
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));

            // Delete an entry
            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.DeleteEntry(babyEntry);
            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));

            // Undo deletion
            vm.Undo();
            Assert.True(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.Contains(vm.Entries, e => e.Name == "anim_baby");

            // Redo deletion
            vm.Redo();
            Assert.False(vm.AnimationSprites.ContainsKey("anim_baby"));
            Assert.DoesNotContain(vm.Entries, e => e.Name == "anim_baby");
        }

        [Fact]
        public void Bug7_FlipperScheduleMatrixViewModel_ImportHexpAnimations_SupportsMultiSelectDialog()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "hexp_multi_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string file1 = Path.Combine(tempDir, "anim_walk.hexp");
                string file2 = Path.Combine(tempDir, "anim_run.hexp");

                File.WriteAllText(file1, JsonSerializer.Serialize(new SpriteState(128, 64) { FrameRateFps = 10 }));
                File.WriteAllText(file2, JsonSerializer.Serialize(new SpriteState(128, 64) { FrameRateFps = 20 }));

                var dialogServiceMock = new Mock<IDialogService>();
                dialogServiceMock.Setup(d => d.ShowOpenFilesDialog(It.IsAny<string>(), It.IsAny<string>()))
                    .Returns([file1, file2]);

                var vm = new FlipperScheduleMatrixViewModel(dialogService: dialogServiceMock.Object);
                vm.ImportHexpAnimations(); // Call with null to trigger dialog

                Assert.True(vm.AnimationSprites.ContainsKey("anim_walk"));
                Assert.True(vm.AnimationSprites.ContainsKey("anim_run"));
                Assert.Equal(10, vm.AnimationSprites["anim_walk"].FrameRateFps);
                Assert.Equal(20, vm.AnimationSprites["anim_run"].FrameRateFps);
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Flaw1_ShellViewModel_OpenSpriteInTab_StartsCleanNotDirty()
        {
            var shell = Hexprite.Tests.E2E.E2ETestHelper.CreateTestShellViewModel();
            var sprite = new SpriteState(128, 64) { FrameRateFps = 12 };
            sprite.Frames.Add(new FrameState { Name = "Frame 2", LayerPixels = [new MonochromePixelBuffer(128 * 64)] });

            shell.OpenSpriteInTab(sprite, "anim_test", @"C:\path\anim_test.hexp");

            var openedDoc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.False(openedDoc.IsDirty);
            Assert.Equal(@"C:\path\anim_test.hexp", openedDoc.FilePath);
            Assert.Equal("anim_test", openedDoc.Title);
        }

        [Fact]
        public void Flaw2_AssetPackDocument_RoundTrips_AnimationFilePaths_WithRelativeResolution()
        {
            var doc = AssetPackDocument.CreateNew("RelPathPack");
            var vm = new AssetPackViewModel(doc);

            string tempDir = Path.Combine(Path.GetTempPath(), "hexpack_rel_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string packPath = Path.Combine(tempDir, "pack.hexpack");
                string animPath = Path.Combine(tempDir, "anims", "anim_baby.hexp");
                Directory.CreateDirectory(Path.Combine(tempDir, "anims"));
                File.WriteAllText(animPath, JsonSerializer.Serialize(new SpriteState(128, 64)));

                vm.MatrixViewModel.SetAnimationFilePath("anim_baby", animPath);
                vm.SaveToPath(packPath);

                // Verify the saved JSON has relative path
                string json = File.ReadAllText(packPath);
                var savedDoc = JsonSerializer.Deserialize<AssetPackDocument>(json);
                Assert.NotNull(savedDoc);
                Assert.True(savedDoc.AnimationFilePaths.ContainsKey("anim_baby"));
                Assert.Equal(@"anims\anim_baby.hexp", savedDoc.AnimationFilePaths["anim_baby"]);

                // Reload and verify absolute path is resolved
                var reloadedVm = new AssetPackViewModel(savedDoc);
                reloadedVm.FilePath = packPath;
                reloadedVm.LoadDocument(savedDoc);

                string? resolvedPath = reloadedVm.MatrixViewModel.GetAnimationFilePath("anim_baby");
                Assert.NotNull(resolvedPath);
                Assert.Equal(Path.GetFullPath(animPath), Path.GetFullPath(resolvedPath));
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, true); } catch { }
                }
            }
        }

        [Fact]
        public void Flaw3_SyncFromWorkspace_CapturesFilePathFromOpenCanvasTab()
        {
            var tabServiceMock = new Mock<IWorkspaceTabService>();

            var activeCanvasSprite = new SpriteState(128, 64) { FrameRateFps = 25 };
            string filePath = @"C:\dev\anims\anim_baby.hexp";

            tabServiceMock.Setup(t => t.GetAllOpenSpritesWithPaths())
                .Returns([("anim_baby", activeCanvasSprite, filePath)]);

            var vm = new FlipperScheduleMatrixViewModel(tabService: tabServiceMock.Object);
            vm.SyncFromWorkspace();

            Assert.Equal(filePath, vm.GetAnimationFilePath("anim_baby"));
            Assert.Equal(25, vm.AnimationSprites["anim_baby"].FrameRateFps);
        }

        [Fact]
        public void Flaw4_NavigateToEntryTab_ForwardsFilePathToTabService()
        {
            var tabServiceMock = new Mock<IWorkspaceTabService>();
            string expectedPath = @"C:\dev\pack\anim_baby.hexp";

            SpriteState? passedSprite = null;
            string? passedTitle = null;
            string? passedFilePath = null;

            tabServiceMock.Setup(t => t.ActivateTabByTitle(It.IsAny<string>())).Returns(false);
            tabServiceMock.Setup(t => t.OpenSpriteInTab(It.IsAny<SpriteState>(), It.IsAny<string>(), It.IsAny<string?>()))
                .Callback<SpriteState, string, string?>((s, title, fp) =>
                {
                    passedSprite = s;
                    passedTitle = title;
                    passedFilePath = fp;
                });

            var vm = new FlipperScheduleMatrixViewModel(tabService: tabServiceMock.Object);
            vm.SetAnimationFilePath("anim_baby", expectedPath);

            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.NavigateToEntryTab(babyEntry);

            Assert.NotNull(passedSprite);
            Assert.Equal("anim_baby", passedTitle);
            Assert.Equal(expectedPath, passedFilePath);
        }

        [Fact]
        public void Flaw5_RenameEntry_MigratesAnimationFilePath()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SetAnimationFilePath("anim_baby", @"C:\path\anim_baby.hexp");

            var entry = vm.Entries.First(e => e.Name == "anim_baby");
            entry.Name = "anim_toddler";

            Assert.Null(vm.GetAnimationFilePath("anim_baby"));
            Assert.Equal(@"C:\path\anim_baby.hexp", vm.GetAnimationFilePath("anim_toddler"));
        }

        [Fact]
        public void Flaw6_UndoRedo_RestoresAnimationFilePaths()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            vm.SetAnimationFilePath("anim_baby", @"C:\path\anim_baby.hexp");

            var babyEntry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.DeleteEntry(babyEntry);
            Assert.Null(vm.GetAnimationFilePath("anim_baby"));

            vm.Undo();
            Assert.Equal(@"C:\path\anim_baby.hexp", vm.GetAnimationFilePath("anim_baby"));

            vm.Redo();
            Assert.Null(vm.GetAnimationFilePath("anim_baby"));
        }
    }
}
