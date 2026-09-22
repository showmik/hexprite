using System;
using System.Windows;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Handles bitmap-level shape plotting for live previews during drag operations.
    /// Depends only on <see cref="IBitmapBufferContext"/> rather than the full
    /// MainViewModel, keeping rendering concerns cleanly separated.
    /// </summary>
    /// <remarks>Initializes a new renderer instance with the provided buffer context.</remarks>
    public class BitmapPreviewRenderer(IBitmapBufferContext context)
    {
        private readonly IBitmapBufferContext _ctx = context ?? throw new ArgumentNullException(nameof(context));

        // ── Preview methods (called during shape drag, do not commit to state) ──

        /// <summary>Previews a line shape.</summary>
        public void PreviewLine(int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
            => PreviewShape(PlotLine, x0, y0, x1, y1, newState, brushSize, shape, angleDeg);

        /// <summary>Previews a rectangle shape.</summary>
        public void PreviewRectangle(int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
            => PreviewShape(PlotRectangle, x0, y0, x1, y1, newState, brushSize, shape, angleDeg);

        /// <summary>Previews an ellipse shape.</summary>
        public void PreviewEllipse(int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
            => PreviewShape(PlotEllipse, x0, y0, x1, y1, newState, brushSize, shape, angleDeg);

        /// <summary>Previews a filled rectangle shape.</summary>
        public void PreviewFilledRectangle(int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
            => PreviewShape(PlotFilledRectangle, x0, y0, x1, y1, newState, brushSize, shape, angleDeg);

        /// <summary>Previews a filled ellipse shape.</summary>
        public void PreviewFilledEllipse(int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
            => PreviewShape(PlotFilledEllipse, x0, y0, x1, y1, newState, brushSize, shape, angleDeg);

        public void PreviewDitherGradient(int x0, int y0, int x1, int y1)
        {
            _ctx.RedrawGridFromMemory(updateHardware: false);
            
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lengthSq = dx * dx + dy * dy;
            
            uint cTrue = _ctx.ColorOnUint;
            uint pTrue = _ctx.PreviewOnUint;
            uint cFalse = _ctx.ColorOffUint;
            uint pFalse = _ctx.PreviewOffUint;
            
            int[] bayer = [
                0, 8, 2, 10,
                12, 4, 14, 6,
                3, 11, 1, 9,
                15, 7, 13, 5,
            ];
            
            int w = _ctx.SpriteState.Width;
            int h = _ctx.SpriteState.Height;
            var sel = _ctx.SelectionService;
            
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (sel.HasActiveSelection && !sel.IsPixelInSelection(x, y))
                        continue;
                        
                    float t = 1.0f;
                    if (lengthSq > 0.0001f)
                    {
                        float dot = (x - x0) * dx + (y - y0) * dy;
                        t = dot / lengthSq;
                    }
                    
                    t = Math.Clamp(t, 0.0f, 1.0f);
                    float threshold = t * 17.0f;
                    
                    int bayerValue = bayer[(y % 4) * 4 + (x % 4)];
                    bool pixelState = bayerValue < threshold - 1.0f;
                    
                    int i = (y * w) + x;
                    if (pixelState)
                    {
                        _ctx.CanvasBuffer[i] = cTrue;
                        _ctx.PreviewBuffer[i] = pTrue;
                    }
                    else
                    {
                        _ctx.CanvasBuffer[i] = cFalse;
                        _ctx.PreviewBuffer[i] = pFalse;
                    }
                }
            }
            FlushBitmaps();
        }

        // ── Shared preview pipeline ───────────────────────────────────────
        // All preview methods follow the same pattern: redraw base → plot → flush.
        // Extracted into a single helper to eliminate the repeated boilerplate.

        private delegate void PlotAction(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg);

        private void PreviewShape(PlotAction plot, int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape, int angleDeg)
        {
            _ctx.RedrawGridFromMemory(updateHardware: false);
            uint cc = newState ? _ctx.ColorOnUint : _ctx.ColorOffUint;
            uint pc = newState ? _ctx.PreviewOnUint : _ctx.PreviewOffUint;
            
            bool horz = _ctx.IsSymmetryHorizontalEnabled;
            bool vert = _ctx.IsSymmetryVerticalEnabled;

            plot(x0, y0, x1, y1, cc, pc, brushSize, shape, angleDeg);

            int mx0 = vert ? (int)Math.Round(2 * _ctx.SymmetryAxisX - x0 - 1, MidpointRounding.AwayFromZero) : x0;
            int mx1 = vert ? (int)Math.Round(2 * _ctx.SymmetryAxisX - x1 - 1, MidpointRounding.AwayFromZero) : x1;
            int my0 = horz ? (int)Math.Round(2 * _ctx.SymmetryAxisY - y0 - 1, MidpointRounding.AwayFromZero) : y0;
            int my1 = horz ? (int)Math.Round(2 * _ctx.SymmetryAxisY - y1 - 1, MidpointRounding.AwayFromZero) : y1;

            bool diffV = vert && (mx0 != x0 || mx1 != x1);
            bool diffH = horz && (my0 != y0 || my1 != y1);

            if (diffV) plot(mx0, y0, mx1, y1, cc, pc, brushSize, shape, angleDeg);
            if (diffH) plot(x0, my0, x1, my1, cc, pc, brushSize, shape, angleDeg);
            if (diffV && diffH) plot(mx0, my0, mx1, my1, cc, pc, brushSize, shape, angleDeg);
            
            FlushBitmaps();
        }

        // ── Bitmap plotting (writes directly to pixel buffers) ────────────────

        private void PlotPixel(int x, int y, uint canvasColor, uint previewColor)
        {
            int w = _ctx.SpriteState.Width;
            int h = _ctx.SpriteState.Height;
            if (x < 0 || x >= w || y < 0 || y >= h) return;

            // Clip preview to the active selection (if any)
            var sel = _ctx.SelectionService;
            if (sel.HasActiveSelection && !sel.IsPixelInSelection(x, y))
                return;

            int i = (y * w) + x;
            _ctx.CanvasBuffer[i] = canvasColor;
            _ctx.PreviewBuffer[i] = previewColor;
        }

        private void PlotLine(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg)
        {
            var offsets = brushSize > 1 ? Hexprite.Services.DrawingService.ComputeStampOffsets(brushSize, shape, angleDeg) : null;
            void Stamp(int cx, int cy) {
                if (offsets == null) PlotPixel(cx, cy, cc, pc);
                else { foreach (var (dx, dy) in offsets) PlotPixel(cx + dx, cy + dy, cc, pc); }
            }
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;
            while (true)
            {
                Stamp(x0, y0);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                bool stepX = false, stepY = false;
                if (e2 > -dy) { err -= dy; x0 += sx; stepX = true; }
                if (e2 < dx) { err += dx; y0 += sy; stepY = true; }
                
                if (stepX && stepY && shape == BrushShape.Line)
                {
                    Stamp(x0 - sx, y0);
                }
            }
        }

        private void PlotRectangle(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg)
        {
            var offsets = brushSize > 1 ? Hexprite.Services.DrawingService.ComputeStampOffsets(brushSize, shape, angleDeg) : null;
            void Stamp(int cx, int cy) {
                if (offsets == null) PlotPixel(cx, cy, cc, pc);
                else { foreach (var (dx, dy) in offsets) PlotPixel(cx + dx, cy + dy, cc, pc); }
            }
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int x = minX; x <= maxX; x++) { Stamp(x, minY); Stamp(x, maxY); }
            for (int y = minY + 1; y < maxY; y++) { Stamp(minX, y); Stamp(maxX, y); }
        }

        private void PlotEllipse(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg)
        {
            var offsets = brushSize > 1 ? Hexprite.Services.DrawingService.ComputeStampOffsets(brushSize, shape, angleDeg) : null;
            void Stamp(int cx, int cy) {
                if (offsets == null) PlotPixel(cx, cy, cc, pc);
                else { foreach (var (dx, dy) in offsets) PlotPixel(cx + dx, cy + dy, cc, pc); }
            }
            int a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
            long dx = 4L * (1 - a) * b * b, dy = 4L * (b1 + 1) * a * a;
            long err = dx + dy + (long)b1 * a * a, e2;

            if (x0 > x1) { x0 = x1; x1 += a; }
            if (y0 > y1) y0 = y1;
            y0 += (b + 1) / 2;
            y1 = y0 - b1;
            a *= 8 * a;
            b1 = 8 * b * b;

            do
            {
                Stamp(x1, y0); Stamp(x0, y0);
                Stamp(x0, y1); Stamp(x1, y1);
                e2 = 2 * err;
                if (e2 <= dy) { y0++; y1--; err += dy += a; }
                if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
            }
            while (x0 <= x1);

            while (y0 - y1 < b)
            {
                Stamp(x0 - 1, y0); Stamp(x1 + 1, y0);
                Stamp(x0 - 1, y1); Stamp(x1 + 1, y1);
                y0++; y1--;
            }
        }

        private void PlotFilledRectangle(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg)
        {
            if (brushSize > 1) { PlotRectangle(x0, y0, x1, y1, cc, pc, brushSize, shape, angleDeg); }
            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);
            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    PlotPixel(x, y, cc, pc);
        }

        private void PlotFilledEllipse(int x0, int y0, int x1, int y1, uint cc, uint pc, int brushSize, BrushShape shape, int angleDeg)
        {
            if (brushSize > 1) { PlotEllipse(x0, y0, x1, y1, cc, pc, brushSize, shape, angleDeg); }
            int a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
            long dx = 4L * (1 - a) * b * b, dy = 4L * (b1 + 1) * a * a;
            long err = dx + dy + (long)b1 * a * a, e2;

            if (x0 > x1) { x0 = x1; x1 += a; }
            if (y0 > y1) y0 = y1;
            y0 += (b + 1) / 2;
            y1 = y0 - b1;
            a *= 8 * a;
            b1 = 8 * b * b;

            do
            {
                for (int px = x0; px <= x1; px++) { PlotPixel(px, y0, cc, pc); PlotPixel(px, y1, cc, pc); }
                e2 = 2 * err;
                if (e2 <= dy) { y0++; y1--; err += dy += a; }
                if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
            }
            while (x0 <= x1);

            while (y0 - y1 < b)
            {
                // FIX: Sort inline bounds to handle Bresenham variable crossover
                int startX = Math.Min(x0 - 1, x1 + 1);
                int endX = Math.Max(x0 - 1, x1 + 1);
                
                for (int px = startX; px <= endX; px++) { PlotPixel(px, y0, cc, pc); PlotPixel(px, y1, cc, pc); }
                y0++; y1--;
            }
        }

        private void FlushBitmaps()
        {
            var rect = new Int32Rect(0, 0, _ctx.SpriteState.Width, _ctx.SpriteState.Height);
            _ctx.CanvasBitmap.WritePixels(rect, _ctx.CanvasBuffer, _ctx.SpriteState.Width * 4, 0);
            _ctx.PreviewBitmap.WritePixels(rect, _ctx.PreviewBuffer, _ctx.SpriteState.Width * 4, 0);
            // Shape preview can emit many updates per second during drag.
            // Defer realistic preview simulation to commit time to keep drawing smooth.
        }
    }
}
