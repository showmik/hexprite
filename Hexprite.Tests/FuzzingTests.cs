using FsCheck;
using FsCheck.Xunit;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;
using System;
using System.Linq;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class FuzzingTests
    {
        private readonly IDrawingService _drawingService = new DrawingService();

        [Property(MaxTest = 10000)]
        public void DrawLine_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, bool newState)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;

            try
            {
                _drawingService.DrawLine(state, x0, y0, x1, y1, newState);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawLine crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 5000)]
        public void DrawRectangle_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, int brushSize)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            brushSize = Math.Abs(brushSize % 50) + 1; // Constrain to prevent massive hangs
            
            try
            {
                _drawingService.DrawRectangle(state, x0, y0, x1, y1, true, brushSize);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawRectangle crashed on {state.Width}x{state.Height} canvas with brush {brushSize}!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 5000)]
        public void FloodFill_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int startX, int startY, bool newState)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            
            try
            {
                _drawingService.ApplyFloodFill(state, startX, startY, newState);
            }
            catch (Exception ex)
            {
                throw new Exception($"FloodFill crashed on {state.Width}x{state.Height} canvas at ({startX}, {startY})!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 5000)]
        public void DrawEllipse_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, int brushSize)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            brushSize = Math.Abs(brushSize % 50) + 1;
            
            try
            {
                _drawingService.DrawEllipse(state, x0, y0, x1, y1, true, brushSize);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawEllipse crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 1000)]
        public void RotatePixels_ShouldReturnValidBufferAndNotCrash(bool[] srcPixels, RotationDirection dir)
        {
            if (srcPixels == null || srcPixels.Length == 0) return; // Skip trivial

            int w = srcPixels.Length;
            int h = 1;

            try
            {
                var result = _drawingService.RotatePixels(srcPixels, w, h, dir);
                
                if (result.Length != srcPixels.Length)
                {
                    throw new Exception($"Rotate changed array size! Expected {srcPixels.Length}, got {result.Length}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception("RotatePixels crashed!", ex);
            }
        }

        [Property(MaxTest = 5000)]
        public void DrawFilledRectangle_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, int brushSize)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            brushSize = Math.Abs(brushSize % 50) + 1;
            
            try
            {
                _drawingService.DrawFilledRectangle(state, x0, y0, x1, y1, true, brushSize);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawFilledRectangle crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 5000)]
        public void DrawFilledEllipse_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, int brushSize)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            brushSize = Math.Abs(brushSize % 50) + 1;
            
            try
            {
                _drawingService.DrawFilledEllipse(state, x0, y0, x1, y1, true, brushSize);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawFilledEllipse crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 5000)]
        public void DrawDitherGradient_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int x0, int y0, int x1, int y1, bool newState)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            
            try
            {
                _drawingService.DrawDitherGradient(state, x0, y0, x1, y1, newState);
            }
            catch (Exception ex)
            {
                throw new Exception($"DrawDitherGradient crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 3000)]
        public void ShiftGrid_ShouldNotCrash_AndNotResizeBuffer(int w, int h, int offsetX, int offsetY)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            
            try
            {
                _drawingService.ShiftGrid(state, offsetX, offsetY);
            }
            catch (Exception ex)
            {
                throw new Exception($"ShiftGrid crashed on {state.Width}x{state.Height} canvas with offset ({offsetX}, {offsetY})!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        [Property(MaxTest = 1000)]
        public void InvertGrid_ShouldNotCrash_AndNotResizeBuffer(int w, int h)
        {
            w = Math.Abs(w % 256) + 1;
            h = Math.Abs(h % 256) + 1;
            var state = new SpriteState(w, h);
            int expectedLength = state.Pixels.Length;
            
            try
            {
                _drawingService.InvertGrid(state);
            }
            catch (Exception ex)
            {
                throw new Exception($"InvertGrid crashed on {state.Width}x{state.Height} canvas!", ex);
            }

            AssertBufferUnchanged(state, expectedLength);
        }

        private static void AssertBufferUnchanged(SpriteState state, int expectedLength)
        {
            if (state.Pixels.Length != expectedLength)
            {
                throw new Exception($"Drawing operation altered the buffer length! Expected {expectedLength}, got {state.Pixels.Length}");
            }
        }
    }
}
