using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

/// <summary>
/// Tests for the ControllerFactory that creates document-scoped controllers.
/// </summary>
[Trait("Category", "Unit")]
    public class ControllerFactoryTests
{
    private readonly IControllerFactory _factory = new ControllerFactory();

    [Fact]
    public void CreatePreviewRenderer_ReturnsBitmapPreviewRenderer()
    {
        var stub = new BitmapBufferContextStub();
        var renderer = _factory.CreatePreviewRenderer(stub);

        Assert.NotNull(renderer);
        Assert.IsType<BitmapPreviewRenderer>(renderer);
    }

    /// <summary>
    /// Minimal stub implementing IBitmapBufferContext for testing preview renderer creation.
    /// </summary>
    private sealed class BitmapBufferContextStub : IBitmapBufferContext
    {
        public SpriteState SpriteState { get; set; } = new(16, 16);
        public uint[] CanvasBuffer => [];
        public uint[] PreviewBuffer => [];
        public uint ColorOnUint => 0;
        public uint ColorOffUint => 0;
        public uint PreviewOnUint => 0;
        public uint PreviewOffUint => 0;
        public System.Windows.Media.Imaging.WriteableBitmap CanvasBitmap { get; set; } = null!;
        public System.Windows.Media.Imaging.WriteableBitmap PreviewBitmap { get; set; } = null!;
        public ISelectionService SelectionService => new SelectionService();
        public bool IsSymmetryHorizontalEnabled { get; set; }
        public bool IsSymmetryVerticalEnabled { get; set; }
        public double SymmetryAxisX { get; set; }
        public double SymmetryAxisY { get; set; }
        public void RedrawGridFromMemory(bool updateHardware = true) { }
        public void UpdatePreviewSimulation() { }
    }
}
