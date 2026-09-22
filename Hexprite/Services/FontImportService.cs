using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hexprite.Core;
using Hexprite.Rendering;

namespace Hexprite.Services
{
    /// <summary>
    /// Service for importing fonts from TrueType / WPF FontFamilies into Hexprite FontDocuments.
    /// </summary>
    public class FontImportService : IFontImportService
    {
        public FontDocument ImportFromTrueType(FontFamily fontFamily, FontImportOptions options)
        {
            ArgumentNullException.ThrowIfNull(fontFamily);
            ArgumentNullException.ThrowIfNull(options);

            int targetHeight = Math.Clamp(options.TargetHeight, 1, 512);
            int nativeHeight = TextRenderer.GetNativePixelHeight(fontFamily);
            int renderSize = Math.Clamp(nativeHeight > 0 ? nativeHeight : targetHeight, 1, 512);

            int firstChar = Math.Clamp(options.FirstChar, 0, 0x10FFFF);
            int lastChar = Math.Clamp(options.LastChar, 0, 0x10FFFF);
            if (firstChar > lastChar)
            {
                (firstChar, lastChar) = (lastChar, firstChar);
            }
            if (lastChar - firstChar > 65535)
            {
                lastChar = firstChar + 65535;
            }

            var masks = new Dictionary<int, bool[,]>();
            int maxW = 0;
            int maxH = 0;

            for (int cp = firstChar; cp <= lastChar; cp++)
            {
                string text = (cp >= 0 && cp <= 0x10FFFF && (cp < 0xD800 || cp > 0xDFFF))
                    ? char.ConvertFromUtf32(cp)
                    : " ";
                bool[,] mask = TextRenderer.RenderTextToMonochromeMask(
                    text, fontFamily, renderSize, isBold: false, isItalic: false, 0, 1, 1,
                    options.AntiAlias, options.Threshold);

                masks[cp] = mask;
                int maskW = mask.GetLength(0);
                int maskH = mask.GetLength(1);

                if (maskW > maxW) maxW = maskW;
                if (maskH > maxH) maxH = maskH;
            }

            int finalHeight = Math.Max(targetHeight, maxH);
            int baseline = (int)(finalHeight * 0.75) + options.BaselineOffset;

            var doc = new FontDocument
            {
                CellHeight = finalHeight,
                MaxCellWidth = Math.Max(1, maxW),
                FirstChar = firstChar,
                LastChar = lastChar,
                Baseline = baseline,
                YAdvance = finalHeight,
                FontName = "importedFont",
                ExportSettings = new FontExportSettings(),
            };

            var glyphs = new List<GlyphState>();
            int actualMaxW = 0;

            for (int cp = firstChar; cp <= lastChar; cp++)
            {
                var mask = masks[cp];
                int maskW = mask.GetLength(0);
                int maskH = mask.GetLength(1);

                if (maskW == 0 || maskH == 0)
                {
                    maskW = Math.Max(1, finalHeight / 2);
                    maskH = finalHeight;
                    mask = new bool[maskW, maskH];
                }

                int w = maskW;
                int h = finalHeight;

                if (w > actualMaxW) actualMaxW = w;

                bool[] flatPixels = new bool[w * h];
                for (int y = 0; y < maskH; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        flatPixels[y * w + x] = mask[x, y];
                    }
                }

                var glyph = new GlyphState
                {
                    CodePoint = cp,
                    Width = w,
                    Height = h,
                    Pixels = flatPixels,
                    XOffset = 0,
                    YOffset = 0,
                    XAdvance = Math.Max(0, w + options.LetterSpacing),
                    IsCustomized = false,
                };
                glyphs.Add(glyph);
            }

            doc.MaxCellWidth = Math.Max(1, actualMaxW);
            doc.Glyphs = glyphs;
            doc.NormalizeGlyphs();
            return doc;
        }

