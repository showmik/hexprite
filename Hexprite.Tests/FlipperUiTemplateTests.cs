using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperUiTemplateTests
    {
        [Fact]
        public void ApplyTemplate_HeaderBar_DrawsDividerAndTitle()
        {
            var sprite = new SpriteState(128, 64);
            FlipperUiTemplateService.ApplyTemplate(sprite, FlipperUiTemplateType.HeaderBar, "Hexprite");

            var pixels = sprite.ActiveLayerPixels;

            // Header line at y=11 should have pixels
            bool hasDivider = false;
            for (int x = 0; x < 128; x++)
            {
                if (pixels[11 * 128 + x]) { hasDivider = true; break; }
            }

            Assert.True(hasDivider);
        }

        [Fact]
        public void ApplyTemplate_DialogBox_DrawsBorderAndButtons()
        {
            var sprite = new SpriteState(128, 64);
            FlipperUiTemplateService.ApplyTemplate(sprite, FlipperUiTemplateType.DialogBox, "Notice");

            var pixels = sprite.ActiveLayerPixels;

            bool hasPixels = false;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i]) { hasPixels = true; break; }
            }

            Assert.True(hasPixels);
        }

        [Theory]
        [InlineData(FlipperUiTemplateType.HeaderBar)]
        [InlineData(FlipperUiTemplateType.DialogBox)]
        [InlineData(FlipperUiTemplateType.ListView)]
        [InlineData(FlipperUiTemplateType.AppIconGuide)]
        public void ApplyTemplate_FapScreenTemplates_RendersSignificantPixelOutput(FlipperUiTemplateType templateType)
        {
            var sprite = new SpriteState(128, 64);
            FlipperUiTemplateService.ApplyTemplate(sprite, templateType, "TestTitle");

            var pixels = sprite.ActiveLayerPixels;
            int count = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i]) count++;
            }

            Assert.True(count > 20, $"Template {templateType} should render pixels, got {count}");
        }
    }
}
