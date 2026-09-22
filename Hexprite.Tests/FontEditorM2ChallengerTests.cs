using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Input;
using Hexprite.Core;
using Hexprite.ViewModels;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontEditorM2ChallengerTests
    {
        public FontEditorM2ChallengerTests()
        {
            WpfTestHelper.EnsureApplication();
            WpfTestHelper.RunOnSta(() =>
            {
                if (System.Windows.Application.Current != null && System.Windows.Application.Current.Resources.MergedDictionaries.Count == 0)
                {
                    System.Windows.Application.Current.Resources.MergedDictionaries.Add(
                        new System.Windows.ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Dim.xaml", UriKind.RelativeOrAbsolute) });
                    System.Windows.Application.Current.Resources.MergedDictionaries.Add(
                        new System.Windows.ResourceDictionary { Source = new Uri("/Hexprite;component/Themes/Styles.xaml", UriKind.RelativeOrAbsolute) });
                }
            });
        }

        // ─────────────────────────────────────────────────────────────────────
        // Helper: Canvas Drag Simulator matching FontEditorPanel.xaml.cs logic
        // ─────────────────────────────────────────────────────────────────────

        private sealed class CanvasDragSimulator
        {
            private readonly FontViewModel _vm;
            private int _lastCellX = -1;
            private int _lastCellY = -1;
            private bool _isDrawing;
            private bool _isErasing;

            public int LastCellX => _lastCellX;
            public int LastCellY => _lastCellY;
            public bool IsDrawing => _isDrawing;
            public bool IsErasing => _isErasing;

            public CanvasDragSimulator(FontViewModel vm)
            {
                _vm = vm;
            }

            public void MouseDown(int cellX, int cellY, bool erase = false)
            {
                _isDrawing = !erase;
                _isErasing = erase;
                _lastCellX = -1;
                _lastCellY = -1;
                _vm.BeginDrawing();
                ProcessCell(cellX, cellY);
            }

            public void MouseMove(int cellX, int cellY)
            {
                ProcessCell(cellX, cellY);
            }

            public void MouseUp()
            {
                _isDrawing = false;
                _isErasing = false;
                _lastCellX = -1;
                _lastCellY = -1;
                _vm.EndDrawing();
            }

            public void MouseLeave()
            {
                _lastCellX = -1;
                _lastCellY = -1;
            }

            private void ProcessCell(int cellX, int cellY)
            {
                _vm.CursorX = cellX;
                _vm.CursorY = cellY;

                if (_isDrawing || _isErasing)
                {
                    if (_vm.ActiveGlyph != null &&
                        cellX >= 0 && cellX < _vm.ActiveGlyph.Width &&
                        cellY >= 0 && cellY < _vm.ActiveGlyph.Height)
                    {
                        if (_lastCellX >= 0 && _lastCellY >= 0 && (_lastCellX != cellX || _lastCellY != cellY))
                        {
                            int x0 = _lastCellX;
                            int y0 = _lastCellY;
                            int x1 = cellX;
                            int y1 = cellY;

                            int dx = Math.Abs(x1 - x0);
                            int dy = -Math.Abs(y1 - y0);
                            int sx = x0 < x1 ? 1 : -1;
                            int sy = y0 < y1 ? 1 : -1;
                            int err = dx + dy;

                            while (true)
                            {
                                _vm.DrawPixel(x0, y0, _isErasing);
                                if (x0 == x1 && y0 == y1) break;
                                int e2 = 2 * err;
                                if (e2 >= dy)
                                {
                                    err += dy;
                                    x0 += sx;
                                }
                                if (e2 <= dx)
                                {
                                    err += dx;
                                    y0 += sy;
                                }
                            }
                        }
                        else
                        {
                            _vm.DrawPixel(cellX, cellY, _isErasing);
                        }

                        _lastCellX = cellX;
                        _lastCellY = cellY;
                    }
                    else
                    {
                        _lastCellX = -1;
                        _lastCellY = -1;
                    }
                }
            }
        }

        // Independent mathematical Bresenham line generator oracle
        private static List<(int x, int y)> GenerateBresenhamPoints(int x0, int y0, int x1, int y1)
        {
            var points = new List<(int x, int y)>();
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
            return points;
        }

        private static FontViewModel CreateTestFontViewModel(int width = 16, int height = 16, int xAdvance = 20)
        {
            var vm = new FontViewModel();
            var doc = FontDocument.CreateNew(width, height, 65, 65);
            doc.Glyphs[0].XAdvance = xAdvance;
            vm.Document = doc;
            return vm;
        }

        // ═════════════════════════════════════════════════════════════════════
        // PART 1: BRESENHAM DRAG-DRAWING STRESS & ORACLE CHALLENGES
        // ═════════════════════════════════════════════════════════════════════

        [Theory]
        [InlineData(2, 1, 4, 14)]   // Octant 2: Steep positive
        [InlineData(4, 14, 2, 1)]   // Octant 7: Steep negative
        [InlineData(13, 1, 10, 14)] // Octant 3: Steep negative x
        [InlineData(10, 14, 13, 1)] // Octant 6: Steep positive x, negative y
        [InlineData(1, 0, 3, 15)]   // High aspect ratio steep (~1:7.5)
        [InlineData(7, 15, 8, 0)]   // Near vertical steep (dx=1, dy=15)
        public void Challenge1_SteepAngles_AllIntermediateCellsPainted_NoCellsDropped(int x0, int y0, int x1, int y1)
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            sim.MouseDown(x0, y0);
            sim.MouseMove(x1, y1);
            sim.MouseUp();

            var expectedPoints = GenerateBresenhamPoints(x0, y0, x1, y1);
            var glyph = vm.ActiveGlyph!;

            // 1. Verify 8-way connectivity (no gaps in intermediate cells)
            for (int i = 1; i < expectedPoints.Count; i++)
            {
                int stepX = Math.Abs(expectedPoints[i].x - expectedPoints[i - 1].x);
                int stepY = Math.Abs(expectedPoints[i].y - expectedPoints[i - 1].y);
                Assert.True(stepX <= 1 && stepY <= 1, $"Chebyshev gap between step {i - 1} and {i}");
            }

            // 2. Verify all discrete path cells are set to true in glyph bitmap
            foreach (var (x, y) in expectedPoints)
            {
                Assert.True(glyph.Pixels[y * glyph.Width + x],
                    $"Cell ({x}, {y}) along steep path ({x0},{y0})->({x1},{y1}) must be painted");
            }

            // 3. Verify total painted pixel count exactly matches unique points
            var uniqueExpected = new HashSet<(int, int)>(expectedPoints);
            int paintedCount = 0;
            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                if (glyph.Pixels[i]) paintedCount++;
            }
            Assert.Equal(uniqueExpected.Count, paintedCount);
        }

        [Theory]
        [InlineData(1, 2, 14, 4)]   // Octant 1: Shallow positive
        [InlineData(14, 4, 1, 2)]   // Octant 8: Shallow negative
        [InlineData(14, 2, 1, 5)]   // Octant 4: Shallow negative x
        [InlineData(1, 5, 14, 2)]   // Octant 5: Shallow positive x, negative y
        [InlineData(0, 1, 15, 3)]   // High aspect ratio shallow (~7.5:1)
        [InlineData(15, 7, 0, 8)]   // Near horizontal shallow (dx=15, dy=1)
        public void Challenge1_ShallowAngles_AllIntermediateCellsPainted_NoCellsDropped(int x0, int y0, int x1, int y1)
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            sim.MouseDown(x0, y0);
            sim.MouseMove(x1, y1);
            sim.MouseUp();

            var expectedPoints = GenerateBresenhamPoints(x0, y0, x1, y1);
            var glyph = vm.ActiveGlyph!;

            for (int i = 1; i < expectedPoints.Count; i++)
            {
                int stepX = Math.Abs(expectedPoints[i].x - expectedPoints[i - 1].x);
                int stepY = Math.Abs(expectedPoints[i].y - expectedPoints[i - 1].y);
                Assert.True(stepX <= 1 && stepY <= 1, $"Chebyshev gap between step {i - 1} and {i}");
            }

            foreach (var (x, y) in expectedPoints)
            {
                Assert.True(glyph.Pixels[y * glyph.Width + x],
                    $"Cell ({x}, {y}) along shallow path ({x0},{y0})->({x1},{y1}) must be painted");
            }

            var uniqueExpected = new HashSet<(int, int)>(expectedPoints);
            int paintedCount = 0;
            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                if (glyph.Pixels[i]) paintedCount++;
            }
            Assert.Equal(uniqueExpected.Count, paintedCount);
        }

        [Theory]
        [InlineData(0, 0, 15, 15)] // NW to SE
        [InlineData(15, 15, 0, 0)] // SE to NW
        [InlineData(0, 15, 15, 0)] // SW to NE
        [InlineData(15, 0, 0, 15)] // NE to SW
        public void Challenge1_45DegreeDiagonals_CompleteFidelity(int x0, int y0, int x1, int y1)
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            sim.MouseDown(x0, y0);
            sim.MouseMove(x1, y1);
            sim.MouseUp();

            var glyph = vm.ActiveGlyph!;
            var expected = GenerateBresenhamPoints(x0, y0, x1, y1);
            Assert.Equal(16, expected.Count);

            foreach (var (x, y) in expected)
            {
                Assert.True(glyph.Pixels[y * glyph.Width + x], $"Diagonal cell ({x},{y}) should be painted");
            }

            int paintedCount = 0;
            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                if (glyph.Pixels[i]) paintedCount++;
            }
            Assert.Equal(16, paintedCount);
        }

        [Fact]
        public void Challenge1_HorizontalAndVerticalSweeps_FullCoverage()
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            // Left to right on row 3
            sim.MouseDown(0, 3);
            sim.MouseMove(15, 3);
            sim.MouseUp();

            // Right to left on row 8
            sim.MouseDown(15, 8);
            sim.MouseMove(0, 8);
            sim.MouseUp();

            // Top to bottom on col 4
            sim.MouseDown(4, 0);
            sim.MouseMove(4, 15);
            sim.MouseUp();

            // Bottom to top on col 11
            sim.MouseDown(11, 15);
            sim.MouseMove(11, 0);
            sim.MouseUp();

            var glyph = vm.ActiveGlyph!;

            // Verify row 3
            for (int x = 0; x < 16; x++) Assert.True(glyph.Pixels[3 * 16 + x], $"Row 3 col {x} missing");
            // Verify row 8
            for (int x = 0; x < 16; x++) Assert.True(glyph.Pixels[8 * 16 + x], $"Row 8 col {x} missing");
            // Verify col 4
            for (int y = 0; y < 16; y++) Assert.True(glyph.Pixels[y * 16 + 4], $"Col 4 row {y} missing");
            // Verify col 11
            for (int y = 0; y < 16; y++) Assert.True(glyph.Pixels[y * 16 + 11], $"Col 11 row {y} missing");
        }

        [Fact]
        public void Challenge1_RapidZigzagPaths_ContinuousUnbrokenStroke()
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            // Multi-segment rapid zigzag bouncing across canvas edges
            var waypoints = new (int x, int y)[]
            {
                (0, 0),
                (15, 3),
                (1, 7),
                (14, 11),
                (0, 15)
            };

            sim.MouseDown(waypoints[0].x, waypoints[0].y);
            for (int i = 1; i < waypoints.Length; i++)
            {
                sim.MouseMove(waypoints[i].x, waypoints[i].y);
            }
            sim.MouseUp();

            var glyph = vm.ActiveGlyph!;
            var allExpectedPoints = new List<(int x, int y)>();

            for (int seg = 0; seg < waypoints.Length - 1; seg++)
            {
                var segPoints = GenerateBresenhamPoints(waypoints[seg].x, waypoints[seg].y,
                                                       waypoints[seg + 1].x, waypoints[seg + 1].y);
                allExpectedPoints.AddRange(segPoints);
            }

            // Verify no intermediate cell was dropped along any segment
            foreach (var (x, y) in allExpectedPoints)
            {
                Assert.True(glyph.Pixels[y * glyph.Width + x],
                    $"Zigzag path pixel ({x},{y}) should be painted");
            }

            // Verify the path is 8-way contiguous from (0,0) to (0,15)
            for (int i = 1; i < allExpectedPoints.Count; i++)
            {
                int stepX = Math.Abs(allExpectedPoints[i].x - allExpectedPoints[i - 1].x);
                int stepY = Math.Abs(allExpectedPoints[i].y - allExpectedPoints[i - 1].y);
                Assert.True(stepX <= 1 && stepY <= 1, $"Gap between {allExpectedPoints[i - 1]} and {allExpectedPoints[i]}");
            }
        }

        [Fact]
        public void Challenge1_TightHighFrequencyZigzag_SawtoothPattern()
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var sim = new CanvasDragSimulator(vm);

            // Tight 1-pixel sawtooth wave: alternating up and down
            sim.MouseDown(0, 2);
            for (int x = 1; x < 16; x++)
            {
                int y = (x % 2 == 0) ? 2 : 5;
                sim.MouseMove(x, y);
            }
            sim.MouseUp();

            var glyph = vm.ActiveGlyph!;

            // Verify continuous coverage
            for (int x = 0; x < 15; x++)
            {
                int y0 = (x % 2 == 0) ? 2 : 5;
                int y1 = ((x + 1) % 2 == 0) ? 2 : 5;
                var pts = GenerateBresenhamPoints(x, y0, x + 1, y1);
                foreach (var (px, py) in pts)
                {
                    Assert.True(glyph.Pixels[py * glyph.Width + px], $"Sawtooth point ({px},{py}) missing");
                }
            }
        }

        [Fact]
        public void Challenge1_DragErasing_ErasesCompleteBresenhamPath()
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var glyph = vm.ActiveGlyph!;
            Array.Fill(glyph.Pixels, true); // Fill canvas with ink

            var sim = new CanvasDragSimulator(vm);

            // Erase a steep line
            sim.MouseDown(2, 1, erase: true);
            sim.MouseMove(5, 14);
            sim.MouseUp();

            var erasedPoints = GenerateBresenhamPoints(2, 1, 5, 14);
            foreach (var (x, y) in erasedPoints)
            {
                Assert.False(glyph.Pixels[y * glyph.Width + x], $"Cell ({x},{y}) should have been erased");
            }

            // Verify untouched pixels remain ink
            var erasedSet = new HashSet<(int, int)>(erasedPoints);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    if (!erasedSet.Contains((x, y)))
                    {
                        Assert.True(glyph.Pixels[y * 16 + x], $"Untouched cell ({x},{y}) must remain true");
                    }
                }
            }
        }

        [Fact]
        public void Challenge1_DragStroke_CreatesSingleDiscreteUndoFrame()
        {
            var vm = CreateTestFontViewModel(16, 16, 16);
            var glyph = vm.ActiveGlyph!;

            var sim = new CanvasDragSimulator(vm);

            // Multi-segment drag stroke painting dozens of pixels
            sim.MouseDown(0, 0);
            sim.MouseMove(15, 5);
            sim.MouseMove(2, 10);
            sim.MouseMove(14, 15);
            sim.MouseUp();

            Assert.True(vm.IsDirty);

            // A single Undo must revert the entire multi-segment stroke
            vm.Undo();
            var undoneGlyph = vm.ActiveGlyph!;

            for (int i = 0; i < undoneGlyph.Pixels.Length; i++)
            {
                Assert.False(undoneGlyph.Pixels[i], "All pixels must be restored to empty after single Undo");
            }

            // Redo restores the full multi-segment stroke
            vm.Redo();
            var redoneGlyph = vm.ActiveGlyph!;

            var pts1 = GenerateBresenhamPoints(0, 0, 15, 5);
            var pts2 = GenerateBresenhamPoints(15, 5, 2, 10);
            var pts3 = GenerateBresenhamPoints(2, 10, 14, 15);
            var allPts = new HashSet<(int, int)>(pts1);
            allPts.UnionWith(pts2);
            allPts.UnionWith(pts3);

            foreach (var (x, y) in allPts)
            {
                Assert.True(redoneGlyph.Pixels[y * redoneGlyph.Width + x], $"Redo must restore pixel ({x},{y})");
            }
        }

        [Fact]
        public void Challenge1_RandomizedAngleOracleStress_500Strokes()
        {
            var rng = new Random(42);

            for (int iter = 0; iter < 500; iter++)
            {
                int w = rng.Next(6, 25);
                int h = rng.Next(6, 25);
                var vm = CreateTestFontViewModel(w, h, w);
                var glyph = vm.ActiveGlyph!;

                int x0 = rng.Next(0, w);
                int y0 = rng.Next(0, h);
                int x1 = rng.Next(0, w);
                int y1 = rng.Next(0, h);

                var sim = new CanvasDragSimulator(vm);
                sim.MouseDown(x0, y0);
                sim.MouseMove(x1, y1);
                sim.MouseUp();

                var expected = GenerateBresenhamPoints(x0, y0, x1, y1);

                // Verify 8-connectivity
                for (int i = 1; i < expected.Count; i++)
                {
                    int stepX = Math.Abs(expected[i].x - expected[i - 1].x);
                    int stepY = Math.Abs(expected[i].y - expected[i - 1].y);
                    Assert.True(stepX <= 1 && stepY <= 1, $"Chebyshev gap on iter {iter}");
                }

                // Verify painted
                foreach (var (px, py) in expected)
                {
                    Assert.True(glyph.Pixels[py * glyph.Width + px], $"Missing pixel ({px},{py}) on iter {iter}");
                }
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        // PART 2: ADVANCE MARGIN SUPPRESSION CHALLENGES
        // ═════════════════════════════════════════════════════════════════════

        [Fact]
        public void Challenge2_AdvanceMargin_SingleClicksAcrossEntireMargin_ZeroGlyphMutations()
        {
            // Glyph Width = 8, Height = 12, XAdvance = 14.
            // Advance margin columns are 8, 9, 10, 11, 12, 13.
            var vm = CreateTestFontViewModel(width: 8, height: 12, xAdvance: 14);
            var glyph = vm.ActiveGlyph!;
            Array.Fill(glyph.Pixels, false);

            var sim = new CanvasDragSimulator(vm);

            // Probe every coordinate in the advance margin
            for (int marginX = glyph.Width; marginX < glyph.XAdvance; marginX++)
            {
                for (int y = 0; y < glyph.Height; y++)
                {
                    sim.MouseDown(marginX, y);
                    sim.MouseUp();

                    // Specifically check right border (Width - 1 = 7)
                    Assert.False(glyph.Pixels[y * glyph.Width + (glyph.Width - 1)],
                        $"Right border pixel at row {y} was corrupted by click at advance margin ({marginX}, {y})");
                }
            }

            // Verify entire glyph remains 100% untouched
            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                Assert.False(glyph.Pixels[i], "Advance margin clicks must not alter any glyph pixels");
            }

            // IsDirty should remain false because no pixels ever mutated
            Assert.False(vm.IsDirty, "IsDirty must remain false when clicks land in advance margin");
        }

        [Fact]
        public void Challenge2_AdvanceMargin_HorizontalAndVerticalDragsWithinMargin_ZeroMutations()
        {
            var vm = CreateTestFontViewModel(width: 8, height: 16, xAdvance: 15);
            var glyph = vm.ActiveGlyph!;
            var sim = new CanvasDragSimulator(vm);

            // Horizontal drag within margin (from x=9 to x=14 on row 5)
            sim.MouseDown(9, 5);
            for (int x = 10; x <= 14; x++)
            {
                sim.MouseMove(x, 5);
            }
            sim.MouseUp();

            // Vertical sweep within margin (from y=0 to y=15 on col 11)
            sim.MouseDown(11, 0);
            for (int y = 1; y < 16; y++)
            {
                sim.MouseMove(11, y);
            }
            sim.MouseUp();

            // Diagonal sweep within margin (x=8,y=0 to x=14,y=15)
            sim.MouseDown(8, 0);
            sim.MouseMove(14, 15);
            sim.MouseUp();

            // Verify zero pixels modified in glyph or on border (col 7)
            for (int y = 0; y < glyph.Height; y++)
            {
                Assert.False(glyph.Pixels[y * glyph.Width + 7], $"Border col 7 row {y} corrupted by margin drag");
            }

            for (int i = 0; i < glyph.Pixels.Length; i++)
            {
                Assert.False(glyph.Pixels[i], "Entire glyph must remain blank");
            }
        }

        [Fact]
        public void Challenge2_AdvanceMargin_BoundaryTransition_InsideToMargin_StopsAtBorder()
        {
            var vm = CreateTestFontViewModel(width: 8, height: 16, xAdvance: 14);
            var glyph = vm.ActiveGlyph!;
            var sim = new CanvasDragSimulator(vm);

            // Drag starting inside glyph at (2, 4) and moving out into margin at (12, 4)
            sim.MouseDown(2, 4);
            // Intermediate steps crossing the border
            sim.MouseMove(5, 4);
            sim.MouseMove(7, 4);  // exactly on border (Width - 1)
            sim.MouseMove(10, 4); // into margin
            sim.MouseMove(13, 4); // deep in margin
            sim.MouseUp();

            // Verify inside pixels [2..7] at row 4 are painted
            for (int x = 2; x <= 7; x++)
            {
                Assert.True(glyph.Pixels[4 * glyph.Width + x], $"Pixel ({x}, 4) inside glyph should be painted");
            }

            // Verify other rows and advance area are untouched
            for (int y = 0; y < 16; y++)
            {
                if (y != 4)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        Assert.False(glyph.Pixels[y * glyph.Width + x], $"Pixel ({x}, {y}) should not be painted");
                    }
                }
            }
        }

        [Fact]
        public void Challenge2_AdvanceMargin_BoundaryTransition_MarginToInside_StartsOnlyUponEntry()
        {
            var vm = CreateTestFontViewModel(width: 8, height: 16, xAdvance: 14);
            var glyph = vm.ActiveGlyph!;
            var sim = new CanvasDragSimulator(vm);

            // Drag starting in advance margin (12, 6) and entering glyph towards (1, 6)
            sim.MouseDown(12, 6);
            sim.MouseMove(10, 6); // still in margin
            sim.MouseMove(8, 6);  // still in margin (Width = 8)
            sim.MouseMove(7, 6);  // enters glyph at border
            sim.MouseMove(3, 6);  // deep inside glyph
            sim.MouseUp();

            // Verify only pixels inside glyph [3..7] at row 6 are painted
            for (int x = 3; x <= 7; x++)
            {
                Assert.True(glyph.Pixels[6 * glyph.Width + x], $"Pixel ({x}, 6) inside glyph should be painted");
            }

            // Verify pixels 0..2 are NOT painted
            for (int x = 0; x < 3; x++)
            {
                Assert.False(glyph.Pixels[6 * glyph.Width + x], $"Pixel ({x}, 6) should remain unpainted");
            }
        }

        [Fact]
        public void Challenge2_AdvanceMargin_DragOutAndBackIn_DoesNotBridgeAcrossMargin()
        {
            var vm = CreateTestFontViewModel(width: 8, height: 16, xAdvance: 14);
            var glyph = vm.ActiveGlyph!;
            var sim = new CanvasDragSimulator(vm);

            // Start inside at (2, 2)
            sim.MouseDown(2, 2);
            // Drag out into advance margin at (12, 2)
            sim.MouseMove(12, 2);
            // Move inside advance margin to (12, 8)
            sim.MouseMove(12, 8);
            // Move back inside glyph at (2, 8)
            sim.MouseMove(2, 8);
            // Move inside glyph to (6, 8)
            sim.MouseMove(6, 8);
            sim.MouseUp();

            // In FontEditorPanel, when coordinates leave the glyph bounds, _lastCellX and _lastCellY
            // are reset to -1. Therefore, re-entering at (2, 8) does NOT draw a spurious diagonal
            // from (12, 2) or bridge across the advance margin!
            Assert.True(glyph.Pixels[2 * glyph.Width + 2], "Start point (2,2) painted");
            Assert.True(glyph.Pixels[8 * glyph.Width + 2], "Re-entry point (2,8) painted");

            // Verify border column 7 at rows between 2 and 8 has NO spurious pixels
            for (int y = 3; y < 8; y++)
            {
                Assert.False(glyph.Pixels[y * glyph.Width + 7],
                    $"Border pixel at row {y} should not be bridged across margin");
            }
        }

        [Theory]
        [InlineData(1, 16)]  // Extreme narrow glyph (Width=1, XAdvance=16)
        [InlineData(7, 8)]   // 1-pixel advance margin
        [InlineData(10, 10)] // Zero advance margin (XAdvance == Width)
        [InlineData(12, 8)]  // Proportional negative advance margin (XAdvance < Width)
        public void Challenge2_AdvanceMargin_VariousWidthAndAdvanceCombinations(int width, int xAdvance)
        {
            var vm = CreateTestFontViewModel(width: width, height: 10, xAdvance: xAdvance);
            var glyph = vm.ActiveGlyph!;
            var sim = new CanvasDragSimulator(vm);

            if (xAdvance > width)
            {
                // Clicks in margin [width..xAdvance - 1]
                for (int x = width; x < xAdvance; x++)
                {
                    sim.MouseDown(x, 3);
                    sim.MouseUp();
                }

                // Check right border
                Assert.False(glyph.Pixels[3 * width + (width - 1)], "Border pixel must not be set");
            }
            else
            {
                // When XAdvance <= Width, clicks at x = width - 1 are inside
                sim.MouseDown(width - 1, 3);
                sim.MouseUp();
                Assert.True(glyph.Pixels[3 * width + (width - 1)], "In-bounds pixel must be set");
            }
        }

        [Fact]
        public void Challenge2_FontEditorPanel_StateResetOnMouseUpAndLeave()
        {
            // Verify via reflection that FontEditorPanel resets its tracking variables
            WpfTestHelper.RunOnSta(() =>
            {
                var panel = new FontEditorPanel();
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                var fieldLastX = typeof(FontEditorPanel).GetField("_lastCellX", flags);
                var fieldLastY = typeof(FontEditorPanel).GetField("_lastCellY", flags);
                var fieldDrawing = typeof(FontEditorPanel).GetField("_isDrawing", flags);
                var fieldErasing = typeof(FontEditorPanel).GetField("_isErasing", flags);

                Assert.NotNull(fieldLastX);
                Assert.NotNull(fieldLastY);
                Assert.NotNull(fieldDrawing);
                Assert.NotNull(fieldErasing);

                // Initially -1
                Assert.Equal(-1, (int)fieldLastX.GetValue(panel)!);
                Assert.Equal(-1, (int)fieldLastY.GetValue(panel)!);
                Assert.False((bool)fieldDrawing.GetValue(panel)!);
                Assert.False((bool)fieldErasing.GetValue(panel)!);

                // Set state as if drawing
                fieldLastX.SetValue(panel, 5);
                fieldLastY.SetValue(panel, 5);
                fieldDrawing.SetValue(panel, true);

                // Call Canvas_MouseLeave via reflection
                var methodLeave = typeof(FontEditorPanel).GetMethod("Canvas_MouseLeave", flags);
                Assert.NotNull(methodLeave);
                methodLeave.Invoke(panel, new object?[] { null, null });

                // _lastCellX and _lastCellY must be reset to -1
                Assert.Equal(-1, (int)fieldLastX.GetValue(panel)!);
                Assert.Equal(-1, (int)fieldLastY.GetValue(panel)!);

                // Set state again and test LostMouseCapture
                fieldLastX.SetValue(panel, 8);
                fieldLastY.SetValue(panel, 8);
                fieldDrawing.SetValue(panel, true);

                var methodLostCapture = typeof(FontEditorPanel).GetMethod("Canvas_LostMouseCapture", flags);
                Assert.NotNull(methodLostCapture);
                methodLostCapture.Invoke(panel, new object?[] { null, null });

                Assert.Equal(-1, (int)fieldLastX.GetValue(panel)!);
                Assert.Equal(-1, (int)fieldLastY.GetValue(panel)!);
                Assert.False((bool)fieldDrawing.GetValue(panel)!);
            });
        }
    }
}
