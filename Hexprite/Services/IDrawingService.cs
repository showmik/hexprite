using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IDrawingService
    {
        bool ApplyFloodFill(SpriteState state, int startX, int startY, bool newState, bool isContiguous = true, IPixelClip? clip = null);
        void DrawLine(SpriteState state, int x0, int y0, int x1, int y1, bool newState, IPixelClip? clip = null);
        void DrawLine(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawBrushStamp(SpriteState state, int cx, int cy, int brushSize, bool newState, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawBrushStampDithered(SpriteState state, int cx, int cy, int brushSize, bool newState, DitherPattern ditherPattern, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawLineDithered(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize, DitherPattern ditherPattern, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawRectangle(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawFilledRectangle(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawEllipse(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawFilledEllipse(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null);
        void DrawDitherGradient(SpriteState state, int x0, int y0, int x1, int y1, bool newState, IPixelClip? clip = null);
        void ShiftGrid(SpriteState state, int offsetX, int offsetY);
        void InvertGrid(SpriteState state);
        void OutlineLayer(SpriteState state, OutlineSettings settings, IPixelClip? clip = null);

        /// <summary>
        /// Rotates a flat row-major pixel buffer (width × height). Returns a new buffer;
        /// width and height swap for 90° rotations.
        /// </summary>
        bool[] RotatePixels(bool[] src, int srcW, int srcH, RotationDirection dir);

        /// <summary>
        /// Flips a flat row-major pixel buffer (width × height). Returns a new buffer.
        /// </summary>
        bool[] FlipPixels(bool[] src, int srcW, int srcH, FlipDirection dir);
        bool[,] GetFloodFillMask(SpriteState state, int startX, int startY, ISelectionService? selection, FloatingPasteMode pasteMode, out int minX, out int minY, out int maxX, out int maxY);
    }
}
