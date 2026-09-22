using System;
using System.Collections.Generic;
using System.Linq;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Hexprite.Tests.Services
{
    [Trait("Category", "Unit")]
    public class FlipperWindowManagerTests
    {
        public FlipperWindowManagerTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public void FlipperWindowManager_CanBeConstructed_WithDefaultServices()
        {
            var manager = new FlipperWindowManager();
            Assert.NotNull(manager);
        }

        [Fact]
        public void FlipperWindowManager_CanBeConstructed_WithServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IFlipperExportService, FlipperExportService>();
            services.AddSingleton<IFlipperImportService, FlipperImportService>();
            services.AddSingleton<IFlipperUsbDeployer, FlipperUsbDeployer>();
            services.AddSingleton<IFlipperScreenStreamService, FlipperScreenStreamService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IUserFeedbackService, UserFeedbackService>();
            var sp = services.BuildServiceProvider();

            var manager = new FlipperWindowManager(sp);
            Assert.NotNull(manager);
        }

        [Fact]
        public void WorkspaceTabService_DelegatesToShellViewModel_Correctly()
        {
            var codeGenMock = new Mock<ICodeGeneratorService>();
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
            var updateMock = new Mock<IUpdateService>();

            var autosaveMock = new Mock<IAutosaveService>();
            var spMock = new Mock<IServiceProvider>();
            spMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            var shell = new ShellViewModel(
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
                updateMock.Object);

            var tabService = new WorkspaceTabService(shell);

            // Test OpenSpriteInTab
            var sprite = new SpriteState(32, 32);
            tabService.OpenSpriteInTab(sprite, "Test Sprite");

            Assert.Single(shell.OpenDocuments);
            var active = tabService.GetActiveSpriteState();
            Assert.NotNull(active);
            Assert.Equal(32, active.Width);

            // Test GetActiveFramePixels
            var pixels = tabService.GetActiveFramePixels(false);
            Assert.NotNull(pixels);
            Assert.Equal(32 * 32, pixels.Length);

            // Test OpenSpritesInTabs list
            var sprite2 = new SpriteState(16, 16);
            tabService.OpenSpritesInTabs([sprite2], "Batch");
            Assert.Equal(2, shell.OpenDocuments.Count);
        }

        [Fact]
        public void WorkspaceTabService_ConstructedWithServiceProvider_ResolvesShellLazily()
        {
            var services = new ServiceCollection();

            var codeGenMock = new Mock<ICodeGeneratorService>();
            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            var dialogMock = new Mock<IDialogService>();
            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var exportMock = new Mock<IExportService>();
            var importExportMock = new Mock<IFileImportExportService>();
            var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
            var updateMock = new Mock<IUpdateService>();
            var autosaveMock = new Mock<IAutosaveService>();

            services.AddSingleton(codeGenMock.Object);
            services.AddSingleton(drawingMock.Object);
            services.AddSingleton(clipboardMock.Object);
            services.AddSingleton(pixelClipboardMock.Object);
            services.AddSingleton(dialogMock.Object);
            services.AddSingleton(themeMock.Object);
            services.AddSingleton(bugReportMock.Object);
            services.AddSingleton(feedbackMock.Object);
            services.AddSingleton(autosaveMock.Object);
            services.AddSingleton<IControllerFactory, ControllerFactory>();
            services.AddSingleton(exportMock.Object);
            services.AddSingleton(importExportMock.Object);
            services.AddSingleton(hardwarePreviewMock.Object);
            services.AddSingleton(updateMock.Object);
            services.AddSingleton<IFlipperWindowManager, FlipperWindowManager>();
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<IWorkspaceTabService>(sp => new WorkspaceTabService(sp));

            var sp = services.BuildServiceProvider();
            var tabService = sp.GetRequiredService<IWorkspaceTabService>();

            Assert.NotNull(tabService);
            Assert.Null(tabService.GetActiveSpriteState());

            var sprite = new SpriteState(16, 16);
            tabService.OpenSpriteInTab(sprite, "Lazy Test");

            var active = tabService.GetActiveSpriteState();
            Assert.NotNull(active);
            Assert.Equal(16, active.Width);
        }

        [Fact]
        public void FlipperWindowManager_ShowDeployOverloads_CanBeConstructed()
        {
            var manager = new FlipperWindowManager();
            Assert.NotNull(manager);
            // Verify method signatures and interface compliance exist
            Assert.True(typeof(IFlipperWindowManager).GetMethod("ShowDeploy", [typeof(IReadOnlyList<(string RelativePath, byte[] Data)>), typeof(string)]) != null);
            Assert.True(typeof(IFlipperWindowManager).GetMethod("ShowDeploy", [typeof(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>), typeof(string)]) != null);
        }
    }
}
