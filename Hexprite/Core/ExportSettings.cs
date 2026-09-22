using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    public enum AnimationExportLayout
    {
        ArrayOfFrames,
        VerticalSpriteSheet,
        HorizontalSpriteSheet,
    }

    /// <summary>
    /// All user-configurable options that control how the export code is generated.
    /// Persisted inside the .hexp file so the last-used settings are remembered
    /// per document. All properties have sensible defaults.
    /// </summary>
    public class ExportSettings
    {
        // ── Format ────────────────────────────────────────────────────────────

        /// <summary>Target platform / library.</summary>
        public ExportFormat Format { get; set; } = ExportFormat.AdafruitGfx;

        /// <summary>
        /// Selects how layers are mapped into export pixels.
        /// Current UI uses <see cref="ExportLayerMode.CompositeVisible"/> only.
        /// </summary>
        public ExportLayerMode LayerMode { get; set; } = ExportLayerMode.CompositeVisible;

        // ── Naming ────────────────────────────────────────────────────────────

        /// <summary>
        /// User-defined variable / sprite name.
        /// Automatically sanitised to a valid C / Python identifier before use.
        /// Falls back to "sprite" when blank after sanitisation.
        /// </summary>
        public string SpriteName { get; set; } = "mySprite";

        // ── Code structure ────────────────────────────────────────────────────

        /// <summary>
        /// Emit a usage-comment above the array showing the appropriate
        /// display.drawBitmap / u8g2.drawBitmap call.
        /// </summary>
        public bool IncludeUsageComment { get; set; } = true;

        /// <summary>
        /// Emit <c>const uint8_t NAME_WIDTH = W;</c> and
        /// <c>const uint8_t NAME_HEIGHT = H;</c> before the array.
        /// </summary>
        public bool IncludeDimensionConstants { get; set; } = true;

        /// <summary>Bytes separated by ", " (true) or " " (false).</summary>
        public bool UseCommaSeparator { get; set; } = true;

        /// <summary>
        /// How many bytes appear on each line of the array body.
        /// 0 = match the canvas width in bytes (one row per line).
        /// </summary>
        public int BytesPerLine { get; set; }   // 0 = canvas width

        /// <summary>Emit hex digits in upper-case (0xFF) vs lower-case (0xff).</summary>
        public bool UppercaseHex { get; set; } = true;

        /// <summary>Append a <c>// row N</c> comment at the end of each data line.</summary>
        public bool IncludeRowComments { get; set; }

        /// <summary>Include the byte count inside the array brackets (e.g. name[32]).</summary>
        public bool IncludeArraySize { get; set; }

        // ── Animation ───────────────────────────────────────────────────────

        /// <summary>Export all frames as an animation (2D array) instead of single frame.</summary>
        public bool ExportAsAnimation { get; set; } = true;

        /// <summary>Controls the memory layout of the animation frames in the generated array.</summary>
        public AnimationExportLayout AnimationLayout { get; set; } = AnimationExportLayout.ArrayOfFrames;

        /// <summary>Frames per second for animation playback in generated code.</summary>
        public int FrameRateFps { get; set; } = 12;

        // ── Code structure ────────────────────────────────────────────────────

        /// <summary>
        /// When true, wraps the exported byte arrays in a complete, ready-to-run
        /// standalone sketch/code file (.ino for Arduino/C++, .py for MicroPython)
        /// with display initialization and animation playback loops.
        /// </summary>
        public bool GenerateFullSketch { get; set; }

        // ── Compression ───────────────────────────────────────────────────────

        /// <summary>
        /// Compression algorithm applied to the exported byte array.
        /// <see cref="CompressionMode.None"/> preserves current raw behavior.
        /// </summary>
        public CompressionMode Compression { get; set; } = CompressionMode.None;


        // ── Thread safety ─────────────────────────────────────────────────────

        /// <summary>
        /// Returns a shallow copy of this instance.
        /// Safe for all current properties because every field is either a value
        /// type or an immutable <see cref="string"/>, so MemberwiseClone is
        /// equivalent to a full deep copy.
        /// </summary>
        public ExportSettings Clone() => (ExportSettings)MemberwiseClone();
    }
}
