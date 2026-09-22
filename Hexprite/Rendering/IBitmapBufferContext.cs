using Hexprite.Core;
using Hexprite.Services;
using System.Windows.Media.Imaging;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Provides the bitmap buffer data needed by <see cref="BitmapPreviewRenderer"/>
    /// without exposing the full MainViewModel. This decouples the renderer from
    /// ViewModel internals, making both easier to maintain and test.
    /// </summary>
    public interface IBitmapBufferContext
    {
        SpriteState SpriteState { get; }
        uint[] CanvasBuffer { get; }
        uint[] PreviewBuffer { get; }
        uint ColorOnUint { get; }
        uint ColorOffUint { get; }
        uint PreviewOnUint { get; }
        uint PreviewOffUint { get; }
        WriteableBitmap CanvasBitmap { get; }
        WriteableBitmap PreviewBitmap { get; }
        ISelectionService SelectionService { get; }
        void RedrawGridFromMemory(bool updateHardware = true);
        void UpdatePreviewSimulation();

        // ── Symmetry state ────────────────────────────────────────────────
        bool IsSymmetryHorizontalEnabled { get; }
        bool IsSymmetryVerticalEnabled { get; }
        double SymmetryAxisX { get; }
        double SymmetryAxisY { get; }
    }
}
