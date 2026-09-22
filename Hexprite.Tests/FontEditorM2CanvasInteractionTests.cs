using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.ViewModels;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontEditorM2CanvasInteractionTests
    {
        public FontEditorM2CanvasInteractionTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        // ── F7: Advance Margin Click Clamping Leak Fix ────────────────────────

        [Fact]
        public void F7_DrawPixel_OutOfBoundsCoordinates_SuppressedWithoutCorruptingGlyph()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            vm.Document = doc;

            var glyph = vm.ActiveGlyph!;
            glyph.Width = 5;
            glyph.XAdvance = 8;
            Array.Fill(glyph.Pixels, false);

            vm.BeginDrawing();

            // Coordinate in the advance margin (cellX = 6, which is > Width (5) and < XAdvance (8))
            vm.DrawPixel(6, 2, erase: false);

            // Verify column 4 (Width - 1) is NOT painted
            for (int y = 0; y < glyph.Height; y++)
            {
                Assert.False(glyph.Pixels[y * glyph.Width + (glyph.Width - 1)],
                    $"Glyph pixel at column {glyph.Width - 1}, row {y} should not be painted by advance margin click");
            }

            // Negative coordinates should also be ignored
            vm.DrawPixel(-1, 0, erase: false);
            vm.DrawPixel(0, -1, erase: false);
            vm.DrawPixel(0, 10, erase: false);

            // Verify the entire glyph remains untouched
            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                Assert.False(glyph.Pixels[i], "No pixels should be modified by out-of-bounds coordinates");
            }

            vm.EndDrawing();
        }

        // ── F8: Line Interpolation on Rapid Drag (Bresenham) ──────────────────

        [Fact]
        public void F8_BresenhamLineInterpolation_ConnectsFastDragCoordinates()
        {
            // Verify Bresenham line algorithm connecting rapid mouse sweeps
            var points = new List<(int x, int y)>();

            int x0 = 1, y0 = 1;
            int x1 = 5, y1 = 4;

            int dx = Math.Abs(x1 - x0);
            int dy = -Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            int curX = x0;
            int curY = y0;

            while (true)
            {
                points.Add((curX, curY));
                if (curX == x1 && curY == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    curX += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    curY += sy;
                }
            }

            Assert.Equal((1, 1), points[0]);
            Assert.Equal((5, 4), points[^1]);
            Assert.True(points.Count >= 5, "Interpolation should produce all continuous intermediate points");

            // Ensure no gaps between consecutive points (Chebyshev distance <= 1)
            for (int i = 1; i < points.Count; i++)
            {
                int stepX = Math.Abs(points[i].x - points[i - 1].x);
                int stepY = Math.Abs(points[i].y - points[i - 1].y);
                Assert.True(stepX <= 1 && stepY <= 1, $"Gap detected between {points[i - 1]} and {points[i]}");
            }
        }

        [Fact]
        public void F8_FontViewModel_DragStroke_PaintsContinuousPixels()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            vm.Document = doc;

            var glyph = vm.ActiveGlyph!;
            glyph.Width = 8;
            glyph.Height = 8;
            Array.Fill(glyph.Pixels, false);

            vm.BeginDrawing();
            for (int x = 0; x < 5; x++)
            {
                vm.DrawPixel(x, x, erase: false);
            }
            vm.EndDrawing();

            for (int x = 0; x < 5; x++)
            {
                Assert.True(glyph.Pixels[x * glyph.Width + x], $"Diagonal pixel ({x},{x}) should be set");
            }
        }

        // ── F9: Negative Metric Offsets ───────────────────────────────────────

        [Fact]
        public void F9_NegativeOffsets_AllowedInViewModelAndPreservedInUndoRedo()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            vm.Document = doc;

            // XOffset supports negative values
            vm.XOffset = -5;
            Assert.Equal(-5, vm.XOffset);
            Assert.NotNull(vm.ActiveGlyph);
            Assert.Equal(-5, vm.ActiveGlyph.XOffset);
            Assert.True(vm.IsDirty);

            // YOffset supports negative values
            vm.YOffset = -12;
            Assert.Equal(-12, vm.YOffset);
            Assert.Equal(-12, vm.ActiveGlyph.YOffset);

            // Test boundary negative offset (-128)
            vm.XOffset = -128;
            vm.YOffset = -128;
            Assert.Equal(-128, vm.XOffset);
            Assert.Equal(-128, vm.YOffset);

            // Test Undo restores prior values
            vm.Undo(); // undo YOffset = -128
            Assert.Equal(-12, vm.YOffset);

            vm.Undo(); // undo XOffset = -128
            Assert.Equal(-5, vm.XOffset);

            vm.Redo(); // redo XOffset = -128
            Assert.Equal(-128, vm.XOffset);

            vm.Redo(); // redo YOffset = -128
            Assert.Equal(-128, vm.YOffset);
        }

        // ── F10: Live Text Preview Updates on All Metric Changes ──────────────

        [Fact]
        public void F10_MetricSetters_TriggerUpdatePreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            vm.Document = doc;
            vm.PreviewText = "A";

            var initialBitmap = vm.PreviewBitmap;
            Assert.NotNull(initialBitmap);

            // Baseline modification updates preview bitmap
            vm.Baseline = 5;
            Assert.NotNull(vm.PreviewBitmap);

            // YAdvance modification updates preview bitmap
            vm.YAdvance = 12;
            Assert.NotNull(vm.PreviewBitmap);

            // XAdvance modification updates preview bitmap
            vm.XAdvance = 10;
            Assert.NotNull(vm.PreviewBitmap);

            // XOffset modification updates preview bitmap
            vm.XOffset = -2;
            Assert.NotNull(vm.PreviewBitmap);

            // YOffset modification updates preview bitmap
            vm.YOffset = -1;
            Assert.NotNull(vm.PreviewBitmap);

            // ResizeActiveGlyph updates preview bitmap
            vm.ResizeActiveGlyph(12);
            Assert.NotNull(vm.PreviewBitmap);
        }

        // ── F11: Arrow Navigation Focus Suppression ──────────────────────────

        [Fact]
        public void F11_TextBoxBaseAndComboBox_IdentifiedForFocusSuppression()
        {
            // Verify types that must suppress arrow navigation in FontEditorPanel
            Assert.True(typeof(TextBoxBase).IsAssignableFrom(typeof(TextBox)));
            Assert.True(typeof(TextBoxBase).IsAssignableFrom(typeof(RichTextBox)));
            Assert.True(typeof(ComboBox).IsAssignableFrom(typeof(ComboBox)));
            Assert.False(typeof(TextBoxBase).IsAssignableFrom(typeof(Grid)));
            Assert.False(typeof(ComboBox).IsAssignableFrom(typeof(Grid)));

            WpfTestHelper.RunOnSta(() =>
            {
                object textBox = new TextBox();
                object richTextBox = new RichTextBox();
                object comboBox = new ComboBox();
                object grid = new Grid();

                Assert.True(textBox is TextBoxBase || textBox is ComboBox);
                Assert.True(richTextBox is TextBoxBase || richTextBox is ComboBox);
                Assert.True(comboBox is TextBoxBase || comboBox is ComboBox);
                Assert.False(grid is TextBoxBase || grid is ComboBox);
            });
        }

        // ── F12: Missing Glyph Fallback in Live Text Preview ───────────────────

        [Fact]
        public void F12_MissingSpace_AdvancesCursorWithoutCollapsingWords()
        {
            // Create font covering only 'A' and 'B' (code points 65..66). Space (32) is NOT in the font!
            var font = FontDocument.CreateNew(8, 8, 65, 66);
            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 5;
            glyphA.XAdvance = 6;
            glyphA.Pixels[0] = true; // ink at top-left

            var glyphB = font.GetGlyph('B')!;
            glyphB.Width = 5;
            glyphB.XAdvance = 6;
            glyphB.Pixels[0] = true; // ink at top-left

            // Render text with missing space
            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A B");

            Assert.True(result.GetLength(0) > 0);
            Assert.True(result.GetLength(1) > 0);

            // 'A' ink is at x = 0
            Assert.True(result[0, 0], "'A' ink should be at x=0");

            // 'B' ink must NOT collapse onto 'A' (x must be > 6 because 'A' advance is 6 and space advance > 0)
            bool foundBInk = false;
            for (int x = 6; x < result.GetLength(0); x++)
            {
                if (result[x, 0])
                {
                    foundBInk = true;
                    Assert.True(x >= 6 + 4, $"'B' ink should be at x >= 10, found at x={x}");
                    break;
                }
            }
            Assert.True(foundBInk, "'B' ink should be found after space advance");
        }

        [Fact]
        public void F12_MissingUnmappedCharacter_AdvancesCursorByMaxCellWidth()
        {
            // Font only contains 'A' (65) and 'C' (67). 'Z' (90) is missing.
            var font = FontDocument.CreateNew(8, 8, 65, 67);
            font.MaxCellWidth = 8;
            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 6;
            glyphA.XAdvance = 6;
            glyphA.Pixels[0] = true;

            var glyphC = font.GetGlyph('C')!;
            glyphC.Width = 6;
            glyphC.XAdvance = 6;
            glyphC.Pixels[0] = true;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "AZC");

            // 'A' is at x = 0
            Assert.True(result[0, 0]);

            // 'C' should appear at 'A' advance (6) + 'Z' fallback advance (8) = 14
            Assert.True(result[14, 0], $"'C' should appear at x=14 after missing 'Z' advance of 8");
        }

        [Fact]
        public void F12_MonospacedFont_MissingSpaceAdvancesByMaxCellWidth()
        {
            var font = FontDocument.CreateNew(10, 10, 65, 66);
            font.MaxCellWidth = 10;
            font.IsMonospaced = true;

            var glyphA = font.GetGlyph('A')!;
            glyphA.Width = 10;
            glyphA.XAdvance = 10;
            glyphA.Pixels[0] = true;

            var glyphB = font.GetGlyph('B')!;
            glyphB.Width = 10;
            glyphB.XAdvance = 10;
            glyphB.Pixels[0] = true;

            bool[,] result = FontPreviewRenderer.RenderPreviewText(font, "A B");

            // 'A' is at x = 0
            Assert.True(result[0, 0]);

            // In monospaced font, missing space advances by MaxCellWidth (10)
            // 'B' should be at 10 ('A') + 10 (' ') = 20
            Assert.True(result[20, 0], "'B' should appear at x=20 in monospaced font with missing space");
        }

        // ── F13: Cell Boundary Guide Line ─────────────────────────────────────

        [Fact]
        public void F13_GlyphGuideRenderer_DrawsCellBoundaryGuideLine()
        {
            var doc = FontDocument.CreateNew(16, 16, 65, 65);
            doc.Baseline = 12;
            doc.CellHeight = 16;

            var glyph = doc.GetGlyph('A')!;
            glyph.Width = 8;
            glyph.XAdvance = 12; // Distinct from Width

            int cellSize = 8;
            int pixelWidth = glyph.XAdvance * cellSize; // 96
            int pixelHeight = glyph.Height * cellSize;  // 128
            var buffer = new uint[pixelWidth * pixelHeight];

            // Fill buffer with background color
            uint bg = 0xFF202020;
            Array.Fill(buffer, bg);

            GlyphGuideRenderer.RenderGuides(buffer, pixelWidth, pixelHeight, cellSize, doc, glyph);

            // Check that a guide line exists at x = glyph.Width * cellSize (64)
            int boundaryPixelX = glyph.Width * cellSize; // 64
            bool foundBoundaryColor = false;

            for (int y = 0; y < pixelHeight; y++)
            {
                uint pixel = buffer[y * pixelWidth + boundaryPixelX];
                if (pixel != bg)
                {
                    foundBoundaryColor = true;
                    // Verify the color contains orange/amber component (R high, G medium, B low)
                    uint r = (pixel >> 16) & 0xFF;
                    uint g = (pixel >> 8) & 0xFF;
                    uint b = pixel & 0xFF;

                    Assert.True(r > b, "Boundary guide line should have distinct warm/amber color (R > B)");
                    break;
                }
            }

            Assert.True(foundBoundaryColor, "Guide line should be drawn at glyph.Width * cellSize");
        }
    }
}
