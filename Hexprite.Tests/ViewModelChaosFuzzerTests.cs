using FsCheck;
using FsCheck.Xunit;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using System;
using System.IO;
using System.Windows.Input;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class ViewModelChaosFuzzerTests
    {
        public ViewModelChaosFuzzerTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public void MainViewModel_ShouldNotCrashUnderChaoticInput()
        {
            // Arrange
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
            var autosaveMock = new Mock<IAutosaveService>();
            var serviceProviderMock = new Mock<IServiceProvider>();
            serviceProviderMock.Setup(sp => sp.GetService(typeof(IAutosaveService))).Returns(autosaveMock.Object);

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
                serviceProviderMock.Object);

            // Act
            // Create a document to test against
            shell.NewDocumentCommand.Execute("16x16");
            var vm = shell.ActiveDocument as MainViewModel;
            Assert.NotNull(vm);

            var random = new Random(42); // Deterministic seed for reproducible crash
            int iterations = FuzzTestHelper.GetIterationCount(defaultFastCount: 500, deepCount: 10000);
            
            for (int i = 0; i < iterations; i++)
            {
                int action = random.Next(16);
                try
                {
                    switch (action)
                    {
                        case 0: SafeExecute(vm.UndoCommand); break;
                        case 1: SafeExecute(vm.RedoCommand); break;
                        case 2: SafeExecute(vm.ClearCommand); break;
                        case 3: SafeExecute(vm.InvertCommand); break;
                        case 4: SafeExecute(vm.OutlineCommand); break;
                        case 5: SafeExecute(vm.AddLayerCommand); break;
                        case 6: SafeExecute(vm.DeleteLayerCommand); break;
                        case 7: SafeExecute(vm.DuplicateLayerCommand); break;
                        case 8: SafeExecute(vm.MergeLayerCommand); break;
                        case 9: SafeExecute(vm.MoveLayerUpCommand); break;
                        case 10: SafeExecute(vm.MoveLayerDownCommand); break;
                        case 11: SafeExecute(vm.SelectToolCommand, "Pen"); break;
                        case 12: SafeExecute(vm.SelectToolCommand, "Eraser"); break;
                        case 13: SafeExecute(vm.SelectToolCommand, "Pan"); break;
                        case 14: SafeExecute(vm.AddFrameCommand); break;
                        case 15: SafeExecute(vm.DeleteFrameCommand); break;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Crash during chaos action {action} at iteration {i}: {ex.Message}", ex);
                }
            }

            Assert.True(true);
        }

        private static void SafeExecute(ICommand command, object? parameter = null)
        {
            if (command != null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
            }
        }
    }
}
