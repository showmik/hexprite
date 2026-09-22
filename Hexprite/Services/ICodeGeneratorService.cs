using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface ICodeGeneratorService
    {
        // ── Export ────────────────────────────────────────────────────────────

        /// <summary>
        /// Generates formatted export code from the current sprite state.
        /// All options (format, name, structure flags) are taken from <paramref name="settings"/>.
        /// Layer semantics are resolved by the caller into <see cref="SpriteState.Pixels"/>
        /// before this method is invoked.
        /// Supports animation via <paramref name="frames"/> — multiple frames generate 2D arrays.
        /// </summary>
        string GenerateCode(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Async wrapper — runs generation on a thread-pool thread so the UI
        /// thread is never blocked, even for 256×256 canvases.
        /// </summary>
        Task<string> GenerateCodeAsync(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Generates a complete, ready-to-run single sketch/code file (.ino or .py)
        /// for the current canvas and settings.
        /// </summary>
        string GenerateSketch(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Async wrapper for <see cref="GenerateSketch"/>.
        /// </summary>
        Task<string> GenerateSketchAsync(
            List<bool[]> frames, int width, int height,
            ExportSettings settings,
            bool isFloating, bool[,]? floatingPixels,
            int floatX, int floatY, int floatW, int floatH,
            FloatingPasteMode pasteMode = FloatingPasteMode.Transparent,
            List<int>? frameDelays = null,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Computes the output byte count directly from frame dimensions and settings.
        /// </summary>
        int CalculateByteCount(
            List<bool[]> frames, int width, int height,
            ExportSettings settings);

        // ── Import ────────────────────────────────────────────────────────────

        /// <summary>
        /// Parses an Adafruit GFX / plain C array block back into the canvas.
        /// Handles both <c>0xFF</c> and <c>0xff</c> hex literals.
        /// Parsed data is written to the active layer pixel buffer.
        /// </summary>
        void ParseAdafruitGfxToState(string code, SpriteState state);

        /// <summary>
        /// Parses raw hex output (space- or comma-separated 0xNN tokens)
        /// back into the canvas. Kept from original implementation.
        /// Parsed data is written to the active layer pixel buffer.
        /// </summary>
        void ParseHexToState(string hexText, SpriteState state);

        /// <summary>
        /// Parses an XBM array back into the canvas (LSB-first, bit-reversed).
        /// Parsed data is written to the active layer pixel buffer.
        /// </summary>
        void ParseXbmToState(string code, SpriteState state);

        /// <summary>
        /// Parses a raw binary string back into the canvas.
        /// Parsed data is written to the active layer pixel buffer.
        /// </summary>
        void ParseBinaryToState(string code, SpriteState state);

        /// <summary>
        /// Parses a 2D pixel array or indexed palette matrix (1 byte/element per pixel) back into the canvas.
        /// Parsed data is written to the active layer pixel buffer.
        /// </summary>
        void ParseIndexed2DToState(string code, SpriteState state);

        /// <summary>
        /// Parses a LiquidCrystal custom character array (byte name[8] = { B..., ... };) back into the canvas.
        /// </summary>
        void ParseLiquidCrystalToState(string code, SpriteState state);

        /// <summary>
        /// Generates a complete Arduino sketch folder contents: main .ino sketch and sprites.h header.
        /// </summary>
        (string InoContent, string HeaderContent, string SketchName) GenerateStandaloneSketchFiles(
            SpriteState sprite,
            ExportSettings settings);
    }
}
