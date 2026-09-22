using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Hexprite.Core;

namespace Hexprite.Services
{
    public partial class XbmService : IXbmService
    {
        [GeneratedRegex(@"(?i)#define\s+\w+_(?:width|w)\s+(0[xX][0-9a-fA-F]+|\d+)", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex WidthRegex { get; }

        [GeneratedRegex(@"(?i)#define\s+\w+_(?:height|h)\s+(0[xX][0-9a-fA-F]+|\d+)", RegexOptions.None, matchTimeoutMilliseconds: 250)]
        private static partial Regex HeightRegex { get; }

        private static bool TryParseDimension(string raw, out int value)
        {
            raw = raw.Trim();
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value) && value > 0;
            }
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
        }

        public (int Width, int Height, string Body) ParseFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException($"XBM file not found: {path}", path);

            string text = SafeFileIo.ReadAllTextWithRetry(path);

            var widthMatch = WidthRegex.Match(text);
            var heightMatch = HeightRegex.Match(text);

            if (!widthMatch.Success || !heightMatch.Success
                || !TryParseDimension(widthMatch.Groups[1].Value, out int width)
                || !TryParseDimension(heightMatch.Groups[1].Value, out int height))
            {
                throw new FormatException(
                    $"'{Path.GetFileName(path)}' does not contain valid #define ..._width / #define ..._height declarations.");
            }

            return (width, height, text);
        }

        public void ExportImage(SpriteState spriteState, int frameIndex, string targetFilePath)
        {
            ArgumentNullException.ThrowIfNull(spriteState);
            if (spriteState.Width <= 0 || spriteState.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(spriteState), "Sprite dimensions must be greater than zero.");
            if (string.IsNullOrWhiteSpace(targetFilePath))
                throw new ArgumentException("Target file path cannot be null or whitespace.", nameof(targetFilePath));

            bool[] pixels = spriteState.CompositeFramePixels(frameIndex, isExport: true);
            int width = spriteState.Width;
            int height = spriteState.Height;
            string name = CodeGeneratorService.SanitiseName(Path.GetFileNameWithoutExtension(targetFilePath));

            int rowBytes = (width + 7) / 8;
            byte[] buffer = new byte[rowBytes * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x])
                        buffer[y * rowBytes + (x / 8)] |= (byte)(1 << (x % 8));
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"#define {name}_width {width}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"#define {name}_height {height}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"static unsigned char {name}_bits[] = {{");
            for (int i = 0; i < buffer.Length; i++)
            {
                if (i % 12 == 0) sb.Append("  ");
                sb.Append(CultureInfo.InvariantCulture, $"0x{buffer[i]:x2}");
                if (i < buffer.Length - 1) sb.Append(',');
                sb.Append(i % 12 == 11 || i == buffer.Length - 1 ? "\n" : " ");
            }
            sb.AppendLine("};");

            SafeFileIo.WriteAllTextAtomic(targetFilePath, sb.ToString(), maxRetries: 3, createBackup: false);
        }
    }
}
