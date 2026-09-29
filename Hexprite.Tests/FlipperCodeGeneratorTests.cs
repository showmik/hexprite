using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperCodeGeneratorTests
    {
        private readonly CodeGeneratorService _generator = new();

        [Fact]
        public void GenerateCode_FlipperCompressedBitmap_EmitsCanvasDrawBitmapCommentAndData()
        {
            var pixels = new bool[16 * 16];
            pixels[0] = true;
            pixels[15] = true;

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCompressedBitmap,
                IncludeUsageComment = true,
                IncludeDimensionConstants = true,
                SpriteName = "FlipperLogo"
            };

            string code = _generator.GenerateCode(new List<bool[]> { pixels }, 16, 16, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_bitmap(canvas, x, y, 16, 16, FlipperLogo_compressed);", code);
            Assert.Contains("const uint16_t FLIPPERLOGO_WIDTH  = 16;", code);
            Assert.Contains("const uint16_t FLIPPERLOGO_HEIGHT = 16;", code);
            Assert.Contains("static const uint8_t FlipperLogo_compressed[", code);
        }

        [Fact]
        public void GenerateCode_FlipperCompressedBitmapAnimation_EmitsFrameArray()
        {
            var frame1 = new bool[64];
            var frame2 = new bool[64];

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCompressedBitmap,
                IncludeUsageComment = true,
                IncludeDimensionConstants = true,
                SpriteName = "FlipperWalk",
                FrameRateFps = 10,
            };

            string code = _generator.GenerateCode(new List<bool[]> { frame1, frame2 }, 8, 8, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_bitmap(canvas, x, y, 8, 8, FlipperWalk_frames[frame_index]);", code);
            Assert.Contains("const uint16_t FLIPPERWALK_FRAMES = 2;", code);
            Assert.Contains("static const uint8_t FlipperWalk_frame_0[", code);
            Assert.Contains("static const uint8_t FlipperWalk_frame_1[", code);
            Assert.Contains("static const uint8_t* const FlipperWalk_frames[2] = {", code);
        }

        [Fact]
        public void GenerateCode_FlipperXbm_EmitsLsbFirstBitmap()
        {
            var pixels = new bool[64];
            pixels[0] = true; // x=0, y=0 -> bit 0 of byte 0 = 0x01

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperXbm,
                IncludeUsageComment = true,
                IncludeDimensionConstants = true,
                IncludeArraySize = true,
                SpriteName = "FlipperIcon",
            };

            string code = _generator.GenerateCode(new List<bool[]> { pixels }, 8, 8, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_xbm(canvas, x, y, 8, 8, FlipperIcon_xbm);", code);
            Assert.Contains("static const uint8_t FlipperIcon_xbm[8] = {", code);
            Assert.Contains("0x01", code);
        }

        [Fact]
        public void GenerateCode_FlipperCanvasIcon_EmitsExternDeclaration()
        {
            var pixels = new bool[100];

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCanvasIcon,
                IncludeUsageComment = true,
                IncludeDimensionConstants = true,
                SpriteName = "BatteryIcon",
            };

            string code = _generator.GenerateCode(new List<bool[]> { pixels }, 10, 10, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("canvas_draw_icon(canvas, x, y, &I_BatteryIcon_10x10);", code);
            Assert.Contains("extern const Icon I_BatteryIcon_10x10;", code);
        }

        [Fact]
        public void GenerateCode_FlipperCanvasIconAnimation_EmitsExternIconDeclarationAndLifecycleComments()
        {
            var frame1 = new bool[100];
            var frame2 = new bool[100];

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCanvasIcon,
                IncludeUsageComment = true,
                IncludeDimensionConstants = true,
                SpriteName = "BatteryIcon",
            };

            string code = _generator.GenerateCode(new List<bool[]> { frame1, frame2 }, 10, 10, settings, true, null, 0, 0, 0, 0);

            // In Flipper Zero firmware, animations are typed as extern const Icon A_... (not IconAnimation)
            Assert.Contains("extern const Icon A_BatteryIcon_10x10;", code);
            Assert.DoesNotContain("extern const IconAnimation A_", code);
            Assert.Contains("icon_animation_alloc", code);
            Assert.Contains("canvas_draw_icon_animation", code);
        }

        [Fact]
        public void GenerateCode_FlipperCanvasIconAnimation_FullFapSketch_ManagesIconAnimationLifecycle()
        {
            var frame1 = new bool[100];
            var frame2 = new bool[100];

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCanvasIcon,
                GenerateFullSketch = true,
                SpriteName = "BatteryIcon",
            };

            string code = _generator.GenerateCode(new List<bool[]> { frame1, frame2 }, 10, 10, settings, true, null, 0, 0, 0, 0);

            Assert.Contains("#include <gui/icon_animation.h>", code);
            Assert.Contains("IconAnimation* icon_anim;", code);
            Assert.Contains("app.icon_anim = icon_animation_alloc(&A_BatteryIcon_10x10);", code);
            Assert.Contains("icon_animation_start(app.icon_anim);", code);
            Assert.Contains("canvas_draw_icon_animation(canvas, x, y, app->icon_anim);", code);
            Assert.Contains("icon_animation_stop(app.icon_anim);", code);
            Assert.Contains("icon_animation_free(app.icon_anim);", code);
        }

        [Fact]
        public void GenerateCode_FlipperFapSketch_EmitsCompleteApp()
        {
            var pixels = new bool[16 * 16];

            var settings = new ExportSettings
            {
                Format = ExportFormat.FlipperCompressedBitmap,
                GenerateFullSketch = true,
                SpriteName = "DemoApp",
            };

            string code = _generator.GenerateCode(new List<bool[]> { pixels }, 16, 16, settings, false, null, 0, 0, 0, 0);

            Assert.Contains("Flipper Zero Application (FAP) — Generated by Hexprite", code);
            Assert.Contains("application.fam content:", code);
            Assert.Contains("appid=\"DemoApp\"", code);
            Assert.Contains("#include <furi.h>", code);
            Assert.Contains("#include <gui/gui.h>", code);
            Assert.Contains("static void render_callback(Canvas* canvas, void* ctx)", code);
            Assert.Contains("int32_t DemoApp_app(void* p)", code);
            Assert.Contains("gui_add_view_port(app.gui, app.view_port, GuiLayerFullscreen);", code);
        }
    }
}
