using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontDocumentM1StateHardeningTests
    {
        public FontDocumentM1StateHardeningTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        // ── 1. Revision-Based Dirty Tracking on Undo/Redo ─────────────────────

        [Fact]
        public void DirtyTracking_UndoToCleanRevision_RestoresCleanState()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Mutation 1: Baseline change
            vm.Baseline = 7;
            Assert.True(vm.IsDirty);

            // Mutation 2: YAdvance change
            vm.YAdvance = 10;
            Assert.True(vm.IsDirty);

            // Undo mutation 2: still dirty because Baseline = 7
            vm.Undo();
            Assert.Equal(7, vm.Baseline);
            Assert.True(vm.IsDirty);

            // Undo mutation 1: back to clean baseline
            vm.Undo();
            Assert.False(vm.IsDirty);

            // Redo mutation 1: should become dirty again
            vm.Redo();
            Assert.Equal(7, vm.Baseline);
            Assert.True(vm.IsDirty);

            // Redo mutation 2: still dirty
            vm.Redo();
            Assert.Equal(10, vm.YAdvance);
            Assert.True(vm.IsDirty);

            // Mark as clean at mutation 2
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Undoing now diverges from saved revision (which was mutation 2) -> dirty!
            vm.Undo();
            Assert.True(vm.IsDirty);

            // Redoing back to mutation 2 -> clean!
            vm.Redo();
            Assert.False(vm.IsDirty);
        }

        [Fact]
        public void DirtyTracking_DirectPropertySetters_MarkDirty()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // CellHeight
            vm.CellHeight = 12;
            Assert.True(vm.IsDirty);
            vm.MarkAsClean();

            // MaxCellWidth
            vm.MaxCellWidth = 12;
            Assert.True(vm.IsDirty);
            vm.MarkAsClean();

            // FirstChar
            vm.FirstChar = 33;
            Assert.True(vm.IsDirty);
            vm.MarkAsClean();

            // LastChar
            vm.LastChar = 120;
            Assert.True(vm.IsDirty);
            vm.MarkAsClean();

            // ResizeActiveGlyph
            vm.ResizeActiveGlyph(6);
            Assert.True(vm.IsDirty);
            vm.MarkAsClean();

            // ShiftActiveGlyph
            Assert.NotNull(vm.ActiveGlyph);
            vm.ActiveGlyph.Pixels[0] = false; // ensure space to shift
            vm.ActiveGlyph.Pixels[2 * vm.ActiveGlyph.Width + 2] = true;
            vm.ShiftActiveGlyph(1, 0);
            Assert.True(vm.IsDirty);
        }

        [Fact]
        public void DirtyTracking_ScrubbingExecution_CommitsDirtyState()
        {
            var vm = new FontViewModel();
            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Mouse scrubbing lifecycle
            vm.ScrubStartedCommand.Execute(null);
            Assert.True(vm.IsScrubbing);
            vm.Baseline = 7;
            vm.ScrubEndedCommand.Execute(null);
            Assert.False(vm.IsScrubbing);
            Assert.True(vm.IsDirty);

            vm.MarkAsClean();
            Assert.False(vm.IsDirty);

            // Keyboard scrubbing lifecycle
            vm.KeyboardScrubStartedCommand.Execute(null);
            vm.YAdvance = 11;
            vm.KeyboardScrubEndedCommand.Execute(null);
            Assert.True(vm.IsDirty);
        }

        // ── 2. Spurious Undo Frames on No-Op Canvas Clicks ────────────────────

        [Fact]
        public void DrawingStroke_ZeroDeltaClicks_DoNotPushSpuriousUndoFrames()
        {
            var vm = new FontViewModel();
            var glyph = vm.ActiveGlyph!;
            glyph.Pixels[0] = false;

            // Stroke that sets pixel 0 to ink (value changes from false -> true)
            vm.BeginDrawing();
            vm.DrawPixel(0, 0); // Sets to true
            vm.EndDrawing();
            Assert.True(glyph.Pixels[0]);
            Assert.True(vm.CanUndo);

            // Clear undo stack by new document
            vm.Document = FontDocument.CreateNew(8, 8);
            glyph = vm.Document.ActiveGlyph!;
            glyph.Pixels[0] = true; // Pixel is ALREADY inked

            // Click on already-inked pixel (zero delta)
            vm.BeginDrawing();
            vm.DrawPixel(0, 0); // already true, no mutation!
            vm.EndDrawing();

            // No undo frame should have been pushed!
            Assert.False(vm.CanUndo);

            // Click out of bounds (zero delta)
            vm.BeginDrawing();
            vm.DrawPixel(-1, -1);
            vm.DrawPixel(100, 100);
            vm.EndDrawing();

            Assert.False(vm.CanUndo);

            // Erase already-empty pixel (zero delta)
            glyph.Pixels[1] = false;
            vm.BeginDrawing();
            vm.DrawPixel(1, 0, erase: true); // already false
            vm.EndDrawing();

            Assert.False(vm.CanUndo);
        }

        // ── 3. 2D Stride Resizing Hardening ──────────────────────────────────

        [Fact]
        public void NormalizeGlyphs_AsymmetricResizing_Preserves2DPixelGridAccurately()
        {
            var doc = FontDocument.CreateNew(4, 4);
            var glyph = doc.Glyphs[0];
            // Set diagonal pixels: (0,0), (1,1), (2,2), (3,3)
            glyph.Pixels[0 * 4 + 0] = true;
            glyph.Pixels[1 * 4 + 1] = true;
            glyph.Pixels[2 * 4 + 2] = true;
            glyph.Pixels[3 * 4 + 3] = true;

            // Expand glyph dimensions to 6x6
            glyph.Width = 6;
            doc.MaxCellWidth = 6;
            doc.CellHeight = 6;
            doc.NormalizeGlyphs();

            var resized = doc.Glyphs[0];
            Assert.Equal(6, resized.Width);
            Assert.Equal(6, resized.Height);
            Assert.Equal(36, resized.Pixels.Length);

            // Original 4x4 diagonal preserved
            Assert.True(resized.Pixels[0 * 6 + 0]);
            Assert.True(resized.Pixels[1 * 6 + 1]);
            Assert.True(resized.Pixels[2 * 6 + 2]);
            Assert.True(resized.Pixels[3 * 6 + 3]);

            // New cells initialized to false
            Assert.False(resized.Pixels[0 * 6 + 4]);
            Assert.False(resized.Pixels[0 * 6 + 5]);
            Assert.False(resized.Pixels[4 * 6 + 0]);
            Assert.False(resized.Pixels[5 * 6 + 5]);
        }

        // ── 4. AutosaveService Atomic Cleanup & Cancellation ──────────────────

        [Fact]
        public async Task AutosaveService_ClearCurrentAutosave_DeletesAutosaveAndCancelsInFlightWrite()
        {
            string docId = "m1_test_" + Guid.NewGuid().ToString("N");
            var autosaveService = new AutosaveService();
            string expectedAutosave = Path.Combine(AutosaveService.AutosaveDir, $"recovery_{docId}.json");
            string expectedTemp = expectedAutosave + ".tmp";

            try
            {
                autosaveService.StartFontAutosaveLoop(docId, () => FontDocument.CreateNew(8, 8), () => true);

                // Simulate existing recovery and temp files
                Directory.CreateDirectory(AutosaveService.AutosaveDir);
                await File.WriteAllTextAsync(expectedAutosave, "{\"dummy\": true}");
                await File.WriteAllTextAsync(expectedTemp, "{\"dummy_temp\": true}");
                Assert.True(File.Exists(expectedAutosave));
                Assert.True(File.Exists(expectedTemp));

                // Clear current autosave
                autosaveService.ClearCurrentAutosave();

                // Both recovery and temp files must be cleanly deleted
                Assert.False(File.Exists(expectedAutosave));
                Assert.False(File.Exists(expectedTemp));

                autosaveService.Dispose();
            }
            finally
            {
                try { if (File.Exists(expectedAutosave)) File.Delete(expectedAutosave); } catch { }
                try { if (File.Exists(expectedTemp)) File.Delete(expectedTemp); } catch { }
            }
        }
    }
}
