using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Threading;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public sealed class LinkedFileWatcherTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly Mock<IDialogService> _dialogMock;
        private readonly Mock<IFileImportExportService> _importExportMock;
        private readonly Mock<ICodeGeneratorService> _codeGenMock;
        private readonly ShellViewModel _shell;
        private readonly MainViewModel _vm;

        public LinkedFileWatcherTests()
        {
            WpfTestHelper.EnsureApplication();
            _tempDir = Path.Combine(Path.GetTempPath(), "HexpriteWatcherTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);

            _codeGenMock = new Mock<ICodeGeneratorService>();
            _codeGenMock.Setup(c => c.GenerateCodeAsync(
                It.IsAny<List<bool[]>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<ExportSettings>(),
                It.IsAny<bool>(),
                It.IsAny<bool[,]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<FloatingPasteMode>(),
                It.IsAny<List<int>>(),
                It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("const uint8_t sprite[] = { 0x00 };");

            _codeGenMock.Setup(c => c.CalculateByteCount(
                It.IsAny<List<bool[]>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<ExportSettings>()))
                .Returns(32);

            var drawingMock = new Mock<IDrawingService>();
            var clipboardMock = new Mock<IClipboardService>();
            var pixelClipboardMock = new Mock<IPixelClipboardService>();
            _dialogMock = new Mock<IDialogService>();
            var themeMock = new Mock<IThemeService>();
            var bugReportMock = new Mock<IBugReportService>();
            var feedbackMock = new Mock<IUserFeedbackService>();
            var controllerFactory = new ControllerFactory();
            var exportMock = new Mock<IExportService>();
            _importExportMock = new Mock<IFileImportExportService>();
            var hardwarePreviewMock = new Mock<IHardwarePreviewService>();
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

            _shell = new ShellViewModel(
                _codeGenMock.Object,
                drawingMock.Object,
                clipboardMock.Object,
                pixelClipboardMock.Object,
                _dialogMock.Object,
                themeMock.Object,
                bugReportMock.Object,
                feedbackMock.Object,
                controllerFactory,
                exportMock.Object,
                _importExportMock.Object,
                hardwarePreviewMock.Object,
                serviceProviderMock.Object);

            _shell.NewDocumentCommand.Execute("16x16");
            _vm = (MainViewModel)_shell.ActiveDocument!;
            FlushDispatcher();
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        private void FlushDispatcher()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new DispatcherOperationCallback(f =>
            {
                ((DispatcherFrame)f).Continue = false;
                return null;
            }), frame);
            Dispatcher.PushFrame(frame);
        }

        [Fact]
        public void OnLinkedFileChanged_50SequentialEvents_SurfacesExactlyOneConfirmationDialog()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "linked_source.c");
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x00 };");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            // External editor modifies the file
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0xFF };");

            _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()))
                       .Returns(false);

            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "linked_source.c");

            // Act: 50 events fire sequentially
            for (int i = 0; i < 50; i++)
            {
                _vm.OnLinkedFileChanged(this, eventArgs);
            }

            FlushDispatcher();

            // Assert: ShowConfirmation was called EXACTLY ONCE
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
            Assert.True(_vm.LinkedFileChangedExternally);
        }

        [Fact]
        public void OnLinkedFileChanged_50ConcurrentEvents_SurfacesExactlyOneConfirmationDialog()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "concurrent_source.c");
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x00 };");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            // External editor modifies the file
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0xAA };");

            _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()))
                       .Returns(false);

            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "concurrent_source.c");

            // Act: 50 events fire concurrently from multiple threads
            Parallel.For(0, 50, _ =>
            {
                _vm.OnLinkedFileChanged(this, eventArgs);
            });

            FlushDispatcher();

            // Assert: ShowConfirmation was called EXACTLY ONCE
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Fact]
        public void OnLinkedFileChanged_UnreadableOrNonExistentFile_DoesNotSurfaceDialog()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "nonexistent.c");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;

            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "nonexistent.c");

            // Act
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            // Assert
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Assert.False(_vm.LinkedFileChangedExternally);
        }

        [Fact]
        public void OnLinkedFileChanged_IdenticalContent_DoesNotSurfaceDialog()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "unchanged.c");
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x00 };");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "unchanged.c");

            // Act: Event fires without any disk content changes
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            // Assert
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Assert.False(_vm.LinkedFileChangedExternally);
        }

        [Fact]
        public async Task StandardFileSyncOperations_RestoreAndUpdate_DoNotTriggerFalseWarnings()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "sync_test.c");
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x00 };");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            _importExportMock.Setup(m => m.HasBackup(filePath)).Returns(true);
            _importExportMock.Setup(m => m.RestoreSpriteInFile(filePath)).Returns("const uint8_t sprite[] = { 0x00 };");
            _importExportMock.Setup(m => m.ExtractSpritesFromFile(filePath))
                .Returns(new List<DetectedSprite>
                {
                    new DetectedSprite { Name = "sprite", Format = ExportFormat.AdafruitGfx, Width = 16, Height = 16, CodeSnippet = "{ 0x00 }" }
                });

            _codeGenMock.Setup(c => c.GenerateCodeAsync(
                It.IsAny<List<bool[]>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<ExportSettings>(),
                It.IsAny<bool>(),
                It.IsAny<bool[,]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<FloatingPasteMode>(),
                It.IsAny<List<int>>(),
                It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("const uint8_t sprite[] = { 0x00 };");

            // Execute restore operation
            await _vm.ExecuteRestoreLinkedSourceAsync(skipConfirmation: true);
            FlushDispatcher();

            Assert.False(_vm.LinkedFileChangedExternally);

            // Simulating watcher event generated by restore itself
            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "sync_test.c");
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            // Assert: Restore did not cause false warning dialog
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Fact]
        public void OnLinkedFileChanged_LegitimateSecondChangeAfterUserResolution_TriggersSecondDialog()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "two_changes.c");
            File.WriteAllText(filePath, "v1");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            _dialogMock.Setup(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()))
                       .Returns(false);

            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "two_changes.c");

            // Change 1
            File.WriteAllText(filePath, "v2");
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Once());

            // User resolves / clears warning by updating file from Hexprite
            _vm.UpdateLinkedFileHashIfMatches(filePath);
            _vm.LinkedFileChangedExternally = false;

            // Change 2 (External editor modifies file again)
            File.WriteAllText(filePath, "v3");
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            // Assert: Total of 2 dialogs surfaced across the 2 distinct modification sequences
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Fact]
        public async Task StandardFileSyncOperations_UpdateLinkedSource_DoesNotTriggerFalseWarning()
        {
            // Arrange
            string filePath = Path.Combine(_tempDir, "update_test.c");
            File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x00 };");

            _vm.SpriteState.LinkedSourceFile = filePath;
            _vm.SpriteState.LinkedVariableName = "sprite";
            _vm.SpriteState.LinkedFormat = ExportFormat.AdafruitGfx;
            _vm.UpdateLinkedFileHashIfMatches(filePath);

            _codeGenMock.Setup(c => c.GenerateCodeAsync(
                It.IsAny<List<bool[]>>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<ExportSettings>(),
                It.IsAny<bool>(),
                It.IsAny<bool[,]>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<FloatingPasteMode>(),
                It.IsAny<List<int>>(),
                It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync("const uint8_t sprite[] = { 0x01 };");

            _importExportMock.Setup(m => m.UpdateSpriteInFile(filePath, "sprite", It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int?>()))
                .Callback(() => File.WriteAllText(filePath, "const uint8_t sprite[] = { 0x01 };"))
                .Returns("const uint8_t sprite[] = { 0x01 };");

            // Execute Update Linked Source
            bool updateResult = await _vm.ExecuteUpdateLinkedSourceAsync();
            Assert.True(updateResult);
            FlushDispatcher();

            Assert.False(_vm.LinkedFileChangedExternally);

            // Simulating watcher event generated by update itself
            var eventArgs = new FileSystemEventArgs(WatcherChangeTypes.Changed, _tempDir, "update_test.c");
            _vm.OnLinkedFileChanged(this, eventArgs);
            FlushDispatcher();

            // Assert: Update did not cause false warning dialog
            _dialogMock.Verify(d => d.ShowConfirmation(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Assert.False(_vm.LinkedFileChangedExternally);
        }
    }
}
