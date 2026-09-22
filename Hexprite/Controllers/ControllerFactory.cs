using Hexprite.Rendering;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Default implementation of the controller factory. Creates new controller instances
    /// for each document to maintain proper state isolation.
    /// </summary>
    public class ControllerFactory : IControllerFactory
    {
        public BitmapPreviewRenderer CreatePreviewRenderer(IBitmapBufferContext context)
            => new(context);

        public IToolInputController CreateToolInputController(
            MainViewModel vm,
            IDrawingService drawingService,
            BitmapPreviewRenderer previewRenderer)
            => new ToolInputController(vm, drawingService, previewRenderer);

        public ISelectionInputController CreateSelectionInputController(
            MainViewModel vm,
            ISelectionService selectionService,
            IDrawingService drawingService)
            => new SelectionInputController(vm, selectionService, drawingService);
    }
}
