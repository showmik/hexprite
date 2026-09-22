using System;
using System.Linq;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontEditorM2Challenger2PreviewAndMetricsTests
    {
        public FontEditorM2Challenger2PreviewAndMetricsTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        private static FontDocument CreateMinimalFont(int cellW = 8, int cellH = 8, int firstChar = 65, int lastChar = 66)
        {
            var doc = FontDocument.CreateNew(cellW, cellH, firstChar, lastChar);
            doc.FontName = "ChallengerFont";
            doc.Baseline = 6;
            doc.YAdvance = cellH;
            doc.MaxCellWidth = cellW;

            // Populate some ink on 'A' and 'B'
            var gA = doc.GetGlyph('A');
            if (gA != null)
            {
                gA.Width = 6;
                gA.XAdvance = 7;
                gA.Pixels[0] = true;
                gA.Pixels[1] = true;
            }

            var gB = doc.GetGlyph('B');
            if (gB != null)
            {
                gB.Width = 6;
                gB.XAdvance = 7;
                gB.Pixels[0] = true;
                gB.Pixels[gB.Width - 1] = true;
            }

            return doc;
        }

        private static int CountInk(bool[,] preview)
        {
            int count = 0;
            int w = preview.GetLength(0);
            int h = preview.GetLength(1);
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    if (preview[x, y]) count++;
                }
            }
            return count;
        }

        // ════════════════════════════════════════════════════════════════════════════
        // AREA 1: LIVE PREVIEW RENDERING & FALLBACKS (SPECIFICATION VERIFICATION)
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Challenge_MissingCharacters_AdvanceCursorWithoutWordCollapse()
        {
            // Font has only 'A' and 'B'.
            // 'M', 'N', 'O' are missing.
            var font = CreateMinimalFont(8, 8, 65, 66);
            var gA = font.GetGlyph('A')!;
            gA.Width = 5;
            gA.XAdvance = 6;
            gA.Pixels[0] = true;

            var gB = font.GetGlyph('B')!;
            gB.Width = 5;
            gB.XAdvance = 6;
            gB.Pixels[0] = true;

            // 1. "AB" - standard contiguous
            var resultAB = FontPreviewRenderer.RenderPreviewText(font, "AB");
            Assert.True(resultAB[0, 0], "'A' ink should be at x=0");
            Assert.True(resultAB[6, 0], "'B' ink should be at x=6");

            // 2. "AMB" - 'M' is missing, should advance cursor by MaxCellWidth (8)
            var resultAMB = FontPreviewRenderer.RenderPreviewText(font, "AMB");
            Assert.True(resultAMB[0, 0], "'A' ink should be at x=0");
            // 'B' should be at x = 6 (A advance) + 8 (M fallback) = 14
            Assert.True(resultAMB[14, 0], "'B' ink should be at x=14 after missing 'M' advance of 8");
            Assert.False(resultAMB[6, 0], "Position x=6 should not have 'B' ink (words did not collapse)");

            // 3. "A M B" - missing space and missing 'M'
            var resultASpaceMB = FontPreviewRenderer.RenderPreviewText(font, "A M B");
            Assert.True(resultASpaceMB[0, 0], "'A' ink at x=0");
            // Fallback for space in proportional font (CellHeight=8 / 2 = 4)
            // 'M' starts at 6 + 4 = 10, advances by 8
            // ' ' starts at 18, advances by 4
            // 'B' starts at 18 + 4 = 22
            Assert.True(resultASpaceMB[22, 0], "'B' ink should be at x=22 after spaces and missing 'M'");
        }

        [Fact]
        public void Challenge_ConsecutiveSpaces_SpacingScalesLinearly()
        {
            var font = CreateMinimalFont(8, 8, 65, 66);
            var gA = font.GetGlyph('A')!;
            gA.Width = 5;
            gA.XAdvance = 6;
            gA.Pixels[0] = true;

            var gB = font.GetGlyph('B')!;
            gB.Width = 5;
            gB.XAdvance = 6;
            gB.Pixels[0] = true;

            // Measure position of B across increasing space counts
            int GetBPosition(string text)
            {
                var res = FontPreviewRenderer.RenderPreviewText(font, text);
                for (int x = 6; x < res.GetLength(0); x++)
                {
                    if (res[x, 0]) return x;
                }
                return -1;
            }

            int pos1 = GetBPosition("A B");
            int pos2 = GetBPosition("A  B");
            int pos3 = GetBPosition("A   B");
            int pos4 = GetBPosition("A    B");

            Assert.True(pos1 > 6, "Space must advance beyond 'A'");
            Assert.True(pos2 > pos1, "Two spaces must advance further than one");
            Assert.True(pos3 > pos2, "Three spaces must advance further than two");
            Assert.True(pos4 > pos3, "Four spaces must advance further than three");

            // Verify linear delta
            int delta1 = pos2 - pos1;
            int delta2 = pos3 - pos2;
            int delta3 = pos4 - pos3;
            Assert.Equal(delta1, delta2);
            Assert.Equal(delta2, delta3);
        }

        [Fact]
        public void Challenge_Punctuation_MissingAndMappedPunctuation()
        {
            var font = CreateMinimalFont(8, 8, 65, 66);
            // Font does NOT have punctuation. Test all standard ASCII punctuation
            string punctuationSample = "A!@#$%^&*()_+-=[]{}|;':\",./<>?B";

            var result = FontPreviewRenderer.RenderPreviewText(font, punctuationSample);
            Assert.NotNull(result);
            Assert.True(result.GetLength(0) > 0);
            Assert.True(result.GetLength(1) > 0);

            // Exactly 2 glyphs are in font ('A' and 'B'), so total ink pixels must be 4
            Assert.Equal(4, CountInk(result));

            // 'A' ink is at x=0
            Assert.True(result[0, 0]);

            // 'B' ink should be far to the right, advancing by at least punctuation length * 4
            int punctuationCharCount = punctuationSample.Length - 2; // 29 chars
            int minExpectedBPos = 6 + punctuationCharCount * 4;
            bool foundB = false;
            for (int x = minExpectedBPos; x < result.GetLength(0); x++)
            {
                if (result[x, 0])
                {
                    foundB = true;
                    break;
                }
            }
            Assert.True(foundB, $"'B' should be found at or beyond x={minExpectedBPos}");
        }

        [Fact]
        public void Challenge_MixedAsciiAndNonAscii_ProbingMultibyteAndSurrogates()
        {
            var font = CreateMinimalFont(8, 8, 65, 66);
            // Test Cyrillic, CJK, Latin accented, and Emoji (surrogate pair)
            string mixedText = "A Привет 世界 Café 😀 B";

            var result = FontPreviewRenderer.RenderPreviewText(font, mixedText);
            Assert.NotNull(result);
            Assert.True(result.GetLength(0) > 0);
            Assert.True(result.GetLength(1) > 0);

            // 'A' ink at x=0
            Assert.True(result[0, 0]);

            // Total ink pixels should still be exactly 4 (from 'A' and 'B')
            Assert.Equal(4, CountInk(result));

            // 'B' should not collapse onto 'A'
            Assert.False(result[6, 0]);
        }

        [Fact]
        public void Challenge_WindowsCrLf_And_MultiNewlineStress()
        {
            var font = CreateMinimalFont(8, 8, 65, 66);
            font.Baseline = 6;
            font.YAdvance = 10;

            // Probe Windows \r\n line ending
            string textCrLf = "A\r\nB";
            var resultCrLf = FontPreviewRenderer.RenderPreviewText(font, textCrLf);
            Assert.True(resultCrLf.GetLength(0) > 0);
            Assert.True(resultCrLf.GetLength(1) >= 18);

            // 'A' ink on line 0
            Assert.True(resultCrLf[0, 0]);
            // 'B' ink on line 1 (y=10)
            Assert.True(resultCrLf[0, 10]);

            // Probe excessive \r\r\n\n\r
            string messyNewlines = "A\r\r\n\n\rB";
            var resultMessy = FontPreviewRenderer.RenderPreviewText(font, messyNewlines);
            Assert.True(resultMessy.GetLength(0) > 0);
            Assert.True(resultMessy.GetLength(1) > 0);
            Assert.Equal(4, CountInk(resultMessy));
        }

        [Fact]
        public void Challenge_ExtremeNegativeOffsets_NoIndexOutOfRange()
        {
            var font = CreateMinimalFont(8, 8, 65, 66);
            var gA = font.GetGlyph('A')!;
            gA.XOffset = -30;
            gA.YOffset = -25;
            gA.Width = 10;
            gA.Height = 10;
            gA.Pixels = new bool[100];
            gA.Pixels[0] = true; // Top-left
            gA.Pixels[99] = true; // Bottom-right
            gA.XAdvance = 15;

            // Must not throw IndexOutOfRangeException
            var result = FontPreviewRenderer.RenderPreviewText(font, "A");
            Assert.NotNull(result);
            Assert.True(result.GetLength(0) > 0);
            Assert.True(result.GetLength(1) > 0);
            Assert.Equal(2, CountInk(result));

            // Both pixels must be safely mapped within bounds
            Assert.True(result[0, 0], "Top-left should be shifted to (0,0)");
            Assert.True(result[9, 9], "Bottom-right should be shifted to (9,9)");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // AREA 2: METRIC PROPERTY UPDATES & PREVIEW REFRESH (WORKING PROPERTIES)
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void Challenge_MetricUpdates_Baseline_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Modify Baseline
            vm.Baseline = 4;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when Baseline changes");
        }

        [Fact]
        public void Challenge_MetricUpdates_YAdvance_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A\nB";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);
            int h1 = b1.PixelHeight;

            // Modify YAdvance
            vm.YAdvance = 20;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when YAdvance changes");
            Assert.True(b2.PixelHeight > h1, $"PixelHeight should increase from {h1} when YAdvance is doubled, got {b2.PixelHeight}");
        }

        [Fact]
        public void Challenge_MetricUpdates_XAdvance_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "AA";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);
            int w1 = b1.PixelWidth;

            // Modify XAdvance on active glyph 'A'
            vm.XAdvance = 24;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when XAdvance changes");
            Assert.True(b2.PixelWidth > w1, $"PixelWidth should increase from {w1} when XAdvance is enlarged, got {b2.PixelWidth}");
        }

        [Fact]
        public void Challenge_MetricUpdates_XOffset_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Modify XOffset
            vm.XOffset = 6;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when XOffset changes");
        }

        [Fact]
        public void Challenge_MetricUpdates_YOffset_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Modify YOffset
            vm.YOffset = 4;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when YOffset changes");
        }

        [Fact]
        public void Challenge_MetricUpdates_CellHeight_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Modify CellHeight
            vm.CellHeight = 16;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when CellHeight changes");
        }

        [Fact]
        public void Challenge_MetricUpdates_GlyphWidth_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Modify GlyphWidth
            vm.GlyphWidth = 14;

            var b2 = vm.PreviewBitmap;
            Assert.NotNull(b2);
            Assert.False(ReferenceEquals(b1, b2), "PreviewBitmap must be replaced with a new instance when GlyphWidth changes");
        }

        [Fact]
        public void Challenge_ScrubbingStress_100IterativeMetricChanges_NeverCorruptsPreview()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "A B";

            var rng = new Random(42);

            for (int i = 0; i < 100; i++)
            {
                int metricChoice = rng.Next(6);
                switch (metricChoice)
                {
                    case 0:
                        vm.Baseline = rng.Next(1, 16);
                        break;
                    case 1:
                        vm.YAdvance = rng.Next(4, 24);
                        break;
                    case 2:
                        vm.XAdvance = rng.Next(2, 20);
                        break;
                    case 3:
                        vm.XOffset = rng.Next(-10, 10);
                        break;
                    case 4:
                        vm.YOffset = rng.Next(-10, 10);
                        break;
                    case 5:
                        vm.GlyphWidth = rng.Next(2, 20);
                        break;
                }

                Assert.NotNull(vm.PreviewBitmap);
                Assert.True(vm.PreviewBitmap.PixelWidth > 0);
                Assert.True(vm.PreviewBitmap.PixelHeight > 0);
            }
        }

        [Fact]
        public void Challenge_UndoRedo_RefreshesPreviewBitmap()
        {
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            vm.Document = doc;
            vm.PreviewText = "AA";

            var bOriginal = vm.PreviewBitmap;
            Assert.NotNull(bOriginal);

            // Mutate XAdvance
            vm.XAdvance = 20;
            var bMutated = vm.PreviewBitmap;
            Assert.False(ReferenceEquals(bOriginal, bMutated));

            // Undo
            vm.Undo();
            var bUndone = vm.PreviewBitmap;
            Assert.NotNull(bUndone);
            Assert.False(ReferenceEquals(bMutated, bUndone), "Undo must refresh PreviewBitmap");

            // Redo
            vm.Redo();
            var bRedone = vm.PreviewBitmap;
            Assert.NotNull(bRedone);
            Assert.False(ReferenceEquals(bUndone, bRedone), "Redo must refresh PreviewBitmap");
        }

        // ════════════════════════════════════════════════════════════════════════════
        // AREA 3: EMPIRICAL DEFECT REPRODUCERS & AUDIT CHECKS
        // ════════════════════════════════════════════════════════════════════════════

        [Fact]
        public void DefectAudit_MaxCellWidth_InProportionalFont_OmittedUpdatePreviewBitmap()
        {
            // AUDIT: When IsMonospaced is false, MaxCellWidth affects fallback advances in FontPreviewRenderer,
            // but FontViewModel.MaxCellWidth setter omits UpdatePreviewBitmap().
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            doc.IsMonospaced = false;
            doc.MaxCellWidth = 8;
            vm.Document = doc;
            vm.PreviewText = "A Z"; // 'Z' is missing

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Change MaxCellWidth
            vm.MaxCellWidth = 32;
            var b2 = vm.PreviewBitmap;

            // Verify that UpdatePreviewBitmap was called and refreshed the bitmap
            bool didRefresh = !ReferenceEquals(b1, b2);
            Assert.True(didRefresh, "MaxCellWidth setter in proportional mode refreshes UpdatePreviewBitmap()");
        }

        [Fact]
        public void DefectAudit_IsMonospacedToggle_OmittedUpdatePreviewBitmap()
        {
            // AUDIT: FontViewModel.IsMonospaced setter resizes glyphs or restores cached widths,
            // and now calls UpdatePreviewBitmap().
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            doc.IsMonospaced = false;
            doc.MaxCellWidth = 12;
            vm.Document = doc;
            vm.PreviewText = "AB";

            var b1 = vm.PreviewBitmap;
            Assert.NotNull(b1);

            // Toggle Monospaced ON
            vm.IsMonospaced = true;
            var b2 = vm.PreviewBitmap;

            // Verify that UpdatePreviewBitmap was called and refreshed the bitmap
            bool didRefresh = !ReferenceEquals(b1, b2);
            Assert.True(didRefresh, "IsMonospaced setter refreshes UpdatePreviewBitmap()");
        }

        [Fact]
        public void DefectAudit_MaxCellWidth_InMonospacedFont_OnlyResizesActiveGlyph()
        {
            // AUDIT: In monospaced mode, changing MaxCellWidth calls ResizeActiveGlyph,
            // which only resizes the active glyph, violating the monospaced invariant for other glyphs!
            var vm = new FontViewModel();
            var doc = CreateMinimalFont(8, 8, 65, 66);
            doc.IsMonospaced = true;
            doc.MaxCellWidth = 8;
            vm.Document = doc;

            // Initially both glyphs are width 6
            Assert.Equal(6, doc.Glyphs[0].Width);
            Assert.Equal(6, doc.Glyphs[1].Width);

            // User edits MaxCellWidth to 16
            vm.MaxCellWidth = 16;

            // Active glyph (Glyphs[0]) is resized to 16
            Assert.Equal(16, doc.Glyphs[0].Width);

            // Inactive glyph (Glyphs[1]) is also resized to 16 by ResizeActiveGlyph in monospaced mode
            Assert.Equal(16, doc.Glyphs[1].Width);
        }
    }
}
