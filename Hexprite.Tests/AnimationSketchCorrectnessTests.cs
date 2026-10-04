using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Xunit;

namespace Hexprite.Tests
{
    /// <summary>
    /// Validates that generated Arduino animation sketches are compilable (every referenced
    /// constant is declared), read PROGMEM data correctly, and implement accurate timing
    /// for both blocking and non-blocking modes across every animation layout.
    /// </summary>
    [Trait("Category", "Unit")]
    public class AnimationSketchCorrectnessTests
    {
        private const string Name = "anim";
        private const string Upper = "ANIM";

        private static readonly ExportFormat[] SketchFormats =
        [
            ExportFormat.AdafruitGfx,
            ExportFormat.U8g2DrawBitmap,
            ExportFormat.U8g2DrawXBM,
            ExportFormat.LiquidCrystalChar,
        ];

        public static IEnumerable<object[]> AllCombinations()
        {
            foreach (var fmt in SketchFormats)
            foreach (AnimationExportLayout layout in Enum.GetValues(typeof(AnimationExportLayout)))
            foreach (AnimationTimingMode timing in Enum.GetValues(typeof(AnimationTimingMode)))
            foreach (var compression in new[] { CompressionMode.None, CompressionMode.Rle })
            foreach (var withDelays in new[] { false, true })
            {
                if (fmt == ExportFormat.LiquidCrystalChar && (layout != AnimationExportLayout.ArrayOfFrames || compression != CompressionMode.None))
                    continue; // layout/compression are not applicable to LCD custom chars
                yield return [fmt, layout, timing, compression, withDelays];
            }
        }

        private static string Generate(ExportFormat fmt, AnimationExportLayout layout, AnimationTimingMode timing,
            CompressionMode compression, bool withDelays)
        {
            int w = fmt == ExportFormat.LiquidCrystalChar ? 5 : 16;
            int h = 8;
            var frames = new List<bool[]>();
            for (int f = 0; f < 3; f++)
            {
                var px = new bool[w * h];
                // Sparse, compressible, distinct-per-frame content
                for (int i = 0; i < px.Length; i++) px[i] = i % 7 == f;
                frames.Add(px);
            }

            var settings = new ExportSettings
            {
                Format = fmt,
                SpriteName = Name,
                ExportAsAnimation = true,
                AnimationLayout = layout,
                TimingMode = timing,
                Compression = compression,
                FrameRateFps = 12,
                GenerateFullSketch = true,
            };

            var delays = withDelays ? new List<int> { 1, 3, 2 } : new List<int> { 1, 1, 1 };
            return new CodeGeneratorService(new CompressionService())
                .GenerateCode(frames, w, h, settings, false, null, 0, 0, 0, 0, FloatingPasteMode.Transparent, delays);
        }

        private static string LoopBody(string code)
        {
            int i = code.IndexOf("void loop()", StringComparison.Ordinal);
            Assert.True(i >= 0, "Sketch has no loop()");
            return code[i..];
        }

        [Theory]
        [MemberData(nameof(AllCombinations))]
        public void EveryReferencedSpriteConstant_IsDeclared(ExportFormat fmt, AnimationExportLayout layout,
            AnimationTimingMode timing, CompressionMode compression, bool withDelays)
        {
            string code = Generate(fmt, layout, timing, compression, withDelays);

            var referenced = Regex.Matches(code, $@"\b{Upper}_[A-Z0-9_]+\b").Select(m => m.Value).Distinct();
            var missing = referenced
                .Where(id => !Regex.IsMatch(code, $@"(\b(const|constexpr)\b[^;\r\n]*\b{id}\s*(\[|=))|(#define\s+{id}\b)"))
                .ToList();

            Assert.True(missing.Count == 0,
                $"[{fmt}|{layout}|{timing}|{compression}|delays={withDelays}] undeclared identifiers: {string.Join(", ", missing)}");
        }

