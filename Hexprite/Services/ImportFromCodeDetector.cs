using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>How <see cref="ImportFromCodeDetector.TryInferDimensionsFromHexData"/> chose width/height.</summary>
    public enum ImportDimensionInferHint
    {
        None,
        FromLineStructure,
        FromByteCount,
        AmbiguousLineStructure,
    }

    /// <summary>
    /// Pure helpers for the Import from Code dialog: dimension hints, variable names,
    /// line-structure analysis, and XBM detection. Keeps parsing rules unit-testable without WPF.
    /// </summary>
    public static partial class ImportFromCodeDetector
    {
        /// <summary>Returns true if pasted code likely uses XBM / LSB-first byte layout.</summary>
        public static bool IsLikelyXbmFormat(string code) =>
            code.Contains("drawXBM", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("XBM", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("_bits", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("x_hot", StringComparison.OrdinalIgnoreCase);

        public static bool IsLikelyBinaryFormat(string code)
        {
            string cleanCode = StripComments(code);
            int hexCount = HexByteRegex.Count(cleanCode);
            int binCount = BinaryByteRegex.Count(cleanCode);
            return binCount > 0 && hexCount == 0;
        }

        /// <summary>Returns true if pasted code likely represents an HD44780 1602/2004 LiquidCrystal custom character (5x8).</summary>
        public static bool IsLikelyLiquidCrystalFormat(string code) =>
            code.Contains("createChar", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("LiquidCrystal", StringComparison.OrdinalIgnoreCase) ||
            (code.Contains("byte ", StringComparison.OrdinalIgnoreCase) && code.Contains("[8]", StringComparison.OrdinalIgnoreCase));

        public static bool IsLikely2DMatrixFormat(string code)
        {
            string cleanCode = StripComments(code);
            string body = ExtractArrayBody(cleanCode, out _);

            // Exclude animation arrays
            if (code.Contains("animation", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("anim", StringComparison.OrdinalIgnoreCase) ||
                code.Contains("Frame 0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Exclude packed hex bitmasks (e.g. 0x80, 0xC0, 0xFF) unless explicitly declared 1 byte per pixel or indexed
            if (HasPackedHexBitmasks(body) &&
                !code.Contains("1 byte per pixel", StringComparison.OrdinalIgnoreCase) &&
                !code.Contains("indexed", StringComparison.OrdinalIgnoreCase) &&
                !code.Contains("palette", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // 1. If explicit keywords for 1-byte-per-pixel / indexed / multi-color / palette-mapped sprite exist
            if (Indexed2DKeywordsRegex.IsMatch(code))
            {
                if (!HasPackedHexBitmasks(body))
                    return true;
            }

            // 2. If a companion palette/colormap definition exists alongside a sprite array
            if (PaletteDeclarationRegex.IsMatch(cleanCode))
            {
                if (!HasPackedHexBitmasks(body))
                    return true;
            }

            // 3. Check 2D C array declarations like player_sprite[4][4] or icon[16][16]
            var array2DMatches = Array2DDimRegex.Matches(cleanCode);
            foreach (Match m in array2DMatches)
            {
                string name = m.Groups[1].Value;
                string dim2Str = m.Groups[3].Value;
                TryResolveDimension(dim2Str, cleanCode, out int dim2);
                if (!IsPaletteTableName(name) && dim2 != 3)
                {
                    if (!HasPackedHexBitmasks(body))
                        return true;
                }
            }

            // 4. Check nested bracket/brace rows [[...], [...]] or {{...}, {...}}
            if (NestedRowBracesRegex.IsMatch(cleanCode) || NestedRowBracketsRegex.IsMatch(cleanCode) || PyJs2DArrayRegex.IsMatch(cleanCode))
            {
                if (!HasPackedHexBitmasks(body))
                    return true;
            }

            if (TryDetectNestedMatrixDimensions(code, out _, out _))
            {
                if (!HasPackedHexBitmasks(body))
                    return true;
            }

            // 5. Dimension vs. byte count equivalence: if detected dimensions W*H == totalBytes (and W > 1)
            int byteCount = CountDataBytes(code);
            if (byteCount > 0 && !HasPackedHexBitmasks(body))
            {
                if (TryParseExplicitDimensions(code, out int ew, out int eh, out _) && ew > 1 && eh > 0 && (ew * eh == byteCount))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the token body contains hex or binary bitmasks with values > 3 (characteristic of 1-bit packed monochrome bitmaps).
        /// </summary>
        public static bool HasPackedHexBitmasks(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return false;

            var matches = ByteTokenRegex.Matches(body);
            foreach (Match m in matches)
            {
                string val = m.Value;
                if (val.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                    val.StartsWith("\\x", StringComparison.OrdinalIgnoreCase))
                {
                    ReadOnlySpan<char> hex = val.AsSpan(2);
                    if (byte.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    {
                        if (b > 3) return true;
                    }
                }
                else if (val.StartsWith("0b", StringComparison.OrdinalIgnoreCase) || val.StartsWith("B", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Attempts to detect 2D matrix dimensions from nested row structures { {...}, {...} } or [ [...], [...] ].
        /// </summary>
        public static bool TryDetectNestedMatrixDimensions(string code, out int width, out int height)
        {
            width = height = 0;
            string cleanCode = StripComments(code);

            string body = ExtractArrayBody(cleanCode, out bool isBraced);
            if (string.IsNullOrWhiteSpace(body) || !isBraced) return false;

            var rowMatches = NestedInnerRowsRegex.Matches(body);
            if (rowMatches.Count == 0) return false;

            int rowCount = 0;
            int firstRowTokens = 0;

            foreach (Match m in rowMatches)
            {
                string rowContent = !string.IsNullOrEmpty(m.Groups[1].Value) ? m.Groups[1].Value : m.Groups[2].Value;
                var tokens = ByteTokenRegex.Matches(rowContent);
                if (tokens.Count == 0) continue;

                if (rowCount == 0)
                {
                    firstRowTokens = tokens.Count;
                }
                else if (tokens.Count != firstRowTokens)
                {
                    return false;
                }
                rowCount++;
            }

            if (rowCount >= 1 && firstRowTokens >= 1 && (rowCount >= 2 || firstRowTokens >= 2) &&
                rowCount <= 512 && firstRowTokens <= 512)
            {
                // Skip palette RGB table (e.g., palette[4][3] where tokens per row is 3 and row count is small <= 256) unless sprite keyword present
                if (firstRowTokens == 3 && rowCount <= 256 && !cleanCode.Contains("sprite", StringComparison.OrdinalIgnoreCase))
                    return false;

                width = firstRowTokens;
                height = rowCount;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Strategy 1–2 from the dialog: NAME_WIDTH / HEIGHT constants, usage comments, comment dimensions, and 2D array dimensions.
        /// </summary>
        public static bool TryParseExplicitDimensions(string code, out int width, out int height, out string detectionSource)
        {
            width = height = 0;
            detectionSource = "";

            string cleanCode = StripComments(code);
            string body = ExtractArrayBody(cleanCode, out _);

            // 1. Check explicit dimension constants / comments first (e.g. WIDTH=16, HEIGHT=8 or Width: 16 pixels, Height: 8 pixels)
            var widthMatch = WidthRegex.Match(code);
            var heightMatch = HeightRegex.Match(code);
            var adafruitMatch = AdafruitDrawBitmapRegex.Match(code);
            var u8g2BitmapMatch = U8g2DrawBitmapRegex.Match(code);
            var u8g2XbmMatch = DrawXbmRegex.Match(code);
            var plainCMatch = PlainCBitmapRegex.Match(code);
            var pythonMatch = PythonFrameBufferRegex.Match(code);
            var commentDimMatch = CommentDimRegex.Match(code);

            if (widthMatch.Success && heightMatch.Success &&
                TryParseDimensionToken(widthMatch.Groups[1].Value, out int cw) &&
                TryParseDimensionToken(heightMatch.Groups[1].Value, out int ch) &&
                cw > 0 && cw <= 512 && ch > 0 && ch <= 512)
            {
                width = cw;
                height = ch;
                detectionSource = "constants";
                return true;
            }

            if (adafruitMatch.Success)
            {
                width = int.Parse(adafruitMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(adafruitMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                detectionSource = "usage comment";
                return true;
            }

            if (u8g2XbmMatch.Success)
            {
                width = int.Parse(u8g2XbmMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(u8g2XbmMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                detectionSource = "usage comment";
                return true;
            }

            if (u8g2BitmapMatch.Success)
            {
                int bpr = int.Parse(u8g2BitmapMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                width = bpr * 8;
                height = int.Parse(u8g2BitmapMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                detectionSource = "usage comment";
                return true;
            }

            if (plainCMatch.Success)
            {
                width = int.Parse(plainCMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(plainCMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                detectionSource = "usage comment";
                return true;
            }

            if (pythonMatch.Success)
            {
                width = int.Parse(pythonMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                height = int.Parse(pythonMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                detectionSource = "usage comment";
                return true;
            }

            if (commentDimMatch.Success &&
                int.TryParse(commentDimMatch.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cdw) &&
                int.TryParse(commentDimMatch.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cdh) &&
                cdw > 0 && cdw <= 512 && cdh > 0 && cdh <= 512)
            {
                width = cdw;
                height = cdh;
                detectionSource = "comment dimension";
                return true;
            }

            // 2. Check 2D C array declarations like player_sprite[4][4], wide_ship_sprite[8][2], eye_animation[2][8], or block_animation[3][FRAME_SIZE]
            var array2DMatches = Array2DDimRegex.Matches(cleanCode);
            Match? best2DMatch = null;
            int bestDim1 = 0;
            int bestDim2 = 0;
            foreach (Match m in array2DMatches)
            {
                string name = m.Groups[1].Value;
                string dim1Str = m.Groups[2].Value;
                string dim2Str = m.Groups[3].Value;
                TryResolveDimension(dim1Str, code, out int d1);
                TryResolveDimension(dim2Str, code, out int d2);

                if (d2 == 0)
                {
                    // Fallback to counting tokens per inner row
                    if (TryDetectNestedMatrixDimensions(code, out int nw, out int nh))
                    {
                        d2 = nw;
                        if (d1 == 0) d1 = nh;
                    }
                }

                // Skip palette lookup tables (e.g. palette[4][3] or colors[16][3] for RGB)
                if (IsPaletteTableName(name) || (d2 == 3 && d1 <= 256))
                    continue;

                best2DMatch = m;
                bestDim1 = d1;
                bestDim2 = d2;
                break;
            }

            if (best2DMatch != null)
            {
                string name = best2DMatch.Groups[1].Value;
                int dim1 = bestDim1;
                int dim2 = bestDim2;

                // Check if this is an Animation Array [NUM_FRAMES][BYTES_PER_FRAME]
                bool isAnim = name.Contains("animation", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("anim", StringComparison.OrdinalIgnoreCase) ||
                              name.Contains("frames", StringComparison.OrdinalIgnoreCase) ||
                              code.Contains("Frame 0", StringComparison.OrdinalIgnoreCase);

                if (isAnim && dim2 > 0)
                {
                    if (TryGuessDimensionsFromByteCount(dim2, out int frameW, out int frameH))
                    {
                        width = frameW;
                        height = frameH;
                        detectionSource = string.Create(CultureInfo.InvariantCulture, $"animation ({dim1} frames @ {frameW}×{frameH})");
                        return true;
                    }
                }

                bool hasPackedHexMasks = HasPackedHexBitmasks(body);

                if (dim1 > 0 && dim2 > 0 && dim1 <= 512 && dim2 <= 512)
                {
                    // Check if this is a row-packed 1-bit monochrome bitmap [HEIGHT][BYTES_PER_ROW]
                    if (dim2 <= 16 && hasPackedHexMasks &&
                        !code.Contains("1 byte per pixel", StringComparison.OrdinalIgnoreCase) &&
                        !code.Contains("indexed", StringComparison.OrdinalIgnoreCase))
                    {
                        width = dim2 * 8;
                        height = dim1;
                        detectionSource = string.Create(CultureInfo.InvariantCulture, $"2D row-packed [H][{dim2}B] ({width}×{height})");
                        return true;
                    }

                    width = dim2;
                    height = dim1;
                    detectionSource = "2D array [H][W]";
                    return true;
                }

                if (dim1 == 0 && dim2 > 0 && dim2 <= 512)
                {
                    // Open first dimension [][W] - find height from nested rows or total bytes
                    if (TryDetectNestedMatrixDimensions(code, out int nw, out int nh) && nw == dim2)
                    {
                        if (dim2 <= 16 && hasPackedHexMasks)
                        {
                            width = dim2 * 8;
                            height = nh;
                            detectionSource = string.Create(CultureInfo.InvariantCulture, $"2D row-packed [][{dim2}B] ({width}×{height})");
                            return true;
                        }
                        width = nw;
                        height = nh;
                        detectionSource = string.Create(CultureInfo.InvariantCulture, $"2D array [][{dim2}]");
                        return true;
                    }
                    int totalB = CountDataBytes(code);
                    if (totalB > 0 && totalB % dim2 == 0)
                    {
                        width = dim2;
                        height = totalB / dim2;
                        detectionSource = string.Create(CultureInfo.InvariantCulture, $"2D array [][{dim2}]");
                        return true;
                    }
                }
            }

            // 3. Check for nested rows in Python / JS / JSON / C without bracket dims
            if (TryDetectNestedMatrixDimensions(code, out int nestW, out int nestH))
            {
                bool hasPackedHexMasks = HasPackedHexBitmasks(body);
                if (nestW <= 16 && hasPackedHexMasks &&
                    !code.Contains("1 byte per pixel", StringComparison.OrdinalIgnoreCase) &&
                    !code.Contains("indexed", StringComparison.OrdinalIgnoreCase))
                {
                    width = nestW * 8;
                    height = nestH;
                    detectionSource = string.Create(CultureInfo.InvariantCulture, $"nested row-packed ({width}×{height})");
                    return true;
                }

                width = nestW;
                height = nestH;
                detectionSource = string.Create(CultureInfo.InvariantCulture, $"nested rows ({nestW}×{nestH})");
                return true;
            }

            // 4. Check variable name embedded dimension (e.g. sprite_16x16)
            string? varName = DetectVariableName(code);
            if (varName != null)
            {
                var nameDimMatch = NameDimRegex.Match(varName);
                if (nameDimMatch.Success)
                {
                    width = int.Parse(nameDimMatch.Groups[1].Value, CultureInfo.InvariantCulture);
                    height = int.Parse(nameDimMatch.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (width > 0 && width <= 512 && height > 0 && height <= 512)
                    {
                        detectionSource = "variable name";
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool TryParseDimensionToken(string raw, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>
        /// Attempts to parse or resolve a dimension token (literal integer, hex, #define macro, or const variable).
        /// </summary>
        public static bool TryResolveDimension(string token, string code, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(token)) return false;
            token = token.Trim();

            if (TryParseDimensionToken(token, out value))
                return true;

            // Search for #define TOKEN VALUE
            var macroMatches = MacroDefineRegex.Matches(code);
            foreach (Match m in macroMatches)
            {
                if (string.Equals(m.Groups[1].Value, token, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDimensionToken(m.Groups[2].Value, out value))
                        return true;
                }
            }

            // Search for const int TOKEN = VALUE
            var constMatches = ConstVarRegex.Matches(code);
            foreach (Match m in constMatches)
            {
                if (string.Equals(m.Groups[1].Value, token, StringComparison.OrdinalIgnoreCase))
                {
                    if (TryParseDimensionToken(m.Groups[2].Value, out value))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns true if the variable name is indicative of a color palette, colormap, or lookup table.
        /// </summary>
        public static bool IsPaletteTableName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.Contains("palette", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("colormap", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("colors", StringComparison.OrdinalIgnoreCase) ||
                   name.Contains("lut", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "pal", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Isolates the primary sprite array body from surrounding comments, palette tables, and auxiliary definitions.
        /// </summary>
        public static string ExtractArrayBody(string cleanCode, out bool isBraced)
        {
            isBraced = false;

            // 1. Check if there is a specific 2D sprite array to isolate from palette/lookup tables
            var array2DMatches = Array2DDimRegex.Matches(cleanCode);
            foreach (Match m in array2DMatches)
            {
                string name = m.Groups[1].Value;
                string dim2Str = m.Groups[3].Value;
                TryResolveDimension(dim2Str, cleanCode, out int dim2);
                if (!IsPaletteTableName(name) && dim2 != 3)
                {
                    int afterDecl = m.Index + m.Length;
                    int eqPos = cleanCode.IndexOf('=', afterDecl);
                    int searchStart = eqPos >= 0 ? eqPos : afterDecl;
                    int openBrace = cleanCode.IndexOfAny(['{', '['], searchStart);
                    if (openBrace >= 0)
                    {
                        char openChar = cleanCode[openBrace];
                        int closeBrace = FindMatchingClosingDelimiter(cleanCode, openBrace, openChar);
                        if (closeBrace > openBrace)
                        {
                            isBraced = true;
                            return cleanCode.Substring(openBrace + 1, closeBrace - openBrace - 1);
                        }
                    }
                }
            }

            // 2. Check if multiple C array declarations exist, and skip palette tables if a sprite array exists
            var cVarMatches = CVariableNameRegex.Matches(cleanCode);
            if (cVarMatches.Count > 1)
            {
                foreach (Match m in cVarMatches)
                {
                    string name = m.Groups[1].Value;
                    if (!IsPaletteTableName(name))
                    {
                        int afterDecl = m.Index + m.Length;
                        int eqPos = cleanCode.IndexOf('=', afterDecl);
                        int searchStart = eqPos >= 0 ? eqPos : afterDecl;
                        int openBrace = cleanCode.IndexOfAny(['{', '['], searchStart);
                        if (openBrace >= 0)
                        {
                            char openChar = cleanCode[openBrace];
                            int closeBrace = FindMatchingClosingDelimiter(cleanCode, openBrace, openChar);
                            if (closeBrace > openBrace)
                            {
                                isBraced = true;
                                return cleanCode.Substring(openBrace + 1, closeBrace - openBrace - 1);
                            }
                        }
                    }
                }
            }

            // 3. Check for Python/JS variable assignments like player = [[ ... ]] or const sprite = [[ ... ]]
            var pyJsMatches = PyJs2DArrayRegex.Matches(cleanCode);
            foreach (Match m in pyJsMatches)
            {
                string name = m.Groups[1].Value;
                if (!IsPaletteTableName(name))
                {
                    int eqPos = cleanCode.IndexOf('=', m.Index);
                    int searchStart = eqPos >= 0 ? eqPos : m.Index;
                    int openBracket = cleanCode.IndexOf('[', searchStart);
                    if (openBracket >= 0)
                    {
                        int closeBracket = FindMatchingClosingDelimiter(cleanCode, openBracket, '[');
                        if (closeBracket > openBracket)
                        {
                            isBraced = true;
                            return cleanCode.Substring(openBracket + 1, closeBracket - openBracket - 1);
                        }
                    }
                }
            }

            // 4. Standard fallback: find outermost { ... } or [ ... ]
            int fallbackEq = cleanCode.IndexOf('=', StringComparison.Ordinal);
            int fallbackStart = fallbackEq >= 0 ? fallbackEq : 0;

            int braceStart = cleanCode.IndexOf('{', fallbackStart);
            int braceEnd = cleanCode.LastIndexOf('}');

            if (braceStart >= 0 && braceEnd > braceStart)
            {
                isBraced = true;
                return cleanCode.Substring(braceStart + 1, braceEnd - braceStart - 1);
            }

            int bracketStart = cleanCode.IndexOf('[', fallbackStart);
            int bracketEnd = cleanCode.LastIndexOf(']');
            if (bracketStart >= 0 && bracketEnd > bracketStart)
            {
                if (fallbackEq >= 0 || cleanCode.TrimStart().StartsWith('['))
                {
                    isBraced = true;
                    return cleanCode.Substring(bracketStart + 1, bracketEnd - bracketStart - 1);
                }
            }

            return cleanCode;
        }

        private static int FindMatchingClosingDelimiter(string text, int openIndex, char openChar)
        {
            char closeChar = openChar == '{' ? '}' : ']';
            int depth = 0;
            for (int i = openIndex; i < text.Length; i++)
            {
                if (text[i] == openChar)
                    depth++;
                else if (text[i] == closeChar)
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        public static int CountDataBytes(string code)
        {
            string cleanCode = StripComments(code);
            string body = ExtractArrayBody(cleanCode, out bool isBraced);

            int hexCount = HexByteRegex.Count(body);
            if (hexCount > 0)
            {
                hexCount += WideHexRegex.Count(body);
            }
            int binCount = BinaryByteRegex.Count(body);
            if (hexCount > 0 && binCount == 0) return hexCount;
            if (binCount > 0 && hexCount == 0) return binCount;
            if (hexCount > 0 && binCount > 0) return hexCount + binCount;

            if (isBraced)
            {
                int decCount = 0;
                foreach (Match m in DecimalByteRegex.Matches(body))
                {
                    if (byte.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                        decCount++;
                }
                if (decCount > 0) return decCount;
            }

            return 0;
        }

        /// <summary>
        /// Strips C-style single-line (//), Python single-line (#), and multi-line (/* */) comments from the code.
        /// </summary>
        public static string StripComments(string code)
        {
            if (string.IsNullOrEmpty(code)) return code;
            string cleanCode = SingleLineCommentRegex.Replace(code, " ");
            cleanCode = MultiLineCommentRegex.Replace(cleanCode, " ");
            return cleanCode;
        }

        /// <summary>
        /// Analyses data lines to find the most frequent hex-values-per-line (bytes-per-row).
        /// </summary>
        public static int DetectBytesPerRow(string code)
        {
            string[] lines = code.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            var bprCounts = new Dictionary<int, int>();

            foreach (string rawLine in lines)
            {
                string line = StripComments(rawLine);
                string trimmed = line.Trim();

                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                int openBrace = line.IndexOf('{', StringComparison.Ordinal);
                if (openBrace >= 0)
                    line = line[(openBrace + 1)..];

                int closeBrace = line.LastIndexOf('}');
                if (closeBrace >= 0)
                    line = line[..closeBrace];

                trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed))
                    continue;

                int count = HexByteRegex.Count(line);
                if (count == 0)
                    count = BinaryByteRegex.Count(line);
                if (count > 0)
                {
                    bprCounts.TryGetValue(count, out int existing);
                    bprCounts[count] = existing + 1;
                }
            }

            if (bprCounts.Count == 0) return 0;

            int bestBpr = 0, bestFreq = 0;
            foreach (var kvp in bprCounts)
            {
                if (kvp.Value > bestFreq)
                {
                    bestFreq = kvp.Value;
                    bestBpr = kvp.Key;
                }
            }

            return bestBpr;
        }

        /// <summary>
        /// Fallback dimension guess for 2D matrix / indexed color formats where 1 byte = 1 pixel.
        /// </summary>
        public static bool TryGuess2DMatrixDimensionsFromByteCount(int byteCount, out int width, out int height)
        {
            width = height = 0;
            if (byteCount <= 0) return false;

            int bestW = 0, bestH = 0;
            float bestRatio = float.MaxValue;

            for (int w = 1; w <= 512; w++)
            {
                if (byteCount % w != 0) continue;
                int h = byteCount / w;
                if (h > 0 && h <= 512)
                {
                    float ratio = w > h ? w / (float)h : h / (float)w;
                    if (ratio < bestRatio)
                    {
                        bestRatio = ratio;
                        bestW = w;
                        bestH = h;
                    }
                }
            }

            if (bestW > 0)
            {
                width = bestW;
                height = bestH;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Fallback dimension guess from total byte count (square preference, then common widths).
        /// </summary>
        public static bool TryGuessDimensionsFromByteCount(int byteCount, out int width, out int height)
        {
            width = height = 0;
            if (byteCount <= 0) return false;

            int[] candidates = [4, 8, 16, 24, 32, 48, 64, 96, 128, 256];
            
            int bestW = 0, bestH = 0;
            float bestRatio = float.MaxValue;

            foreach (int w in candidates)
            {
                int bpr = (int)Math.Ceiling(w / 8.0);
                if (byteCount % bpr != 0) continue;
                int h = byteCount / bpr;
                if (h > 0 && h <= 256)
                {
                    float ratio = w > h ? w / (float)h : h / (float)w;
                    
                    if (ratio < bestRatio)
                    {
                        bestRatio = ratio;
                        bestW = w;
                        bestH = h;
                    }
                }
            }

            if (bestW > 0)
            {
                width = bestW;
                height = bestH;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Line-structure analysis plus byte-count fallback (mirrors the Import from Code dialog heuristics).
        /// </summary>
        public static bool TryInferDimensionsFromData(
            string code,
            int byteCount,
            out int width,
            out int height,
            out ImportDimensionInferHint hint)
        {
            width = height = 0;
            hint = ImportDimensionInferHint.None;
            if (byteCount <= 0) return false;

            if (IsLikelyLiquidCrystalFormat(code))
            {
                width = 5;
                height = 8;
                hint = ImportDimensionInferHint.FromByteCount;
                return true;
            }

            if (IsLikely2DMatrixFormat(code))
            {
                if (TryGuess2DMatrixDimensionsFromByteCount(byteCount, out int m2dW, out int m2dH))
                {
                    width = m2dW;
                    height = m2dH;
                    hint = ImportDimensionInferHint.FromByteCount;
                    return true;
                }
            }

            int detectedBpr = DetectBytesPerRow(code);
            bool hasGuess = TryGuessDimensionsFromByteCount(byteCount, out int guessW, out int guessH);

            if (detectedBpr > 0 && detectedBpr < byteCount && byteCount % detectedBpr == 0)
            {
                int w = detectedBpr * 8;
                int h = byteCount / detectedBpr;

                if (hasGuess && w != h)
                {
                    float lineRatio = w > h ? w / (float)h : h / (float)w;
                    float guessRatio = guessW > guessH ? guessW / (float)guessH : guessH / (float)guessW;

                    // Override skewed line structures ONLY if the byte count strongly implies a perfect square.
                    // Formatting a square sprite with arbitrary line breaks is very common.
                    if (guessRatio == 1.0f && lineRatio > 1.5f)
                    {
                        width = guessW;
                        height = guessH;
                        hint = ImportDimensionInferHint.FromByteCount;
                        return true;
                    }
                }

                if (w > 0 && w <= 256 && h > 0 && h <= 256)
                {
                    width = w;
                    height = h;
                    hint = ImportDimensionInferHint.FromLineStructure;
                    return true;
                }
            }

            if (detectedBpr > 0 && byteCount % detectedBpr != 0)
                hint = ImportDimensionInferHint.AmbiguousLineStructure;
            else
                hint = ImportDimensionInferHint.FromByteCount;

            if (hasGuess)
            {
                width = guessW;
                height = guessH;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Extracts the bitmap variable name from common C / JavaScript / MicroPython patterns.
        /// </summary>
        public static string? DetectVariableName(string code)
        {
            string cleanCode = StripComments(code);
            var matches = CVariableNameRegex.Matches(cleanCode);
            string? fallbackName = null;

            foreach (Match match in matches)
            {
                string name = match.Groups[1].Value;
                if (name.Contains("palette", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("colormap", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("colors", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("lut", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("pal", StringComparison.OrdinalIgnoreCase))
                {
                    fallbackName ??= name;
                    continue;
                }
                return name;
            }

            var pyJsMatch = PyJs2DArrayRegex.Match(cleanCode);
            if (pyJsMatch.Success)
            {
                string name = pyJsMatch.Groups[1].Value;
                if (!name.Contains("palette", StringComparison.OrdinalIgnoreCase))
                    return name;
                fallbackName ??= name;
            }

            var pyMatch = PyVariableNameRegex.Match(cleanCode);
            if (pyMatch.Success)
            {
                string name = pyMatch.Groups[1].Value;
                if (!name.Contains("palette", StringComparison.OrdinalIgnoreCase))
                    return name;
                fallbackName ??= name;
            }

            if (fallbackName != null) return fallbackName;

            return null;
        }

        /// <summary>Expected byte count for a bitmap of <paramref name="width"/>×<paramref name="height"/> under <paramref name="format"/>.</summary>
        public static int ExpectedByteCount(int width, int height, ExportFormat format = ExportFormat.AdafruitGfx) =>
            format == ExportFormat.Indexed2D ? (width * height) :
            format == ExportFormat.LiquidCrystalChar ? 8 :
            height * (int)Math.Ceiling(width / 8.0);

        [GeneratedRegex(@"(?:0[xX]|\\x)[0-9a-fA-F]{1,4}\b")]
        private static partial Regex HexByteRegex { get; }

        [GeneratedRegex(@"(?:0[xX]|\\x)[0-9a-fA-F]{3,4}\b")]
        private static partial Regex WideHexRegex { get; }

        [GeneratedRegex(@"(?:0[bB][01]{1,8}\b|B[01]{5,8}\b|\b[01]{8}\b)")]
        private static partial Regex BinaryByteRegex { get; }

        [GeneratedRegex(@"(?:\b(?:[a-zA-Z0-9_]*?(?:width\b|_w\b)))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase)]
        private static partial Regex WidthRegex { get; }

        [GeneratedRegex(@"(?:\b(?:[a-zA-Z0-9_]*?(?:height\b|_h\b)))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase)]
        private static partial Regex HeightRegex { get; }

        [GeneratedRegex(@"(?://|/\*|\*|#)[^\r\n]*?\b(\d{1,4})\s*[xX×]\s*(\d{1,4})\b", RegexOptions.IgnoreCase)]
        private static partial Regex CommentDimRegex { get; }

        [GeneratedRegex(@"#\s*define\s+([a-zA-Z_][a-zA-Z0-9_]*)\s+(\d+|0[xX][0-9a-fA-F]+)\b")]
        private static partial Regex MacroDefineRegex { get; }

        [GeneratedRegex(@"(?:const|constexpr|static|readonly)?\s*(?:int|uint8_t|uint16_t|uint32_t|size_t|var|let|const)\s+([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*(\d+|0[xX][0-9a-fA-F]+)\b")]
        private static partial Regex ConstVarRegex { get; }

        [GeneratedRegex(@"\b\d{1,3}\b")]
        private static partial Regex DecimalByteRegex { get; }

        [GeneratedRegex(@"\b(glcdfont|font|palette|colormap|lut|pal)\b", RegexOptions.IgnoreCase)]
        private static partial Regex NonSpriteIdentifierRegex { get; }

        [GeneratedRegex(@"drawBitmap\([^,]+,\s*[^,]+,\s*[^,]+,\s*(\d+)\s*,\s*(\d+)")]
        private static partial Regex AdafruitDrawBitmapRegex { get; }

        [GeneratedRegex(@"u8g2\.drawBitmap\([^,]+,\s*[^,]+,\s*(\d+)\s*,\s*(\d+)")]
        private static partial Regex U8g2DrawBitmapRegex { get; }

        [GeneratedRegex(@"drawXBM\([^,]+,\s*[^,]+,\s*(\d+)\s*,\s*(\d+)")]
        private static partial Regex DrawXbmRegex { get; }
        [GeneratedRegex(@"as a (\d+)[x×](\d+) bitmap")]
        private static partial Regex PlainCBitmapRegex { get; }

        [GeneratedRegex(@"FrameBuffer\([^,]+,\s*(\d+)\s*,\s*(\d+)")]
        private static partial Regex PythonFrameBufferRegex { get; }

        [GeneratedRegex(@"(?<=\D|^)(\d+)[xX](\d+)(?=\D|$)")]
        private static partial Regex NameDimRegex { get; }

        [GeneratedRegex(@"(?://|#).*")]
        private static partial Regex SingleLineCommentRegex { get; }

        [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex MultiLineCommentRegex { get; }

        [GeneratedRegex(@"(?:(?:alignas\s*\([^)]*\)|__attribute__\s*\(\([^)]*\)\)|[a-zA-Z_:][a-zA-Z0-9_:]*|\*)\s+)+([a-zA-Z_][a-zA-Z0-9_]*)\s*\[", RegexOptions.Multiline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex CVariableNameRegex { get; }

        [GeneratedRegex(@"(?:extern|inline|PROGMEM|U8X8_PROGMEM|uint8_t|uint16_t|uint32_t|const|static|unsigned|signed|char|byte|short|int|long|\*|\s)*\b([a-zA-Z_][a-zA-Z0-9_]*)\s*\[\s*([a-zA-Z0-9_]*)\s*\]\s*\[\s*([a-zA-Z0-9_]+)\s*\]", RegexOptions.Multiline)]
        private static partial Regex Array2DDimRegex { get; }

        [GeneratedRegex(@"\{\s*\{[^{}]+\}\s*,\s*\{", RegexOptions.Multiline)]
        private static partial Regex NestedRowBracesRegex { get; }

        [GeneratedRegex(@"\[\s*\[[^\[\]]+\]\s*,\s*\[", RegexOptions.Multiline)]
        private static partial Regex NestedRowBracketsRegex { get; }

        [GeneratedRegex(@"(?:\{([^{}]+)\}|\[([^\[\]]+)\])")]
        private static partial Regex NestedInnerRowsRegex { get; }

        [GeneratedRegex(@"(?:const|let|var)?\s*\b([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*\[\s*\[", RegexOptions.Multiline)]
        private static partial Regex PyJs2DArrayRegex { get; }

        [GeneratedRegex(@"(?:0[xX]|\\x)[0-9a-fA-F]{1,4}\b|0[bB][01]{1,8}\b|B[01]{5,8}\b|\b[01]{8}\b|\b\d{1,3}\b")]
        private static partial Regex ByteTokenRegex { get; }

        [GeneratedRegex(@"([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*bytearray\s*\(", RegexOptions.Multiline)]
        private static partial Regex PyVariableNameRegex { get; }

        [GeneratedRegex(@"\b(?:1\s*byte\s*(?:per|\/)\s*p(?:ixe)?l|multi-?color|palette(?:\s+(?:index|indices|table|lookup|referenced|referencing|values))?|color\s+index(?:es|ices)?|indexed(?:\s+(?:color|sprite|2d|matrix|array))?|pixel\s+(?:matrix|array|grid))\b", RegexOptions.IgnoreCase)]
        private static partial Regex Indexed2DKeywordsRegex { get; }

        [GeneratedRegex(@"(?:extern|inline|PROGMEM|U8X8_PROGMEM|uint8_t|uint16_t|uint32_t|unsigned\s+long|const|static|unsigned|signed|char|byte|short|int|long|\*|\s)*\b(palette|colors|colormap|lut|pal)\s*\[", RegexOptions.IgnoreCase)]
        private static partial Regex PaletteDeclarationRegex { get; }
    }
}
