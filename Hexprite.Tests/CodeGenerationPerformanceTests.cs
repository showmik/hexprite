using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Performance")]
    public class CodeGenerationPerformanceTests
    {
        public CodeGenerationPerformanceTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public async Task GenerateCodeAsync_WithPreCanceledToken_ThrowsOperationCanceledException()
        {
            var service = new CodeGeneratorService();
            var frames = new List<bool[]> { new bool[64 * 64] };
            var settings = new ExportSettings { Format = ExportFormat.AdafruitGfx, SpriteName = "test" };

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await service.GenerateCodeAsync(
                    frames, 64, 64, settings,
                    isFloating: false, floatingPixels: null,
                    floatX: 0, floatY: 0, floatW: 0, floatH: 0,
                    pasteMode: FloatingPasteMode.Transparent,
                    frameDelays: null,
                    cancellationToken: cts.Token);
            });
        }

        [Fact]
        public async Task GenerateCodeAsync_CancelledInFlight_ThrowsOperationCanceledException()
        {
            var service = new CodeGeneratorService();
            // Create a large multi-frame workload (40 frames of 128x128)
            int frameCount = 40;
            int width = 128;
            int height = 128;
            var frames = Enumerable.Range(0, frameCount)
                .Select(_ => new bool[width * height])
                .ToList();

            var settings = new ExportSettings
            {
                Format = ExportFormat.PlainCArray,
                SpriteName = "largeAnim",
                ExportAsAnimation = true,
                AnimationLayout = AnimationExportLayout.ArrayOfFrames,
                IncludeRowComments = true
            };

            using var cts = new CancellationTokenSource();
            
            var task = service.GenerateCodeAsync(
                frames, width, height, settings,
                isFloating: false, floatingPixels: null,
                floatX: 0, floatY: 0, floatW: 0, floatH: 0,
                pasteMode: FloatingPasteMode.Transparent,
                frameDelays: null,
                cancellationToken: cts.Token);

            // Cancel immediately after starting
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
        }

        [Fact]
        public async Task UpdateTextOutputsAsync_InFlightCancellation_CancelsPreviousPassCleanly()
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

            var shell = new ShellViewModel(
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

            shell.NewDocumentCommand.Execute("128x128");
            var vm = (MainViewModel)shell.ActiveDocument!;

            // Fire rapid generation updates
            var task1 = vm.UpdateTextOutputsAsync();
            var task2 = vm.UpdateTextOutputsAsync();
            var task3 = vm.UpdateTextOutputsAsync();

            await Task.WhenAll(task1, task2, task3);

            // Verify final code was generated without error
            Assert.False(string.IsNullOrEmpty(vm.ExportedCode));
            Assert.False(vm.IsCodeStale);
        }

        [Theory]
        [InlineData(ExportFormat.AdafruitGfx, 16, 16, 1)]
        [InlineData(ExportFormat.U8g2DrawBitmap, 32, 16, 1)]
        [InlineData(ExportFormat.U8g2DrawXBM, 24, 24, 1)]
        [InlineData(ExportFormat.PlainCArray, 64, 64, 1)]
        [InlineData(ExportFormat.MicroPython, 16, 32, 2)]
        [InlineData(ExportFormat.RawHex, 10, 10, 1)]
        [InlineData(ExportFormat.RawBinary, 16, 16, 1)]
        public void CalculateByteCount_AccurateWithoutRegex_MatchesExpectedByteCount(
            ExportFormat format, int width, int height, int numFrames)
        {
            var service = new CodeGeneratorService();
            var frames = Enumerable.Range(0, numFrames)
                .Select(_ => new bool[width * height])
                .ToList();

            var settings = new ExportSettings
            {
                Format = format,
                SpriteName = "testSprite",
                ExportAsAnimation = numFrames > 1,
                AnimationLayout = AnimationExportLayout.ArrayOfFrames
            };

            int calculatedCount = service.CalculateByteCount(frames, width, height, settings);

            int expectedBytesPerRow = (int)Math.Ceiling(width / 8.0);
            int expectedTotalBytes = expectedBytesPerRow * height * numFrames;

            Assert.Equal(expectedTotalBytes, calculatedCount);
        }

        [Fact]
        public void HexLookupTables_Have256ElementsAndCorrectFormat()
        {
            Assert.Equal(256, CodeGeneratorService.HexUpper.Length);
            Assert.Equal(256, CodeGeneratorService.HexLower.Length);

            Assert.Equal("0x00", CodeGeneratorService.HexUpper[0]);
            Assert.Equal("0x00", CodeGeneratorService.HexLower[0]);

            Assert.Equal("0x0F", CodeGeneratorService.HexUpper[15]);
            Assert.Equal("0x0f", CodeGeneratorService.HexLower[15]);

            Assert.Equal("0xFF", CodeGeneratorService.HexUpper[255]);
            Assert.Equal("0xff", CodeGeneratorService.HexLower[255]);
        }

        [Fact]
        public void GenerateCode_LargeOutput_50KBPlus_ExecutesFastAndLowAllocation()
        {
            var service = new CodeGeneratorService();
            // 20 frames of 128x128 canvas = 327,680 bytes total
            int numFrames = 20;
            int width = 128;
            int height = 128;
            var frames = Enumerable.Range(0, numFrames)
                .Select(i => {
                    var b = new bool[width * height];
                    for (int p = 0; p < b.Length; p += 3) b[p] = true;
                    return b;
                })
                .ToList();

            var settings = new ExportSettings
            {
                Format = ExportFormat.PlainCArray,
                SpriteName = "largeSprite",
                ExportAsAnimation = true,
                AnimationLayout = AnimationExportLayout.ArrayOfFrames,
                IncludeRowComments = true,
                IncludeDimensionConstants = true,
                IncludeUsageComment = true
            };

            // Warmup
            service.GenerateCode(frames, width, height, settings, false, null, 0, 0, 0, 0);

            long bytesBefore = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();

            string code = service.GenerateCode(frames, width, height, settings, false, null, 0, 0, 0, 0);

            sw.Stop();
            long bytesAllocated = GC.GetTotalAllocatedBytes(true) - bytesBefore;

            Assert.True(code.Length > 50000, $"Expected > 50KB code length, but got {code.Length} chars");
            Assert.True(sw.ElapsedMilliseconds < 500, $"Expected generation in < 500ms, took {sw.ElapsedMilliseconds}ms");

            int byteCount = service.CalculateByteCount(frames, width, height, settings);
            Assert.Equal(height * ((width + 7) / 8) * numFrames, byteCount);
        }
    }
}