        public FontDocument ImportFromFontFile(string filePath, FontImportOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Font file not found: {filePath}", filePath);

            string fullPath = Path.GetFullPath(filePath);
            var uri = new Uri(fullPath, UriKind.Absolute);
            var families = Fonts.GetFontFamilies(uri);
            var family = families.FirstOrDefault();
            
            if (family == null)
            {
                families = Fonts.GetFontFamilies(filePath);
                family = families.FirstOrDefault() ?? throw new InvalidOperationException("Could not load font from file. The font might be invalid or unsupported by WPF.");
            }

            var doc = ImportFromTrueType(family, options);
            doc.FontName = Path.GetFileNameWithoutExtension(filePath);
            return doc;
        }

        public FontDocument ImportFromSpriteSheet(string imagePath, int cellWidth, int cellHeight, FontImportOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("Image path cannot be null or whitespace.", nameof(imagePath));

            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}", imagePath);

            cellWidth = Math.Clamp(cellWidth, 1, 512);
            cellHeight = Math.Clamp(cellHeight, 1, 512);

            string fullPath = Path.GetFullPath(imagePath);
            var uri = new Uri(fullPath, UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            int imgWidth = bmp.PixelWidth;
            int imgHeight = bmp.PixelHeight;

            int cols = Math.Max(1, imgWidth / cellWidth);
            int rows = Math.Max(1, imgHeight / cellHeight);
            int totalCells = cols * rows;
            
            int numChars = Math.Min(totalCells, Math.Max(1, options.LastChar - options.FirstChar + 1));
            int actualLastChar = options.FirstChar + numChars - 1;

            var doc = new FontDocument
            {
                CellHeight = cellHeight,
                MaxCellWidth = cellWidth,
                FirstChar = options.FirstChar,
                LastChar = actualLastChar,
                Baseline = Math.Clamp((int)(cellHeight * 0.75) + options.BaselineOffset, 0, cellHeight),
                YAdvance = cellHeight,
                FontName = Path.GetFileNameWithoutExtension(imagePath),
                ExportSettings = new FontExportSettings(),
            };

            var formatConverted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, destinationPalette: null, 0);
            int stride = imgWidth * 4;
            byte[] pixels = new byte[imgHeight * stride];
            formatConverted.CopyPixels(pixels, stride, 0);

            var (isTransparentBg, isLightBg) = DetectBackground(pixels, imgWidth, imgHeight, stride, options.Threshold);

            var glyphs = new List<GlyphState>();

            for (int i = 0; i < numChars; i++)
            {
                int cp = options.FirstChar + i;
                int r = i / cols;
                int c = i % cols;

                int startX = c * cellWidth;
                int startY = r * cellHeight;

                bool[] flatPixels = new bool[cellWidth * cellHeight];
                int actualWidth = 0;

                for (int y = 0; y < cellHeight; y++)
                {
                    for (int x = 0; x < cellWidth; x++)
                    {
                        int pX = startX + x;
                        int pY = startY + y;

                        if (pX < imgWidth && pY < imgHeight)
                        {
                            int idx = pY * stride + pX * 4;
                            byte b = pixels[idx];
                            byte g = pixels[idx + 1];
                            byte red = pixels[idx + 2];
                            byte a = pixels[idx + 3];

                            bool isOn = IsPixelInk(red, g, b, a, isTransparentBg, isLightBg, options.Threshold);

                            flatPixels[y * cellWidth + x] = isOn;
                            if (isOn && (x + 1) > actualWidth)
                            {
                                actualWidth = x + 1;
                            }
                        }
                    }
                }

                if (actualWidth == 0) actualWidth = Math.Max(1, cellWidth / 2);

                glyphs.Add(new GlyphState
                {
                    CodePoint = cp,
                    Width = cellWidth,
                    Height = cellHeight,
                    Pixels = flatPixels,
                    XOffset = 0,
                    YOffset = 0,
                    XAdvance = Math.Max(0, actualWidth + options.LetterSpacing),
                    IsCustomized = false,
                });
            }

