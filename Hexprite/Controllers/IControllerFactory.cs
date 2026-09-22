using Hexprite.Rendering;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Factory for creating document-scoped controllers. Each document (MainViewModel)
    /// gets its own controller instances since they maintain state specific to that document.
    /// </summary>
    public interface IControllerFactory
    {
        /// <summary>
        /// Creates a preview renderer for the given bitmap buffer context.
        /// </summary>
        BitmapPreviewRenderer CreatePreviewRenderer(IBitmapBufferContext context);

        /// <summary>
        /// Creates a tool input controller for the given viewmodel.
        /// </summary>
        IToolInputController CreateToolInputController(
            MainViewModel vm,
            IDrawingService drawingService,
            BitmapPreviewRenderer previewRenderer);

        /// <summary>
        /// Creates a selection input controller for the given viewmodel.
        /// </summary>
        ISelectionInputController CreateSelectionInputController(
            MainViewModel vm,
            ISelectionService selectionService,
            IDrawingService drawingService);
    }
}
