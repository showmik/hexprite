using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hexprite.Core;

namespace Hexprite.Services
{
    public partial class FileImportExportService : IFileImportExportService
    {
        [GeneratedRegex(@"(?:(?:alignas\s*\([^)]*\)|__attribute__\s*\(\([^)]*\)\)|[a-zA-Z_:][a-zA-Z0-9_:]*|\*)\s+)+([a-zA-Z_][a-zA-Z0-9_]*)\s*(?:\[[^\]]*\])+\s*(?:[a-zA-Z_][a-zA-Z0-9_]*\s*)*=\s*\{(?:[^{}]|\{(?:[^{}]|\{[^}]*\})*\})*\}\s*;", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex CArrayRegex { get; }

        [GeneratedRegex(@"([a-zA-Z_][a-zA-Z0-9_]*)\s*=\s*(?:bytearray|bytes)\s*\(\s*\[.*?\]\s*\)", RegexOptions.Singleline, matchTimeoutMilliseconds: 2000)]
        private static partial Regex PythonByteArrayRegex { get; }

        [GeneratedRegex(@"(//.*|/\*.*?\*/)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex LineEndCommentRegex { get; }

        [GeneratedRegex(@"0x[0-9a-fA-F]+|\bB[01]{8}\b|0[bB][01]{1,8}\b|\d+,", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex DataByteTokenRegex { get; }

        [GeneratedRegex(@"\s*(//.*|/\*.*?\*/)\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex StripLineEndCommentRegex { get; }

        private static bool TryParseDimensionNumber(string raw, out int value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(raw)) return false;
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        public List<DetectedSprite> ExtractSpritesFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or whitespace.", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}", filePath);

            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > 50 * 1024 * 1024) // 50MB safety limit
                throw new InvalidOperationException($"File is too large ({fileInfo.Length / (1024 * 1024)} MB). Maximum allowed size for code parsing is 50 MB.");

            var results = new List<DetectedSprite>();
            string rawText = ReadAllTextWithRetry(filePath);
            string text = ImportFromCodeDetector.StripComments(rawText);

            foreach (Match match in CArrayRegex.Matches(text))
            {
                string name = match.Groups[1].Value;
                if (ImportFromCodeDetector.IsPaletteTableName(name))
                    continue;

                string snippet = match.Value;

                ExportFormat format;
                if (ImportFromCodeDetector.IsLikely2DMatrixFormat(snippet))
                {
                    format = ExportFormat.Indexed2D;
                }
                else if (ImportFromCodeDetector.IsLikelyXbmFormat(snippet))
                {
                    format = ExportFormat.U8g2DrawXBM;
                }
                else if (ImportFromCodeDetector.IsLikelyBinaryFormat(snippet))
                {
                    format = ExportFormat.RawBinary;
                }
                else
                {
                    format = ExportFormat.AdafruitGfx;
                }

                int byteCount = ImportFromCodeDetector.CountDataBytes(snippet);
                int width = 16, height = 16;

                // Extract true surrounding context from rawText using variable name location
                // to avoid comment-stripping offset misalignment or false matches in comments/substrings
                var varMatch = Regex.Match(rawText, @"\b" + Regex.Escape(name) + @"\s*(?:\[|=)", RegexOptions.None, TimeSpan.FromSeconds(1));
                int rawIndex = varMatch.Success ? varMatch.Index : rawText.IndexOf(name, StringComparison.Ordinal);
                string context = snippet;
                if (rawIndex >= 0)
                {
                    int matchStart = Math.Max(0, rawIndex - 500);
                    int matchEnd = Math.Min(rawText.Length, rawIndex + snippet.Length + 100);
                    context = rawText[matchStart..matchEnd];
                }

                var candidates = GetCandidateVariableIdentifiers(name);
                bool explicitFound = false;

                foreach (var cand in candidates)
                {
                    string escapedName = Regex.Escape(cand);
                    var nameWidthMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_width\b|_w\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    var nameHeightMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_height\b|_h\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                    if (nameWidthMatch.Success && nameHeightMatch.Success &&
                        TryParseDimensionNumber(nameWidthMatch.Groups[1].Value, out int specW) &&
                        TryParseDimensionNumber(nameHeightMatch.Groups[1].Value, out int specH) &&
                        specW > 0 && specW <= 512 && specH > 0 && specH <= 512)
                    {
                        width = specW;
                        height = specH;
                        explicitFound = true;
                        break;
                    }
                }

                if (!explicitFound)
                {
                    if (ImportFromCodeDetector.TryParseExplicitDimensions(snippet, out int w1, out int h1, out _))
                    {
                        width = w1; height = h1;
                    }
                    else if (ImportFromCodeDetector.TryInferDimensionsFromData(snippet, byteCount, out int w2, out int h2, out _))
                    {
                        width = w2; height = h2;
                    }
                }

                int frameCount = 1;
                foreach (var cand in candidates)
                {
                    string escapedName = Regex.Escape(cand);
                    var nameFrameMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_frames\b|_frame_count\b|_num_frames\b|_count\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    if (nameFrameMatch.Success && TryParseDimensionNumber(nameFrameMatch.Groups[1].Value, out int specFrames) && specFrames > 0 && specFrames <= 256)
                    {
                        frameCount = specFrames;
                        break;
                    }
                }

                if (frameCount == 1)
                {
                    var bracketMatch = Regex.Match(snippet, @"\b" + Regex.Escape(name) + @"\s*\[(\d+|0[xX][0-9a-fA-F]+)\]\s*\[", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
                    if (bracketMatch.Success && TryParseDimensionNumber(bracketMatch.Groups[1].Value, out int bracketFrames) && bracketFrames > 1 && bracketFrames <= 256)
                    {
                        frameCount = bracketFrames;
                    }
                    else if (width > 0 && height > 0 && byteCount > 0)
                    {
                        int bytesPerRow = (width + 7) / 8;
                        int bytesPerFrame = (format == ExportFormat.Indexed2D) ? (width * height) : (height * bytesPerRow);
                        if (bytesPerFrame > 0 && byteCount > bytesPerFrame && (byteCount % bytesPerFrame == 0))
                        {
                            frameCount = byteCount / bytesPerFrame;
                        }
                    }
                }

                results.Add(new DetectedSprite
                {
                    Name = name,
                    Width = width,
                    Height = height,
                    FrameCount = frameCount,
                    CodeSnippet = snippet,
                    Format = format,
                });
            }

            // Match Python bytearrays / bytes: name = bytearray([ ... ]) or name = bytes([ ... ])
            foreach (Match match in PythonByteArrayRegex.Matches(text))
            {
                string name = match.Groups[1].Value;
                string snippet = match.Value;

                var format = ExportFormat.MicroPython;

                int byteCount = ImportFromCodeDetector.CountDataBytes(snippet);
                int width = 16, height = 16;

                var varMatch = Regex.Match(rawText, @"\b" + Regex.Escape(name) + @"\s*=", RegexOptions.None, TimeSpan.FromSeconds(1));
                int rawIndex = varMatch.Success ? varMatch.Index : rawText.IndexOf(name, StringComparison.Ordinal);
                string context = snippet;
                if (rawIndex >= 0)
                {
                    int matchStart = Math.Max(0, rawIndex - 500);
                    int matchEnd = Math.Min(rawText.Length, rawIndex + snippet.Length + 100);
                    context = rawText[matchStart..matchEnd];
                }

                var candidates = GetCandidateVariableIdentifiers(name);
                bool explicitFound = false;

                foreach (var cand in candidates)
                {
                    string escapedName = Regex.Escape(cand);
                    var nameWidthMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_width\b|_w\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    var nameHeightMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_height\b|_h\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                    if (nameWidthMatch.Success && nameHeightMatch.Success &&
                        TryParseDimensionNumber(nameWidthMatch.Groups[1].Value, out int specW) &&
                        TryParseDimensionNumber(nameHeightMatch.Groups[1].Value, out int specH) &&
                        specW > 0 && specW <= 512 && specH > 0 && specH <= 512)
                    {
                        width = specW;
                        height = specH;
                        explicitFound = true;
                        break;
                    }
                }

                if (!explicitFound)
                {
                    if (ImportFromCodeDetector.TryParseExplicitDimensions(snippet, out int w1, out int h1, out _))
                    {
                        width = w1; height = h1;
                    }
                    else if (ImportFromCodeDetector.TryInferDimensionsFromData(snippet, byteCount, out int w2, out int h2, out _))
                    {
                        width = w2; height = h2;
                    }
                }

                int frameCount = 1;
                foreach (var cand in candidates)
                {
                    string escapedName = Regex.Escape(cand);
                    var nameFrameMatch = Regex.Match(context, @"(?:\b" + escapedName + @"(?:_frames\b|_frame_count\b|_num_frames\b|_count\b))\s*(?:=|:|\s)\s*(\d+|0[xX][0-9a-fA-F]+)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                    if (nameFrameMatch.Success && TryParseDimensionNumber(nameFrameMatch.Groups[1].Value, out int specFrames) && specFrames > 0 && specFrames <= 256)
                    {
                        frameCount = specFrames;
                        break;
                    }
                }

                if (frameCount == 1 && width > 0 && height > 0 && byteCount > 0)
                {
                    int bytesPerRow = (width + 7) / 8;
                    int bytesPerFrame = height * bytesPerRow;
                    if (bytesPerFrame > 0 && byteCount > bytesPerFrame && (byteCount % bytesPerFrame == 0))
                    {
                        frameCount = byteCount / bytesPerFrame;
                    }
                }

                results.Add(new DetectedSprite
                {
                    Name = name,
                    Width = width,
                    Height = height,
                    FrameCount = frameCount,
                    CodeSnippet = snippet,
                    Format = format,
                });
            }

            return results;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Threading.SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
        private static System.Threading.SemaphoreSlim GetFileLock(string filePath) => _fileLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new System.Threading.SemaphoreSlim(1, 1));

        public string UpdateSpriteInFile(string filePath, string variableName, string newCodeSnippet, int? newWidth = null, int? newHeight = null, int? newFrameCount = null)
        {
            var fileLock = GetFileLock(filePath);
            fileLock.Wait();
            try
            {
                string text = ReadAllTextWithRetry(filePath);
                string lineEnding = DetectLineEnding(text);

                // Backup (only if not already backed up to preserve the original clean state)
                string backupPath = GetBackupPath(filePath);
                if (!File.Exists(backupPath) && !File.Exists(GetLegacyBackupPath(filePath)))
                {
                    SafeFileIo.CopyAtomic(filePath, backupPath, overwrite: false);
                }

                bool replaced = false;

                // Try C-style array first
                // Group 1: Signature prefix (supports qualifiers, namespace, pointers, attributes)
                // Group 2: Array body and brackets (supports up to 2 nested levels of braces)
                var cArrayRegex = new Regex(
                    @"((?:(?:alignas\s*\([^)]*\)|__attribute__\s*\(\([^)]*\)\)|[a-zA-Z_:][a-zA-Z0-9_:]*|\*)\s+)+)" + 
                    Regex.Escape(variableName) + 
                    @"(\s*(?:\[[^\]]*\])+\s*(?:[a-zA-Z_][a-zA-Z0-9_]*\s*)*=\s*\{(?:[^{}]|\{(?:[^{}]|\{[^}]*\})*\})*\}\s*;)",
                    RegexOptions.Singleline, TimeSpan.FromSeconds(2));

                var match = cArrayRegex.Match(text);
                if (match.Success)
                {
                    string originalSignature = match.Groups[1].Value;

                    var newMatch = cArrayRegex.Match(newCodeSnippet);
                    if (newMatch.Success)
                    {
                        string newBody = PreserveRowComments(match.Groups[2].Value, newMatch.Groups[2].Value, lineEnding);
                        string adaptedSnippet = originalSignature + variableName + newBody;
                        text = string.Concat(text.AsSpan()[..match.Index], adaptedSnippet, text.AsSpan(match.Index + match.Length));
                    }
                    else
                    {
                        string newBody = PreserveRowComments(match.Groups[2].Value, newCodeSnippet, lineEnding);
                        string adaptedSnippet = originalSignature + variableName + (newBody.StartsWith('[') || newBody.StartsWith(' ') ? newBody : (" " + newBody));
                        text = string.Concat(text.AsSpan()[..match.Index], adaptedSnippet, text.AsSpan(match.Index + match.Length));
                    }
                    replaced = true;
                }
                else
                {
                    // Try Python bytearrays / bytes
                    var pyRegex = new Regex(
                        @"\b" + Regex.Escape(variableName) + @"\s*=\s*(?:bytearray|bytes)\s*\(\s*\[.*?\]\s*\)",
                        RegexOptions.Singleline, TimeSpan.FromSeconds(2));

                    var pyMatch = pyRegex.Match(text);
                    if (pyMatch.Success)
                    {
                        text = string.Concat(text.AsSpan()[..pyMatch.Index], newCodeSnippet, text.AsSpan(pyMatch.Index + pyMatch.Length));
                        replaced = true;
                    }
                }

                if (!replaced)
                {
                    throw new InvalidOperationException($"Could not find the array for '{variableName}' in the file.");
                }

                // Update dimension constants (#define NAME_WIDTH/HEIGHT or Python NAME_WIDTH = N)
                // so they stay in sync with the array data.
                if (newWidth.HasValue && newHeight.HasValue)
                {
                    text = UpdateDimensionConstants(text, variableName, newWidth.Value, newHeight.Value, newFrameCount);
                }

                WriteAllTextWithRetry(filePath, text);
                return text;
            }
            finally
            {
                fileLock.Release();
            }
        }

        private static string DetectLineEnding(string text)
        {
            int crlfCount = 0;
            int lfCount = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        crlfCount++;
                    }
                }
                else if (text[i] == '\n')
                {
                    lfCount++;
                }
            }
            return crlfCount >= lfCount && crlfCount > 0 ? "\r\n" : (lfCount > 0 ? "\n" : Environment.NewLine);
        }

        private static string PreserveRowComments(string oldBody, string newBody, string? preferredLineEnding = null)
        {
            string lineEnding = preferredLineEnding ?? DetectLineEnding(oldBody);
            var oldLines = oldBody.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            var newLines = newBody.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);

            var oldComments = new List<string?>();
            foreach (var line in oldLines)
            {
                var match = LineEndCommentRegex.Match(line);
                if (match.Success)
                {
                    if (DataByteTokenRegex.IsMatch(line))
                    {
                        oldComments.Add(match.Groups[1].Value);
                    }
                }
                else if (DataByteTokenRegex.IsMatch(line))
                {
                    oldComments.Add(null);
                }
            }

            int newDataRowCount = newLines.Count(l => DataByteTokenRegex.IsMatch(l));
            if (newDataRowCount != oldComments.Count)
            {
                // Shape changed (e.g. canvas resized or different format), comments will be misaligned, so drop them.
                return string.Join(lineEnding, newLines);
            }

            var mergedLines = new List<string>();
            int commentIndex = 0;
            foreach (var line in newLines)
            {
                if (DataByteTokenRegex.IsMatch(line) && commentIndex < oldComments.Count)
                {
                    string? comment = oldComments[commentIndex++];
                    if (!string.IsNullOrEmpty(comment))
                    {
                        string cleanLine = LineEndCommentRegex.Replace(line, "").TrimEnd();
                        mergedLines.Add($"{cleanLine} {comment}");
                    }
                    else
                    {
                        mergedLines.Add(line);
                    }
                }
                else
                {
                    mergedLines.Add(line);
                }
            }

            return string.Join(lineEnding, mergedLines);
        }

        private static List<string> GetCandidateVariableIdentifiers(string name)
        {
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { name };

            string[] suffixes = ["_bits", "_data", "_bitmap", "_sprite", "_img"];
            string[] prefixes = ["spr_", "img_", "g_spr_", "g_img_", "g_", "s_", "k_"];

            foreach (var prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length > prefix.Length)
                {
                    candidates.Add(name[prefix.Length..]);
                }
            }

            var currentCandidates = candidates.ToList();
            foreach (var cand in currentCandidates)
            {
                foreach (var suffix in suffixes)
                {
                    if (cand.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && cand.Length > suffix.Length)
                    {
                        candidates.Add(cand[..^suffix.Length]);
                    }
                }
            }

            return [.. candidates];
        }

        private static string GetBackupPath(string originalPath)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string backupDir = string.IsNullOrEmpty(appData) 
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups")
                : Path.Combine(appData, "Hexprite", "Backups");
            string safeName = Path.GetFileName(originalPath);
            string hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(originalPath)))[..12];
            return Path.Combine(backupDir, $"{safeName}.{hash}.bak");
        }

