using System;
using System.Linq;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Moq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class MainViewModelTests
    {
        public MainViewModelTests()
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
        public void Constructor_InitializesWithOneLayerAndOneSequence()
        {
            var vm = CreateMainViewModel();

            Assert.Single(vm.SpriteState.Layers);
            Assert.Single(vm.SpriteState.Frames);
            Assert.Equal("Layer 1", vm.SpriteState.Layers[0].Name);
        }

        [Fact]
        public void AddLayer_IncrementsLayerCountAndSelectsNewLayer()
        {
            var vm = CreateMainViewModel();
            Assert.Single(vm.SpriteState.Layers);

            vm.AddLayerCommand.Execute(null);

            Assert.Equal(2, vm.SpriteState.Layers.Count);
            Assert.Equal("Layer 2", vm.SpriteState.Layers[0].Name); // Added at index 0
            Assert.Equal(0, vm.SpriteState.ActiveLayerIndex); // The new layer is selected at index 0
        }

        [Fact]
        public void AddFrame_IncrementsFrameCount()
        {
            var vm = CreateMainViewModel();
            Assert.Single(vm.SpriteState.Frames);

            vm.AddFrameCommand.Execute(null);

            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.Equal(1, vm.SpriteState.ActiveFrameIndex); // The new frame is selected
        }

        [Fact]
        public void DeleteFrame_RemovesSelectedFrame()
        {
            var vm = CreateMainViewModel();
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            
            Assert.Equal(3, vm.SpriteState.Frames.Count);
            Assert.Equal(2, vm.SpriteState.ActiveFrameIndex);

            // Act
            vm.DeleteFrameCommand.Execute(null);

            // Assert
            Assert.Equal(2, vm.SpriteState.Frames.Count);
            Assert.Equal(1, vm.SpriteState.ActiveFrameIndex); // Selection moves to previous
        }

        [Fact]
        public void BatchInvert_ChangesOnlySelectedFrames_AndUndoRestoresAll()
        {
            var vm = CreateMainViewModel();
            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);

            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.SpriteState.Frames[1].LayerPixels[0].GetMonochromeData()[0] = false;
            vm.SpriteState.Frames[2].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.Frames[0].IsSelected = true;
            vm.Frames[1].IsSelected = false;
            vm.Frames[2].IsSelected = true;

            vm.BatchFrameOperationCommand.Execute(FrameBatchOperation.InvertActiveLayer);

            Assert.False(vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
            Assert.False(vm.SpriteState.Frames[1].LayerPixels[0].GetMonochromeData()[0]);
            Assert.False(vm.SpriteState.Frames[2].LayerPixels[0].GetMonochromeData()[0]);
            Assert.True(vm.Frames[0].IsSelected);
            Assert.True(vm.Frames[2].IsSelected);

            vm.UndoCommand.Execute(null);

            Assert.True(vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
            Assert.False(vm.SpriteState.Frames[1].LayerPixels[0].GetMonochromeData()[0]);
            Assert.True(vm.SpriteState.Frames[2].LayerPixels[0].GetMonochromeData()[0]);
        }

        [Fact]
        public void BatchClearActiveLayer_LeavesUnselectedFramesUntouched()
        {
            var vm = CreateMainViewModel();
            vm.IsAnimationEnabled = true;
            vm.AddFrameCommand.Execute(null);
            vm.AddFrameCommand.Execute(null);
            for (int frame = 0; frame < 3; frame++)
                vm.SpriteState.Frames[frame].LayerPixels[0].GetMonochromeData()[frame] = true;

            vm.Frames[0].IsSelected = true;
            vm.Frames[1].IsSelected = false;
            vm.Frames[2].IsSelected = true;

            vm.BatchFrameOperationCommand.Execute(FrameBatchOperation.ClearActiveLayer);

            Assert.False(vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
            Assert.True(vm.SpriteState.Frames[1].LayerPixels[0].GetMonochromeData()[1]);
            Assert.False(vm.SpriteState.Frames[2].LayerPixels[0].GetMonochromeData()[2]);
        }

        [Fact]
        public void ClearEntireSelectedFrames_SkipsLockedLayers()
        {
            var vm = CreateMainViewModel();
            vm.IsAnimationEnabled = true;
            vm.SpriteState.Layers.Add(new LayerState { Name = "Unlocked", IsVisible = true });
            vm.SpriteState.EnsureLayers();
            vm.SpriteState.Layers[0].IsLocked = true;
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[1] = true;
            vm.RebuildFrameViewModels();

            vm.ClearFrameCommand.Execute(null);

            Assert.True(vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0]);
            Assert.False(vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[1]);
        }

        [Fact]
        public void UndoRedo_SetsDirtyFlag()
        {
            var vm = CreateMainViewModel();
            
            Assert.False(vm.IsDirty);
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.SaveStateForUndo();
            
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void LayerCommands_CanExecute_ReflectsLayerStateAndSelection()
        {
            var vm = CreateMainViewModel();

            // Initial: 1 layer at index 0
            Assert.False(vm.CanMoveActiveLayerUp);
            Assert.False(vm.CanMoveActiveLayerDown);
            Assert.False(vm.CanDeleteSelectedLayers);
            Assert.False(vm.CanMergeLayers);
            Assert.True(vm.CanDuplicateActiveLayer);
            Assert.False(vm.DeleteLayerCommand.CanExecute(null));
            Assert.False(vm.MoveLayerUpCommand.CanExecute(null));
            Assert.False(vm.MoveLayerDownCommand.CanExecute(null));
            Assert.False(vm.MergeLayerCommand.CanExecute(null));

            // Add layer -> now 2 layers (new layer inserted at index 0 and active)
            vm.AddLayerCommand.Execute(null);
            Assert.Equal(2, vm.Layers.Count);
            Assert.Equal(0, vm.SpriteState.ActiveLayerIndex);
            Assert.False(vm.CanMoveActiveLayerUp);
            Assert.True(vm.CanMoveActiveLayerDown);
            Assert.True(vm.CanDeleteSelectedLayers);
            Assert.True(vm.CanMergeLayers);
            Assert.False(vm.MoveLayerUpCommand.CanExecute(null));
            Assert.True(vm.MoveLayerDownCommand.CanExecute(null));
            Assert.True(vm.DeleteLayerCommand.CanExecute(null));
            Assert.True(vm.MergeLayerCommand.CanExecute(null));

            // Move to layer 1 (bottom-most)
            vm.SetSingleLayerSelection(1);
            Assert.True(vm.CanMoveActiveLayerUp);
            Assert.False(vm.CanMoveActiveLayerDown);
            Assert.True(vm.CanDeleteSelectedLayers);
            Assert.False(vm.CanMergeLayers); // Cannot merge down from bottom
            Assert.True(vm.MoveLayerUpCommand.CanExecute(null));
            Assert.False(vm.MoveLayerDownCommand.CanExecute(null));
        }

        [Fact]
        public void RebuildLayerViewModels_PreservesViewModelInstances()
        {
            var vm = CreateMainViewModel();
            vm.AddLayerCommand.Execute(null);
            Assert.Equal(2, vm.Layers.Count);

            var firstVm = vm.Layers[0];
            var secondVm = vm.Layers[1];

            // Rebuilding when layers didn't add/remove should update instances in place
            vm.ReloadLayersFromState();

            Assert.Same(firstVm, vm.Layers[0]);
            Assert.Same(secondVm, vm.Layers[1]);
        }

        [Fact]
        public void MergeLayers_AppliesMergeDescending_AndPreservesTargetBlendMode()
        {
            var vm = CreateMainViewModel();

            // Layer 0
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[1] = true;
            vm.SpriteState.Layers[0].BlendMode = LayerBlendMode.Normal;

            // Add Layer 1
            vm.SpriteState.Layers.Add(new LayerState { Name = "Layer 2", BlendMode = LayerBlendMode.Xor, IsVisible = true });
            vm.SpriteState.Frames[0].LayerPixels.Add(new Hexprite.Core.MonochromePixelBuffer(vm.SpriteState.Width * vm.SpriteState.Height));
            vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[1] = true;
            vm.SpriteState.Frames[0].LayerPixels[1].GetMonochromeData()[2] = true;
            vm.ReloadLayersFromState();
            Assert.Equal(2, vm.Layers.Count);

            // Select layer 0 and merge down into layer 1
            vm.SetSingleLayerSelection(0);
            Assert.True(vm.CanMergeLayers);
            vm.MergeLayerCommand.Execute(null);

            // Now 1 layer remains
            Assert.Single(vm.Layers);
            var mergedLayer = vm.SpriteState.Layers[0];
            // Target layer (Layer 1) was XOR, so its blend mode is preserved
            Assert.Equal(LayerBlendMode.Xor, mergedLayer.BlendMode);

            var mergedPixels = vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData();
            // Pixel 0 is true
            Assert.True(mergedPixels[0]);
            // Pixel 1 is true
            Assert.True(mergedPixels[1]);
            // Pixel 2 is true
            Assert.True(mergedPixels[2]);
        }

        [Fact]
        public void CompositeFramePixels_WithIsExport_ExcludesDraftLayers()
        {
            var vm = CreateMainViewModel();

            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[0] = true;
            vm.SpriteState.Layers[0].ExcludeFromExport = false;

            vm.AddLayerCommand.Execute(null);
            vm.SpriteState.Frames[0].LayerPixels[0].GetMonochromeData()[1] = true;
            vm.SpriteState.Layers[0].ExcludeFromExport = true; // Mark top layer as draft

            var exportComposite = vm.SpriteState.CompositeFramePixels(0, isExport: true);
            var normalComposite = vm.SpriteState.CompositeFramePixels(0, isExport: false);

            // In export: draft layer is excluded, so pixel 1 is false
            Assert.True(exportComposite[0]);
            Assert.False(exportComposite[1]);

            // In normal display: draft layer is included, so pixel 1 is true
            Assert.True(normalComposite[0]);
            Assert.True(normalComposite[1]);
        }
    }
}