        [Theory]
        [MemberData(nameof(AllCombinations))]
        public void DelaysArray_IsReadConsistentlyWithItsStorageClass(ExportFormat fmt, AnimationExportLayout layout,
            AnimationTimingMode timing, CompressionMode compression, bool withDelays)
        {
            string code = Generate(fmt, layout, timing, compression, withDelays);
            string loop = LoopBody(code);
            if (!loop.Contains($"{Upper}_DELAYS", StringComparison.Ordinal)) return;

            bool isProgmem = Regex.IsMatch(code, $@"PROGMEM\s+{Upper}_DELAYS\s*\[");
            int totalReads = Regex.Matches(loop, $@"{Upper}_DELAYS\s*\[").Count;
            int pgmReads = Regex.Matches(loop, $@"pgm_read_byte\(\s*&{Upper}_DELAYS\s*\[").Count;

            if (isProgmem)
                Assert.True(totalReads == pgmReads, $"[{fmt}|{layout}|{timing}] PROGMEM delays read directly from RAM ({totalReads - pgmReads} raw reads)");
            else
                Assert.True(pgmReads == 0, $"[{fmt}|{layout}|{timing}] RAM delays read with pgm_read_byte");
        }

        [Theory]
        [MemberData(nameof(AllCombinations))]
        public void FrameDuration_MultipliesBeforeDividing(ExportFormat fmt, AnimationExportLayout layout,
            AnimationTimingMode timing, CompressionMode compression, bool withDelays)
        {
            string loop = LoopBody(Generate(fmt, layout, timing, compression, withDelays));

            // (1000 / FPS) * d truncates first: at 12 FPS a 3x frame becomes 249 ms instead of 250 ms.
            Assert.DoesNotMatch(@"\(1000(UL)?\s*/\s*\w+\)\s*\*", loop);
            if (withDelays)
                Assert.Matches(@"1000UL\s*\*", loop);
        }

        [Theory]
        [MemberData(nameof(AllCombinations))]
        public void NonBlockingMode_UsesMillisStateMachineWithAccurateSchedule(ExportFormat fmt, AnimationExportLayout layout,
            AnimationTimingMode timing, CompressionMode compression, bool withDelays)
        {
            if (timing != AnimationTimingMode.NonBlockingMillis) return;
            string loop = LoopBody(Generate(fmt, layout, timing, compression, withDelays));
            string ctx = $"[{fmt}|{layout}|{compression}|delays={withDelays}]";

            Assert.False(Regex.IsMatch(loop, @"\bdelay\s*\("), $"{ctx} non-blocking loop must not call delay()");
            Assert.True(loop.Contains("millis()", StringComparison.Ordinal), $"{ctx} must use millis()");

            // Drift-free fixed schedule (with resync when far behind) instead of `lastFrameTime = now`.
            Assert.True(loop.Contains("lastFrameTime + frameDuration", StringComparison.Ordinal), $"{ctx} schedule drifts");

            // First frame drawn immediately rather than after one full interval of blank screen.
            Assert.True(loop.Contains("static bool started", StringComparison.Ordinal), $"{ctx} first frame is delayed");

            // The wait must be the duration of the frame currently ON SCREEN: compute frameDuration
            // from currentFrame, then advance, then draw the new frame.
            int durIdx = loop.IndexOf("frameDuration =", StringComparison.Ordinal);
            int advIdx = loop.IndexOf("currentFrame = (currentFrame + 1)", StringComparison.Ordinal);
            int drawIdx = loop.IndexOf("if (redraw)", StringComparison.Ordinal);
            Assert.True(durIdx >= 0 && advIdx > durIdx && drawIdx > advIdx,
                $"{ctx} expected order: frameDuration(current) -> advance -> draw (dur={durIdx}, adv={advIdx}, draw={drawIdx})");
        }

        [Theory]
        [MemberData(nameof(AllCombinations))]
        public void BlockingMode_UsesDelay(ExportFormat fmt, AnimationExportLayout layout,
            AnimationTimingMode timing, CompressionMode compression, bool withDelays)
        {
            if (timing != AnimationTimingMode.BlockingDelay) return;
            string loop = LoopBody(Generate(fmt, layout, timing, compression, withDelays));
            Assert.Matches(@"\bdelay\s*\(", loop);
            Assert.DoesNotContain("lastFrameTime", loop);
        }
    }
}