        private static string GetLegacyBackupPath(string originalPath)
        {
            string dir = Path.GetDirectoryName(originalPath) ?? string.Empty;
            string fileName = Path.GetFileName(originalPath);
            return Path.Combine(dir, $"{fileName}.hexprite.bak");
        }

        private static string ReadAllTextWithRetry(string filePath, int maxRetries = 5, int delayMs = 100)
        {
            return SafeFileIo.ReadAllTextWithRetry(filePath, maxRetries);
        }

        private static void WriteAllTextWithRetry(string filePath, string content, int maxRetries = 5, int delayMs = 100)
        {
            SafeFileIo.WriteAllTextAtomic(filePath, content, maxRetries, createBackup: false);
        }

        public string RestoreSpriteInFile(string filePath)
        {
            var fileLock = GetFileLock(filePath);
            fileLock.Wait();
            try
            {
                string backupPath = GetBackupPath(filePath);
                if (!File.Exists(backupPath))
                {
                    backupPath = GetLegacyBackupPath(filePath);
                    if (!File.Exists(backupPath))
                    {
                        throw new InvalidOperationException("No backup found to restore.");
                    }
                }

                SafeFileIo.CopyAtomic(backupPath, filePath, overwrite: true);
                return SafeFileIo.ReadAllTextWithRetry(backupPath);
            }
            finally
            {
                fileLock.Release();
            }
        }

