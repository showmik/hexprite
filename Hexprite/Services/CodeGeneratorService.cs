using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Hexprite.Core;
using Hexprite.Services.Compression;

namespace Hexprite.Services
{
    /// <summary>
    /// Service responsible for converting sprite data into various code formats (C-arrays, MicroPython, raw binary)
    /// and parsing existing code back into the internal <see cref="SpriteState"/>.
    /// </summary>
    public partial class CodeGeneratorService : ICodeGeneratorService
    {
        private readonly ICompressionService? _compression;

        /// <summary>Parameterless constructor for backward compatibility and tests.</summary>
        public CodeGeneratorService() { }

        /// <summary>DI constructor — receives the compression service.</summary>
        public CodeGeneratorService(ICompressionService compression)
        {
            _compression = compression ?? throw new ArgumentNullException(nameof(compression));
        }

        [GeneratedRegex(@"(?://|#).*")]
        private static partial Regex LineCommentRegex { get; }

        [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
        private static partial Regex BlockCommentRegex { get; }

        [GeneratedRegex(@"0[xX]([0-9a-fA-F]{1,2})")]
        private static partial Regex HexByteRegex { get; }

        [GeneratedRegex(@"(?:0[xX]|\\x)[0-9a-fA-F]{1,4}\b|0[bB][01]{1,8}\b|B[01]{5,8}\b|\b[01]{8}\b|\b\d{1,3}\b")]
        private static partial Regex ByteTokenRegex { get; }

        [GeneratedRegex(@"^\s*\d+:\s*", RegexOptions.Multiline)]
        private static partial Regex BinaryRowPrefixRegex { get; }

        [GeneratedRegex(@"[^a-zA-Z0-9_]")]
        private static partial Regex InvalidIdentifierCharRegex { get; }

        [GeneratedRegex(@"(?:extern|inline|PROGMEM|U8X8_PROGMEM|uint8_t|uint16_t|uint32_t|const|static|unsigned|signed|char|byte|short|int|long|\*|\s)*\b([a-zA-Z_][a-zA-Z0-9_]*)\s*\[\s*([a-zA-Z0-9_]*)\s*\]\s*\[\s*([a-zA-Z0-9_]+)\s*\]", RegexOptions.Multiline)]
        private static partial Regex Array2DDimRegex { get; }

        [GeneratedRegex(@"(?:(?:alignas\s*\([^)]*\)|__attribute__\s*\(\([^)]*\)\)|[a-zA-Z_:][a-zA-Z0-9_:]*|\*)\s+)+([a-zA-Z_][a-zA-Z0-9_]*)\s*\[", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex CVariableNameRegex { get; }

        [GeneratedRegex(@"(?:0[xXbB]|\\x|B[01]{8})\b")]
        private static partial Regex PrefixedTokenRegex { get; }

        public static readonly string[] HexUpper = [.. Enumerable.Range(0, 256).Select(i => $"0x{i:X2}")];
        public static readonly string[] HexLower = [.. Enumerable.Range(0, 256).Select(i => $"0x{i:x2}")];

        // ═══════════════════════════════════════════════════════════════════════
        //  Public API
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates source code for the sprite based on the specified export settings.
        /// </summary>
        /// <param name="frames">List of frame pixel buffers.</param>
        /// <param name="width">Canvas width.</param>
        /// <param name="height">Canvas height.</param>
        /// <param name="settings">Export configuration.</param>
        /// <param name="isFloating">Whether a floating layer is currently active.</param>
        /// <param name="floatingPixels">Floating layer pixel buffer.</param>
        /// <param name="floatX">X offset of the floating layer.</param>
        /// <param name="floatY">Y offset of the floating layer.</param>
        /// <param name="floatW">Width of the floating layer.</param>
        /// <param name="floatH">Height of the floating layer.</param>
        /// <param name="pasteMode">Determines how floating pixels are stamped.</param>
        /// <returns>Formatted source code string.</returns>
        public string GenerateCode(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (settings.GenerateFullSketch && !settings.IncludeDimensionConstants)
            {
                settings = settings.Clone();
                settings.IncludeDimensionConstants = true;
            }

            string name = SanitiseName(settings.SpriteName);
            string hexFmt = settings.UppercaseHex ? "X2" : "x2";
            bool isAnimation = settings.ExportAsAnimation && frames.Count > 1;

            int outputWidth = width;
            int outputHeight = height;
            int numFrames = frames.Count;

            // FIX: Apply floating pixels to frames BEFORE flattening or processing
            if (isFloating && floatingPixels != null)
            {
                var mergedFrames = new List<bool[]>(numFrames);
                foreach (var frame in frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var merged = (bool[])frame.Clone();
                    for (int fy = 0; fy < floatH; fy++)
                    {
                        for (int fx = 0; fx < floatW; fx++)
                        {
                            int px = floatX + fx;
                            int py = floatY + fy;
                            if (px >= 0 && px < width && py >= 0 && py < height)
                            {
                                bool floatingPixel = floatingPixels[fx, fy];
                                if (pasteMode == FloatingPasteMode.Transparent)
                                {
                                    if (floatingPixel) merged[py * width + px] = true;
                                }
                                else
                                {
                                    merged[py * width + px] = floatingPixel;
                                }
                            }
                        }
                    }
                    mergedFrames.Add(merged);
                }
                frames = mergedFrames;
                isFloating = false; // Prevent double-application in BuildByteArray
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (isAnimation)
            {
                (frames, outputWidth, outputHeight) = FlattenAnimationFrames(
                    frames, width, height, settings.AnimationLayout, numFrames, cancellationToken);
            }

            // Build byte arrays for each frame
            var frameData = new List<byte[]>();
            bool isLsbFirst = settings.Format == ExportFormat.U8g2DrawXBM
                || settings.Format == ExportFormat.FlipperXbm
                || settings.Format == ExportFormat.FlipperCompressedBitmap
                || settings.Format == ExportFormat.FlipperCanvasIcon;

            foreach (var frame in frames)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] data;
                if (settings.Format == ExportFormat.LiquidCrystalChar)
                {
                    data = BuildLiquidCrystalByteArray(frame, outputWidth, outputHeight);
                }
                else
                {
                    data = BuildByteArray(frame, outputWidth, outputHeight, isLsbFirst,
                        isFloating, floatingPixels, floatX, floatY, floatW, floatH, pasteMode, cancellationToken);
                }
                frameData.Add(data);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ── Apply compression ────────────────────────────────────────────
            bool compressionActive = _compression != null
                && settings.Compression != CompressionMode.None
                && settings.Format != ExportFormat.RawHex
                && settings.Format != ExportFormat.RawBinary
                && settings.Format != ExportFormat.MicroPython
                && settings.Format != ExportFormat.LiquidCrystalChar
                && settings.Format != ExportFormat.FlipperCompressedBitmap
                && settings.Format != ExportFormat.FlipperCanvasIcon;

            var emitData = new List<byte[]>();
            int uncompressedFrameSize = frameData.FirstOrDefault()?.Length ?? 0;

            if (compressionActive)
            {
                int totalOriginal = 0, totalCompressed = 0;
                bool anyFrameFailedToCompress = false;
                foreach (var raw in frameData)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var compressed = _compression!.Compress(raw, settings.Compression);
                    if (ReferenceEquals(compressed, raw))
                        anyFrameFailedToCompress = true;
                    emitData.Add(compressed);
                    totalOriginal += raw.Length;
                    totalCompressed += compressed.Length;
                }
                // Fall back to raw for the whole export if compression didn't help overall,
                // or if any individual frame didn't shrink — a single shared decompressor
                // function is emitted per export, so a mix of compressed and raw frames
                // would be misdecoded on-device.
                if (anyFrameFailedToCompress || totalCompressed >= totalOriginal)
                {
                    compressionActive = false;
                    emitData = frameData;
                }
            }
            else
            {
                emitData = frameData;
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ── Build format output ──────────────────────────────────────────
            string coreOutput;
            bool useAnimationBuilder = isAnimation && !compressionActive && settings.AnimationLayout == AnimationExportLayout.ArrayOfFrames;

            if (useAnimationBuilder)
            {
                coreOutput = settings.Format switch
                {
                    ExportFormat.AdafruitGfx             => BuildAdafruitGfxAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.U8g2DrawBitmap          => BuildU8g2DrawBitmapAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.U8g2DrawXBM             => BuildU8g2DrawXBMAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.PlainCArray             => BuildPlainCArrayAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.MicroPython             => BuildMicroPythonAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.RawHex                  => BuildRawHexAnimation(emitData, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.RawBinary               => BuildRawBinaryAnimation(emitData, outputWidth, outputHeight, settings, frameDelays, cancellationToken),
                    ExportFormat.Indexed2D               => BuildIndexed2DAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.LiquidCrystalChar       => BuildLiquidCrystalAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.FlipperCompressedBitmap => BuildFlipperCompressedBitmapAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.FlipperXbm              => BuildFlipperXbmAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    ExportFormat.FlipperCanvasIcon       => BuildFlipperCanvasIconAnimation(emitData, name, outputWidth, outputHeight, settings, hexFmt, frameDelays, cancellationToken),
                    _                                    => string.Empty,
                };
            }
            else
            {
                byte[] data;
                if (compressionActive && isAnimation && settings.AnimationLayout == AnimationExportLayout.ArrayOfFrames)
                    data = [.. emitData.SelectMany(b => b)];
                else
                    data = emitData.FirstOrDefault() ?? [];

                coreOutput = settings.Format switch
                {
                    ExportFormat.AdafruitGfx             => BuildAdafruitGfx(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.U8g2DrawBitmap          => BuildU8g2DrawBitmap(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.U8g2DrawXBM             => BuildU8g2DrawXBM(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.PlainCArray             => BuildPlainCArray(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.MicroPython             => BuildMicroPython(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.RawHex                  => BuildRawHex(data, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.RawBinary               => BuildRawBinary(data, outputWidth, outputHeight, settings, cancellationToken),
                    ExportFormat.Indexed2D               => BuildIndexed2D(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.LiquidCrystalChar       => BuildLiquidCrystal(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.FlipperCompressedBitmap => BuildFlipperCompressedBitmap(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.FlipperXbm              => BuildFlipperXbm(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    ExportFormat.FlipperCanvasIcon       => BuildFlipperCanvasIcon(data, name, outputWidth, outputHeight, settings, hexFmt, cancellationToken),
                    _                                    => string.Empty,
                };

                // Append delays array for flattened sprite sheets, and for ArrayOfFrames when
                // compression is active (the uncompressed ArrayOfFrames case is handled by the
                // dedicated *Animation builders below via useAnimationBuilder instead).
                if (isAnimation && frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    var sb = new StringBuilder(coreOutput);
                    sb.AppendLine();
                    if (settings.Format == ExportFormat.MicroPython)
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_DELAYS = [");
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    {string.Join(", ", frameDelays.Select(d => settings.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                        sb.AppendLine("]");
                    }
                    else if (settings.Format != ExportFormat.RawHex && settings.Format != ExportFormat.RawBinary)
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => settings.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                        sb.AppendLine("};");
                    }
                    coreOutput = sb.ToString();
                }
            }

            if (compressionActive)
            {
                bool isArrayOfFrames = isAnimation && settings.AnimationLayout == AnimationExportLayout.ArrayOfFrames;
                coreOutput = WrapWithCompression(coreOutput, name, settings, uncompressedFrameSize, emitData, isArrayOfFrames);
            }

            if (settings.GenerateFullSketch)
            {
                return WrapWithSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, settings, compressionActive, uncompressedFrameSize, frameDelays);
            }

            return coreOutput;
        }

        public Task<string> GenerateCodeAsync(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool[,]? fpClone = floatingPixels != null
                ? (bool[,])floatingPixels.Clone()
                : null;

            // Clone frames and settings to avoid race conditions with the UI thread
            var framesClone   = frames.Select(f => (bool[])f.Clone()).ToList();
            ExportSettings settingsClone = settings.Clone();
            List<int>? delaysClone = frameDelays?.ToList();

            return Task.Run(() => GenerateCode(
                framesClone, width, height, settingsClone, isFloating, fpClone,
                floatX, floatY, floatW, floatH, pasteMode, delaysClone, cancellationToken), cancellationToken);
        }

        public string GenerateSketch(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var sketchSettings = settings.Clone();
            sketchSettings.GenerateFullSketch = true;
            sketchSettings.IncludeDimensionConstants = true;
            return GenerateCode(frames, width, height, sketchSettings, isFloating, floatingPixels, floatX, floatY, floatW, floatH, pasteMode, frameDelays, cancellationToken);
        }

        public Task<string> GenerateSketchAsync(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var sketchSettings = settings.Clone();
            sketchSettings.GenerateFullSketch = true;
            sketchSettings.IncludeDimensionConstants = true;
            return GenerateCodeAsync(frames, width, height, sketchSettings, isFloating, floatingPixels, floatX, floatY, floatW, floatH, pasteMode, frameDelays, cancellationToken);
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Format generators
        // ═══════════════════════════════════════════════════════════════════════

        // ── Adafruit GFX ─────────────────────────────────────────────────────

        private static string BuildAdafruitGfx(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// display.drawBitmap(x, y, {name}, {width}, {height}, SSD1306_WHITE);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? data.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name}[{arraySize}] = {{");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        // ── u8g2 drawBitmap ──────────────────────────────────────────────────

        private static string BuildU8g2DrawBitmap(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// u8g2.drawBitmap(x, y, {BytesPerRow(width)}, {height}, {name});");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? data.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t U8X8_PROGMEM {name}[{arraySize}] = {{");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        // ── u8g2 drawXBM ────────────────────────────────────────────────────

        private static string BuildU8g2DrawXBM(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// u8g2.drawXBMP(x, y, {width}, {height}, {name});");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? data.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name}[{arraySize}] = {{");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        // ── Plain C array ────────────────────────────────────────────────────

        private static string BuildPlainCArray(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Bitmap: {width}×{height} pixels (MSB first)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Draw: display.drawBitmap(x, y, {name}, {width}, {height}, 1);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? data.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name}[{arraySize}] = {{");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        // ── MicroPython bytearray ────────────────────────────────────────────

        private static string BuildMicroPython(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                int stride = ((width + 7) / 8) * 8;
                sb.AppendLine(CultureInfo.InvariantCulture, $"# fb = framebuf.FrameBuffer({name}, {width}, {height}, framebuf.MONO_HLSB, {stride})");
                sb.AppendLine("# oled.blit(fb, x, y)");
                sb.AppendLine("# oled.show()");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_WIDTH  = {width}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_HEIGHT = {height}");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"{name} = bytearray([");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "    ", cancellationToken);
            sb.Append("])");

            return sb.ToString();
        }

        // ── Raw Hex ──────────────────────────────────────────────────────────

        private static string BuildRawHex(
            byte[] data, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "", cancellationToken);
            return sb.ToString().TrimEnd('\r', '\n');
        }

        // ── Raw Binary ───────────────────────────────────────────────────────

        private static string BuildRawBinary(
            byte[] data, int width, int height, ExportSettings cfg, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            AppendByteBody(sb, data, width, cfg, b => Convert.ToString(b, 2).PadLeft(8, '0'), rowPrefix: "", cancellationToken);
            return sb.ToString().TrimEnd('\r', '\n');
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Animation Format generators (2D arrays)
        // ═══════════════════════════════════════════════════════════════════════

        private static string BuildAdafruitGfxAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0 || frames[0] == null) return string.Empty;
            int bytesPerFrame = frames[0].Length;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// for (int i = 0; i < {frames.Count}; i++) {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"//   display.drawBitmap(x, y, {name}[i], {width}, {height}, color);");
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay((1000 / {fps}) * {name.ToUpperInvariant()}_DELAYS[i]);");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay(1000 / {fps});");
                }
                sb.AppendLine($"// }}");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FPS = {fps};");
            }

            string arraySize = cfg.IncludeArraySize
                ? string.Create(CultureInfo.InvariantCulture, $"{frames.Count}][{bytesPerFrame}")
                : string.Create(CultureInfo.InvariantCulture, $"][{bytesPerFrame}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name}[{arraySize}] = {{");

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("  {");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "    ", cancellationToken);
                sb.Append("  }");
                if (i < frames.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine("};");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("};");
            }

            return sb.ToString();
        }

        private static string BuildU8g2DrawBitmapAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0 || frames[0] == null) return string.Empty;
            int bytesPerFrame = frames[0].Length;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// for (int i = 0; i < {frames.Count}; i++) {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"//   u8g2.drawBitmap(x, y, {BytesPerRow(width)}, {height}, {name}[i]);");
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay((1000 / {fps}) * {name.ToUpperInvariant()}_DELAYS[i]);");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay(1000 / {fps});");
                }
                sb.AppendLine(CultureInfo.InvariantCulture, $"// }}");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FPS = {fps};");
            }

            string arraySize = cfg.IncludeArraySize
                ? string.Create(CultureInfo.InvariantCulture, $"{frames.Count}][{bytesPerFrame}")
                : string.Create(CultureInfo.InvariantCulture, $"][{bytesPerFrame}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t U8X8_PROGMEM {name}[{arraySize}] = {{");

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("  {");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "    ", cancellationToken);
                sb.Append("  }");
                if (i < frames.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine("};");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t U8X8_PROGMEM {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("};");
            }

            return sb.ToString();
        }

        private static string BuildU8g2DrawXBMAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0 || frames[0] == null) return string.Empty;
            int bytesPerFrame = frames[0].Length;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// for (int i = 0; i < {frames.Count}; i++) {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"//   u8g2.drawXBMP(x, y, {width}, {height}, {name}[i]);");
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay((1000 / {fps}) * {name.ToUpperInvariant()}_DELAYS[i]);");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay(1000 / {fps});");
                }
                sb.AppendLine(CultureInfo.InvariantCulture, $"// }}");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FPS = {fps};");
            }

            string arraySize = cfg.IncludeArraySize
                ? string.Create(CultureInfo.InvariantCulture, $"{frames.Count}][{bytesPerFrame}")
                : string.Create(CultureInfo.InvariantCulture, $"][{bytesPerFrame}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name}[{arraySize}] = {{");

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("  {");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "    ", cancellationToken);
                sb.Append("  }");
                if (i < frames.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine("};");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t PROGMEM {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("};");
            }

            return sb.ToString();
        }

        private static string BuildPlainCArrayAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0 || frames[0] == null) return string.Empty;
            int bytesPerFrame = frames[0].Length;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// for (int i = 0; i < {frames.Count}; i++) {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"//   drawBitmap(x, y, {name}[i], {width}, {height});");
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay((1000 / {fps}) * {name.ToUpperInvariant()}_DELAYS[i]);");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"//   delay(1000 / {fps});");
                }
                sb.AppendLine($"// }}");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FPS = {fps};");
            }

            string arraySize = cfg.IncludeArraySize
                ? string.Create(CultureInfo.InvariantCulture, $"{frames.Count}][{bytesPerFrame}")
                : string.Create(CultureInfo.InvariantCulture, $"][{bytesPerFrame}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name}[{arraySize}] = {{");

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("  {");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "    ", cancellationToken);
                sb.Append("  }");
                if (i < frames.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine("};");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("};");
            }

            return sb.ToString();
        }

        private static string BuildMicroPythonAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"# Animation: {frames.Count} frames @ {fps} FPS");
                sb.AppendLine(CultureInfo.InvariantCulture, $"# for i in range({frames.Count}):");
                sb.AppendLine(CultureInfo.InvariantCulture, $"#     fb = framebuf.FrameBuffer({name}[i], {width}, {height}, framebuf.MONO_HLSB)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"#     display.blit(fb, x, y)");
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"#     time.sleep_ms((1000 // {fps}) * {name.ToUpperInvariant()}_DELAYS[i])");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"#     time.sleep_ms(1000 // {fps})");
                }
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_WIDTH  = {width}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_HEIGHT = {height}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_FRAMES = {frames.Count}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_FPS = {fps}");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"{name} = [");
            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("    bytearray([");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "        ", cancellationToken);
                sb.Append("    ])");
                if (i < frames.Count - 1)
                    sb.AppendLine(",");
                else
                    sb.AppendLine();
            }
            sb.AppendLine("]");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"{name.ToUpperInvariant()}_DELAYS = [");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("]");
            }

            return sb.ToString();
        }

        private static string BuildRawHexAnimation(
            List<byte[]> frames, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Frame {i + 1}:");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "", cancellationToken);
                if (i < frames.Count - 1)
                    sb.AppendLine();
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }

        private static string BuildRawBinaryAnimation(
            List<byte[]> frames, int width, int height, ExportSettings cfg, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"// Animation: {frames.Count} frames @ {fps} FPS");
            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Frame {i + 1}:");
                AppendByteBody(sb, frames[i], width, cfg, b => Convert.ToString(b, 2).PadLeft(8, '0'), rowPrefix: "", cancellationToken);
                if (i < frames.Count - 1)
                    sb.AppendLine();
            }
            return sb.ToString().TrimEnd('\r', '\n');
        }


        private static string BuildIndexed2D(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// {name}: {height}×{width} 2D pixel array (1 byte per pixel)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Access pixel at (x, y): uint8_t pixel = {name}[y][x];");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name}[{height}][{width}] = {{");
            int bpr = BytesPerRow(width);
            for (int r = 0; r < height; r++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.Append("  {");
                for (int c = 0; c < width; c++)
                {
                    int byteIdx = r * bpr + (c / 8);
                    int bitIdx = 7 - (c % 8);
                    bool on = byteIdx < data.Length && ((data[byteIdx] >> bitIdx) & 1) == 1;
                    sb.Append(on ? "1" : "0");
                    if (c < width - 1) sb.Append(", ");
                }
                sb.Append('}');
                if (r < height - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append("};");
            return sb.ToString();
        }

        private static string BuildIndexed2DAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt,
            List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// {name}: {frames.Count} frames @ {fps} FPS of {height}×{width} 2D pixel arrays");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Access frame pixel at (x, y): uint8_t pixel = {name}[frame][y][x];");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FPS = {fps};");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name}[{frames.Count}][{height}][{width}] = {{");
            int bpr = BytesPerRow(width);
            for (int f = 0; f < frames.Count; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sb.AppendLine("  {");
                byte[] data = frames[f];
                for (int r = 0; r < height; r++)
                {
                    sb.Append("    {");
                    for (int c = 0; c < width; c++)
                    {
                        int byteIdx = r * bpr + (c / 8);
                        int bitIdx = 7 - (c % 8);
                        bool on = byteIdx < data.Length && ((data[byteIdx] >> bitIdx) & 1) == 1;
                        sb.Append(on ? "1" : "0");
                        if (c < width - 1) sb.Append(", ");
                    }
                    sb.Append('}');
                    if (r < height - 1) sb.Append(',');
                    sb.AppendLine();
                }
                sb.Append("  }");
                if (f < frames.Count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.Append("};");

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_DELAYS[{frameDelays.Count}] = {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", frameDelays.Select(d => cfg.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]))}");
                sb.AppendLine("};");
            }

            return sb.ToString();
        }

        // ── Flipper Zero Generators ──────────────────────────────────────────

        private static byte[] PrepareFlipperCompressedPayload(byte[] uncompressed)
        {
            byte[] compressed = HeatshrinkCompressor.Compress(uncompressed);
            if (compressed.Length + 4 < uncompressed.Length)
            {
                byte[] finalBuffer = new byte[compressed.Length + 4];
                finalBuffer[0] = 0x01; // Heatshrink magic
                finalBuffer[1] = 0x00;
                byte[] lenBytes = BitConverter.GetBytes((ushort)compressed.Length);
                finalBuffer[2] = lenBytes[0];
                finalBuffer[3] = lenBytes[1];
                Array.Copy(compressed, 0, finalBuffer, 4, compressed.Length);
                return finalBuffer;
            }
            byte[] rawWithHeader = new byte[uncompressed.Length + 1];
            rawWithHeader[0] = 0x00;
            Array.Copy(uncompressed, 0, rawWithHeader, 1, uncompressed.Length);
            return rawWithHeader;
        }

        private static string BuildFlipperCompressedBitmap(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            byte[] payload = PrepareFlipperCompressedPayload(data);

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Flipper Zero Canvas API: canvas_draw_bitmap(canvas, x, y, {width}, {height}, {name}_compressed);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? payload.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t {name}_compressed[{arraySize}] = {{");
            AppendByteBody(sb, payload, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        private static string BuildFlipperCompressedBitmapAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Flipper Zero Canvas API: canvas_draw_bitmap(canvas, x, y, {width}, {height}, {name}_frames[frame_index]);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t  {name.ToUpperInvariant()}_FPS = {fps};");
            }

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] payload = PrepareFlipperCompressedPayload(frames[i]);
                string frameSize = cfg.IncludeArraySize ? payload.Length.ToString(CultureInfo.InvariantCulture) : "";
                sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t {name}_frame_{i}[{frameSize}] = {{");
                AppendByteBody(sb, payload, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
                sb.AppendLine("};");
            }

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t* const {name}_frames[{frames.Count}] = {{");
            for (int i = 0; i < frames.Count; i++)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}_frame_{i},");
            }
            sb.Append("};");

            return sb.ToString();
        }

        private static string BuildFlipperXbm(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Flipper Zero Canvas API: canvas_draw_xbm(canvas, x, y, {width}, {height}, {name}_xbm);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            string arraySize = cfg.IncludeArraySize ? data.Length.ToString(CultureInfo.InvariantCulture) : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t {name}_xbm[{arraySize}] = {{");
            AppendByteBody(sb, data, width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
            sb.Append("};");

            return sb.ToString();
        }

        private static string BuildFlipperXbmAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Flipper Zero Canvas API: canvas_draw_xbm(canvas, x, y, {width}, {height}, {name}_xbm_frames[frame_index]);");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t  {name.ToUpperInvariant()}_FPS = {fps};");
            }

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string frameSize = cfg.IncludeArraySize ? frames[i].Length.ToString(CultureInfo.InvariantCulture) : "";
                sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t {name}_xbm_frame_{i}[{frameSize}] = {{");
                AppendByteBody(sb, frames[i], width, cfg, b => cfg.UppercaseHex ? HexUpper[b] : HexLower[b], rowPrefix: "  ", cancellationToken);
                sb.AppendLine("};");
            }

            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"static const uint8_t* const {name}_xbm_frames[{frames.Count}] = {{");
            for (int i = 0; i < frames.Count; i++)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {name}_xbm_frame_{i},");
            }
            sb.Append("};");

            return sb.ToString();
        }

        private static string BuildFlipperCanvasIcon(
            byte[] data, string name, int width, int height, ExportSettings cfg, string hexFmt, System.Threading.CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 1. Save icon as: icons/I_{name}_{width}x{height}.png");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 2. In application.fam: fap_icon_assets=\"icons\"");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 3. In C source: #include \"{name}_icons.h\"");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 4. In ViewPort callback: canvas_draw_icon(canvas, x, y, &I_{name}_{width}x{height});");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"// Icon asset declaration for {name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"extern const Icon I_{name}_{width}x{height};");

            return sb.ToString();
        }

        private static string BuildFlipperCanvasIconAnimation(
            List<byte[]> frames, string name, int width, int height, ExportSettings cfg, string hexFmt, List<int>? frameDelays, System.Threading.CancellationToken cancellationToken = default)
        {
            if (frames == null || frames.Count == 0) return string.Empty;
            int fps = Math.Max(1, cfg.FrameRateFps);
            var sb = new StringBuilder();

            if (cfg.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 1. Save animated icon frames in: icons/A_{name}_{width}x{height}/");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 2. In application.fam: fap_icon_assets=\"icons\"");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 3. In C source: #include \"{name}_icons.h\"");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// 4. In ViewPort callback: canvas_draw_icon_animation(canvas, x, y, &A_{name}_{width}x{height});");
            }

            if (cfg.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {name.ToUpperInvariant()}_FRAMES = {frames.Count};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t  {name.ToUpperInvariant()}_FPS = {fps};");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"// Animated Icon asset declaration for {name}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"extern const IconAnimation A_{name}_{width}x{height};");

            return sb.ToString();
        }


        // ═══════════════════════════════════════════════════════════════════════
        //  Import / Parse
        // ═══════════════════════════════════════════════════════════════════════

        private static List<byte> ExtractDataBytes(string code)
        {
            string cleanText = LineCommentRegex.Replace(code, " ");
            cleanText = BlockCommentRegex.Replace(cleanText, " ");

            string body = ImportFromCodeDetector.ExtractArrayBody(cleanText, out bool isBraced);

            var bytes = new List<byte>();
            var matches = ByteTokenRegex.Matches(body);
            foreach (Match m in matches)
            {
                string val = m.Value;
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
                {
                    string hexOnly = val[2..];
                    if (hexOnly.Length > 2 && ushort.TryParse(hexOnly, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort val16))
                    {
                        bytes.Add((byte)(val16 >> 8));
                        bytes.Add((byte)(val16 & 0xFF));
                    }
                    else if (byte.TryParse(hexOnly, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte val8))
                    {
                        bytes.Add(val8);
                    }
                }
                else if (val.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        bytes.Add(Convert.ToByte(val[2..], 2));
                    }
                    catch { }
                }
                else if (val.StartsWith("B", StringComparison.OrdinalIgnoreCase) && val.Length >= 6 && val.Length <= 9)
                {
                    try
                    {
                        bytes.Add(Convert.ToByte(val[1..], 2));
                    }
                    catch { }
                }
                else if (val.Length == 8 && val.All(c => c == '0' || c == '1'))
                {
                    try
                    {
                        bytes.Add(Convert.ToByte(val, 2));
                    }
                    catch { }
                }
                else if (isBraced && byte.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte decVal))
                {
                    bytes.Add(decVal);
                }
            }
            return bytes;
        }

        public void ParseAdafruitGfxToState(string code, SpriteState state)
        {
            if (state.Width <= 0 || state.Height <= 0) return;

            var bytes = ExtractDataBytes(code);
            int bytesPerRow = BytesPerRow(state.Width);
            int bytesPerFrame = state.Height * bytesPerRow;

            if (bytesPerFrame <= 0) return;

            int frameCount = Math.Clamp((bytes.Count + bytesPerFrame - 1) / bytesPerFrame, 1, 512);
            if (frameCount == 1)
            {
                Array.Clear(state.Pixels, 0, state.Pixels.Length);
                int matchIndex = 0;
                for (int row = 0; row < state.Height; row++)
                {
                    for (int chunk = 0; chunk < bytesPerRow; chunk++)
                    {
                        if (matchIndex >= bytes.Count) return;
                        byte b = bytes[matchIndex++];
                        for (int bit = 7; bit >= 0; bit--)
                        {
                            int col = (chunk * 8) + (7 - bit);
                            if (col < state.Width)
                                state.Pixels[(row * state.Width) + col] = ((b >> bit) & 1) == 1;
                        }
                    }
                }
                return;
            }

            state.Frames.Clear();
            int matchIdx = 0;

            for (int f = 0; f < frameCount; f++)
            {
                var framePixels = new bool[state.Width * state.Height];

                for (int row = 0; row < state.Height; row++)
                {
                    for (int chunk = 0; chunk < bytesPerRow; chunk++)
                    {
                        if (matchIdx >= bytes.Count) break;

                        byte b = bytes[matchIdx++];

                        for (int bit = 7; bit >= 0; bit--)
                        {
                            int col = (chunk * 8) + (7 - bit);
                            if (col < state.Width)
                                framePixels[(row * state.Width) + col] = ((b >> bit) & 1) == 1;
                        }
                    }
                }

                state.Frames.Add(new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {f + 1}"),
                    LayerPixels = [new MonochromePixelBuffer(framePixels)],
                });
            }

            if (state.Frames.Count == 0)
            {
                state.Frames.Add(new FrameState
                {
                    Name = "Frame 1",
                    LayerPixels = [new MonochromePixelBuffer(new bool[state.Width * state.Height])],
                });
            }

            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            state.Pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        }

        public void ParseHexToState(string hexText, SpriteState state)
        {
            ParseAdafruitGfxToState(hexText, state);
        }

        public void ParseXbmToState(string code, SpriteState state)
        {
            if (state.Width <= 0 || state.Height <= 0) return;

            var bytes = ExtractDataBytes(code);
            int bytesPerRow = BytesPerRow(state.Width);
            int bytesPerFrame = state.Height * bytesPerRow;

            if (bytesPerFrame <= 0) return;

            int frameCount = Math.Clamp((bytes.Count + bytesPerFrame - 1) / bytesPerFrame, 1, 512);
            if (frameCount == 1)
            {
                Array.Clear(state.Pixels, 0, state.Pixels.Length);
                int matchIndex = 0;
                for (int row = 0; row < state.Height; row++)
                {
                    for (int chunk = 0; chunk < bytesPerRow; chunk++)
                    {
                        if (matchIndex >= bytes.Count) return;
                        byte b = bytes[matchIndex++];
                        for (int bit = 0; bit < 8; bit++)
                        {
                            int col = (chunk * 8) + bit;
                            if (col < state.Width)
                                state.Pixels[(row * state.Width) + col] = ((b >> bit) & 1) == 1;
                        }
                    }
                }
                return;
            }

            state.Frames.Clear();
            int matchIdx = 0;

            for (int f = 0; f < frameCount; f++)
            {
                var framePixels = new bool[state.Width * state.Height];

                for (int row = 0; row < state.Height; row++)
                {
                    for (int chunk = 0; chunk < bytesPerRow; chunk++)
                    {
                        if (matchIdx >= bytes.Count) break;

                        byte b = bytes[matchIdx++];

                        // XBM is LSB-first: bit 0 maps to the leftmost pixel
                        for (int bit = 0; bit < 8; bit++)
                        {
                            int col = (chunk * 8) + bit;
                            if (col < state.Width)
                                framePixels[(row * state.Width) + col] = ((b >> bit) & 1) == 1;
                        }
                    }
                }

                state.Frames.Add(new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {f + 1}"),
                    LayerPixels = [new MonochromePixelBuffer(framePixels)],
                });
            }

            if (state.Frames.Count == 0)
            {
                state.Frames.Add(new FrameState
                {
                    Name = "Frame 1",
                    LayerPixels = [new MonochromePixelBuffer(new bool[state.Width * state.Height])],
                });
            }

            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            state.Pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        }

        public void ParseBinaryToState(string code, SpriteState state)
        {
            string cleanCode = LineCommentRegex.Replace(code, " ");
            cleanCode = BlockCommentRegex.Replace(cleanCode, " ");
            bool hasPrefixedTokens = PrefixedTokenRegex.IsMatch(cleanCode);
            bool hasBraces = cleanCode.Contains('{', StringComparison.Ordinal) || cleanCode.Contains('[', StringComparison.Ordinal);

            // If byte tokens (0b..., 0x..., B..., or braced array) are present,
            // unpack them into rows MSB-first according to bytesPerRow.
            if (hasPrefixedTokens || hasBraces)
            {
                var bytes = ExtractDataBytes(code);
                if (bytes.Count > 0)
                {
                    ParseAdafruitGfxToState(code, state);
                    return;
                }
            }

            // Otherwise, parse as raw ASCII bit grid (lines of 0s and 1s)
            string cleanText = LineCommentRegex.Replace(code, "");
            cleanText = BlockCommentRegex.Replace(cleanText, "");

            // Remove optional row prefixes like "0: " or "1: "
            cleanText = BinaryRowPrefixRegex.Replace(cleanText, "");

            string[] lines = cleanText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var validRows = new List<string>();
            foreach (var line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && (trimmed.Contains('0', StringComparison.Ordinal) || trimmed.Contains('1', StringComparison.Ordinal)))
                {
                    validRows.Add(trimmed);
                }
            }

            if (state.Width <= 0 || state.Height <= 0) return;

            int frameCount = Math.Clamp((validRows.Count + state.Height - 1) / state.Height, 1, 512);
            if (frameCount == 1)
            {
                Array.Clear(state.Pixels, 0, state.Pixels.Length);
                int row = 0;
                foreach (string line in validRows)
                {
                    if (row >= state.Height) break;
                    int col = 0;
                    foreach (char c in line)
                    {
                        if (c == '0' || c == '1')
                        {
                            if (col < state.Width)
                            {
                                state.Pixels[(row * state.Width) + col] = (c == '1');
                            }
                            col++;
                        }
                    }
                    if (col > 0) row++;
                }
                return;
            }

            state.Frames.Clear();
            int rowIndex = 0;

            for (int f = 0; f < frameCount; f++)
            {
                var framePixels = new bool[state.Width * state.Height];

                for (int row = 0; row < state.Height; row++)
                {
                    if (rowIndex >= validRows.Count) break;

                    string line = validRows[rowIndex++];
                    int col = 0;
                    foreach (char c in line)
                    {
                        if (c == '0' || c == '1')
                        {
                            if (col < state.Width)
                            {
                                framePixels[(row * state.Width) + col] = (c == '1');
                            }
                            col++;
                        }
                    }
                }

                state.Frames.Add(new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {f + 1}"),
                    LayerPixels = [new MonochromePixelBuffer(framePixels)],
                });
            }

            if (state.Frames.Count == 0)
            {
                state.Frames.Add(new FrameState
                {
                    Name = "Frame 1",
                    LayerPixels = [new MonochromePixelBuffer(new bool[state.Width * state.Height])],
                });
            }

            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            state.Pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        }

        public void ParseIndexed2DToState(string code, SpriteState state)
        {
            if (state.Width <= 0 || state.Height <= 0) return;

            var bytes = ExtractDataBytes(code);
            int bytesPerFrame = state.Width * state.Height;

            if (bytesPerFrame <= 0) return;

            int frameCount = Math.Clamp((bytes.Count + bytesPerFrame - 1) / bytesPerFrame, 1, 512);
            if (frameCount == 1)
            {
                Array.Clear(state.Pixels, 0, state.Pixels.Length);
                int idx = 0;
                for (int row = 0; row < state.Height; row++)
                {
                    for (int col = 0; col < state.Width; col++)
                    {
                        if (idx >= bytes.Count) return;
                        state.Pixels[(row * state.Width) + col] = bytes[idx++] > 0;
                    }
                }
                return;
            }

            state.Frames.Clear();
            int matchIdx = 0;

            for (int f = 0; f < frameCount; f++)
            {
                var framePixels = new bool[state.Width * state.Height];

                for (int row = 0; row < state.Height; row++)
                {
                    for (int col = 0; col < state.Width; col++)
                    {
                        if (matchIdx >= bytes.Count) break;
                        framePixels[(row * state.Width) + col] = bytes[matchIdx++] > 0;
                    }
                }

                state.Frames.Add(new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Frame {f + 1}"),
                    LayerPixels = [new MonochromePixelBuffer(framePixels)],
                });
            }

            if (state.Frames.Count == 0)
            {
                state.Frames.Add(new FrameState
                {
                    Name = "Frame 1",
                    LayerPixels = [new MonochromePixelBuffer(new bool[state.Width * state.Height])],
                });
            }

            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            state.Pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        }

        public void ParseLiquidCrystalToState(string code, SpriteState state)
        {
            var bytes = ExtractDataBytes(code);
            if (bytes.Count == 0) return;

            int w = state.Width > 0 ? state.Width : 5;
            int h = state.Height > 0 ? state.Height : 8;

            int bytesPerFrame = 8;
            int frameCount = Math.Clamp((bytes.Count + bytesPerFrame - 1) / bytesPerFrame, 1, 8);

            if (frameCount == 1)
            {
                Array.Clear(state.Pixels, 0, state.Pixels.Length);
                for (int row = 0; row < Math.Min(h, bytes.Count); row++)
                {
                    byte b = bytes[row];
                    for (int bit = 0; bit < Math.Min(w, 5); bit++)
                    {
                        bool isSet = ((b >> (4 - bit)) & 1) == 1;
                        state.Pixels[row * w + bit] = isSet;
                    }
                }
                return;
            }

            state.Frames.Clear();
            int byteIndex = 0;

            for (int f = 0; f < frameCount; f++)
            {
                var framePixels = new bool[w * h];
                for (int row = 0; row < h; row++)
                {
                    if (byteIndex >= bytes.Count) break;
                    byte b = bytes[byteIndex++];
                    for (int bit = 0; bit < Math.Min(w, 5); bit++)
                    {
                        bool isSet = ((b >> (4 - bit)) & 1) == 1;
                        framePixels[row * w + bit] = isSet;
                    }
                }

                state.Frames.Add(new FrameState
                {
                    Name = string.Create(CultureInfo.InvariantCulture, $"Char {f}"),
                    LayerPixels = [new MonochromePixelBuffer(framePixels)],
                });
            }

            if (state.Frames.Count == 0)
            {
                state.Frames.Add(new FrameState
                {
                    Name = "Char 0",
                    LayerPixels = [new MonochromePixelBuffer(new bool[w * h])],
                });
            }

            state.ActiveFrameIndex = 0;
            state.ActiveLayerIndex = 0;
            state.Pixels = state.Frames[0].LayerPixels[0].GetMonochromeData();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Compression wrapper
        // ═══════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Wraps the core format output with a compression header, size constants,
        /// and the auto-generated C decompressor function.
        /// </summary>
        private string WrapWithCompression(
            string coreOutput, string name, ExportSettings cfg,
            int uncompressedFrameSize, List<byte[]> compressedFrames, bool isAnimation)
        {
            var sb = new StringBuilder();
            string NAME = name.ToUpperInvariant();
            int compSize = isAnimation
                ? compressedFrames.Sum(f => f.Length)
                : (compressedFrames.FirstOrDefault()?.Length ?? 0);
            int origSize = isAnimation
                ? uncompressedFrameSize * compressedFrames.Count
                : uncompressedFrameSize;
            int pct = origSize > 0
                ? (int)Math.Round(100.0 * (origSize - compSize) / origSize, MidpointRounding.AwayFromZero)
                : 0;
            string algoName = cfg.Compression == CompressionMode.Rle ? "RLE" : "LZSS";
            string decodeFn = cfg.Compression == CompressionMode.Rle
                ? "hexprite_rle_decode" : "hexprite_lzss_decode";

            // Header comment
            sb.AppendLine(CultureInfo.InvariantCulture, $"// Compressed with {algoName} — {origSize} bytes → {compSize} bytes ({pct}% savings)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"// Decompress into a {uncompressedFrameSize}-byte buffer, then draw as usual:");
            sb.AppendLine($"//");
            sb.AppendLine(CultureInfo.InvariantCulture, $"//   uint8_t buf[{NAME}_UNCOMPRESSED_SIZE];");
            sb.AppendLine(CultureInfo.InvariantCulture, $"//   {decodeFn}({name}, sizeof({name}), buf, sizeof(buf));");
            sb.AppendLine();

            // Core output (the array + dimension constants)
            sb.Append(coreOutput);
            sb.AppendLine();

            // Size constants
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {NAME}_COMPRESSED_SIZE   = {compSize};");
            sb.AppendLine(CultureInfo.InvariantCulture, $"const uint16_t {NAME}_UNCOMPRESSED_SIZE = {uncompressedFrameSize};");

            if (isAnimation)
            {
                sb.Append(CultureInfo.InvariantCulture, $"const uint16_t {NAME}_FRAME_SIZES[] = {{ ");
                sb.AppendJoin(", ", compressedFrames.Select(f => f.Length));
                sb.AppendLine(" };");

                sb.Append(CultureInfo.InvariantCulture, $"const uint16_t {NAME}_FRAME_OFFSETS[] = {{ ");
                int offset = 0;
                var offsets = new List<int>();
                foreach (var f in compressedFrames)
                {
                    offsets.Add(offset);
                    offset += f.Length;
                }
                sb.AppendJoin(", ", offsets);
                sb.AppendLine(" };");
            }

            // Decompressor function
            sb.AppendLine();
            sb.Append(_compression!.GenerateDecompressorCode(cfg.Compression));

            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  Helpers
        // ═══════════════════════════════════════════════════════════════════════

        // ── Byte extraction ───────────────────────────────────────────────────

        /// <summary>
        /// Builds the full byte array for the sprite.
        /// If <paramref name="lsbFirst"/> is true (XBM), each byte has its bits
        /// reversed so that the leftmost pixel maps to bit 0.
        /// Partial bytes (canvas width not a multiple of 8) are zero-padded.
        /// </summary>
        public static byte[] BuildByteArray(
            bool[] pixels, int width, int height, bool lsbFirst,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode,
            System.Threading.CancellationToken cancellationToken = default)
        {
            int bytesPerRow = BytesPerRow(width);
            byte[] result = new byte[height * bytesPerRow];

            System.Diagnostics.Debug.Assert(
                !isFloating || floatingPixels == null ||
                (floatingPixels.GetLength(0) == floatW && floatingPixels.GetLength(1) == floatH),
                $"floatingPixels must be [floatW={floatW}, floatH={floatH}] but is [{floatingPixels?.GetLength(0)}, {floatingPixels?.GetLength(1)}]");

            for (int row = 0; row < height; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (int chunk = 0; chunk < bytesPerRow; chunk++)
                {
                    byte b = SampleByte(pixels, width, height, row, chunk,
                                        isFloating, floatingPixels,
                                        floatX, floatY, floatW, floatH,
                                        lsbFirst, pasteMode);
                    result[(row * bytesPerRow) + chunk] = b;
                }
            }

            return result;
        }

        public static byte[] BuildLiquidCrystalByteArray(bool[] pixels, int width, int height)
        {
            byte[] result = new byte[8];
            for (int y = 0; y < 8; y++)
            {
                if (y >= height) break;
                byte b = 0;
                for (int x = 0; x < Math.Min(5, width); x++)
                {
                    if (pixels[y * width + x])
                    {
                        b |= (byte)(1 << (4 - x));
                    }
                }
                result[y] = b;
            }
            return result;
        }

        private static byte SampleByte(
            bool[] pixels, int width, int height, int row, int chunk,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            bool lsbFirst, FloatingPasteMode pasteMode)
        {
            byte b = 0;

            for (int bit = 0; bit < 8; bit++)
            {
                int col = (chunk * 8) + bit;
                bool isOn = col < width && pixels[(row * width) + col];

                // Overlay floating selection pixels
                if (isFloating && floatingPixels != null && col < width)
                {
                    int lx = col - floatX;
                    int ly = row - floatY;
                    if (lx >= 0 && lx < floatW && ly >= 0 && ly < floatH)
                    {
                        bool floatingPixel = floatingPixels[lx, ly];
                        // FloatingPasteMode determines how false pixels are handled
                        if (pasteMode == FloatingPasteMode.Transparent)
                        {
                            // Transparent: only true pixels show (false = skip/transparent)
                            if (floatingPixel)
                                isOn = true;
                        }
                        else // Opaque
                        {
                            // Opaque: all pixels overwrite canvas (full stamp)
                            isOn = floatingPixel;
                        }
                    }
                }

                if (isOn)
                {
                    // MSB first: pixel at 'bit=0' is the most significant bit
                    // LSB first (XBM): pixel at 'bit=0' maps to bit 0
                    int shift = lsbFirst ? bit : (7 - bit);
                    b |= (byte)(1 << shift);
                }
            }

            return b;
        }

        // ── Body emitter ──────────────────────────────────────────────────────

        /// <summary>
        /// Writes the data bytes into <paramref name="sb"/>, respecting
        /// bytes-per-line chunking, row comments, and separator style.
        /// Each byte falls into exactly one of three cases:
        ///   – middle of a line: append byte + separator
        ///   – last byte on a line (more bytes follow): append byte + optional trailing comma + newline
        ///   – very last byte: append byte + newline (no trailing comma)
        /// This ensures the separator is emitted exactly once per gap.
        /// </summary>
        private static void AppendByteBody(
            StringBuilder sb, byte[] data, int width,
            ExportSettings cfg, Func<byte, string> formatter, string rowPrefix,
            System.Threading.CancellationToken cancellationToken = default)
        {
            int bytesPerRow = BytesPerRow(width);
            bool useComma = cfg.UseCommaSeparator || 
                            !(cfg.Format == ExportFormat.RawHex || cfg.Format == ExportFormat.RawBinary);
            string sep     = useComma ? ", " : " ";
            int lineLen    = cfg.BytesPerLine <= 0 ? bytesPerRow : cfg.BytesPerLine;
            int totalBytes = data.Length;

            for (int i = 0; i < totalBytes; i++)
            {
                if ((i & 0x7F) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                int visualPos  = cfg.BytesPerLine <= 0 ? (i % bytesPerRow) : (i % cfg.BytesPerLine);
                int rowIndex   = i / bytesPerRow;     // which canvas row we're in
                
                bool isFirstOnLine = visualPos == 0;
                bool isLastByte    = i == totalBytes - 1;
                bool isLastOnLine  = isLastByte || (visualPos + 1 == lineLen);

                if (isFirstOnLine)
                    sb.Append(rowPrefix);

                sb.Append(formatter(data[i]));

                string commentPrefix = cfg.Format == ExportFormat.MicroPython ? "#" : "//";
                if (isLastByte)
                {
                    bool isEndOfCanvasRow = (i % bytesPerRow) == (bytesPerRow - 1);
                    bool emitComment = cfg.IncludeRowComments && (cfg.BytesPerLine <= 0 || isEndOfCanvasRow);
                    if (emitComment)
                        sb.Append(CultureInfo.InvariantCulture, $"  {commentPrefix} row {rowIndex}");
                    sb.AppendLine();
                }
                else if (isLastOnLine)
                {
                    bool isEndOfCanvasRow = (i % bytesPerRow) == (bytesPerRow - 1);
                    bool emitComment = cfg.IncludeRowComments && (cfg.BytesPerLine <= 0 || isEndOfCanvasRow);
                    string trail = useComma ? "," : "";
                    if (emitComment)
                        sb.Append(CultureInfo.InvariantCulture, $"{trail}  {commentPrefix} row {rowIndex}");
                    else if (trail.Length > 0)
                        sb.Append(trail);
                    sb.AppendLine();
                }
                else
                {
                    // Middle of a line — just append the separator
                    sb.Append(sep);
                }
            }
        }

        /// <summary>
        /// Flattens multi-frame animation data into a single sprite-sheet frame for
        /// <see cref="AnimationExportLayout.VerticalSpriteSheet"/>/<see cref="AnimationExportLayout.HorizontalSpriteSheet"/>
        /// layouts, returning the (possibly single-element) frame list plus the resulting
        /// output dimensions. Returns <paramref name="frames"/> unchanged for <see cref="AnimationExportLayout.ArrayOfFrames"/>.
        /// Shared by <see cref="GenerateCode"/> and <see cref="CalculateByteCount(System.Collections.Generic.List{bool[]}, int, int, ExportSettings, ICompressionService?)"/>
        /// so their notion of "output dimensions" never diverges.
        /// </summary>
        private static (List<bool[]> Frames, int OutputWidth, int OutputHeight) FlattenAnimationFrames(
            List<bool[]> frames, int width, int height, AnimationExportLayout layout, int numFrames,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (layout == AnimationExportLayout.ArrayOfFrames)
                return (frames, width, height);

            int outputWidth = width;
            int outputHeight = height;
            bool[] flattened;

            if (layout == AnimationExportLayout.VerticalSpriteSheet)
            {
                outputHeight = height * numFrames;
                flattened = new bool[outputWidth * outputHeight];
                for (int i = 0; i < numFrames; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Array.Copy(frames[i], 0, flattened, i * (width * height), width * height);
                }
            }
            else // HorizontalSpriteSheet
            {
                outputWidth = width * numFrames;
                flattened = new bool[outputWidth * outputHeight];
                for (int i = 0; i < numFrames; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            flattened[y * outputWidth + (i * width + x)] = frames[i][y * width + x];
                        }
                    }
                }
            }

            return (new List<bool[]> { flattened }, outputWidth, outputHeight);
        }

        /// <summary>
        /// Instance overload implementing ICodeGeneratorService.
        /// </summary>
        public int CalculateByteCount(
            List<bool[]> frames, int width, int height,
            ExportSettings settings)
        {
            return CalculateByteCount(frames, width, height, settings, _compression);
        }

        /// <summary>
        /// Computes the exact output byte count directly from frame dimensions and export settings
        /// without requiring Regex scanning over output strings.
        /// </summary>
        public static int CalculateByteCount(
            List<bool[]> frames, int width, int height,
            ExportSettings settings, ICompressionService? compression)

        {
            ArgumentNullException.ThrowIfNull(frames);
            if (frames == null || frames.Count == 0 || width <= 0 || height <= 0)
                return 0;
            ArgumentNullException.ThrowIfNull(settings);
            bool isAnimation = settings.ExportAsAnimation && frames.Count > 1;
            int outputWidth = width;
            int outputHeight = height;
            int numFrames = frames.Count;

            if (isAnimation)
            {
                (frames, outputWidth, outputHeight) = FlattenAnimationFrames(
                    frames, width, height, settings.AnimationLayout, numFrames);
            }

            int uncompressedFrameSize;
            int totalUncompressed;

            if (settings.Format == ExportFormat.LiquidCrystalChar)
            {
                int charCount = isAnimation ? Math.Min(numFrames, 8) : 1;
                return charCount * 8;
            }

            if (isAnimation && settings.AnimationLayout != AnimationExportLayout.ArrayOfFrames)
            {
                uncompressedFrameSize = outputHeight * BytesPerRow(outputWidth);
                totalUncompressed = uncompressedFrameSize;
            }
            else
            {
                uncompressedFrameSize = height * BytesPerRow(width);
                totalUncompressed = uncompressedFrameSize * numFrames;
            }

            bool compressionActive = compression != null
                && settings.Compression != CompressionMode.None
                && settings.Format != ExportFormat.RawHex
                && settings.Format != ExportFormat.RawBinary
                && settings.Format != ExportFormat.MicroPython;

            if (!compressionActive)
            {
                return totalUncompressed;
            }

            int totalCompressed = 0;
            bool anyFrameFailedToCompress = false;
            foreach (var frame in frames)
            {
                byte[] data = BuildByteArray(frame, outputWidth, outputHeight, settings.Format == ExportFormat.U8g2DrawXBM,
isFloating: false, floatingPixels: null, 0, 0, 0, 0, FloatingPasteMode.Transparent);
                var compressed = compression!.Compress(data, settings.Compression);
                if (ReferenceEquals(compressed, data))
                    anyFrameFailedToCompress = true;
                totalCompressed += compressed.Length;
            }

            if (anyFrameFailedToCompress)
                return totalUncompressed;

            return totalCompressed < totalUncompressed ? totalCompressed : totalUncompressed;
        }

        // ── Name sanitiser ────────────────────────────────────────────────────

        /// <summary>C and Python keywords — a sanitised name matching one of these exactly would produce uncompilable/unrunnable generated code.</summary>
        private static readonly HashSet<string> ReservedIdentifiers = new(StringComparer.Ordinal)
        {
            // C / C++ keywords
            "auto", "break", "case", "char", "const", "continue", "default", "do", "double",
            "else", "enum", "extern", "float", "for", "goto", "if", "int", "long", "register",
            "return", "short", "signed", "sizeof", "static", "struct", "switch", "typedef",
            "union", "unsigned", "void", "volatile", "while", "class", "namespace", "template",
            "public", "private", "protected", "new", "delete", "this", "true", "false", "bool",
            // Arduino built-in objects, functions, and types
            "setup", "loop", "lcd", "display", "u8g2", "Serial", "Wire", "SPI", "byte", "word", "boolean",
            // Python keywords
            "and", "as", "assert", "async", "await", "def", "del", "elif", "except", "finally",
            "from", "global", "import", "in", "is", "lambda", "None", "nonlocal", "not", "or",
            "pass", "raise", "try", "yield", "True", "False",
        };

        /// <summary>
        /// Returns a valid C / Python identifier derived from <paramref name="name"/>.
        /// Strips invalid characters, prepends '_' if the result starts with a digit,
        /// appends '_' if the result is a reserved C/Python keyword,
        /// and falls back to "sprite" if the result is empty.
        /// </summary>
        public static string SanitiseName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "sprite";

            // Replace spaces and invalid characters with underscores
            string safe = InvalidIdentifierCharRegex.Replace(name.Trim(), "_");

            // C identifiers cannot start with a digit
            if (safe.Length > 0 && char.IsDigit(safe[0]))
                safe = "_" + safe;

            if (string.IsNullOrWhiteSpace(safe))
                return "sprite";

            // Output can be C or Python — avoid emitting a bare reserved word from either
            // language, which would otherwise produce uncompilable/unrunnable generated code.
            if (ReservedIdentifiers.Contains(safe))
                safe += "_";

            return safe;
        }

        // ── Misc ──────────────────────────────────────────────────────────────

        public static int BytesPerRow(int width) => (int)Math.Ceiling(width / 8.0);

        // ═══════════════════════════════════════════════════════════════════════
        //  Full Sketch Wrappers
        // ═══════════════════════════════════════════════════════════════════════

        private static string WrapWithSketch(
            string coreOutput,
            string name,
            int outputWidth,
            int outputHeight,
            int numFrames,
            bool isAnimation,
            ExportSettings cfg,
            bool compressionActive,
            int uncompressedFrameSize,
            List<int>? frameDelays)
        {
            return cfg.Format switch
            {
                ExportFormat.AdafruitGfx    => BuildAdafruitGfxSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, compressionActive, uncompressedFrameSize, frameDelays),
                ExportFormat.U8g2DrawBitmap => BuildU8g2Sketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, isXbm: false, compressionActive, uncompressedFrameSize, frameDelays),
                ExportFormat.U8g2DrawXBM    => BuildU8g2Sketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, isXbm: true, compressionActive, uncompressedFrameSize, frameDelays),
                ExportFormat.PlainCArray    => BuildPlainCArraySketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, compressionActive, uncompressedFrameSize, frameDelays),
                ExportFormat.MicroPython    => BuildMicroPythonSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, frameDelays),
                ExportFormat.RawHex                  => BuildRawDataSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, isHex: true),
                ExportFormat.RawBinary               => BuildRawDataSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, isHex: false),
                ExportFormat.Indexed2D               => BuildIndexed2DSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, frameDelays),
                ExportFormat.LiquidCrystalChar       => BuildLiquidCrystalSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, frameDelays),
                ExportFormat.FlipperCompressedBitmap or ExportFormat.FlipperXbm or ExportFormat.FlipperCanvasIcon
                    => BuildFlipperFapSketch(coreOutput, name, outputWidth, outputHeight, numFrames, isAnimation, cfg, frameDelays),
                _                                    => coreOutput,
            };
        }

        private static string BuildIndexed2DSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("//  Ready-to-use C / C++ Harness — Generated by Hexprite");
            sb.AppendLine("//  Format: Indexed 2D pixel array (1 byte per pixel)");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdio.h>");
            sb.AppendLine();
            sb.AppendLine("#if defined(ARDUINO)");
            sb.AppendLine("#include <Arduino.h>");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine("// ─── Sprite / Animation Data ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── Program Entry / Harness ─────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("#if defined(ARDUINO)");
            sb.AppendLine("void setup() {");
            sb.AppendLine("  Serial.begin(115200);");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Serial.println(F(\"Loaded 2D sprite: {name} ({width}x{height})\"));");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void loop() {");
            sb.AppendLine("  delay(1000);");
            sb.AppendLine("}");
            sb.AppendLine("#else");
            sb.AppendLine("int main(void) {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  printf(\"Loaded 2D sprite: {name} (%u x %u)\\n\", (unsigned int){upperName}_WIDTH, (unsigned int){upperName}_HEIGHT);");
            sb.AppendLine("  return 0;");
            sb.AppendLine("}");
            sb.AppendLine("#endif");

            return sb.ToString();
        }

        private static string BuildAdafruitGfxSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, bool compressionActive, int uncompressedFrameSize, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();
            int fps = Math.Max(1, cfg.FrameRateFps);
            string decodeFn = cfg.Compression == CompressionMode.Rle ? "hexprite_rle_decode" : "hexprite_lzss_decode";
            int screenWidth = Math.Max(128, width);
            int screenHeight = (height <= 32 && width <= 128) ? 32 : Math.Max(64, height);

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("//  Ready-to-use Arduino Sketch — Generated by Hexprite");
            sb.AppendLine("//  Format: Adafruit GFX (C array)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"//  Hardware: SSD1306 {screenWidth}x{screenHeight} I2C OLED (or compatible)");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("#include <SPI.h>");
            sb.AppendLine("#include <Wire.h>");
            sb.AppendLine("#include <Adafruit_GFX.h>");
            sb.AppendLine("#include <Adafruit_SSD1306.h>");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"#define SCREEN_WIDTH {screenWidth} // OLED display width, in pixels");
            sb.AppendLine(CultureInfo.InvariantCulture, $"#define SCREEN_HEIGHT {screenHeight} // OLED display height, in pixels");
            sb.AppendLine("#define OLED_RESET    -1 // Reset pin # (or -1 if sharing Arduino reset pin)");
            sb.AppendLine("#define SCREEN_ADDRESS 0x3C // Standard I2C address (0x3C or 0x3D)");
            sb.AppendLine();
            sb.AppendLine("Adafruit_SSD1306 display(SCREEN_WIDTH, SCREEN_HEIGHT, &Wire, OLED_RESET);");
            sb.AppendLine();
            sb.AppendLine("// ─── Sprite / Animation Data ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── Arduino Setup & Main Loop ───────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("void setup() {");
            sb.AppendLine("  Serial.begin(115200);");
            sb.AppendLine();
            sb.AppendLine("  // Initialize OLED display with I2C address 0x3C");
            sb.AppendLine("  if (!display.begin(SSD1306_SWITCHCAPVCC, SCREEN_ADDRESS)) {");
            sb.AppendLine("    Serial.println(F(\"SSD1306 allocation failed. Check wiring.\"));");
            sb.AppendLine("    for (;;); // Don't proceed, loop forever");
            sb.AppendLine("  }");
            sb.AppendLine();
            sb.AppendLine("  display.clearDisplay();");
            sb.AppendLine("  display.display();");

            if (!isAnimation)
            {
                sb.AppendLine();
                sb.AppendLine("  // Center sprite on display");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t x = (int16_t)max(0, ((int)SCREEN_WIDTH - (int){upperName}_WIDTH) / 2);");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t y = (int16_t)max(0, ((int)SCREEN_HEIGHT - (int){upperName}_HEIGHT) / 2);");
                sb.AppendLine();
                if (compressionActive)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  uint8_t buffer[{upperName}_UNCOMPRESSED_SIZE];");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {decodeFn}({name}, sizeof({name}), buffer, sizeof(buffer));");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  display.drawBitmap(x, y, buffer, {upperName}_WIDTH, {upperName}_HEIGHT, SSD1306_WHITE);");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  display.drawBitmap(x, y, {name}, {upperName}_WIDTH, {upperName}_HEIGHT, SSD1306_WHITE);");
                }
                sb.AppendLine("  display.display();");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void loop() {");

            if (isAnimation)
            {
                sb.AppendLine("  // Center sprite on display");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t x = (int16_t)max(0, ((int)SCREEN_WIDTH - (int){upperName}_WIDTH) / 2);");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t y = (int16_t)max(0, ((int)SCREEN_HEIGHT - (int){upperName}_HEIGHT) / 2);");
                sb.AppendLine();

                if (compressionActive)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  uint8_t buffer[{upperName}_UNCOMPRESSED_SIZE];");
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  for (int i = 0; i < {numFrames}; i++) {{"));
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {decodeFn}(&{name}[{upperName}_FRAME_OFFSETS[i]], {upperName}_FRAME_SIZES[i], buffer, sizeof(buffer));");
                    sb.AppendLine("    display.clearDisplay();");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    display.drawBitmap(x, y, buffer, {upperName}_WIDTH, {upperName}_HEIGHT, SSD1306_WHITE);");
                    sb.AppendLine("    display.display();");
                    if (frameDelays != null && frameDelays.Exists(d => d != 1))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay((1000 / {upperName}_FPS) * {upperName}_DELAYS[i]);");
                    }
                    else
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay(1000 / {upperName}_FPS);");
                    }
                    sb.AppendLine("  }");
                }
                else if (cfg.AnimationLayout == AnimationExportLayout.ArrayOfFrames)
                {
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  for (int i = 0; i < {numFrames}; i++) {{"));
                    sb.AppendLine("    display.clearDisplay();");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    display.drawBitmap(x, y, {name}[i], {upperName}_WIDTH, {upperName}_HEIGHT, SSD1306_WHITE);");
                    sb.AppendLine("    display.display();");
                    if (frameDelays != null && frameDelays.Exists(d => d != 1))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay((1000 / {upperName}_FPS) * {upperName}_DELAYS[i]);");
                    }
                    else
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay(1000 / {upperName}_FPS);");
                    }
                    sb.AppendLine("  }");
                }
                else
                {
                    // Sprite sheet
                    sb.AppendLine("  display.clearDisplay();");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  display.drawBitmap(x, y, {name}, {upperName}_WIDTH, {upperName}_HEIGHT, SSD1306_WHITE);");
                    sb.AppendLine("  display.display();");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  delay(1000 / {upperName}_FPS);");
                }
            }
            else
            {
                sb.AppendLine("  // Static sprite rendered in setup() — idle loop");
                sb.AppendLine("  delay(1000);");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildU8g2Sketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, bool isXbm, bool compressionActive, int uncompressedFrameSize, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();
            string formatTitle = isXbm ? "u8g2 drawXBM (LSB first)" : "u8g2 drawBitmap (C array)";
            string decodeFn = cfg.Compression == CompressionMode.Rle ? "hexprite_rle_decode" : "hexprite_lzss_decode";
            int bytesPerRow = BytesPerRow(width);

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("//  Ready-to-use Arduino Sketch — Generated by Hexprite");
            sb.AppendLine(CultureInfo.InvariantCulture, $"//  Format: {formatTitle}");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("#include <Arduino.h>");
            sb.AppendLine("#include <U8g2lib.h>");
            sb.AppendLine("#include <Wire.h>");
            sb.AppendLine();
            sb.AppendLine("// Display constructor — edit to match your screen controller and connection");
            if (height <= 32 && width <= 128)
            {
                sb.AppendLine("U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
                sb.AppendLine("// U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
                sb.AppendLine("// U8G2_SH1106_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
            }
            else
            {
                sb.AppendLine("U8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
                sb.AppendLine("// U8G2_SH1106_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
                sb.AppendLine("// U8G2_SSD1306_128X32_UNIVISION_F_HW_I2C u8g2(U8G2_R0, /* reset=*/ U8X8_PIN_NONE);");
            }
            sb.AppendLine();
            sb.AppendLine("// ─── Sprite / Animation Data ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── Arduino Setup & Main Loop ───────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("void setup() {");
            sb.AppendLine("  u8g2.begin();");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void loop() {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t x = (int16_t)max(0, ((int)u8g2.getDisplayWidth() - (int){upperName}_WIDTH) / 2);");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  int16_t y = (int16_t)max(0, ((int)u8g2.getDisplayHeight() - (int){upperName}_HEIGHT) / 2);");
            sb.AppendLine();

            if (isAnimation)
            {
                if (compressionActive)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  uint8_t buffer[{upperName}_UNCOMPRESSED_SIZE];");
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  for (int i = 0; i < {numFrames}; i++) {{"));
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    {decodeFn}(&{name}[{upperName}_FRAME_OFFSETS[i]], {upperName}_FRAME_SIZES[i], buffer, sizeof(buffer));");
                    sb.AppendLine("    u8g2.clearBuffer();");
                    if (isXbm)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    u8g2.drawXBM(x, y, {upperName}_WIDTH, {upperName}_HEIGHT, buffer);");
                    else
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    u8g2.drawBitmap(x, y, {bytesPerRow}, {upperName}_HEIGHT, buffer);"));
                    sb.AppendLine("    u8g2.sendBuffer();");
                    if (frameDelays != null && frameDelays.Exists(d => d != 1))
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay((1000 / {upperName}_FPS) * {upperName}_DELAYS[i]);");
                    else
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay(1000 / {upperName}_FPS);");
                    sb.AppendLine("  }");
                }
                else if (cfg.AnimationLayout == AnimationExportLayout.ArrayOfFrames)
                {
                    sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  for (int i = 0; i < {numFrames}; i++) {{"));
                    sb.AppendLine("    u8g2.clearBuffer();");
                    if (isXbm)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    u8g2.drawXBMP(x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name}[i]);");
                    else
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    u8g2.drawBitmap(x, y, {bytesPerRow}, {upperName}_HEIGHT, {name}[i]);"));
                    sb.AppendLine("    u8g2.sendBuffer();");
                    if (frameDelays != null && frameDelays.Exists(d => d != 1))
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay((1000 / {upperName}_FPS) * {upperName}_DELAYS[i]);");
                    else
                        sb.AppendLine(CultureInfo.InvariantCulture, $"    delay(1000 / {upperName}_FPS);");
                    sb.AppendLine("  }");
                }
                else
                {
                    // Sprite sheet
                    sb.AppendLine("  u8g2.clearBuffer();");
                    if (isXbm)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  u8g2.drawXBMP(x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name});");
                    else
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"    u8g2.drawBitmap(x, y, {bytesPerRow}, {upperName}_HEIGHT, {name});"));
                    sb.AppendLine("  u8g2.sendBuffer();");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  delay(1000 / {upperName}_FPS);");
                }
            }
            else
            {
                sb.AppendLine("  u8g2.clearBuffer();");
                if (compressionActive)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  uint8_t buffer[{upperName}_UNCOMPRESSED_SIZE];");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  {decodeFn}({name}, sizeof({name}), buffer, sizeof(buffer));");
                    if (isXbm)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  u8g2.drawXBM(x, y, {upperName}_WIDTH, {upperName}_HEIGHT, buffer);");
                    else
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  u8g2.drawBitmap(x, y, {bytesPerRow}, {upperName}_HEIGHT, buffer);"));
                }
                else
                {
                    if (isXbm)
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  u8g2.drawXBMP(x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name});");
                    else
                        sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"  u8g2.drawBitmap(x, y, {bytesPerRow}, {upperName}_HEIGHT, {name});"));
                }
                sb.AppendLine("  u8g2.sendBuffer();");
                sb.AppendLine("  delay(1000);");
            }

            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string BuildMicroPythonSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();
            int screenWidth = Math.Max(128, width);
            int screenHeight = (height <= 32 && width <= 128) ? 32 : Math.Max(64, height);

            sb.AppendLine("# =============================================================================");
            sb.AppendLine("#  Ready-to-use MicroPython Script — Generated by Hexprite");
            sb.AppendLine("#  Format: MicroPython (bytearray)");
            sb.AppendLine("# =============================================================================");
            sb.AppendLine();
            sb.AppendLine("import time");
            sb.AppendLine("import framebuf");
            sb.AppendLine("from machine import Pin, I2C");
            sb.AppendLine("import ssd1306");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"SCREEN_WIDTH = {screenWidth}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"SCREEN_HEIGHT = {screenHeight}");
            sb.AppendLine();
            sb.AppendLine("# Configure I2C display pins (e.g. ESP32: scl=Pin(22), sda=Pin(21); RP2040: scl=Pin(5), sda=Pin(4))");
            sb.AppendLine("i2c = I2C(0, scl=Pin(22), sda=Pin(21))");
            sb.AppendLine("oled = ssd1306.SSD1306_I2C(SCREEN_WIDTH, SCREEN_HEIGHT, i2c)");
            sb.AppendLine();
            sb.AppendLine("# ─── Sprite / Animation Data ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("# ─── Main Program ─────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"x = max(0, (SCREEN_WIDTH - {upperName}_WIDTH) // 2)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"y = max(0, (SCREEN_HEIGHT - {upperName}_HEIGHT) // 2)");
            sb.AppendLine();

            if (isAnimation)
            {
                if (cfg.AnimationLayout == AnimationExportLayout.ArrayOfFrames)
                {
                    sb.AppendLine("while True:");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    for i, frame in enumerate({name}):");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"        fb = framebuf.FrameBuffer(frame, {upperName}_WIDTH, {upperName}_HEIGHT, framebuf.MONO_HLSB, (({upperName}_WIDTH + 7) // 8) * 8)");
                    sb.AppendLine("        oled.fill(0)");
                    sb.AppendLine("        oled.blit(fb, x, y)");
                    sb.AppendLine("        oled.show()");
                    if (frameDelays != null && frameDelays.Exists(d => d != 1))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"        time.sleep_ms(int((1000 / {upperName}_FPS) * {upperName}_DELAYS[i]))");
                    }
                    else
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"        time.sleep_ms(1000 // {upperName}_FPS)");
                    }
                }
                else
                {
                    // Sprite sheet
                    sb.AppendLine(CultureInfo.InvariantCulture, $"fb = framebuf.FrameBuffer({name}, {upperName}_WIDTH, {upperName}_HEIGHT, framebuf.MONO_HLSB, (({upperName}_WIDTH + 7) // 8) * 8)");
                    sb.AppendLine("oled.fill(0)");
                    sb.AppendLine("oled.blit(fb, x, y)");
                    sb.AppendLine("oled.show()");
                    sb.AppendLine("while True:");
                    sb.AppendLine("    time.sleep(1)");
                }
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"fb = framebuf.FrameBuffer({name}, {upperName}_WIDTH, {upperName}_HEIGHT, framebuf.MONO_HLSB, (({upperName}_WIDTH + 7) // 8) * 8)");
                sb.AppendLine("oled.fill(0)");
                sb.AppendLine("oled.blit(fb, x, y)");
                sb.AppendLine("oled.show()");
                sb.AppendLine();
                sb.AppendLine("while True:");
                sb.AppendLine("    time.sleep(1)");
            }

            return sb.ToString();
        }

        private static string BuildPlainCArraySketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, bool compressionActive, int uncompressedFrameSize, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("//  Ready-to-use C / C++ Harness — Generated by Hexprite");
            sb.AppendLine("//  Format: Plain C array");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("#include <stdint.h>");
            sb.AppendLine("#include <stdio.h>");
            sb.AppendLine();
            sb.AppendLine("#if defined(ARDUINO)");
            sb.AppendLine("#include <Arduino.h>");
            sb.AppendLine("#endif");
            sb.AppendLine();
            sb.AppendLine("// ─── Sprite / Animation Data ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── Program Entry / Harness ─────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("void display_sprite_info(void) {");
            sb.AppendLine("#if defined(ARDUINO)");
            sb.AppendLine("  Serial.print(F(\"Loaded sprite: \"));");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Serial.print({upperName}_WIDTH);");
            sb.AppendLine("  Serial.print(F(\"x\"));");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Serial.println({upperName}_HEIGHT);");
            sb.AppendLine("#else");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  printf(\"Loaded sprite: %u x %u\\n\", (unsigned int){upperName}_WIDTH, (unsigned int){upperName}_HEIGHT);");
            sb.AppendLine("#endif");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("#if defined(ARDUINO)");
            sb.AppendLine("void setup() {");
            sb.AppendLine("  Serial.begin(115200);");
            sb.AppendLine("  display_sprite_info();");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("void loop() {");
            sb.AppendLine("  delay(1000);");
            sb.AppendLine("}");
            sb.AppendLine("#else");
            sb.AppendLine("int main(void) {");
            sb.AppendLine("  display_sprite_info();");
            sb.AppendLine("  return 0;");
            sb.AppendLine("}");
            sb.AppendLine("#endif");

            return sb.ToString();
        }

        private static string BuildRawDataSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, bool isHex)
        {
            var sb = new StringBuilder();
            string formatName = isHex ? "Raw Hex" : "Raw Binary";
            sb.AppendLine(CultureInfo.InvariantCulture, $"// Generated by Hexprite — {formatName}");
            sb.AppendLine(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"// Width: {width}px, Height: {height}px, Frames: {numFrames}"));
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            return sb.ToString();
        }

        private static string BuildFlipperFapSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            string upperName = name.ToUpperInvariant();
            int fps = Math.Max(1, cfg.FrameRateFps);

            sb.AppendLine("/*");
            sb.AppendLine(" * =============================================================================");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *  Flipper Zero Application (FAP) — Generated by Hexprite");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *  Sprite: {name} ({width}x{height}) | Format: {cfg.Format}");
            sb.AppendLine(" * =============================================================================");
            sb.AppendLine(" *");
            sb.AppendLine(" * HOW TO BUILD AND RUN:");
            sb.AppendLine(CultureInfo.InvariantCulture, $" * 1. Create directory: applications_user/{name}/");
            sb.AppendLine(CultureInfo.InvariantCulture, $" * 2. Save this file as: applications_user/{name}/{name}_app.c");
            sb.AppendLine(CultureInfo.InvariantCulture, $" * 3. Save the application.fam manifest below as: applications_user/{name}/application.fam");
            sb.AppendLine(" * 4. Build and launch with uFBT:  ufbt launch");
            sb.AppendLine(" *");
            sb.AppendLine(" * application.fam content:");
            sb.AppendLine(" * -----------------------------------------------------------------------------");
            sb.AppendLine(" * App(");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *     appid=\"{name}\",");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *     name=\"{name}\",");
            sb.AppendLine(" *     apptype=FlipperAppType.EXTERNAL,");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *     entry_point=\"{name}_app\",");
            sb.AppendLine(" *     stack_size=2 * 1024,");
            sb.AppendLine(" *     fap_category=\"Graphics\",");
            sb.AppendLine(CultureInfo.InvariantCulture, $" *     fap_description=\"Created with Hexprite Embedded Pixel Art Editor\",");
            sb.AppendLine(" * )");
            sb.AppendLine(" * -----------------------------------------------------------------------------");
            sb.AppendLine(" */");
            sb.AppendLine();
            sb.AppendLine("#include <furi.h>");
            sb.AppendLine("#include <gui/gui.h>");
            sb.AppendLine("#include <input/input.h>");
            sb.AppendLine("#include <gui/canvas.h>");
            sb.AppendLine();
            sb.AppendLine("// ─── Sprite Data ─────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── FAP Context & Callbacks ──────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("typedef struct {");
            sb.AppendLine("    ViewPort* view_port;");
            sb.AppendLine("    Gui* gui;");
            sb.AppendLine("    FuriMessageQueue* event_queue;");
            if (isAnimation)
            {
                sb.AppendLine("    uint32_t current_frame;");
            }
            sb.AppendLine(CultureInfo.InvariantCulture, $"}} {name}App;");
            sb.AppendLine();
            sb.AppendLine("static void render_callback(Canvas* canvas, void* ctx) {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {name}App* app = ctx;");
            sb.AppendLine("    UNUSED(app);");
            sb.AppendLine("    canvas_clear(canvas);");
            sb.AppendLine();
            sb.AppendLine("    // Header / Title Bar");
            sb.AppendLine("    canvas_set_font(canvas, FontSecondary);");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_str(canvas, 2, 9, \"{name}\");");
            sb.AppendLine("    canvas_draw_line(canvas, 0, 11, 127, 11);");
            sb.AppendLine();
            sb.AppendLine("    // Draw Sprite (Centered)");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    int32_t x = (128 - {upperName}_WIDTH) / 2;");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    int32_t y = 13 + (51 - {upperName}_HEIGHT) / 2;");
            sb.AppendLine("    if (x < 0) x = 0;");
            sb.AppendLine("    if (y < 12) y = 12;");
            sb.AppendLine();

            if (cfg.Format == ExportFormat.FlipperCompressedBitmap)
            {
                if (isAnimation)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_bitmap(canvas, x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name}_frames[app->current_frame % {upperName}_FRAMES]);");
                else
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_bitmap(canvas, x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name}_compressed);");
            }
            else if (cfg.Format == ExportFormat.FlipperXbm)
            {
                if (isAnimation)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_xbm(canvas, x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name}_xbm_frames[app->current_frame % {upperName}_FRAMES]);");
                else
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_xbm(canvas, x, y, {upperName}_WIDTH, {upperName}_HEIGHT, {name}_xbm);");
            }
            else
            {
                if (isAnimation)
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_icon_animation(canvas, x, y, &A_{name}_{width}x{height});");
                else
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    canvas_draw_icon(canvas, x, y, &I_{name}_{width}x{height});");
            }

            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("static void input_callback(InputEvent* input_event, void* ctx) {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {name}App* app = ctx;");
            sb.AppendLine("    furi_message_queue_put(app->event_queue, input_event, FuriWaitForever);");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine(CultureInfo.InvariantCulture, $"int32_t {name}_app(void* p) {{");
            sb.AppendLine("    UNUSED(p);");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {name}App app;");
            sb.AppendLine("    app.event_queue = furi_message_queue_alloc(8, sizeof(InputEvent));");
            if (isAnimation)
            {
                sb.AppendLine("    app.current_frame = 0;");
            }
            sb.AppendLine("    app.view_port = view_port_alloc();");
            sb.AppendLine("    view_port_draw_callback_set(app.view_port, render_callback, &app);");
            sb.AppendLine("    view_port_input_callback_set(app.view_port, input_callback, &app);");
            sb.AppendLine();
            sb.AppendLine("    app.gui = furi_record_open(RECORD_GUI);");
            sb.AppendLine("    gui_add_view_port(app.gui, app.view_port, GuiLayerFullscreen);");
            sb.AppendLine();
            sb.AppendLine("    InputEvent event;");
            sb.AppendLine("    while(furi_message_queue_get(app.event_queue, &event, FuriWaitForever) == FuriStatusOk) {");
            sb.AppendLine("        if(event.type == InputTypeShort && event.key == InputKeyBack) {");
            sb.AppendLine("            break;");
            sb.AppendLine("        }");
            if (isAnimation)
            {
                sb.AppendLine("        if(event.type == InputTypeShort) {");
                sb.AppendLine(CultureInfo.InvariantCulture, $"            app.current_frame = (app.current_frame + 1) % {upperName}_FRAMES;");
                sb.AppendLine("        }");
            }
            sb.AppendLine("        view_port_update(app.view_port);");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    gui_remove_view_port(app.gui, app.view_port);");
            sb.AppendLine("    view_port_free(app.view_port);");
            sb.AppendLine("    furi_record_close(RECORD_GUI);");
            sb.AppendLine("    furi_message_queue_free(app.event_queue);");
            sb.AppendLine("    return 0;");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string FormatLiquidCrystalByte(byte b, bool useHex, bool uppercaseHex)
        {
            if (useHex)
            {
                return uppercaseHex ? $"0x{b:X2}" : $"0x{b:x2}";
            }
            return "B" + Convert.ToString(b & 0x1F, 2).PadLeft(5, '0');
        }

        private static string BuildLiquidCrystal(
            byte[] data, string name, int width, int height,
            ExportSettings settings, string hexFmt,
            System.Threading.CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            if (settings.IncludeUsageComment)
            {
                sb.AppendLine("// LiquidCrystal 1602/2004 Custom Character (5x8)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// Register in setup(): lcd.createChar(0, {name});");
                sb.AppendLine("// Display in loop():   lcd.write((byte)0);");
                sb.AppendLine();
            }

            if (settings.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_HEIGHT = {height};");
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"byte {name}[8] = {{");
            for (int y = 0; y < 8; y++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte b = (y < data.Length) ? data[y] : (byte)0;
                string formattedByte = FormatLiquidCrystalByte(b, settings.Format == ExportFormat.RawHex, settings.UppercaseHex);
                sb.Append("  ");
                sb.Append(formattedByte);
                if (y < 7) sb.Append(',');
                if (settings.IncludeRowComments)
                {
                    sb.Append(CultureInfo.InvariantCulture, $" // Row {y}");
                }
                sb.AppendLine();
            }
            sb.AppendLine("};");
            return sb.ToString();
        }

        private static string BuildLiquidCrystalAnimation(
            List<byte[]> frames, string name, int width, int height,
            ExportSettings settings, string hexFmt, List<int>? frameDelays,
            System.Threading.CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            int count = Math.Min(frames.Count, 8); // HD44780 has 8 custom character slots (0..7)

            if (settings.IncludeUsageComment)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"// LiquidCrystal 1602/2004 Custom Characters ({count} frames, slots 0-{count - 1})");
                sb.AppendLine("// Register in setup():");
                sb.AppendLine(CultureInfo.InvariantCulture, $"// for (int i = 0; i < {count}; i++) lcd.createChar(i, {name}_frames[i]);");
                sb.AppendLine("// Display in loop(): lcd.write((byte)frameIndex);");
                sb.AppendLine();
            }

            if (settings.IncludeDimensionConstants)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_WIDTH  = {width};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_HEIGHT = {height};");
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_FRAMES = {count};");
            }

            if (frameDelays != null && frameDelays.Exists(d => d != 1))
            {
                sb.AppendLine();
                sb.AppendLine(CultureInfo.InvariantCulture, $"const uint8_t {name.ToUpperInvariant()}_DELAYS[{count}] = {{");
                var delaysToUse = frameDelays.Take(count).Select(d => settings.UppercaseHex ? HexUpper[Math.Clamp(d, 0, 255)] : HexLower[Math.Clamp(d, 0, 255)]);
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {string.Join(", ", delaysToUse)}");
                sb.AppendLine("};");
            }

            for (int f = 0; f < count; f++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string frameName = $"{name}_{f}";
                sb.AppendLine(CultureInfo.InvariantCulture, $"byte {frameName}[8] = {{");
                byte[] data = frames[f];
                for (int y = 0; y < 8; y++)
                {
                    byte b = (y < data.Length) ? data[y] : (byte)0;
                    string formattedByte = FormatLiquidCrystalByte(b, settings.Format == ExportFormat.RawHex, settings.UppercaseHex);
                    sb.Append("  ");
                    sb.Append(formattedByte);
                    if (y < 7) sb.Append(',');
                    if (settings.IncludeRowComments)
                    {
                        sb.Append(CultureInfo.InvariantCulture, $" // Row {y}");
                    }
                    sb.AppendLine();
                }
                sb.AppendLine("};");
                sb.AppendLine();
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"byte* const {name}_frames[{count}] = {{");
            for (int f = 0; f < count; f++)
            {
                sb.Append(CultureInfo.InvariantCulture, $"  {name}_{f}");
                if (f < count - 1) sb.Append(',');
                sb.AppendLine();
            }
            sb.AppendLine("};");
            return sb.ToString();
        }

        private static string BuildLiquidCrystalSketch(
            string coreOutput, string name, int width, int height, int numFrames, bool isAnimation,
            ExportSettings cfg, List<int>? frameDelays)
        {
            var sb = new StringBuilder();
            int fps = Math.Max(1, cfg.FrameRateFps);
            int count = Math.Min(numFrames, 8);
            string upperName = name.ToUpperInvariant();

            sb.AppendLine("// =============================================================================");
            sb.AppendLine("//  Ready-to-use Arduino Sketch — Generated by Hexprite");
            sb.AppendLine("//  Format: LiquidCrystal 1602/2004 Custom Character (5x8)");
            sb.AppendLine("//  Hardware: HD44780 LCD (I2C Backpack or Parallel)");
            sb.AppendLine("// =============================================================================");
            sb.AppendLine();
            sb.AppendLine("#include <Wire.h>");
            sb.AppendLine("#include <LiquidCrystal_I2C.h> // Library: 'LiquidCrystal I2C' by Frank de Brabander");
            sb.AppendLine();
            sb.AppendLine("// Standard I2C address: 0x27 (PCF8574) or 0x3F (PCF8574A)");
            sb.AppendLine("LiquidCrystal_I2C lcd(0x27, 16, 2);");
            sb.AppendLine();
            sb.AppendLine("// For standard 4-bit parallel connection without I2C backpack, uncomment below:");
            sb.AppendLine("// (and in setup(), replace lcd.init()/backlight() with lcd.begin(16, 2);)");
            sb.AppendLine("// #include <LiquidCrystal.h>");
            sb.AppendLine("// const int rs = 12, en = 11, d4 = 5, d5 = 4, d6 = 3, d7 = 2;");
            sb.AppendLine("// LiquidCrystal lcd(rs, en, d4, d5, d6, d7);");
            sb.AppendLine();
            sb.AppendLine("// ─── Custom Character Data ───────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine(coreOutput);
            sb.AppendLine();
            sb.AppendLine("// ─── Arduino Setup & Main Loop ───────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine("void setup() {");
            sb.AppendLine("  lcd.init();");
            sb.AppendLine("  lcd.backlight();");
            sb.AppendLine();

            if (isAnimation)
            {
                sb.AppendLine("  // Register custom character frames in CGRAM (up to 8 slots: 0 to 7)");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  for (int i = 0; i < {count}; i++) {{");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    lcd.createChar(i, {name}_frames[i]);");
                sb.AppendLine("  }");
                sb.AppendLine();
                sb.AppendLine("  lcd.setCursor(0, 0);");
                sb.AppendLine("  lcd.print(\"Hexprite Anim: \");");
                sb.AppendLine("  lcd.setCursor(15, 0);");
                sb.AppendLine("  lcd.write((byte)0);");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("void loop() {");
                sb.AppendLine("  static unsigned long lastFrameTime = 0;");
                sb.AppendLine("  static int currentFrame = 0;");
                sb.AppendLine("  unsigned long now = millis();");
                sb.AppendLine();
                if (frameDelays != null && frameDelays.Exists(d => d != 1))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  unsigned long frameDuration = (1000UL / {fps}) * {upperName}_DELAYS[currentFrame];");
                    sb.AppendLine("  if (now - lastFrameTime >= frameDuration) {");
                }
                else
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  if (now - lastFrameTime >= (1000 / {fps})) {{");
                }
                sb.AppendLine("    lastFrameTime = now;");
                sb.AppendLine("    lcd.setCursor(15, 0);");
                sb.AppendLine("    lcd.write((byte)currentFrame);");
                sb.AppendLine(CultureInfo.InvariantCulture, $"    currentFrame = (currentFrame + 1) % {count};");
                sb.AppendLine("  }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine("  // Register custom character in CGRAM slot 0");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  lcd.createChar(0, {name});");
                sb.AppendLine();
                sb.AppendLine("  lcd.setCursor(0, 0);");
                sb.AppendLine("  lcd.print(\"Hexprite: \");");
                sb.AppendLine("  lcd.write((byte)0);");
                sb.AppendLine("}");
                sb.AppendLine();
                sb.AppendLine("void loop() {");
                sb.AppendLine("  // Static custom character rendered in setup() — idle loop");
                sb.AppendLine("  delay(1000);");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        public (string InoContent, string HeaderContent, string SketchName) GenerateStandaloneSketchFiles(
            SpriteState sprite,
            ExportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(sprite);
            ArgumentNullException.ThrowIfNull(settings);

            string name = SanitiseName(settings.SpriteName);
            var clonedSettings = settings.Clone();
            clonedSettings.GenerateFullSketch = false;
            clonedSettings.IncludeDimensionConstants = true;

            // Generate header content (sprites.h)
            var frames = new List<bool[]>();
            if (settings.ExportAsAnimation && sprite.Frames != null && sprite.Frames.Count > 1)
            {
                for (int i = 0; i < sprite.Frames.Count; i++)
                {
                    frames.Add(sprite.CompositeFramePixels(i, isExport: true));
                }
            }
            else
            {
                frames.Add(sprite.CompositeVisiblePixels(isExport: true));
            }

            var delays = (settings.ExportAsAnimation && sprite.Frames != null)
                ? sprite.Frames.Select(f => f.DelayMultiplier).ToList()
                : null;

            string coreOutput = GenerateCode(
                frames, sprite.Width, sprite.Height, clonedSettings,
                isFloating: false, floatingPixels: null, 0, 0, 0, 0,
                pasteMode: FloatingPasteMode.Transparent,
                frameDelays: delays);

            var headerSb = new StringBuilder();
            headerSb.AppendLine("#pragma once");
            headerSb.AppendLine("#include <Arduino.h>");
            headerSb.AppendLine();
            headerSb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
            headerSb.AppendLine("//  Hexprite Generated Sprite Header");
            headerSb.AppendLine("// ═══════════════════════════════════════════════════════════════════════════════");
            headerSb.AppendLine();
            headerSb.AppendLine(coreOutput);
            string headerContent = headerSb.ToString();

            // Generate sketch content (MySprite.ino)
            clonedSettings.GenerateFullSketch = true;
            string fullSketch = GenerateCode(
                frames, sprite.Width, sprite.Height, clonedSettings,
                isFloating: false, floatingPixels: null, 0, 0, 0, 0,
                pasteMode: FloatingPasteMode.Transparent,
                frameDelays: delays);

            string inoContent;
            if (!string.IsNullOrEmpty(coreOutput) && fullSketch.Contains(coreOutput, StringComparison.Ordinal))
            {
                inoContent = fullSketch.Replace(coreOutput, "#include \"sprites.h\"");
            }
            else
            {
                inoContent = "#include \"sprites.h\"\n\n" + fullSketch;
            }

            return (inoContent, headerContent, name);
        }
    }
}
