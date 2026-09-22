using System;
using System.Linq;
using Hexprite.Core;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontViewModelExtendedTests
    {
        public FontViewModelExtendedTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        // ── Tier 1: Feature Coverage ─────────────────────────────────────────

        [Fact]
        public void InitialState_DefaultPropertiesAndDocumentIntegrity()
        {
            var vm = new FontViewModel();

            Assert.NotNull(vm.Document);
            Assert.Equal("myFont", vm.FontName);
            Assert.Equal(8, vm.CellHeight);
            Assert.Equal(8, vm.MaxCellWidth);
            Assert.Equal(6, vm.Baseline);
            Assert.Equal(8, vm.YAdvance);
            Assert.False(vm.IsMonospaced);
            Assert.True(vm.ShowGridLines);
            Assert.True(vm.AutoAdvance);
            Assert.Equal("Pencil/Eraser", vm.CurrentTool);
            Assert.Equal("8×8", vm.CanvasDimensionText);
            Assert.Equal(16.0, vm.ZoomLevel);
            Assert.True(vm.IsNewlyCreated);
            Assert.False(vm.IsDirty);
            Assert.Equal(95, vm.Document.Glyphs.Count);

            // Rebuilding glyph map on Document assignment
            vm.Document = FontDocument.CreateNew(8, 8);
            Assert.Equal(95, vm.GlyphMap.Count);
        }

        [Fact]
        public void Navigation_NextPreviousAndJumpToChar()
        {
            var vm = new FontViewModel();
            vm.Document = FontDocument.CreateNew(8, 8);
            int totalGlyphs = vm.GlyphMap.Count;

            // Start at 0 (' ')
            vm.SetActiveGlyph(0);
            Assert.Equal(0, vm.Document.ActiveGlyphIndex);
            Assert.Equal(32, vm.ActiveGlyphCodePoint);

            // NextGlyph moves forward
            vm.NextGlyph();
            Assert.Equal(1, vm.Document.ActiveGlyphIndex);
            Assert.Equal(33, vm.ActiveGlyphCodePoint);

            // PreviousGlyph moves backward
            vm.PreviousGlyph();
            Assert.Equal(0, vm.Document.ActiveGlyphIndex);

            // Clamp on previous at 0
            vm.PreviousGlyph();
            Assert.Equal(0, vm.Document.ActiveGlyphIndex);

            // Jump to character 'A' (code 65)
            vm.JumpToChar('A');
            Assert.Equal('A', vm.ActiveGlyphChar);
            Assert.Equal(65, vm.ActiveGlyphCodePoint);
            Assert.Equal(65 - 32, vm.Document.ActiveGlyphIndex);

            // Next at end clamps
            vm.SetActiveGlyph(totalGlyphs - 1);
            vm.NextGlyph();
            Assert.Equal(totalGlyphs - 1, vm.Document.ActiveGlyphIndex);
        }

        [Fact]
        public void CellDimensions_ResizeAllGlyphsHeight()
        {
            var vm = new FontViewModel();
            var doc = vm.Document;
            var activeGlyph = doc.ActiveGlyph!;
            activeGlyph.Pixels[0] = true; // Top-left pixel

            // Change cell height from 8 to 16
            vm.CellHeight = 16;

            Assert.Equal(16, doc.CellHeight);
            Assert.Equal(16, activeGlyph.Height);
            Assert.Equal(activeGlyph.Width * 16, activeGlyph.Pixels.Length);
            Assert.True(activeGlyph.Pixels[0], "Existing pixel (0,0) should be preserved");

            // All glyphs must now have height 16
            foreach (var g in doc.Glyphs)
            {
                Assert.Equal(16, g.Height);
                Assert.Equal(g.Width * 16, g.Pixels.Length);
            }
        }

        [Fact]
        public void GlyphMetrics_UpdatingProperties_SetsDirtyFlag()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Modify Baseline
            vm.Baseline = 7;
            Assert.True(vm.IsDirty);
            Assert.Equal(7, vm.Baseline);

            vm.MarkAsClean();
            // Modify YAdvance
            vm.YAdvance = 12;
            Assert.True(vm.IsDirty);
            Assert.Equal(12, vm.YAdvance);

            vm.MarkAsClean();
            // Modify XAdvance
            vm.XAdvance = 10;
            Assert.True(vm.IsDirty);
            Assert.Equal(10, vm.ActiveGlyph.XAdvance);

            vm.MarkAsClean();
            // Modify XOffset and YOffset
            vm.XOffset = 2;
            Assert.True(vm.IsDirty);
            Assert.Equal(2, vm.ActiveGlyph.XOffset);

            vm.MarkAsClean();
            vm.YOffset = -3;
            Assert.True(vm.IsDirty);
            Assert.Equal(-3, vm.ActiveGlyph.YOffset);
        }

        [Fact]
        public void ZoomAndDisplay_CellSizeClamping()
        {
            var vm = new FontViewModel();

            // Set within range
            vm.CellSize = 32;
            Assert.Equal(32, vm.CellSize);
            Assert.Equal(32.0, vm.ZoomLevel);

            // Clamp below minimum 4
            vm.CellSize = 1;
            Assert.Equal(4, vm.CellSize);

            // Clamp above maximum 128
            vm.CellSize = 256;
            Assert.Equal(128, vm.CellSize);
        }

        // ── Tier 2: Boundary & Corner Cases ──────────────────────────────────

        [Fact]
        public void ShiftActiveGlyph_EdgePixelsBlockShift()
        {
            var vm = new FontViewModel();
            var glyph = vm.ActiveGlyph!;
            int w = glyph.Width;
            int h = glyph.Height;

            // Place pixel on left edge (x=0)
            glyph.Pixels[1 * w + 0] = true;
            vm.ShiftActiveGlyph(-1, 0); // Attempt shift left
            Assert.True(glyph.Pixels[1 * w + 0], "Pixel on left edge should block left shift");

            // Clear and place pixel on right edge (x=w-1)
            Array.Clear(glyph.Pixels);
            glyph.Pixels[1 * w + (w - 1)] = true;
            vm.ShiftActiveGlyph(1, 0); // Attempt shift right
            Assert.True(glyph.Pixels[1 * w + (w - 1)], "Pixel on right edge should block right shift");

            // Clear and place pixel on top row (y=0)
            Array.Clear(glyph.Pixels);
            glyph.Pixels[0 * w + 2] = true;
            vm.ShiftActiveGlyph(0, -1); // Attempt shift up
            Assert.True(glyph.Pixels[0 * w + 2], "Pixel on top edge should block up shift");

            // Clear and place pixel on bottom row (y=h-1)
            Array.Clear(glyph.Pixels);
            glyph.Pixels[(h - 1) * w + 2] = true;
            vm.ShiftActiveGlyph(0, 1); // Attempt shift down
            Assert.True(glyph.Pixels[(h - 1) * w + 2], "Pixel on bottom edge should block down shift");

            // Center pixel: shifting (1, 1) should succeed
            Array.Clear(glyph.Pixels);
            glyph.Pixels[2 * w + 2] = true;
            vm.ShiftActiveGlyph(1, 1);
            Assert.False(glyph.Pixels[2 * w + 2]);
            Assert.True(glyph.Pixels[3 * w + 3], "Pixel should have shifted to (3,3)");
        }

        [Fact]
        public void ResizeActiveGlyph_WidthExpansionAndShrink_AutoAdvanceBehavior()
        {
            var vm = new FontViewModel();
            var glyph = vm.ActiveGlyph!;
            glyph.Pixels[0] = true; // (0,0)
            glyph.Pixels[7] = true; // (7,0)

            // Shrink from 8 to 5: pixel at (7,0) will be truncated
            vm.AutoAdvance = true;
            vm.ResizeActiveGlyph(5);

            Assert.Equal(5, glyph.Width);
            Assert.Equal(5 * glyph.Height, glyph.Pixels.Length);
            Assert.True(glyph.Pixels[0], "Pixel at (0,0) preserved");
            Assert.Equal(6, glyph.XAdvance); // AutoAdvance sets newWidth + 1

            // Expand from 5 to 10 with AutoAdvance = false
            vm.AutoAdvance = false;
            vm.ResizeActiveGlyph(10);

            Assert.Equal(10, glyph.Width);
            Assert.True(glyph.Pixels[0]);
            Assert.Equal(6, glyph.XAdvance); // XAdvance preserved because AutoAdvance was false
        }

        [Fact]
        public void DrawPixel_OutOfBoundsAndOutsideStroke_GracefullyIgnored()
        {
            var vm = new FontViewModel();
            var glyph = vm.ActiveGlyph!;

            // Drawing without BeginDrawing does nothing
            vm.DrawPixel(0, 0);
            Assert.False(glyph.Pixels[0]);

            // Start drawing stroke
            vm.BeginDrawing();

            // Out-of-bounds draws should not throw
            vm.DrawPixel(-1, 0);
            vm.DrawPixel(0, -1);
            vm.DrawPixel(100, 100);

            // Valid draw
            vm.DrawPixel(2, 2);
            Assert.True(glyph.Pixels[2 * glyph.Width + 2]);

            // Erase valid draw
            vm.DrawPixel(2, 2, erase: true);
            Assert.False(glyph.Pixels[2 * glyph.Width + 2]);

            vm.EndDrawing();
        }

        [Fact]
        public void CharacterRange_DynamicRangeChange_NormalizesAndPreservesData()
        {
            var vm = new FontViewModel();
            // Start with small range: 'A'..'D' (65..68)
            vm.FirstChar = 65;
            vm.LastChar = 68;

            Assert.Equal(4, vm.GlyphMap.Count);
            Assert.Equal('A', vm.GlyphMap[0].Character);
            Assert.Equal('D', vm.GlyphMap[3].Character);

            // Draw ink on 'B' (index 1)
            vm.SetActiveGlyph(1);
            vm.ActiveGlyph!.Pixels[0] = true;

            // Expand range to 60..70
            vm.FirstChar = 60;
            vm.LastChar = 70;

            Assert.Equal(11, vm.GlyphMap.Count);
            // 'B' (66) should be at index 66 - 60 = 6
            Assert.Equal('B', vm.GlyphMap[6].Character);
            Assert.True(vm.Document.Glyphs[6].Pixels[0], "Preserved glyph data on range change");
        }

        [Fact]
        public void SelectedGlyphItem_NullOrExternalSelection_HandledGracefully()
        {
            var vm = new FontViewModel();
            vm.Document = FontDocument.CreateNew(8, 8);

            // Setting to null
            vm.SelectedGlyphItem = null;
            Assert.Null(vm.SelectedGlyphItem);

            // Selecting a valid item
            var item = vm.GlyphMap[5];
            vm.SelectedGlyphItem = item;
            Assert.Equal(item, vm.SelectedGlyphItem);
            Assert.Equal(5, vm.Document.ActiveGlyphIndex);
        }

        // ── Tier 3: Cross-Feature Interactions ───────────────────────────────

        [Fact]
        public void DrawingStroke_UndoRedo_AtomicMultiPixelReversion()
        {
            var vm = new FontViewModel();
            var glyph = vm.ActiveGlyph!;
            int w = glyph.Width;

            // Perform a stroke with 4 pixels
            vm.BeginDrawing();
            vm.DrawPixel(0, 0);
            vm.DrawPixel(1, 1);
            vm.DrawPixel(2, 2);
            vm.DrawPixel(3, 3);
            vm.EndDrawing();

            Assert.True(glyph.Pixels[0 * w + 0]);
            Assert.True(glyph.Pixels[1 * w + 1]);
            Assert.True(glyph.Pixels[2 * w + 2]);
            Assert.True(glyph.Pixels[3 * w + 3]);

            // Single undo should revert the entire stroke on the active document
            vm.Undo();
            var restored = vm.ActiveGlyph!;

            Assert.False(restored.Pixels[0 * w + 0]);
            Assert.False(restored.Pixels[1 * w + 1]);
            Assert.False(restored.Pixels[2 * w + 2]);
            Assert.False(restored.Pixels[3 * w + 3]);

            // Redo should restore the entire stroke on the active document
            vm.Redo();
            var redone = vm.ActiveGlyph!;

            Assert.True(redone.Pixels[0 * w + 0]);
            Assert.True(redone.Pixels[1 * w + 1]);
            Assert.True(redone.Pixels[2 * w + 2]);
            Assert.True(redone.Pixels[3 * w + 3]);
        }

        [Fact]
        public void MouseScrubbing_UndoRedo_BatchesContinuousDragIntoSingleSnapshot()
        {
            var vm = new FontViewModel();
            int initialBaseline = vm.Baseline; // 6

            // Simulate mouse drag-scrub on Baseline slider
            vm.ScrubStartedCommand.Execute(null);
            Assert.True(vm.IsScrubbing);

            vm.Baseline = 7;
            vm.Baseline = 8;
            vm.Baseline = 9;

            vm.ScrubEndedCommand.Execute(null);
            Assert.False(vm.IsScrubbing);
            Assert.Equal(9, vm.Baseline);

            // Undo should revert all the way back to initialBaseline in 1 step!
            vm.Undo();
            Assert.Equal(initialBaseline, vm.Baseline);

            // Redo brings it back to 9
            vm.Redo();
            Assert.Equal(9, vm.Baseline);
        }

        [Fact]
        public void KeyboardScrubbing_UndoRedo_BatchesArrowKeyAdjustments()
        {
            var vm = new FontViewModel();
            int initialAdvance = vm.XAdvance;

            // Simulate keyboard arrow scrubbing
            vm.KeyboardScrubStartedCommand.Execute(null);

            vm.XAdvance = initialAdvance + 1;
            vm.XAdvance = initialAdvance + 2;
            vm.XAdvance = initialAdvance + 3;

            vm.KeyboardScrubEndedCommand.Execute(null);
            Assert.Equal(initialAdvance + 3, vm.XAdvance);

            // Single undo reverts the entire arrow scrubbing sequence
            vm.Undo();
            Assert.Equal(initialAdvance, vm.XAdvance);
        }

        [Fact]
        public void MonospacedConstraint_SynchronizesAllGlyphs_AndRestoresPreMonoWidths()
        {
            var vm = new FontViewModel();
            var doc = vm.Document;

            // Set up proportional widths: glyph 0 (' ') has width 4, glyph 1 ('!') has width 6
            vm.SetActiveGlyph(0);
            vm.ResizeActiveGlyph(4);
            vm.SetActiveGlyph(1);
            vm.ResizeActiveGlyph(6);

            Assert.Equal(4, doc.Glyphs[0].Width);
            Assert.Equal(6, doc.Glyphs[1].Width);

            // Toggle Monospaced ON
            vm.IsMonospaced = true;

            // All glyphs must be resized to MaxCellWidth (8)
            Assert.Equal(8, doc.Glyphs[0].Width);
            Assert.Equal(8, doc.Glyphs[1].Width);
            Assert.All(doc.Glyphs, g => Assert.Equal(8, g.Width));

            // While monospaced, resizing active glyph resizes ALL glyphs
            vm.ResizeActiveGlyph(10);
            Assert.Equal(10, doc.MaxCellWidth);
            Assert.All(doc.Glyphs, g => Assert.Equal(10, g.Width));

            // Toggle Monospaced OFF
            vm.IsMonospaced = false;

            // Pre-mono widths should be restored!
            Assert.Equal(4, doc.Glyphs[0].Width);
            Assert.Equal(6, doc.Glyphs[1].Width);
        }

        [Fact]
        public void UndoStack_CapsAtFiftyStepsWithoutUnboundedGrowth()
        {
            var vm = new FontViewModel();

            // Perform 60 discrete modifications
            for (int i = 1; i <= 60; i++)
            {
                vm.Baseline = (i % 8);
            }

            // Verify we can undo 50 times without crashing
            for (int i = 0; i < 50; i++)
            {
                vm.Undo();
            }

            // 51st undo is a no-op (stack exhausted)
            int baselineAtStackBottom = vm.Baseline;
            vm.Undo();
            Assert.Equal(baselineAtStackBottom, vm.Baseline);
        }

        // ── Tier 4: Real-World Scenarios ─────────────────────────────────────

        [Fact]
        public void FullAuthoringLifecycle_CreateDrawResizeAndGeneratePreview()
        {
            var vm = new FontViewModel();
            var newDoc = FontDocument.CreateNew(10, 12, 65, 70); // 10x12 font, 'A'..'F'
            newDoc.FontName = "Arcade10";
            vm.Document = newDoc;

            Assert.Equal("Arcade10", vm.FontName);
            Assert.Equal(12, vm.CellHeight);
            Assert.Equal(10, vm.MaxCellWidth);

            // Draw a custom character 'A' (index 0)
            vm.SetActiveGlyph(0);
            vm.BeginDrawing();
            // Draw horizontal bar on row 6
            for (int x = 2; x <= 7; x++)
            {
                vm.DrawPixel(x, 6);
            }
            vm.EndDrawing();

            Assert.True(vm.ActiveGlyph!.IsCustomized);
            Assert.True(vm.IsDirty);

            // Change metrics
            vm.XAdvance = 11;
            vm.Baseline = 9;

            // Set sample preview text
            vm.PreviewText = "AAA";

            // Verify live preview bitmap was constructed
            Assert.NotNull(vm.PreviewBitmap);
            Assert.True(vm.PreviewBitmap.PixelWidth > 0);
            Assert.True(vm.PreviewBitmap.PixelHeight > 0);

            // Clean state tracking
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void DirtyTracking_AccuratelyReflectsEditsAndReversions()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Toggling a pixel dirties document
            vm.SaveStateForUndo();
            vm.TogglePixel(0, 0);
            Assert.True(vm.IsDirty);

            // Mark clean
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Editing comment / format settings
            vm.IncludeGlyphPreview = !vm.IncludeGlyphPreview;
            Assert.True(vm.IsDirty);
        }
    }
}
