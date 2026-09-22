using System;
using Xunit;
using Hexprite.Core;

namespace Hexprite.Tests.Core
{
    [Trait("Category", "Unit")]
    public class OverflowPixelBufferTests
    {
        [Fact]
        public void Constructor_InitializesWithCorrectDimensions()
        {
            var buffer = new OverflowPixelBuffer(64, 64, 128);
            
            Assert.Equal(64 + 256, buffer.ExtendedWidth);
            Assert.Equal(64 + 256, buffer.ExtendedHeight);
            Assert.Equal(128, buffer.MarginX);
            Assert.Equal(128, buffer.MarginY);
            Assert.True(buffer.PreserveOverflow);
        }

        [Fact]
        public void GetMonochromeData_ReturnsCanvasSizedView()
        {
            var buffer = new OverflowPixelBuffer(32, 32, 10);
            buffer.SetPixel(5, 5, true);
            buffer.SetPixel(-1, -1, true); // Overflow

            var view = buffer.GetMonochromeData();
            Assert.Equal(32 * 32, view.Length);
            Assert.True(view[5 * 32 + 5]);
            
            // Negative coordinates are not in the canvas view
            // (There is no way to check them through GetMonochromeData)
        }

        [Fact]
        public void WriteMonochromeData_UpdatesCanvasRegionOnly()
        {
            var buffer = new OverflowPixelBuffer(16, 16, 5);
            buffer.SetPixel(-2, -2, true); // Keep an overflow pixel
            
            var newData = new bool[16 * 16];
            newData[0] = true;
            
            buffer.WriteMonochromeData(newData);
            
            var extended = buffer.GetExtendedData();
            // Origin is at 5,5
            Assert.True(extended[5 * buffer.ExtendedWidth + 5]); // The one we wrote
            Assert.True(extended[3 * buffer.ExtendedWidth + 3]); // The overflow one should remain
        }

        [Fact]
        public void SetPixel_ExpandsBufferWhenOutOfBounds()
        {
            var buffer = new OverflowPixelBuffer(16, 16, 2);
            // Margin is 2, so extended size is 20x20
            
            buffer.SetPixel(-5, -5, true); // Should expand margin
            
            Assert.True(buffer.MarginX >= 5);
            Assert.True(buffer.MarginY >= 5);
            
            var extended = buffer.GetExtendedData();
            int extX = -5 + buffer.MarginX;
            int extY = -5 + buffer.MarginY;
            Assert.True(extended[extY * buffer.ExtendedWidth + extX]);
        }

        [Fact]
        public void ShiftContent_MovesPixelsAndMargins()
        {
            var buffer = new OverflowPixelBuffer(16, 16, 10);
            buffer.SetPixel(0, 0, true);
            
            buffer.ShiftContent(5, 5);
            
            // Shifting content +5, +5 means the pixel at (0,0) moves to (5,5) in the canvas coordinates.
            // Which means the margin origin decreases by 5.
            var view = buffer.GetMonochromeData();
            Assert.False(view[0]); // No longer at 0,0
            Assert.True(view[5 * 16 + 5]); // Now at 5,5
        }
        
        [Fact]
        public void ResizeCanvas_PreservesOverflowData()
        {
            var buffer = new OverflowPixelBuffer(16, 16, 10);
            buffer.SetPixel(-2, -2, true); // overflow pixel
            buffer.SetPixel(5, 5, true);   // canvas pixel
            
            // Resize canvas to 32x32, shifting content by +8, +8
            var resized = buffer.ResizeCanvas(32, 32, 8, 8);
            
            var view = resized.GetMonochromeData();
            Assert.True(view[13 * 32 + 13]); // old (5,5) + (8,8) = (13,13)
            
            // The overflow pixel at old (-2,-2) should now be at canvas coords (-2+8, -2+8) = (6,6)
            Assert.True(view[6 * 32 + 6]);
        }
    }
}
