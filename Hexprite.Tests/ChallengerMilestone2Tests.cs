using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Hexprite.Views;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Performance")]
    public class ChallengerMilestone2Tests
    {
        public ChallengerMilestone2Tests()
        {
            WpfTestHelper.EnsureApplication();
        }

        [Fact]
        public void Verify_HexLookup_AllocationSavings()
        {
            // Test empirical allocation savings of HexUpper/HexLower static array lookups vs dynamic formatting
            const int byteCount = 100000;
            var data = new byte[byteCount];
            var rng = new Random(42);
            rng.NextBytes(data);

            // Warm up static type and lookups
            _ = CodeGeneratorService.HexUpper[0];

            // 1. Dynamic string formatting simulation ($"0x{b:X2}")
            long allocBeforeDynamic = GC.GetAllocatedBytesForCurrentThread();
            
            var dynamicStrings = new string[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                dynamicStrings[i] = $"0x{data[i]:X2}";
            }
            long allocAfterDynamic = GC.GetAllocatedBytesForCurrentThread();
            long dynamicAllocated = allocAfterDynamic - allocBeforeDynamic;

            // 2. Static HexUpper lookup
            long allocBeforeStatic = GC.GetAllocatedBytesForCurrentThread();

            var staticStrings = new string[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                staticStrings[i] = CodeGeneratorService.HexUpper[data[i]];
            }
            long allocAfterStatic = GC.GetAllocatedBytesForCurrentThread();
            long staticAllocated = allocAfterStatic - allocBeforeStatic;

            // Verify correctness of lookups
            for (int i = 0; i < 100; i++)
            {
                Assert.Equal(dynamicStrings[i], staticStrings[i]);
            }

            // Static lookup array allocation (just array reference copies) must be dramatically lower than dynamic string creation
            Assert.True(staticAllocated <= dynamicAllocated / 4, 
                $"Expected static lookup allocation ({staticAllocated} bytes) to be <= 25% of dynamic allocation ({dynamicAllocated} bytes)");
        }

        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1215:\"GC.Collect\" should not be called", Justification = "Explicit GC warmup required for accurate allocation delta benchmark")]
        public void Verify_LargeGenerationPass_50KB_And_500KB()
        {
            var service = new CodeGeneratorService();
            int width = 128;
            int height = 128; // 16KB per frame

            // 1. Test ~50KB raw data / >200KB code output
            int numFrames50K = 4; // 64KB raw pixel bytes -> ~300KB formatted C code
            var frames50K = Enumerable.Range(0, numFrames50K)
                .Select(_ => new bool[width * height])
                .ToList();

            var settings = new ExportSettings
            {
                Format = ExportFormat.AdafruitGfx,
                SpriteName = "sprite50k",
                ExportAsAnimation = true,
                AnimationLayout = AnimationExportLayout.ArrayOfFrames,
                IncludeRowComments = true,
                IncludeDimensionConstants = true,
                IncludeUsageComment = true
            };

            // Warmup
            service.GenerateCode(frames50K, width, height, settings, false, null, 0, 0, 0, 0);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            long bytesBefore = GC.GetTotalAllocatedBytes(true);
            var sw = Stopwatch.StartNew();

            string code50K = service.GenerateCode(frames50K, width, height, settings, false, null, 0, 0, 0, 0);

            sw.Stop();
            long bytesAllocated = GC.GetTotalAllocatedBytes(true) - bytesBefore;
            int gen0Diff = GC.CollectionCount(0) - gen0Before;
            int gen1Diff = GC.CollectionCount(1) - gen1Before;
            int gen2Diff = GC.CollectionCount(2) - gen2Before;

            Assert.True(code50K.Length > 50000, $"Expected >50KB output code, got {code50K.Length} chars");
            Assert.True(sw.ElapsedMilliseconds < 100, $"Expected generation pass < 100ms, took {sw.ElapsedMilliseconds}ms");
            Assert.Equal(0, gen2Diff); // No Gen2 GC collections allowed during single code pass

            // 2. Test 500KB+ raw data pass (40 frames @ 128x64 = 40KB raw data -> >500KB code)
            int numFrames500K = 40;
            var frames500K = Enumerable.Range(0, numFrames500K)
                .Select(_ => new bool[width * height])
                .ToList();

            var sw500K = Stopwatch.StartNew();
            string code500K = service.GenerateCode(frames500K, width, height, settings, false, null, 0, 0, 0, 0);
            sw500K.Stop();

            Assert.True(code500K.Length > 500000, $"Expected >500KB code output, got {code500K.Length} chars");
            Assert.True(sw500K.ElapsedMilliseconds < 500, $"Expected 500KB+ pass < 500ms, took {sw500K.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void Verify_CalculateByteCount_ZeroRegex_Performance()
        {
            var service = new CodeGeneratorService();
            int width = 256;
            int height = 256;
            int numFrames = 50;
            var frames = Enumerable.Range(0, numFrames)
                .Select(_ => new bool[width * height])
                .ToList();

            var settings = new ExportSettings
            {
                Format = ExportFormat.PlainCArray,
                SpriteName = "perfTest",
                ExportAsAnimation = true,
                AnimationLayout = AnimationExportLayout.ArrayOfFrames
            };

            // Calculate byte count using direct arithmetic service
            var swDirect = Stopwatch.StartNew();
            int calculatedCount = service.CalculateByteCount(frames, width, height, settings);
            swDirect.Stop();

            int expectedBytesPerRow = (int)Math.Ceiling(width / 8.0);
            int expectedTotalBytes = expectedBytesPerRow * height * numFrames; // 32 * 256 * 50 = 409,600

            Assert.Equal(expectedTotalBytes, calculatedCount);
            Assert.True(swDirect.ElapsedMilliseconds < 5, $"CalculateByteCount took {swDirect.ElapsedMilliseconds}ms, expected < 5ms (O(1) arithmetic)");
        }

        [Fact]
        public async Task Verify_ExportSetting_Debouncing()
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

            shell.NewDocumentCommand.Execute("64x64");
            var vm = (MainViewModel)shell.ActiveDocument!;

            int updateCallCount = 0;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ExportedCode))
                {
                    Interlocked.Increment(ref updateCallCount);
                }
            };

            // Fire 20 rapid property updates within 50ms
            for (int i = 0; i < 20; i++)
            {
                vm.BytesPerLine = (i % 4 + 1) * 4;
                await Task.Delay(2);
            }

            // Immediately after rapid changes, updates should be debounced (0 or at most initial pass)
            int countBeforeDebounceWait = updateCallCount;

            // Wait for 150ms debounce timer + margin
            await Task.Delay(250);

            int countAfterDebounceWait = updateCallCount;

            // Debouncer must aggregate all 20 rapid property setters into at most 1 code update after timer expires
            Assert.True(countAfterDebounceWait - countBeforeDebounceWait <= 1,
                $"Debouncer failed to collapse rapid edits. Code updates received: {countAfterDebounceWait - countBeforeDebounceWait}");
        }

        [Fact]
        public async Task Verify_InFlightCancellation_Responsiveness()
        {
            var service = new CodeGeneratorService();
            // Workload: 100 frames of 128x128
            int width = 128;
            int height = 128;
            var frames = Enumerable.Range(0, 100)
                .Select(_ => new bool[width * height])
                .ToList();

            var settings = new ExportSettings
            {
                Format = ExportFormat.AdafruitGfx,
                SpriteName = "cancellationTest",
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

            var swCancel = Stopwatch.StartNew();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
            swCancel.Stop();

            Assert.True(swCancel.ElapsedMilliseconds < 250, $"Cancellation response took {swCancel.ElapsedMilliseconds}ms, expected < 250ms");
        }

        [Fact]
        public void Verify_SidebarPanel_Truncation_And_Tokenization()
        {
            // Verify TokenizeCode handles inline // and # comments, ignores them in strings
            string testCode = @"
const uint8_t sprite[] = {
  0xFF, 0x00, // inline comment
  0x12 // comment with # inside ""str # test""
};
# inline hash comment
const char* str = ""// not a comment # not a comment"";
";
            var tokens = SidebarPanel.TokenizeCode(testCode);
            Assert.NotEmpty(tokens);
            Assert.Contains(tokens, t => t.Type == Rendering.TokenType.Comment);
            Assert.Contains(tokens, t => t.Type == Rendering.TokenType.Keyword);
            Assert.Contains(tokens, t => t.Type == Rendering.TokenType.Literal);

            // Test FullLineComment helper
            Assert.True(SidebarPanel.IsFullLineComment("// full line comment"));
            Assert.True(SidebarPanel.IsFullLineComment("  # full line hash comment"));
            Assert.False(SidebarPanel.IsFullLineComment("  #include <stdio.h>")); // preprocessor directive
            Assert.False(SidebarPanel.IsFullLineComment("  uint8_t x = 5; // inline comment"));

            // Test FindInlineComment helper
            Assert.Equal(17, SidebarPanel.FindInlineComment("uint8_t val = 0; // comment"));
            Assert.Equal(17, SidebarPanel.FindInlineComment("uint8_t val = 0; # comment"));
            Assert.Equal(-1, SidebarPanel.FindInlineComment("string s = \"// string content # content\";"));
        }
    }
}
