using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Hexprite;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class AppCrashHandlingTests : IDisposable
    {
        public AppCrashHandlingTests()
        {
            WpfTestHelper.EnsureApplication();
            App.ResetCrashHandlingForTesting();
        }

        public void Dispose()
        {
            App.ResetCrashHandlingForTesting();
        }

        [Fact]
        public void MainViewModel_SaveAs_WritesFileToDisk()
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

            shell.NewDocumentCommand.Execute("16x16");
            var mvm = (MainViewModel)shell.ActiveDocument!;

            string tempFile = Path.Combine(Path.GetTempPath(), $"HexpriteTest_{Guid.NewGuid():N}.hexp");
            try
            {
                mvm.SaveAs(tempFile);
                Assert.True(File.Exists(tempFile), "Expected SaveAs to write the file to disk.");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void HandleCrash_PreventsDoubleCrashHandling()
        {
            int dialogCount = 0;
            void MockDialog(string msg, string title, MessageBoxImage img)
            {
                dialogCount++;
            }

            bool firstResult = App.HandleCrash(new Exception("UI crash"), "UI Thread", isTerminating: true, null, MockDialog);
            bool secondResult = App.HandleCrash(new Exception("Domain crash"), "AppDomain", isTerminating: true, null, MockDialog);

            Assert.True(firstResult);
            Assert.False(secondResult);
            Assert.Equal(1, dialogCount);
        }

        [Fact]
        public async Task HandleCrash_BackgroundThread_DoesNotThrowCrossThreadException()
        {
            var ex = await Task.Run(() =>
            {
                return Record.Exception(() =>
                {
                    App.HandleCrash(
                        new InvalidOperationException("Background thread crash"),
                        "Background Thread",
                        isTerminating: true,
                        null,
                        (msg, title, img) => { /* no-op dialog */ });
                });
            });

            Assert.Null(ex);
        }
    }
}
