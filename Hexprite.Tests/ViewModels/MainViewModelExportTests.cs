using System;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class MainViewModelExportTests
    {
        public MainViewModelExportTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        private MainViewModel CreateMainViewModel()
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
            return (MainViewModel)shell.ActiveDocument!;
        }

        [Fact]
        public void MainViewModel_TimingMode_ChangesTriggerUpdateAndNotification()
        {
            var vm = CreateMainViewModel();
            bool propChanged = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.TimingMode))
                    propChanged = true;
            };

            vm.TimingMode = AnimationTimingMode.NonBlockingMillis;

            Assert.True(propChanged);
            Assert.Equal(AnimationTimingMode.NonBlockingMillis, vm.ExportSettings.TimingMode);
        }

        [Fact]
        public void MainViewModel_IsTimingModeVisible_DependsOnAnimationAndArduinoFormat()
        {
            var vm = CreateMainViewModel();
            vm.ExportFormat = ExportFormat.AdafruitGfx;
            vm.IsAnimationEnabled = true;
            Assert.True(vm.IsTimingModeVisible);

            vm.ExportFormat = ExportFormat.MicroPython;
            Assert.False(vm.IsTimingModeVisible);

            vm.ExportFormat = ExportFormat.AdafruitGfx;
            vm.IsAnimationEnabled = false;
            Assert.False(vm.IsTimingModeVisible);
        }

        [Fact]
        public void AnimationLayout_CanSelectDeltaPatches()
        {
            var vm = CreateMainViewModel();
            vm.IsAnimationEnabled = true;

            bool propChanged = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.AnimationLayout))
                    propChanged = true;
            };

            vm.AnimationLayout = AnimationExportLayout.DeltaPatches;

            Assert.True(propChanged);
            Assert.Equal(AnimationExportLayout.DeltaPatches, vm.AnimationLayout);
            Assert.Equal(AnimationExportLayout.DeltaPatches, vm.ExportSettings.AnimationLayout);
        }

        [Fact]
        public void IsCompressionVisible_WhenAnimationLayoutChanges_NotifiesAndUpdatesVisibility()
        {
            var vm = CreateMainViewModel();
            vm.ExportFormat = ExportFormat.AdafruitGfx;
            vm.IsAnimationEnabled = true;
            vm.AnimationLayout = AnimationExportLayout.ArrayOfFrames;

            Assert.True(vm.IsCompressionVisible);

            bool compressionVisibleNotified = false;
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsCompressionVisible))
                    compressionVisibleNotified = true;
            };

            vm.AnimationLayout = AnimationExportLayout.VerticalSpriteSheet;

            Assert.True(compressionVisibleNotified);
            Assert.False(vm.IsCompressionVisible);

            compressionVisibleNotified = false;
            vm.AnimationLayout = AnimationExportLayout.DeltaPatches;
            Assert.False(vm.IsCompressionVisible);

            vm.AnimationLayout = AnimationExportLayout.ArrayOfFrames;
            Assert.True(compressionVisibleNotified);
            Assert.True(vm.IsCompressionVisible);
        }

        [Fact]
        public void IsCompressionVisible_LiquidCrystalChar_IsAlwaysFalse()
        {
            var vm = CreateMainViewModel();
            vm.ExportFormat = ExportFormat.LiquidCrystalChar;
            Assert.False(vm.IsCompressionVisible);
        }
    }
}
