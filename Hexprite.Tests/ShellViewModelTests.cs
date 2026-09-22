using System;
using System.IO;
using System.Text.Json;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    // Shares a collection with other tests that touch the real
    // %APPDATA%\Hexprite\window-layout.json file (see ShellViewModelWindowLayoutPersistenceTests)
    // so they never run concurrently and race each other's reads/writes of that file.
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class ShellViewModelTests : IDisposable
    {
        private readonly string _testDir;
        private readonly string _testSettingsFile;
        private readonly string _tempFile;

        public ShellViewModelTests()
        {
            _testDir = Path.Combine(AppContext.BaseDirectory, "ShellUnitData_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_testDir);
            _testSettingsFile = Path.Combine(_testDir, "user-preferences.json");
            UserPreferencesService.SetCustomSettingsPath(_testSettingsFile);
            ShellViewModel.SetCustomWindowLayoutPath(Path.Combine(_testDir, "window-layout.json"));
            _tempFile = Path.Combine(_testDir, "sprite_canvas.hexp");
            File.WriteAllText(_tempFile, "{}");
            WpfTestHelper.EnsureApplication();
        }

        public void Dispose()
        {
            UserPreferencesService.SetCustomSettingsPath(null);
            ShellViewModel.SetCustomWindowLayoutPath(null);
            try { if (Directory.Exists(_testDir)) Directory.Delete(_testDir, recursive: true); } catch { }
        }

        private ShellViewModel CreateShellViewModel(out Mock<IDialogService> dialogMock)
        {
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            return CreateShellViewModel(out dialogMock, serviceProviderMock.Object);
        }

        private ShellViewModel CreateShellViewModel(out Mock<IDialogService> dialogMock, IServiceProvider serviceProvider, ICodeGeneratorService? codeGen = null)
        {
            var codeGenMock = codeGen ?? new Mock<ICodeGeneratorService>().Object;
            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            dialogMock = new Mock<IDialogService>();
            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var controllerFactory = new ControllerFactory();
            var exportMock = new Mock<IExportService>();
            var importExportMock = new Mock<IFileImportExportService>();
            var hardwarePreviewMock = new Mock<IHardwarePreviewService>();

            return new ShellViewModel(
                codeGenMock,
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
                serviceProvider);
        }

        [Fact]
        public void OpenFile_WithValidSpriteState_AddsDocumentAndSetsActive()
        {
            // Arrange
            var spriteState = new SpriteState(16, 16) { ColorMode = ColorMode.Monochrome };
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(spriteState));

            var shell = CreateShellViewModel(out var dialogMock);
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>((msg) => throw new Exception("Dialog Shown: " + msg));

            Assert.Empty(shell.OpenDocuments);
            Assert.Null(shell.ActiveDocument);

            // Act
            shell.OpenFile(_tempFile);

            // Assert
            Assert.Single(shell.OpenDocuments);
            Assert.NotNull(shell.ActiveDocument);
            Assert.Equal(_tempFile, shell.ActiveDocument.FilePath);
            var mainVm = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.Equal(16, mainVm.SpriteState.Width);
            Assert.Equal(16, mainVm.SpriteState.Height);
        }

        [Fact]
        public void OpenFile_WithFontDocument_AddsFontDocumentAndSetsActive()
        {
            // Arrange
            var fontDoc = FontDocument.CreateNew(8, 8);
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(fontDoc));

            var shell = CreateShellViewModel(out var dialogMock);
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>((msg) => throw new Exception("Dialog Shown: " + msg));

            // Act
            shell.OpenFile(_tempFile);

            // Assert
            Assert.Single(shell.OpenDocuments);
            Assert.NotNull(shell.ActiveDocument);
            Assert.Equal(_tempFile, shell.ActiveDocument.FilePath);
            Assert.IsType<FontViewModel>(shell.ActiveDocument);
        }

        [Fact]
        public void OpenFile_WithInvalidJson_ShowsErrorMessage()
        {
            // Arrange
            File.WriteAllText(_tempFile, "{ invalid json }");
            var shell = CreateShellViewModel(out var dialogMock);
            
            string? shownMessage = null;
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>(msg => shownMessage = msg);

            // Act
            shell.OpenFile(_tempFile);

            // Assert
            Assert.Empty(shell.OpenDocuments);
            Assert.NotNull(shownMessage);
            Assert.Contains("Error opening file", shownMessage);
        }

        [Fact]
        public void OpenFile_WithMaxTabsReached_ShowsMessageAndReturns()
        {
            // Arrange
            var shell = CreateShellViewModel(out var dialogMock);
            string? shownMessage = null;
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>(msg => shownMessage = msg);

            // Fill tabs to max
            for (int i = 0; i < ShellViewModel.MaxTabs; i++)
            {
                shell.OpenDocuments.Add(new Mock<IDocumentTab>().Object);
            }

            // Act
            shell.OpenFile(_tempFile);

            // Assert
            Assert.Equal(ShellViewModel.MaxTabs, shell.OpenDocuments.Count);
            Assert.NotNull(shownMessage);
            Assert.Contains("Maximum", shownMessage);
        }

        [Fact]
        public void ImportFlipperFromPath_FolderWithoutMetaTxt_ShowsErrorAndNeverImports()
        {
            // Arrange
            var flipperMock = new Mock<IFlipperImportService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IFlipperImportService))).Returns(flipperMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object);
            string? shownMessage = null;
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>(msg => shownMessage = msg);

            string emptyFolder = Directory.CreateTempSubdirectory().FullName;
            try
            {
                // Act
                shell.ImportFlipperFromPath(emptyFolder);

                // Assert
                Assert.NotNull(shownMessage);
                Assert.Contains("meta.txt", shownMessage);
                flipperMock.Verify(f => f.ImportAnimation(It.IsAny<string>()), Times.Never);
                flipperMock.Verify(f => f.ImportFrame(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
            }
            finally
            {
                Directory.Delete(emptyFolder, true);
            }
        }

        [Fact]
        public void ImportFlipperFromPath_WithMaxTabsReached_ShowsMessageAndNeverImports()
        {
            // Arrange
            var flipperMock = new Mock<IFlipperImportService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IFlipperImportService))).Returns(flipperMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object);
            string? shownMessage = null;
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>(msg => shownMessage = msg);

            for (int i = 0; i < ShellViewModel.MaxTabs; i++)
            {
                shell.OpenDocuments.Add(new Mock<IDocumentTab>().Object);
            }

            // Act
            shell.ImportFlipperFromPath(_tempFile);

            // Assert
            Assert.Equal(ShellViewModel.MaxTabs, shell.OpenDocuments.Count);
            Assert.NotNull(shownMessage);
            Assert.Contains("Maximum", shownMessage);
            flipperMock.Verify(f => f.ImportAnimation(It.IsAny<string>()), Times.Never);
            flipperMock.Verify(f => f.ImportFrame(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void ImportXbmFromPath_WithMaxTabsReached_ShowsMessageAndNeverImports()
        {
            // Arrange
            var xbmMock = new Mock<IXbmService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IXbmService))).Returns(xbmMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object);
            string? shownMessage = null;
            dialogMock.Setup(d => d.ShowMessage(It.IsAny<string>()))
                      .Callback<string>(msg => shownMessage = msg);

            for (int i = 0; i < ShellViewModel.MaxTabs; i++)
            {
                shell.OpenDocuments.Add(new Mock<IDocumentTab>().Object);
            }

            // Act
            shell.ImportXbmFromPath(_tempFile);

            // Assert
            Assert.Equal(ShellViewModel.MaxTabs, shell.OpenDocuments.Count);
            Assert.NotNull(shownMessage);
            Assert.Contains("Maximum", shownMessage);
            xbmMock.Verify(x => x.ParseFile(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async System.Threading.Tasks.Task ImportFlipperFromPath_Animation_EnablesAnimationAndShowsTimeline()
        {
            // Arrange
            var flipperMock = new Mock<IFlipperImportService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var autosaveMock = new Mock<IAutosaveService>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IFlipperImportService))).Returns(flipperMock.Object);

            var sprite = new SpriteState(128, 64);
            sprite.Frames.Add(new FrameState { Name = "Frame 2", Layers = new System.Collections.Generic.List<LayerState> { new LayerState { Name = "Layer 1", Pixels = new bool[128 * 64] } } });
            sprite.FlipperCycle = new FlipperAnimationCycle { FramesOrder = new[] { 0, 1 }, PassiveFrameCount = 1, ActiveFrameCount = 1 };

            string tempDir = Directory.CreateTempSubdirectory().FullName;
            string metaFile = Path.Combine(tempDir, "meta.txt");
            File.WriteAllText(metaFile, "Filetype: Flipper Animation\nWidth: 128\nHeight: 64\nFrame rate: 10\n");

            try
            {
                flipperMock.Setup(f => f.ImportAnimation(metaFile)).Returns(sprite);

                var shell = CreateShellViewModel(out _, serviceProviderMock.Object);
                shell.IsTimelineVisible = false;

                // Act
                await shell.ImportFlipperFromPathAsync(metaFile);

                // Assert
                Assert.Single(shell.OpenDocuments);
                var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
                Assert.True(doc.IsAnimationEnabled);
                Assert.True(doc.SpriteState.IsAnimationEnabled);
                Assert.True(shell.IsTimelineVisible);
                Assert.Equal(2, doc.SpriteState.Frames.Count);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        [Fact]
        public async System.Threading.Tasks.Task ImportFlipperFromPath_SingleFrame_DoesNotForceAnimation()
        {
            // Arrange
            var flipperMock = new Mock<IFlipperImportService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var autosaveMock = new Mock<IAutosaveService>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IFlipperImportService))).Returns(flipperMock.Object);

            var sprite = new SpriteState(128, 64);
            string bmFile = Path.Combine(Path.GetTempPath(), "test_frame.bm");
            File.WriteAllBytes(bmFile, new byte[] { 0x00, 0x01 });

            try
            {
                flipperMock.Setup(f => f.ImportFrame(bmFile, 128, 64)).Returns(sprite);

                var shell = CreateShellViewModel(out _, serviceProviderMock.Object);
                shell.IsTimelineVisible = false;

                // Act
                await shell.ImportFlipperFromPathAsync(bmFile);

                // Assert
                Assert.Single(shell.OpenDocuments);
                var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
                Assert.False(doc.IsAnimationEnabled);
                Assert.False(doc.SpriteState.IsAnimationEnabled);
                Assert.False(shell.IsTimelineVisible);
            }
            finally
            {
                if (File.Exists(bmFile)) File.Delete(bmFile);
            }
        }

        [Fact]
        public void OpenFile_WithAnimation_EnablesAnimationAndShowsTimeline()
        {
            // Arrange
            var shell = CreateShellViewModel(out _);
            shell.IsTimelineVisible = false;

            var state = new SpriteState(16, 16)
            {
                IsAnimationEnabled = true
            };
            state.Frames.Add(new FrameState { Name = "Frame 2", Layers = new System.Collections.Generic.List<LayerState> { new LayerState { Name = "Layer 1", Pixels = new bool[16 * 16] } } });

            string projectFile = Path.Combine(Path.GetTempPath(), "anim_project.hexp");
            File.WriteAllText(projectFile, JsonSerializer.Serialize(state));

            try
            {
                // Act
                shell.OpenFile(projectFile);

                // Assert
                Assert.Single(shell.OpenDocuments);
                var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
                Assert.True(doc.IsAnimationEnabled);
                Assert.True(shell.IsTimelineVisible);
            }
            finally
            {
                if (File.Exists(projectFile)) File.Delete(projectFile);
            }
        }

        [Fact]
        public void ImportFromCode_WithMultiFrameAnimation_EnablesAnimationAndShowsTimeline()
        {
            // Arrange
            var codeGen = new CodeGeneratorService();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var autosaveMock = new Mock<IAutosaveService>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object, codeGen);
            shell.IsTimelineVisible = false;

            string animCode = @"
