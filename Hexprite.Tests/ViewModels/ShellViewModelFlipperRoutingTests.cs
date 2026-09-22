using System;
using System.IO;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class ShellViewModelFlipperRoutingTests : IDisposable
    {
        private readonly string _tempFile;

        public ShellViewModelFlipperRoutingTests()
        {
            _tempFile = Path.GetTempFileName();
            WpfTestHelper.EnsureApplication();
        }

        public void Dispose()
        {
            if (File.Exists(_tempFile))
                File.Delete(_tempFile);
        }

        private class MockFlipperWindowManager : IFlipperWindowManager
        {
            public bool SimulatorCalled { get; private set; }
            public SpriteState? SimulatorSprite { get; private set; }

            public bool MediaSlicerCalled { get; private set; }
            public SpriteState? MediaSlicerSprite { get; private set; }

            public bool ScheduleMatrixCalled { get; private set; }

            public bool ScreenMirrorCalled { get; private set; }

            public bool DeployCalled { get; private set; }
            public SpriteState? DeploySprite { get; private set; }

            public bool ExportDialogCalled { get; private set; }
            public SpriteState? ExportSprite { get; private set; }
            public FlipperExportSettings? ExportSettings { get; private set; }

            public System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? SimulatorAnimations { get; private set; }
            public string? SimulatorPackName { get; private set; }
            public FlipperSimulatorSettings? SimulatorSettings { get; private set; }

            public void ShowSimulator(SpriteState? initialSprite = null)
            {
                SimulatorCalled = true;
                SimulatorSprite = initialSprite;
            }

            public void ShowSimulator(
                System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
                string packName = "AssetPack")
            {
                ShowSimulator(animations, packName, null, null);
            }

            public void ShowSimulator(
                System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
                string packName,
                FlipperSimulatorSettings? settings,
                Action<FlipperSimulatorSettings>? onSettingsChanged = null)
            {
                SimulatorCalled = true;
                SimulatorAnimations = animations;
                SimulatorPackName = packName;
                SimulatorSettings = settings;
                SimulatorSprite = animations?.FirstOrDefault().Sprite;
            }

            public void ShowMediaSlicer(SpriteState? initialSprite = null)
            {
                MediaSlicerCalled = true;
                MediaSlicerSprite = initialSprite;
            }

            public void ShowScheduleMatrix(System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack")
            {
                ScheduleMatrixCalled = true;
            }

            public void ShowScreenMirror()
            {
                ScreenMirrorCalled = true;
            }

            public void ShowDeploy(SpriteState? sprite = null)
            {
                DeployCalled = true;
                DeploySprite = sprite;
            }

            public void ShowDeploy(IReadOnlyList<(string RelativePath, byte[] Data)> files, string packName = "AssetPack")
            {
                DeployCalled = true;
            }

            public void ShowDeploy(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, string packName = "AssetPack")
            {
                DeployCalled = true;
            }

            public bool? ShowExportDialog(SpriteState sprite, FlipperExportSettings settings)
            {
                ExportDialogCalled = true;
                ExportSprite = sprite;
                ExportSettings = settings;
                return true;
            }
        }

        private static ShellViewModel CreateShell(MockFlipperWindowManager winManager, out Mock<IDialogService> dialogMock)
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
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
            var updateMock = new Mock<IUpdateService>();

            var autosaveMock = new Mock<IAutosaveService>();
            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);
            spMock.Setup(sp => sp.GetService(typeof(IFlipperWindowManager))).Returns(winManager);

            return new ShellViewModel(
                codeGenMock.Object,
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
                spMock.Object,
                updateMock.Object,
                winManager);
        }

        [Fact]
        public void OpenFlipperMatrixSimulatorCommand_WithActiveAssetPack_PassesPackAnimationsAndSettings()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            var doc = new AssetPackDocument
            {
                PackName = "DolphinAdventure",
                SimulatorSettings = new FlipperSimulatorSettings { Level = 20, Mood = 8, CustomBubbleText = "Hello Flipper" },
                Entries = [new FlipperManifestEntry { Name = "Jump", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }]
            };
            var sprite = new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome };
            doc.Animations["Jump"] = sprite;

            var apvm = shell.CreateAssetPackViewModel(doc);
            shell.OpenDocuments.Add(apvm);
            shell.ActiveDocument = apvm;

            shell.OpenFlipperMatrixSimulatorCommand.Execute(null);

            Assert.True(winManager.SimulatorCalled);
            Assert.NotNull(winManager.SimulatorAnimations);
            Assert.Single(winManager.SimulatorAnimations);
            Assert.Equal("DolphinAdventure", winManager.SimulatorPackName);
            Assert.NotNull(winManager.SimulatorSettings);
            Assert.Equal(20, winManager.SimulatorSettings.Level);
            Assert.Equal(8, winManager.SimulatorSettings.Mood);
            Assert.Equal("Hello Flipper", winManager.SimulatorSettings.CustomBubbleText);
        }

        [Fact]
        public void OpenFlipperMatrixSimulatorCommand_WithActiveDocument_PassesActiveSprite()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewDocumentCommand.Execute("128x64");
            Assert.NotNull(shell.ActiveDocument);

            shell.OpenFlipperMatrixSimulatorCommand.Execute(null);

            Assert.True(winManager.SimulatorCalled);
            Assert.NotNull(winManager.SimulatorSprite);
            Assert.Equal(128, winManager.SimulatorSprite.Width);
            Assert.Equal(64, winManager.SimulatorSprite.Height);
        }

        [Fact]
        public void OpenFlipperMatrixSimulatorCommand_WithoutActiveDocument_PassesNull()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            Assert.Null(shell.ActiveDocument);

            shell.OpenFlipperMatrixSimulatorCommand.Execute(null);

            Assert.True(winManager.SimulatorCalled);
            Assert.Null(winManager.SimulatorSprite);
        }

        [Fact]
        public void OpenFlipperScreenMirrorCommand_RoutesToWindowManager()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.OpenFlipperScreenMirrorCommand.Execute(null);

            Assert.True(winManager.ScreenMirrorCalled);
        }

        [Fact]
        public void OpenFlipperScheduleMatrixCommand_RoutesToWindowManager()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.OpenFlipperScheduleMatrixCommand.Execute(null);

            Assert.True(winManager.ScheduleMatrixCalled);
        }

        [Fact]
        public void OpenFlipperMediaSlicerCommand_WithActiveDocument_PassesActiveSprite()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewDocumentCommand.Execute("128x64");
            shell.OpenFlipperMediaSlicerCommand.Execute(null);

            Assert.True(winManager.MediaSlicerCalled);
            Assert.NotNull(winManager.MediaSlicerSprite);
        }

        [Fact]
        public void DeployFlipperUsbCommand_WithActiveDocument_PassesActiveSprite()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewDocumentCommand.Execute("128x64");
            shell.DeployFlipperUsbCommand.Execute(null);

            Assert.True(winManager.DeployCalled);
            Assert.NotNull(winManager.DeploySprite);
        }

        [Fact]
        public void ExportFlipperMenuCommand_WithAnimatedDocument_RoutesToWindowManagerExportDialog()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewDocumentCommand.Execute("128x64");
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);
            mvm.IsAnimationEnabled = true;
            mvm.SpriteState.Frames.Add(new FrameState()); // total 2 frames

            shell.ExportFlipperMenuCommand.Execute(null);

            Assert.True(winManager.ExportDialogCalled);
            Assert.NotNull(winManager.ExportSprite);
            Assert.NotNull(winManager.ExportSettings);
            Assert.Equal(2, winManager.ExportSettings.ActiveFrames);
        }

        [Fact]
        public void OpenSpritesInTabs_DeepClonesFrames_PreventingStateMutation()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            var sprite = new SpriteState(16, 16);
            sprite.ActiveLayerPixels[0] = true;

            shell.OpenSpritesInTabs([( "TestSprite", sprite )]);

            Assert.Single(shell.OpenDocuments);
            var mvm = Assert.IsType<MainViewModel>(shell.ActiveDocument);

            // Mutate original sprite
            sprite.ActiveLayerPixels[0] = false;
            sprite.ActiveLayerPixels[1] = true;

            // Cloned document should retain its original state
            Assert.True(mvm.SpriteState.ActiveLayerPixels[0]);
            Assert.False(mvm.SpriteState.ActiveLayerPixels[1]);
        }

        [Fact]
        public void ExportFlipperMenuCommand_WhenAssetPackActive_CanExecuteIsTrueAndTriggersExport()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out var dialogMock);

            shell.NewAssetPackCommand.Execute(null);

            Assert.NotNull(shell.ActiveDocument);
            var apvm = Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);

            Assert.True(shell.ExportFlipperMenuCommand.CanExecute(null));

            dialogMock.Setup(d => d.ShowOpenFolderDialog(It.IsAny<string>()))
                .Returns((string?)null); // User cancels folder dialog

            shell.ExportFlipperMenuCommand.Execute(null);

            dialogMock.Verify(d => d.ShowOpenFolderDialog(It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public void ExportFlipperMenuCommand_WhenNoDocument_CanExecuteIsFalse()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            Assert.Null(shell.ActiveDocument);
            Assert.False(shell.ExportFlipperMenuCommand.CanExecute(null));
        }

        [Fact]
        public void DeployFlipperUsbCommand_WhenAssetPackActive_CanExecuteIsTrueAndRoutesToWindowManager()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewAssetPackCommand.Execute(null);

            Assert.NotNull(shell.ActiveDocument);
            Assert.IsType<AssetPackViewModel>(shell.ActiveDocument);
            Assert.True(shell.DeployFlipperUsbCommand.CanExecute(null));

            shell.DeployFlipperUsbCommand.Execute(null);

            Assert.True(winManager.DeployCalled);
        }

        [Fact]
        public void DeployFlipperUsbCommand_WhenMainViewModelActive_RoutesToWindowManager()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            shell.NewDocumentCommand.Execute("128x64");

            Assert.NotNull(shell.ActiveDocument);
            Assert.IsType<MainViewModel>(shell.ActiveDocument);
            Assert.True(shell.DeployFlipperUsbCommand.CanExecute(null));

            shell.DeployFlipperUsbCommand.Execute(null);

            Assert.True(winManager.DeployCalled);
        }

        [Fact]
        public void DeployFlipperUsbCommand_WhenNoDocument_CanExecuteIsFalse()
        {
            var winManager = new MockFlipperWindowManager();
            var shell = CreateShell(winManager, out _);

            Assert.Null(shell.ActiveDocument);
            Assert.False(shell.DeployFlipperUsbCommand.CanExecute(null));
        }
    }
}