            doc.Glyphs = glyphs;
            doc.NormalizeGlyphs();
            return doc;
        }

        public FontDocument ImportFromBMFont(string fntPath, FontImportOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (string.IsNullOrWhiteSpace(fntPath))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(fntPath));

            if (!File.Exists(fntPath))
                throw new FileNotFoundException($"BMFont file not found: {fntPath}", fntPath);

            string text = SafeFileIo.ReadAllTextWithRetry(fntPath);
            var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException("XML-based BMFont files are not currently supported. Please export your BMFont as 'Text' format.");
            }

            string textureFile = string.Empty;
            int lineHeight = 10;
            int baseLine = 8;
            
            var charDefs = new List<dynamic>();

            foreach (var line in lines)
            {
                if (line.StartsWith("info ", StringComparison.Ordinal) || line.StartsWith("common ", StringComparison.Ordinal))
                {
                    var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("lineHeight=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(11), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedLineHeight))
                            lineHeight = parsedLineHeight;
                        else if (part.StartsWith("base=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(5), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedBase))
                            baseLine = parsedBase;
                    }
                }
                else if (line.StartsWith("page ", StringComparison.Ordinal))
                {
                    var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("file=", StringComparison.Ordinal))
                        {
                            textureFile = part[5..].Trim('"');
                        }
                    }
                }
                else if (line.StartsWith("char ", StringComparison.Ordinal))
                {
                    var parts = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                    int id = 0, x = 0, y = 0, width = 0, height = 0, xoffset = 0, yoffset = 0, xadvance = 0;
                    foreach (var part in parts)
                    {
                        if (part.StartsWith("id=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedId))
                            id = parsedId;
                        else if (part.StartsWith("x=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedX))
                            x = parsedX;
                        else if (part.StartsWith("y=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedY))
                            y = parsedY;
                        else if (part.StartsWith("width=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(6), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedWidth))
                            width = parsedWidth;
                        else if (part.StartsWith("height=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(7), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedHeight))
                            height = parsedHeight;
                        else if (part.StartsWith("xoffset=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedXOffset))
                            xoffset = parsedXOffset;
                        else if (part.StartsWith("yoffset=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedYOffset))
                            yoffset = parsedYOffset;
                        else if (part.StartsWith("xadvance=", StringComparison.Ordinal) && int.TryParse(part.AsSpan(9), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedXAdvance))
                            xadvance = parsedXAdvance;
                    }
                    charDefs.Add(new { id, x, y, width, height, xoffset, yoffset, xadvance });
                }
            }

            if (charDefs.Count == 0)
                throw new InvalidDataException("No character definitions found in BMFont file.");

            if (string.IsNullOrEmpty(textureFile))
                throw new InvalidDataException("Could not find texture file reference in BMFont.");

            string dir = Path.GetFullPath(Path.GetDirectoryName(fntPath) ?? throw new InvalidDataException("Invalid directory for BMFont file."));
            string texPath = Path.GetFullPath(Path.Combine(dir, textureFile));
            if (!texPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                // Prevent directory traversal; confine to dir
                texPath = Path.Combine(dir, Path.GetFileName(textureFile));
            }

            if (!File.Exists(texPath))
                throw new FileNotFoundException($"BMFont texture file not found: {texPath}", texPath);

            var uri = new Uri(texPath, UriKind.Absolute);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();

            int imgWidth = bmp.PixelWidth;
            int imgHeight = bmp.PixelHeight;

            var formatConverted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, destinationPalette: null, 0);
            int stride = imgWidth * 4;
            byte[] pixels = new byte[imgHeight * stride];
            formatConverted.CopyPixels(pixels, stride, 0);

            var (isTransparentBg, isLightBg) = DetectBackground(pixels, imgWidth, imgHeight, stride, options.Threshold);

            int minChar = int.MaxValue;
            int maxChar = int.MinValue;
            int maxCellW = 1;

            var glyphs = new List<GlyphState>();

            foreach (var cDef in charDefs)
            {
                if (cDef.id < minChar) minChar = cDef.id;
                if (cDef.id > maxChar) maxChar = cDef.id;
                if (cDef.width > maxCellW) maxCellW = cDef.width;

                int w = cDef.width <= 0 ? 1 : Math.Clamp(cDef.width, 1, 512);
                int h = cDef.height <= 0 ? Math.Clamp(lineHeight, 1, 512) : Math.Clamp(cDef.height, 1, 512);

                bool[] flatPixels = new bool[w * h];

                for (int cy = 0; cy < cDef.height; cy++)
                {
                    for (int cx = 0; cx < cDef.width; cx++)
                    {
                        int pX = cDef.x + cx;
                        int pY = cDef.y + cy;

                        if (pX >= 0 && pX < imgWidth && pY >= 0 && pY < imgHeight && cy < h && cx < w)
                        {
                            int idx = pY * stride + pX * 4;
                            byte b = pixels[idx];
                            byte g = pixels[idx + 1];
                            byte red = pixels[idx + 2];
                            byte a = pixels[idx + 3];

                            bool isOn = IsPixelInk(red, g, b, a, isTransparentBg, isLightBg, options.Threshold);

                            flatPixels[cy * w + cx] = isOn;
                        }
                    }
                }

                int yOffset = cDef.yoffset;

                glyphs.Add(new GlyphState
                {
                    CodePoint = cDef.id,
                    Width = w,
                    Height = h,
                    Pixels = flatPixels,
                    XOffset = cDef.xoffset,
                    YOffset = yOffset,
                    XAdvance = Math.Max(0, cDef.xadvance + options.LetterSpacing),
                    IsCustomized = true,
                });
            }

            if (minChar > maxChar)
            {
                minChar = 32;
                maxChar = 126;
            }

            var doc = new FontDocument
            {
                CellHeight = Math.Clamp(lineHeight, 1, 512),
                MaxCellWidth = Math.Clamp(maxCellW, 1, 512),
                FirstChar = minChar,
                LastChar = maxChar,
                Baseline = Math.Clamp(baseLine + options.BaselineOffset, 0, Math.Clamp(lineHeight, 1, 512)),
                YAdvance = Math.Clamp(lineHeight, 1, 512),
                FontName = Path.GetFileNameWithoutExtension(fntPath),
                ExportSettings = new FontExportSettings(),
                Glyphs = glyphs,
            };
            doc.NormalizeGlyphs();
            return doc;
        }

        private static (bool isTransparentBg, bool isLightBg) DetectBackground(byte[] pixels, int imgWidth, int imgHeight, int stride, int threshold)
        {
            int transparentCorners = 0;
            int totalLum = 0;
            int cornerCount = 0;
            int[] sampleXs = [0, imgWidth - 1];
            int[] sampleYs = [0, imgHeight - 1];

            foreach (int sy in sampleYs)
            {
                foreach (int sx in sampleXs)
                {
                    if (sx >= 0 && sx < imgWidth && sy >= 0 && sy < imgHeight)
                    {
                        int cIdx = sy * stride + sx * 4;
                        byte a = pixels[cIdx + 3];
                        if (a < threshold)
                        {
                            transparentCorners++;
                        }
                        else
                        {
                            int lum = (int)(0.299 * pixels[cIdx + 2] + 0.587 * pixels[cIdx + 1] + 0.114 * pixels[cIdx]);
                            totalLum += lum;
                        }
                        cornerCount++;
                    }
                }
            }

            if (cornerCount > 0 && transparentCorners >= (cornerCount / 2))
            {
                return (true, false);
            }

            int opaqueCorners = cornerCount - transparentCorners;
            bool isLight = opaqueCorners > 0 && (totalLum / opaqueCorners) > 128;
            return (false, isLight);
        }

        private static bool IsPixelInk(byte red, byte g, byte b, byte a, bool isTransparentBg, bool isLightBg, int threshold)
        {
            if (isTransparentBg)
            {
                return a >= threshold;
            }
            if (a < threshold)
            {
                return false;
            }
            int lum = (int)(0.299 * red + 0.587 * g + 0.114 * b);
            return isLightBg ? (lum < threshold) : (lum >= threshold);
        }
    }
}