const unsigned char block_animation[3][8] = {
    { 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF },
    { 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55, 0xAA, 0x55 },
    { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }
};";

            dialogMock.Setup(d => d.ShowImportFromCodeDialog())
                .Returns((8, 8, animCode, "block_animation", ExportFormat.AdafruitGfx, false));

            // Act
            shell.ImportFromCodeMenuCommand.Execute(null);

            // Assert
            Assert.Single(shell.OpenDocuments);
            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.True(doc.IsAnimationEnabled);
            Assert.True(doc.SpriteState.IsAnimationEnabled);
            Assert.True(shell.IsTimelineVisible);
            Assert.Equal(3, doc.SpriteState.Frames.Count);
        }

        [Fact]
        public void ImportFromCode_WithSingleFrame_DoesNotForceAnimationOrShowTimeline()
        {
            // Arrange
            var codeGen = new CodeGeneratorService();
            var serviceProviderMock = new Mock<IServiceProvider>();
            var autosaveMock = new Mock<IAutosaveService>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object, codeGen);
            shell.IsTimelineVisible = false;

            string singleFrameCode = "const unsigned char single_frame[8] = { 0xFF, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0xFF };";

            dialogMock.Setup(d => d.ShowImportFromCodeDialog())
                .Returns((8, 8, singleFrameCode, "single_frame", ExportFormat.AdafruitGfx, false));

            // Act
            shell.ImportFromCodeMenuCommand.Execute(null);

            // Assert
            Assert.Single(shell.OpenDocuments);
            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.False(doc.IsAnimationEnabled);
            Assert.False(doc.SpriteState.IsAnimationEnabled);
            Assert.False(shell.IsTimelineVisible);
            Assert.Single(doc.SpriteState.Frames);
        }

        [Fact]
        public void FlipperCommands_CanExecute_TransitionsCorrectlyWithDocuments()
        {
            var sprite = new SpriteState(128, 64);
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(sprite));

            var serviceProviderMock = new Mock<IServiceProvider>();
            var autosaveMock = new Mock<IAutosaveService>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            var shell = CreateShellViewModel(out var dialogMock, serviceProviderMock.Object);

            // Initially no documents
            Assert.False(shell.HasOpenDocument);
            Assert.False(shell.ExportFlipperMenuCommand.CanExecute(null));
            Assert.False(shell.DeployFlipperUsbCommand.CanExecute(null));
            Assert.True(shell.OpenFlipperMatrixSimulatorCommand.CanExecute(null)); // Always enabled (supports standalone pack simulation)
            Assert.False(shell.ApplyFlipperTemplateCommand.CanExecute("HeaderBar"));

            // Open a document
            shell.OpenFile(_tempFile);
            Assert.True(shell.HasOpenDocument);
            Assert.True(shell.ExportFlipperMenuCommand.CanExecute(null));
            Assert.True(shell.DeployFlipperUsbCommand.CanExecute(null));
            Assert.True(shell.OpenFlipperMatrixSimulatorCommand.CanExecute(null));
            Assert.True(shell.ApplyFlipperTemplateCommand.CanExecute("HeaderBar"));

            // Close tab
            shell.CloseAllTabsCommand.Execute(null);
            Assert.False(shell.HasOpenDocument);
            Assert.False(shell.ExportFlipperMenuCommand.CanExecute(null));
            Assert.False(shell.DeployFlipperUsbCommand.CanExecute(null));
            Assert.True(shell.OpenFlipperMatrixSimulatorCommand.CanExecute(null));
            Assert.False(shell.ApplyFlipperTemplateCommand.CanExecute("HeaderBar"));
        }

        [Fact]
        public void RecentFiles_PopulatesAndOpensViaCommand()
        {
            var sprite = new SpriteState(128, 64);
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(sprite));

            UserPreferencesService.ClearRecentFiles();
            UserPreferencesService.AddRecentFile(_tempFile);

            var shell = CreateShellViewModel(out _);
            shell.RefreshRecentFilesCommand.Execute(null);

            Assert.True(shell.HasRecentFiles);
            Assert.Contains(shell.RecentFiles, r => r.FullPath == _tempFile);

            shell.OpenRecentFileCommand.Execute(_tempFile);
            Assert.True(shell.HasOpenDocument);
        }

        [Fact]
        public void RecentFiles_MissingFile_AlertsAndPrunesFromList()
        {
            string nonExistentPath = Path.Combine(_testDir, $"missing_{Guid.NewGuid():N}.hexp");
            UserPreferencesService.ClearRecentFiles();
            UserPreferencesService.AddRecentFile(nonExistentPath);

            var shell = CreateShellViewModel(out var mockDialog);
            shell.RefreshRecentFiles();

            shell.OpenRecentFileCommand.Execute(nonExistentPath);

            mockDialog.Verify(d => d.ShowMessage(It.Is<string>(s => s.Contains("could not be found"))), Times.Once);
            Assert.DoesNotContain(shell.RecentFiles, r => r.FullPath == nonExistentPath);
            Assert.False(shell.HasRecentFiles);
        }

        [Fact]
        public void RecentFiles_ClearCommand_EmptiesListAndUpdatesHasRecentFiles()
        {
            var sprite = new SpriteState(128, 64);
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(sprite));

            UserPreferencesService.ClearRecentFiles();
            UserPreferencesService.AddRecentFile(_tempFile);

            var shell = CreateShellViewModel(out _);
            shell.RefreshRecentFiles();
            Assert.True(shell.HasRecentFiles);

            shell.ClearRecentFilesCommand.Execute(null);
            Assert.False(shell.HasRecentFiles);
            Assert.Empty(shell.RecentFiles);
            Assert.Empty(shell.WelcomeRecentFiles);
        }

        [Fact]
        public void RecentFiles_DuplicateOpenFile_FocusesExistingTabWithoutDuplicating()
        {
            var sprite = new SpriteState(128, 64);
            File.WriteAllText(_tempFile, JsonSerializer.Serialize(sprite));

            var shell = CreateShellViewModel(out _);
            shell.OpenFile(_tempFile);
            Assert.Single(shell.OpenDocuments);
            var initialDoc = shell.ActiveDocument;

            // Open the same file a second time
            shell.OpenFile(_tempFile);
            Assert.Single(shell.OpenDocuments);
            Assert.Same(initialDoc, shell.ActiveDocument);
        }

        [Fact]
        public void RecentFiles_RemoveCommand_RemovesSingleItem()
        {
            string temp2 = Path.Combine(_testDir, $"recent_{Guid.NewGuid():N}.hexp");
            try
            {
                File.WriteAllText(_tempFile, JsonSerializer.Serialize(new SpriteState(128, 64)));
                File.WriteAllText(temp2, JsonSerializer.Serialize(new SpriteState(128, 64)));

                UserPreferencesService.ClearRecentFiles();
                UserPreferencesService.AddRecentFile(_tempFile);
                UserPreferencesService.AddRecentFile(temp2);

                var shell = CreateShellViewModel(out _);
                shell.RefreshRecentFiles();
                Assert.Equal(2, shell.RecentFiles.Count);

                shell.RemoveRecentFileCommand.Execute(_tempFile);
                Assert.Single(shell.RecentFiles);
                Assert.Equal(temp2, shell.RecentFiles[0].FullPath);
            }
            finally
            {
                if (File.Exists(temp2)) File.Delete(temp2);
            }
        }

        [Theory]
        [InlineData("pack.hexpack", "PACK")]
        [InlineData("myfont.hexfont", "FONT")]
        [InlineData("oldfont.hexpfont", "FONT")]
        [InlineData("sprite.hexp", "SPRITE")]
        public void RecentFileItem_FormatBadge_IdentifiesCorrectly(string path, string expectedBadge)
        {
            var item = new RecentFileItem(path);
            Assert.Equal(expectedBadge, item.FormatBadge);
        }

        [Fact]
        public void OpenSpriteInTab_AnimationSprite_EnablesAnimationAndShowsTimeline()
        {
            // Arrange
            var shell = CreateShellViewModel(out _);
            shell.IsTimelineVisible = false;

            var sprite = new SpriteState(128, 64)
            {
                IsAnimationEnabled = true,
                FrameRateFps = 12
            };
            sprite.Frames.Add(new FrameState { Name = "Frame 2", Layers = [new() { Name = "Layer 1", Pixels = new bool[128 * 64] }] });

            // Act
            shell.OpenSpriteInTab(sprite, "walk_anim");

            // Assert
            Assert.Single(shell.OpenDocuments);
            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.True(doc.IsAnimationEnabled);
            Assert.True(doc.SpriteState.IsAnimationEnabled);
            Assert.True(shell.IsTimelineVisible);
            Assert.Equal(2, doc.SpriteState.Frames.Count);
        }

        [Fact]
        public void ActiveDocument_SwitchToAnimationTab_ShowsTimelineAndEnablesAnimation()
        {
            // Arrange
            var shell = CreateShellViewModel(out _);
            
            // Tab 1: Static Icon
            var staticSprite = new SpriteState(16, 16);
            shell.OpenSpriteInTab(staticSprite, "icon");
            var staticDoc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            
            // Tab 2: Animation
            var animSprite = new SpriteState(128, 64) { IsAnimationEnabled = true };
            animSprite.Frames.Add(new FrameState { Name = "Frame 2", Layers = [new() { Name = "Layer 1", Pixels = new bool[128 * 64] }] });
            shell.OpenSpriteInTab(animSprite, "flipper_anim");
            var animDoc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.True(shell.IsTimelineVisible);

            // Hide timeline while looking at static tab
            shell.ActiveDocument = staticDoc;
            shell.IsTimelineVisible = false;

            // Act: Switch to animated tab
            shell.ActiveDocument = animDoc;

            // Assert
            Assert.True(shell.IsTimelineVisible);
            Assert.True(animDoc.IsAnimationEnabled);
        }

        [Fact]
        public void RenameTab_WithLinkedFilePath_UpdatesSpriteNameAndFilePathAndSetsDirty()
        {
            // Arrange
            var shell = CreateShellViewModel(out _);
            var sprite = new SpriteState(128, 64) { IsAnimationEnabled = true };
            shell.OpenSpriteInTab(sprite, "anim_old", @"C:\packs\anim_old.hexp");

            var doc = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.Equal(@"C:\packs\anim_old.hexp", doc.FilePath);
            Assert.Equal("anim_old", doc.Title);
            Assert.False(doc.IsDirty);

            // Act: RenameTab "anim_old" -> "anim_new"
            bool renamed = shell.RenameTab("anim_old", "anim_new");

            // Assert
            Assert.True(renamed);
            Assert.Equal("anim_new", doc.SpriteName);
            Assert.Equal(@"C:\packs\anim_new.hexp", doc.FilePath);
            Assert.True(doc.IsDirty);
            Assert.Equal("*anim_new", doc.Title);
        }

        [Fact]
        public void ToolsMenuCommands_CanExecute_ReflectsActiveDocumentState()
        {
            // Arrange
            var shell = CreateShellViewModel(out _);

            // Initially no document open
            Assert.False(shell.OpenDisplaySimulationCommand.CanExecute(null));
            Assert.False(shell.OpenCodeViewerCommand.CanExecute(null));

            // Act: Open a sprite document
            var sprite = new SpriteState(128, 64);
            shell.OpenSpriteInTab(sprite, "test_doc");

            // Assert: Commands can execute for active MainViewModel
            Assert.True(shell.OpenDisplaySimulationCommand.CanExecute(null));
            Assert.True(shell.OpenCodeViewerCommand.CanExecute(null));

            // Act: Close tab
            shell.CloseTabCommand.Execute(shell.ActiveDocument);

            // Assert: Commands disabled when no document
            Assert.False(shell.OpenDisplaySimulationCommand.CanExecute(null));
            Assert.False(shell.OpenCodeViewerCommand.CanExecute(null));
        }
    }
}