        public bool HasBackup(string filePath)
        {
            return File.Exists(GetBackupPath(filePath)) || File.Exists(GetLegacyBackupPath(filePath));
        }

        /// <summary>
        /// Updates any #define, const/constexpr variable, or assignment-style dimension constants that match
        /// the variable name pattern (e.g. <c>#define ICON_WIDTH 16</c>, <c>#define ICON_W 16</c>,
        /// <c>const int ICON_WIDTH = 16</c>, <c>ICON_WIDTH = 16</c>, <c>#define ICON_FRAMES 4</c>)
        /// so they stay in sync with the array data.
        /// Handles C-style (#define, const, constexpr), XBM-style (lowercase), and Python-style (assignment),
        /// including automatic prefix and suffix stripping (e.g. <c>spr_coin</c> -> <c>COIN_FRAMES</c>)
        /// and supporting both hex (0x10) and decimal literals without syntax corruption.
        /// </summary>
        private static string UpdateDimensionConstants(string text, string variableName, int width, int height, int? frameCount = null)
        {
            var namesToTry = GetCandidateVariableIdentifiers(variableName);

            string widthStr = width.ToString(CultureInfo.InvariantCulture);
            string heightStr = height.ToString(CultureInfo.InvariantCulture);
            string? frameCountStr = frameCount?.ToString(CultureInfo.InvariantCulture);

            foreach (var name in namesToTry)
            {
                string escaped = Regex.Escape(name);

                // C / XBM style: #define name_WIDTH 16 or #define name_W 16 (hex or decimal)
                text = Regex.Replace(text,
                    @"(#define\s+" + escaped + @"_(?:WIDTH\b|W\b)\s+)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + widthStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                text = Regex.Replace(text,
                    @"(#define\s+" + escaped + @"_(?:HEIGHT\b|H\b)\s+)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + heightStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                // C/C++ const / constexpr / static const style: const int name_WIDTH = 16
                text = Regex.Replace(text,
                    @"((?:const|constexpr|static\s+const)?\s*(?:int|uint8_t|uint16_t|uint32_t|size_t|auto)\s+" + escaped + @"_(?:WIDTH\b|W\b)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + widthStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                text = Regex.Replace(text,
                    @"((?:const|constexpr|static\s+const)?\s*(?:int|uint8_t|uint16_t|uint32_t|size_t|auto)\s+" + escaped + @"_(?:HEIGHT\b|H\b)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + heightStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                // Python style: NAME_WIDTH = 16 / NAME_W = 16
                text = Regex.Replace(text,
                    @"(" + escaped + @"_(?:WIDTH\b|W\b)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + widthStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                text = Regex.Replace(text,
                    @"(" + escaped + @"_(?:HEIGHT\b|H\b)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                    "${1}" + heightStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                if (frameCountStr != null)
                {
                    // C style: #define name_FRAMES / name_FRAME_COUNT / name_NUM_FRAMES / name_FRAME_NUM / name_COUNT 4
                    text = Regex.Replace(text,
                        @"(#define\s+" + escaped + @"_(?:FRAMES|FRAME_COUNT|NUM_FRAMES|FRAME_NUM|COUNT)\s+)(?:0[xX][0-9a-fA-F]+|\d+)",
                        "${1}" + frameCountStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                    // C/C++ const / constexpr / static const style
                    text = Regex.Replace(text,
                        @"((?:const|constexpr|static\s+const)?\s*(?:int|uint8_t|uint16_t|uint32_t|size_t|auto)\s+" + escaped + @"_(?:FRAMES|FRAME_COUNT|NUM_FRAMES|FRAME_NUM|COUNT)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                        "${1}" + frameCountStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

                    // Python style: NAME_FRAMES = 4 / NAME_FRAME_COUNT = 4
                    text = Regex.Replace(text,
                        @"(" + escaped + @"_(?:FRAMES|FRAME_COUNT|NUM_FRAMES|FRAME_NUM|COUNT)\s*=\s*)(?:0[xX][0-9a-fA-F]+|\d+)",
                        "${1}" + frameCountStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
                }
            }

            return text;
        }

        /// <summary>
        /// Removes backup files older than <paramref name="maxAgeDays"/> days
        /// to prevent unbounded disk growth.
        /// </summary>
        public void CleanupOldBackups(int maxAgeDays = 30)
        {
            var dirs = new List<string>();
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrEmpty(appData))
                dirs.Add(Path.Combine(appData, "Hexprite", "Backups"));
            dirs.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups"));

            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
            foreach (var dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!Directory.Exists(dir)) continue;

                foreach (string file in Directory.GetFiles(dir, "*.bak"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                            File.Delete(file);
                    }
                    catch (IOException) { /* skip locked files */ }
                }
            }
        }
    }
}
