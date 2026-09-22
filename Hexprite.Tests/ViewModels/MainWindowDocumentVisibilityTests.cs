using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Collection("WindowLayoutSettingsFile")]
    [Trait("Category", "Unit")]
    public class MainWindowDocumentVisibilityTests
    {
        private static ShellViewModel CreateShell(out Mock<IDialogService> dialogMock)
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
                updateMock.Object);
        }

        [Fact]
        public void ShellViewModel_DocumentModeTransitions_FireActiveTabChangedAndUpdatesActiveDocument()
        {
            var shell = CreateShell(out var dialogMock);
            int activeTabChangedCount = 0;
            var changedProperties = new List<string?>();

            shell.ActiveTabChanged += (_, _) => activeTabChangedCount++;
            shell.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName);

            // Initial state: no documents
            Assert.Null(shell.ActiveDocument);
            Assert.False(shell.HasOpenDocument);

            // 1. Create Sprite document (Mode == Sprite)
            shell.NewDocumentCommand.Execute("128x64");
            Assert.NotNull(shell.ActiveDocument);
            Assert.Equal(DocumentMode.Sprite, shell.ActiveDocument.Mode);
            Assert.True(shell.HasOpenDocument);
            Assert.Contains(nameof(ShellViewModel.ActiveDocument), changedProperties);
            Assert.True(activeTabChangedCount >= 1);

            int prevCount = activeTabChangedCount;
            changedProperties.Clear();

            // 2. Create Font document (Mode == Font)
            dialogMock.Setup(d => d.ShowNewDocumentDialog())
                .Returns((8, 8, ColorMode.Monochrome, DocumentMode.Font));
            shell.NewDocumentCommand.Execute(null);
            Assert.NotNull(shell.ActiveDocument);
            Assert.Equal(DocumentMode.Font, shell.ActiveDocument.Mode);
            Assert.Contains(nameof(ShellViewModel.ActiveDocument), changedProperties);
            Assert.True(activeTabChangedCount > prevCount);

            prevCount = activeTabChangedCount;
            changedProperties.Clear();

            // 3. Create Asset Pack document (Mode == AssetPack)
            dialogMock.Setup(d => d.ShowNewDocumentDialog())
                .Returns((128, 64, ColorMode.Monochrome, DocumentMode.AssetPack));
            shell.NewDocumentCommand.Execute(null);
            Assert.NotNull(shell.ActiveDocument);
            Assert.Equal(DocumentMode.AssetPack, shell.ActiveDocument.Mode);
            Assert.Contains(nameof(ShellViewModel.ActiveDocument), changedProperties);
            Assert.True(activeTabChangedCount > prevCount);

            prevCount = activeTabChangedCount;
            changedProperties.Clear();

            // 4. Switch back to Sprite tab (index 0)
            shell.ActiveDocument = shell.OpenDocuments[0];
            Assert.Equal(DocumentMode.Sprite, shell.ActiveDocument.Mode);
            Assert.Contains(nameof(ShellViewModel.ActiveDocument), changedProperties);
        }

        [Fact]
        public void ShellViewModel_TabSwitching_FiresActiveTabChangedExactlyOncePerSwitch()
        {
            var shell = CreateShell(out var dialogMock);

            // Create 3 documents: Sprite, Font, AssetPack
            shell.NewDocumentCommand.Execute("128x64");
            dialogMock.Setup(d => d.ShowNewDocumentDialog())
                .Returns((8, 8, ColorMode.Monochrome, DocumentMode.Font));
            shell.NewDocumentCommand.Execute(null);
            dialogMock.Setup(d => d.ShowNewDocumentDialog())
                .Returns((128, 64, ColorMode.Monochrome, DocumentMode.AssetPack));
            shell.NewDocumentCommand.Execute(null);

            Assert.Equal(3, shell.OpenDocuments.Count);

            int eventCount = 0;
            shell.ActiveTabChanged += (_, _) => eventCount++;

            // Switch to Doc 0 (Sprite)
            eventCount = 0;
            shell.ActiveDocument = shell.OpenDocuments[0];
            Assert.Equal(1, eventCount);

            // Switch to Doc 1 (Font)
            eventCount = 0;
            shell.ActiveDocument = shell.OpenDocuments[1];
            Assert.Equal(1, eventCount);

            // Switch to Doc 2 (AssetPack)
            eventCount = 0;
            shell.ActiveDocument = shell.OpenDocuments[2];
            Assert.Equal(1, eventCount);

            // Setting ActiveDocument to the SAME document should NOT fire ActiveTabChanged
            eventCount = 0;
            shell.ActiveDocument = shell.OpenDocuments[2];
            Assert.Equal(0, eventCount);
        }
    }
}