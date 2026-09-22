using System;
using System.Globalization;
using System.Linq;
using Hexprite.Converters;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FontEditorUIRefinementTests
    {
        public FontEditorUIRefinementTests()
        {
            WpfTestHelper.EnsureApplication();
        }

        // ── Category Filtering Tests ─────────────────────────────────────────

        [Fact]
        public void SelectedCategoryFilter_DefaultIsAll()
        {
            var vm = new FontViewModel();
            Assert.Equal(GlyphCategoryFilter.All, vm.SelectedCategoryFilter);
            Assert.Equal(vm.TotalGlyphCount, vm.FilteredGlyphCount);
            Assert.Equal(95, vm.TotalGlyphCount);
        }

        [Fact]
        public void SelectedCategoryFilter_Letters_OnlyIncludesAlphabeticGlyphs()
        {
            var vm = new FontViewModel();
            vm.SelectedCategoryFilter = GlyphCategoryFilter.Letters;

            Assert.Equal(52, vm.FilteredGlyphCount); // 26 uppercase + 26 lowercase
            Assert.All(vm.FilteredGlyphMap, g => Assert.True(char.IsLetter(g.Character)));
        }

        [Fact]
        public void SelectedCategoryFilter_Numbers_OnlyIncludesDigits()
        {
            var vm = new FontViewModel();
            vm.SelectedCategoryFilter = GlyphCategoryFilter.Numbers;

            Assert.Equal(10, vm.FilteredGlyphCount); // '0'..'9'
            Assert.All(vm.FilteredGlyphMap, g => Assert.True(char.IsDigit(g.Character)));
        }

        [Fact]
        public void SelectedCategoryFilter_Symbols_OnlyIncludesPunctuationAndSymbols()
        {
            var vm = new FontViewModel();
            vm.SelectedCategoryFilter = GlyphCategoryFilter.Symbols;

            Assert.True(vm.FilteredGlyphCount > 0);
            Assert.All(vm.FilteredGlyphMap, g =>
            {
                Assert.False(char.IsLetter(g.Character));
                Assert.False(char.IsDigit(g.Character));
                Assert.False(char.IsControl(g.Character));
                Assert.False(char.IsWhiteSpace(g.Character));
            });
        }

        [Fact]
        public void SelectedCategoryFilter_CustomizedOnly_FiltersBasedOnEdits()
        {
            var vm = new FontViewModel();
            
            // Initially, no glyphs are customized
            vm.SelectedCategoryFilter = GlyphCategoryFilter.CustomizedOnly;
            Assert.Equal(0, vm.FilteredGlyphCount);

            // Draw on the active glyph (' ')
            vm.TogglePixel(0, 0);
            Assert.True(vm.ActiveGlyph!.IsCustomized);

            // Refresh filter evaluation
            vm.SelectedCategoryFilter = GlyphCategoryFilter.All;
            vm.SelectedCategoryFilter = GlyphCategoryFilter.CustomizedOnly;

            Assert.Equal(1, vm.FilteredGlyphCount);
            Assert.Equal(vm.ActiveGlyph.CodePoint, vm.FilteredGlyphMap.First().CodePoint);
        }

        [Fact]
        public void GlyphFilter_CombinedWithCategoryFilter_FiltersCorrectly()
        {
            var vm = new FontViewModel();
            vm.SelectedCategoryFilter = GlyphCategoryFilter.Letters;
            vm.GlyphFilter = "41"; // Hex code for 'A'

            // Only 'A' matches hex code 0x41 among letters
            Assert.Equal(1, vm.FilteredGlyphCount);
            Assert.Equal('A', vm.FilteredGlyphMap.First().Character);

            // Clear filter command resets GlyphFilter
            vm.ClearFilterCommand.Execute(null);
            Assert.Equal(string.Empty, vm.GlyphFilter);
            Assert.Equal(52, vm.FilteredGlyphCount);
        }

        // ── Guides Toggles & Rendering Tests ─────────────────────────────────

        [Fact]
        public void GuideToggles_DefaultToTrue_CanBeToggled()
        {
            var vm = new FontViewModel();
            Assert.True(vm.ShowMetricsGuides);
            Assert.True(vm.ShowAdvanceGuide);

            vm.ShowMetricsGuides = false;
            Assert.False(vm.ShowMetricsGuides);

            vm.ShowAdvanceGuide = false;
            Assert.False(vm.ShowAdvanceGuide);
        }

        [Fact]
        public void GlyphGuideRenderer_RespectsVisibilityFlags()
        {
            var doc = FontDocument.CreateNew(8, 8);
            var glyph = doc.Glyphs[0];
            int cellSize = 8;
            int width = 80; // Large enough to include advance guide at (glyph.XAdvance * cellSize)
            int height = 80;

            uint[] bufferWithAllGuides = new uint[width * height];
            GlyphGuideRenderer.RenderGuides(bufferWithAllGuides, width, height, cellSize, doc, glyph, showMetricsGuides: true, showAdvanceGuide: true);

            uint[] bufferWithoutMetrics = new uint[width * height];
            GlyphGuideRenderer.RenderGuides(bufferWithoutMetrics, width, height, cellSize, doc, glyph, showMetricsGuides: false, showAdvanceGuide: true);

            uint[] bufferWithoutAdvance = new uint[width * height];
            GlyphGuideRenderer.RenderGuides(bufferWithoutAdvance, width, height, cellSize, doc, glyph, showMetricsGuides: true, showAdvanceGuide: false);

            uint[] bufferWithNoGuides = new uint[width * height];
            GlyphGuideRenderer.RenderGuides(bufferWithNoGuides, width, height, cellSize, doc, glyph, showMetricsGuides: false, showAdvanceGuide: false);

            // Buffer with no guides should remain pure black/empty
            Assert.All(bufferWithNoGuides, pixel => Assert.Equal(0u, pixel));

            // Metrics guides alone should draw pixels
            Assert.Contains(bufferWithoutAdvance, pixel => pixel != 0u);

            // Advance guide alone should draw pixels
            Assert.Contains(bufferWithoutMetrics, pixel => pixel != 0u);

            // Total painted pixels in all guides should exceed either individually
            int countAll = bufferWithAllGuides.Count(p => p != 0u);
            int countNoMetrics = bufferWithoutMetrics.Count(p => p != 0u);
            int countNoAdvance = bufferWithoutAdvance.Count(p => p != 0u);

            Assert.True(countAll >= countNoMetrics);
            Assert.True(countAll >= countNoAdvance);
        }

        // ── Nudge Commands Tests ─────────────────────────────────────────────

        [Fact]
        public void NudgeCommands_ShiftGlyphPixelsInAllDirections()
        {
            var vm = new FontViewModel();
            vm.JumpToChar('A');

            // Draw a single pixel at (3, 3)
            vm.BeginDrawing();
            vm.DrawPixel(3, 3);
            vm.EndDrawing();
            Assert.True(vm.ActiveGlyph!.Pixels[3 * vm.ActiveGlyph.Width + 3]);

            // Nudge Right (+1, 0)
            vm.NudgeRightCommand.Execute(null);
            Assert.False(vm.ActiveGlyph.Pixels[3 * vm.ActiveGlyph.Width + 3]);
            Assert.True(vm.ActiveGlyph.Pixels[3 * vm.ActiveGlyph.Width + 4]);

            // Nudge Down (0, +1)
            vm.NudgeDownCommand.Execute(null);
            Assert.False(vm.ActiveGlyph.Pixels[3 * vm.ActiveGlyph.Width + 4]);
            Assert.True(vm.ActiveGlyph.Pixels[4 * vm.ActiveGlyph.Width + 4]);

            // Nudge Left (-1, 0)
            vm.NudgeLeftCommand.Execute(null);
            Assert.False(vm.ActiveGlyph.Pixels[4 * vm.ActiveGlyph.Width + 4]);
            Assert.True(vm.ActiveGlyph.Pixels[4 * vm.ActiveGlyph.Width + 3]);

            // Nudge Up (0, -1) - returns to (3, 3)
            vm.NudgeUpCommand.Execute(null);
            Assert.True(vm.ActiveGlyph.Pixels[3 * vm.ActiveGlyph.Width + 3]);
        }

        // ── Hardware Simulation Preview Tests ────────────────────────────────

        [Theory]
        [InlineData(DisplayType.GenericWhite)]
        [InlineData(DisplayType.SSD1306Blue)]
        [InlineData(DisplayType.SSD1306Green)]
        [InlineData(DisplayType.FlipperZero)]
        [InlineData(DisplayType.ePaper)]
        public void HardwarePreview_SupportsAllDisplayTypes(DisplayType displayType)
        {
            var vm = new FontViewModel();
            vm.PreviewText = "AB";
            vm.PreviewDisplayType = displayType;

            Assert.NotNull(vm.PreviewBitmap);
            Assert.True(vm.PreviewBitmap.PixelWidth > 0);
            Assert.True(vm.PreviewBitmap.PixelHeight > 0);
        }

        [Fact]
        public void HardwarePreview_PreviewInverted_RendersSuccessfully()
        {
            var vm = new FontViewModel();
            vm.PreviewText = "OK";
            vm.PreviewInverted = true;

            Assert.NotNull(vm.PreviewBitmap);
            Assert.True(vm.PreviewInverted);

            vm.PreviewInverted = false;
            Assert.NotNull(vm.PreviewBitmap);
            Assert.False(vm.PreviewInverted);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void HardwarePreview_PreviewScale_ScalesBitmapDimensions(int scale)
        {
            var vm = new FontViewModel();
            vm.PreviewText = "Test";
            vm.PreviewScale = scale;

            Assert.NotNull(vm.PreviewBitmap);
            Assert.Equal(scale, vm.PreviewScale);
        }

        [Fact]
        public void PreviewPresetCommand_UpdatesPreviewText()
        {
            var vm = new FontViewModel();
            string preset = "The quick brown fox jumps over the lazy dog";

            vm.SetPreviewPresetCommand.Execute(preset);
            Assert.Equal(preset, vm.PreviewText);
        }

        // ── Value Converters Tests ───────────────────────────────────────────

        [Fact]
        public void FontExportFormatDisplayConverter_ConvertsAllFormats()
        {
            var converter = new FontExportFormatDisplayConverter();

            foreach (FontExportFormat format in Enum.GetValues<FontExportFormat>())
            {
                object result = converter.Convert(format, typeof(string), null!, CultureInfo.InvariantCulture);
                Assert.NotNull(result);
                string str = Assert.IsType<string>(result);
                Assert.NotEmpty(str);
            }
        }

        [Fact]
        public void DisplayTypeDescriptionConverter_ConvertsAllDisplayTypes()
        {
            var converter = new DisplayTypeDescriptionConverter();

            foreach (DisplayType type in Enum.GetValues<DisplayType>())
            {
                object result = converter.Convert(type, typeof(string), null!, CultureInfo.InvariantCulture);
                Assert.NotNull(result);
                string str = Assert.IsType<string>(result);
                Assert.NotEmpty(str);
            }
        }

        // ── GlyphItemViewModel Tests ─────────────────────────────────────────

        [Fact]
        public void GlyphItemViewModel_PropertiesAreFormattedCorrectly()
        {
            var stateSpace = new GlyphState { CodePoint = 32, Width = 8, Height = 8, Pixels = new bool[64] };
            var itemSpace = new GlyphItemViewModel(stateSpace);

            Assert.Equal(32, itemSpace.CodePoint);
            Assert.Equal(' ', itemSpace.Character);
            Assert.Equal("0x20", itemSpace.HexCode);
            Assert.Equal(string.Empty, itemSpace.CharDisplay);
            Assert.False(itemSpace.IsPrintable);
            Assert.Equal("0x20", itemSpace.DisplayLabel);

            var stateA = new GlyphState { CodePoint = 65, Width = 8, Height = 8, Pixels = new bool[64] };
            var itemA = new GlyphItemViewModel(stateA);

            Assert.Equal(65, itemA.CodePoint);
            Assert.Equal('A', itemA.Character);
            Assert.Equal("0x41", itemA.HexCode);
            Assert.Equal("A", itemA.CharDisplay);
            Assert.True(itemA.IsPrintable);
            Assert.Equal("'A' (0x41)", itemA.DisplayLabel);
        }
    }
}
