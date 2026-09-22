using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Hexprite.Core;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontEditorM1ChallengerTests
    {
        public FontEditorM1ChallengerTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        // ── Challenge 1: 2D Stride-Preserving Multi-Cycle Resizing ───────────

        [Fact]
        public void Challenge_2DStride_ComplexPatterns_MultiCycleResizing_PreservesRowPitch()
        {
            // Create initial document 8x8
            var doc = FontDocument.CreateNew(8, 8, 65, 65);
            var glyph = doc.Glyphs[0];

            // 1. Stamp complex 2D raster patterns
            // Diagonal (0,0) to (7,7)
            for (int i = 0; i < 8; i++) glyph.Pixels[i * 8 + i] = true;
            // Anti-diagonal (0,7) to (7,0)
            for (int i = 0; i < 8; i++) glyph.Pixels[i * 8 + (7 - i)] = true;
            // Corners
            glyph.Pixels[0 * 8 + 0] = true;
            glyph.Pixels[0 * 8 + 7] = true;
            glyph.Pixels[7 * 8 + 0] = true;
            glyph.Pixels[7 * 8 + 7] = true;
            // Checkerboard interior (2,2) to (5,5)
            for (int y = 2; y <= 5; y++)
            {
                for (int x = 2; x <= 5; x++)
                {
                    if ((x + y) % 2 == 0) glyph.Pixels[y * 8 + x] = true;
                }
            }

            // Snapshot expected ground truth in 2D array
            int currentW = 8;
            int currentH = 8;
            bool[,] groundTruth = new bool[currentW, currentH];
            for (int y = 0; y < currentH; y++)
            {
                for (int x = 0; x < currentW; x++)
                {
                    groundTruth[x, y] = glyph.Pixels[y * currentW + x];
                }
            }

            // Multi-cycle expansion and contraction sequence
            var dimensionCycles = new (int TargetW, int TargetH)[]
            {
                (12, 16), // Cycle 1: Dual expansion
                (6, 10),  // Cycle 2: Dual contraction
                (14, 5),  // Cycle 3: Asymmetric (width expand, height contract)
                (4, 18),  // Cycle 4: Asymmetric (width contract, height expand)
                (3, 3),   // Cycle 5: Severe contraction
                (20, 20), // Cycle 6: Large expansion
                (1, 1),   // Cycle 7: Minimal 1x1 boundary
                (9, 11),  // Cycle 8: Mixed recovery
            };

            foreach (var (nextW, nextH) in dimensionCycles)
            {
                // Prepare new ground truth before resizing
                bool[,] nextTruth = new bool[nextW, nextH];
                int copyW = Math.Min(currentW, nextW);
                int copyH = Math.Min(currentH, nextH);

                for (int y = 0; y < copyH; y++)
                {
                    for (int x = 0; x < copyW; x++)
                    {
                        nextTruth[x, y] = groundTruth[x, y];
                    }
                }

                // Apply resizing to FontDocument
                glyph.Width = nextW;
                doc.CellHeight = nextH;
                doc.NormalizeGlyphs();

                Assert.Equal(nextW, glyph.Width);
                Assert.Equal(nextH, glyph.Height);
                Assert.Equal(nextW * nextH, glyph.Pixels.Length);

                // Mathematically verify row pitch invariance
                for (int y = 0; y < nextH; y++)
                {
                    int rowStart = y * nextW;
                    for (int x = 0; x < nextW; x++)
                    {
                        int pixelIndex = rowStart + x;
                        bool actual = glyph.Pixels[pixelIndex];
                        bool expected = nextTruth[x, y];

                        // Ensure pixel at (x, y) precisely matches ground truth
                        Assert.True(actual == expected,
                            $"Pixel at ({x}, {y}) mismatch during transition to {nextW}x{nextH}. " +
                            $"Expected: {expected}, Actual: {actual}, Index: {pixelIndex}, RowStart: {rowStart}");

                        // Mathematical row isolation:
                        // Ensure that no pixel from row y has leaked into row y - 1 or row y + 1
                        if (x < copyW && y < copyH)
                        {
                            bool originalPixel = groundTruth[x, y];
                            if (originalPixel)
                            {
                                // Confirm this pixel is at row y and NOT row y-1
                                if (y > 0)
                                {
                                    int aboveRowIndex = (y - 1) * nextW + x;
                                    // If above pixel was not originally inked, it must not be inked now
                                    if (!nextTruth[x, y - 1])
                                    {
                                        Assert.False(glyph.Pixels[aboveRowIndex],
                                            $"Row pitch leak: Pixel ({x}, {y}) leaked upward into row {y - 1}!");
                                    }
                                }
                            }
                        }
                    }
                }

                // Update current state for next iteration
                groundTruth = nextTruth;
                currentW = nextW;
                currentH = nextH;
            }
        }

        [Fact]
        public void Challenge_2DStride_ViewModel_InternalResize_PreservesPixelPitches()
        {
            var vm = new FontViewModel();
            vm.Document = FontDocument.CreateNew(8, 8, 65, 65);
            var glyph = vm.ActiveGlyph!;

            // Draw diagonal line across 8x8
            for (int i = 0; i < 8; i++)
            {
                glyph.Pixels[i * 8 + i] = true;
            }

            // Resize width via ViewModel
            vm.ResizeActiveGlyph(14);
            Assert.Equal(14, glyph.Width);
            Assert.Equal(8, glyph.Height);

            // Verify diagonal is still along y = x for x, y < 8
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 14; x++)
                {
                    bool expected = (x < 8 && x == y);
                    bool actual = glyph.Pixels[y * 14 + x];
                    Assert.Equal(expected, actual);
                }
            }

            // Shrink width down to 4
            vm.ResizeActiveGlyph(4);
            Assert.Equal(4, glyph.Width);
            Assert.Equal(8, glyph.Height);

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool expected = (x == y);
                    bool actual = glyph.Pixels[y * 4 + x];
                    Assert.Equal(expected, actual);
                }
            }

            // Change cell height to 12
            vm.CellHeight = 12;
            Assert.Equal(4, glyph.Width);
            Assert.Equal(12, glyph.Height);

            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bool expected = (y < 4 && x == y);
                    bool actual = glyph.Pixels[y * 4 + x];
                    Assert.Equal(expected, actual);
                }
            }
        }

        // ── Challenge 2: Revision-Based Dirty Tracking on Linear & Divergent Stacks ──

        [Fact]
        public void Challenge_DirtyTracking_LinearSequence_BitIdenticalEquality()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            string initialJson = JsonSerializer.Serialize(vm.Document);

            // Perform sequence of distinct mutations
            vm.Baseline = 7;
            Assert.True(vm.IsDirty);

            vm.YAdvance = 11;
            Assert.True(vm.IsDirty);

            vm.BeginDrawing();
            vm.DrawPixel(2, 2);
            vm.EndDrawing();
            Assert.True(vm.IsDirty);

            // Undo all 3 steps
            vm.Undo(); // Undo DrawPixel
            Assert.True(vm.IsDirty);

            vm.Undo(); // Undo YAdvance
            Assert.True(vm.IsDirty);

            vm.Undo(); // Undo Baseline
            Assert.False(vm.IsDirty);

            // Verify bit-identical serialization match with initial clean state
            string revertedJson = JsonSerializer.Serialize(vm.Document);
            Assert.Equal(initialJson, revertedJson);

            // Redo all 3 steps
            vm.Redo();
            Assert.True(vm.IsDirty);
            vm.Redo();
            Assert.True(vm.IsDirty);
            vm.Redo();
            Assert.True(vm.IsDirty);

            // Mark as clean at the top of the stack
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);
            string savedTopJson = JsonSerializer.Serialize(vm.Document);

            // Undo 2 steps -> must be dirty
            vm.Undo();
            vm.Undo();
            Assert.True(vm.IsDirty);

            // Redo back to top -> must be clean and bit-identical
            vm.Redo();
            vm.Redo();
            Assert.False(vm.IsDirty);
            Assert.Equal(savedTopJson, JsonSerializer.Serialize(vm.Document));
        }

        [Fact]
        public void Challenge_DirtyTracking_DivergentBranch_UndoRestoresCollisionAsFalse()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();

            // 1. Initial edit (rev 1)
            vm.Baseline = 7;
            // 2. Second edit (rev 2)
            vm.YAdvance = 10;

            // 3. Save at rev 2!
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);
            string savedDiskJson = JsonSerializer.Serialize(vm.Document);

            // 4. Undo back to rev 1
            vm.Undo();
            Assert.True(vm.IsDirty);

            // 5. Divergent edit A (pushes rev 1, documentRevision becomes 2)
            vm.FontName = "DivergentNameA";
            Assert.True(vm.IsDirty);

            // 6. Divergent edit B (pushes rev 2, documentRevision becomes 3)
            vm.FontName = "DivergentNameB";
            Assert.True(vm.IsDirty);

            // 7. Undo divergent edit B -> restores state at divergent edit A (rev 2)
            vm.Undo();

            // Memory has: Baseline=7, YAdvance=8 (default), FontName="DivergentNameA"
            // Disk has:   Baseline=7, YAdvance=10,            FontName="myFont"
            string currentMemoryJson = JsonSerializer.Serialize(vm.Document);
            Assert.NotEqual(savedDiskJson, currentMemoryJson);

            // CHALLENGE ASSERTION:
            // Since currentMemory is NOT bit-identical to savedDiskJson, IsDirty MUST BE TRUE!
            Assert.True(vm.IsDirty,
                "CRITICAL REVISION COLLISION: Undoing on a divergent branch restored rev=2, colliding with savedRevision=2 and falsely clearing IsDirty to false!");
        }

        [Fact]
        public void Challenge_DirtyTracking_DivergentBranch_RedoRestoresCollisionAsFalse()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();

            // 1. Initial edit (rev 1)
            vm.Baseline = 7;
            // 2. Second edit (rev 2)
            vm.YAdvance = 10;

            // 3. Save at rev 2!
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);
            string savedDiskJson = JsonSerializer.Serialize(vm.Document);

            // 4. Undo back to rev 1
            vm.Undo();
            Assert.True(vm.IsDirty);

            // 5. Divergent edit A (pushes rev 1, documentRevision becomes 2)
            vm.FontName = "DivergentNameA";

            // 6. Divergent edit B (pushes rev 2, documentRevision becomes 3)
            vm.FontName = "DivergentNameB";

            // 7. Undo 2 times: back to rev 2 (edit A), then back to rev 1
            vm.Undo();
            vm.Undo(); // back to rev 1

            // 8. Now REDO 1 step forward on the divergent branch -> restores rev 2 (edit A)
            vm.Redo();

            string currentMemoryJson = JsonSerializer.Serialize(vm.Document);
            Assert.NotEqual(savedDiskJson, currentMemoryJson);

            // CHALLENGE ASSERTION:
            // Redoing to rev 2 on the divergent branch collides with savedRevision=2!
            // vm.IsDirty MUST be TRUE because the document is on the divergent branch!
            Assert.True(vm.IsDirty,
                "CRITICAL REVISION COLLISION: Redoing on a divergent branch restored rev=2, colliding with savedRevision=2 and falsely clearing IsDirty to false!");
        }
    }
}
