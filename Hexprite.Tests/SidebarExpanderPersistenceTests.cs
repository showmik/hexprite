using System;
using System.IO;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [CollectionDefinition("SidebarExpanderPreferences", DisableParallelization = true)]
    public class SidebarExpanderPreferencesCollection { }

    [Collection("SidebarExpanderPreferences")]
    [Trait("Category", "Unit")]
    public class SidebarExpanderPersistenceTests : IDisposable
    {
        private readonly string _testSettingsFile;

        public SidebarExpanderPersistenceTests()
        {
            WpfTestHelper.EnsureApplication();
            _testSettingsFile = Path.Combine(Path.GetTempPath(), "Hexprite_SidebarExpTests_" + Guid.NewGuid().ToString("N") + ".json");
            UserPreferencesService.SetCustomSettingsPath(_testSettingsFile);
        }

        public void Dispose()
        {
            UserPreferencesService.SetCustomSettingsPath(null);
            try
            {
                if (File.Exists(_testSettingsFile))
                    File.Delete(_testSettingsFile);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        [Fact]
        public void UserPreferences_SidebarExpanders_DefaultValues()
        {
            var prefs = new UserPreferences();
            Assert.True(prefs.IsDisplayPreviewExpanded, "Display preview should be expanded by default on first run.");
            Assert.False(prefs.IsLinkedSourceExpanded, "Linked source should be collapsed by default on first run.");
            Assert.False(prefs.IsHardwarePreviewExpanded, "Hardware preview should be collapsed by default on first run.");
            Assert.False(prefs.IsCodeGenerationExpanded, "Code generation should be collapsed by default on first run.");
            Assert.False(prefs.IsImportExpanded, "Import should be collapsed by default on first run.");
        }

        [Fact]
        public void UserPreferencesService_SidebarExpanders_RoundTripsThroughUpdate()
        {
            UserPreferencesService.Update(p =>
            {
                p.IsDisplayPreviewExpanded = false;
                p.IsLinkedSourceExpanded = true;
                p.IsHardwarePreviewExpanded = true;
                p.IsCodeGenerationExpanded = true;
                p.IsImportExpanded = true;
            });

            var loaded = UserPreferencesService.Get();
            Assert.False(loaded.IsDisplayPreviewExpanded);
            Assert.True(loaded.IsLinkedSourceExpanded);
            Assert.True(loaded.IsHardwarePreviewExpanded);
            Assert.True(loaded.IsCodeGenerationExpanded);
            Assert.True(loaded.IsImportExpanded);
        }

        [Fact]
        public void MainViewModel_SidebarExpanders_InitialStateMatchesPreferences()
        {
            var vm = CreateTestMainViewModel();

            Assert.True(vm.IsDisplayPreviewExpanded);
            Assert.False(vm.IsLinkedSourceExpanded);
            Assert.False(vm.IsHardwarePreviewExpanded);
            Assert.False(vm.IsCodeGenerationExpanded);
            Assert.False(vm.IsImportExpanded);
        }

        [Fact]
        public void MainViewModel_SidebarExpanders_PropertiesUpdateUserPreferences()
        {
            var vm = CreateTestMainViewModel();

            vm.IsDisplayPreviewExpanded = false;
            Assert.False(UserPreferencesService.Get().IsDisplayPreviewExpanded);

            vm.IsHardwarePreviewExpanded = true;
            Assert.True(UserPreferencesService.Get().IsHardwarePreviewExpanded);

            vm.IsCodeGenerationExpanded = true;
            Assert.True(UserPreferencesService.Get().IsCodeGenerationExpanded);

            vm.IsImportExpanded = true;
            Assert.True(UserPreferencesService.Get().IsImportExpanded);
        }

        [Fact]
        public void MainViewModel_IsLinkedSourceExpanded_WhenLinked_DefaultsToExpanded()
        {
            var vm = CreateTestMainViewModel();
            Assert.False(vm.IsLinked, "Should start unlinked.");
            Assert.False(vm.IsLinkedSourceExpanded, "Unlinked canvas should have collapsed Linked Source.");

            // Link a file to the sprite
            vm.SpriteState.LinkedSourceFile = @"C:\Fake\test.c";
            vm.SpriteState.LinkedVariableName = "test_bitmap";
            vm.NotifyLinkChanged();

            Assert.True(vm.IsLinked, "Should now be linked.");
            Assert.True(vm.IsLinkedSourceExpanded, "Linked canvas should automatically expand Linked Source.");

            // User explicitly collapses it during the session
            vm.IsLinkedSourceExpanded = false;
            Assert.False(vm.IsLinkedSourceExpanded, "Explicit collapse should be honored.");

            // If link changes or is refreshed, it resets override and expands
            vm.NotifyLinkChanged();
            Assert.True(vm.IsLinkedSourceExpanded, "Re-linking should re-expand Linked Source.");
        }

        [Fact]
        public void MainViewModel_NotifyLinkChanged_RaisesPropertyChangedForIsLinkedSourceExpanded()
        {
            var vm = CreateTestMainViewModel();
            bool notified = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.IsLinkedSourceExpanded))
                    notified = true;
            };

            vm.NotifyLinkChanged();
            Assert.True(notified, "NotifyLinkChanged should notify IsLinkedSourceExpanded.");
        }

        private static MainViewModel CreateTestMainViewModel()
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
            var hwPreviewMock = new Mock<IHardwarePreviewService>();
            hwPreviewMock.Setup(h => h.GetAvailablePorts()).Returns([]);
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
                hwPreviewMock.Object,
                serviceProviderMock.Object);

            shell.NewDocumentCommand.Execute("16x16");
            return (MainViewModel)shell.ActiveDocument!;
        }
    }
}
