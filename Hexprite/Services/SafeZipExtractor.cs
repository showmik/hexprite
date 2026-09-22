using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Hexprite.Services
{
    /// <summary>
    /// Configuration options for hardened, secure ZIP extraction.
    /// </summary>
    public record SafeZipOptions
    {
        public long MaxTotalDecompressedBytes { get; init; } = 100 * 1024 * 1024; // 100 MB
        public long MaxSingleEntryBytes { get; init; } = 10 * 1024 * 1024; // 10 MB
        public int MaxEntries { get; init; } = 2000;
        public double MaxCompressionRatio { get; init; } = 100.0;

        /// <summary>
        /// Permitted extensions for extracted assets. If non-empty, files with extensions outside this set will be rejected.
        /// </summary>
        public IReadOnlySet<string> AllowedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bm", ".png", ".bmp", ".jpg", ".jpeg", ".txt", ".json", ".c", ".h", ".sub", ".fmf", ".sdr",
            ".hexp", ".hexpack", ".hexfont", ".hexpfont"
        };

        /// <summary>
        /// Explicitly blocked executable and script extensions.
        /// </summary>
        public IReadOnlySet<string> BlockedExtensions { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".dll", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
            ".wsf", ".wsh", ".msc", ".msi", ".msp", ".com", ".scr", ".pif", ".reg", ".lnk",
            ".hta", ".cpl", ".inf", ".ins", ".isp", ".sh", ".bash", ".bin"
        };

        public static readonly SafeZipOptions Default = new();
    }

    /// <summary>
    /// Hardened ZIP extractor providing protection against:
    /// 1. Zip Slip (path traversal beyond target directory)
    /// 2. Sibling directory escape via prefix-match flaws
    /// 3. Zip Bombs &amp; Disk/RAM Exhaustion (max size, entries, ratio)
    /// 4. Arbitrary executable extraction into user directories
    /// 5. Windows reserved device names (CON, PRN, AUX, NUL, COM1-9, LPT1-9) and ADS streams
    /// </summary>
    public static class SafeZipExtractor
    {
        private static readonly char[] PathSeparators = ['/', '\\'];

        private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public static int SafeExtractToDirectory(string zipFilePath, string targetDirectory, SafeZipOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);
            if (!File.Exists(zipFilePath))
            {
                throw new FileNotFoundException("ZIP file not found.", zipFilePath);
            }

            using var stream = File.OpenRead(zipFilePath);
            return SafeExtractToDirectory(stream, targetDirectory, options);
        }

        public static int SafeExtractToDirectory(Stream zipStream, string targetDirectory, SafeZipOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(zipStream);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

            options ??= SafeZipOptions.Default;

            string fullTargetDir = Path.GetFullPath(targetDirectory);
            if (!fullTargetDir.EndsWith(Path.DirectorySeparatorChar) && !fullTargetDir.EndsWith(Path.AltDirectorySeparatorChar))
            {
                fullTargetDir += Path.DirectorySeparatorChar;
            }

            if (!Directory.Exists(fullTargetDir))
            {
                Directory.CreateDirectory(fullTargetDir);
            }

            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > options.MaxEntries)
            {
                throw new InvalidOperationException($"ZIP archive exceeds maximum entry limit ({archive.Entries.Count} > {options.MaxEntries}).");
            }

            long totalExtractedBytes = 0;
            int extractedFileCount = 0;

            foreach (var entry in archive.Entries)
            {
                string rawName = entry.FullName;
                if (string.IsNullOrWhiteSpace(rawName))
                {
                    continue;
                }

                // Disallow alternate data streams (e.g. file:stream)
                if (rawName.Contains(':'))
                {
                    throw new InvalidOperationException($"Invalid entry path contains stream delimiter: '{rawName}'.");
                }

                // Check for reserved Windows device names in path segments
                string[] segments = rawName.Split(PathSeparators, StringSplitOptions.RemoveEmptyEntries);
                foreach (string seg in segments)
                {
                    string nameOnly = Path.GetFileNameWithoutExtension(seg);
                    if (ReservedDeviceNames.Contains(nameOnly))
                    {
                        throw new InvalidOperationException($"Entry path contains reserved device name '{nameOnly}': '{rawName}'.");
                    }
                }

                // Directory entry check
                bool isDirectory = rawName.EndsWith('/') || rawName.EndsWith('\\');
                string destPath = Path.GetFullPath(Path.Combine(fullTargetDir, rawName.Replace('/', Path.DirectorySeparatorChar)));

                // Zip Slip check: destination must strictly begin with fullTargetDir
                if (!destPath.StartsWith(fullTargetDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Zip Slip vulnerability detected in path: '{rawName}'.");
                }

                string relPath = Path.GetRelativePath(fullTargetDir, destPath);
                if (relPath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relPath))
                {
                    throw new InvalidOperationException($"Zip Slip traversal detected: '{rawName}'.");
                }

                if (isDirectory)
                {
                    Directory.CreateDirectory(destPath);
                    continue;
                }

                // File extension whitelist and blacklist check
                string ext = Path.GetExtension(rawName);
                if (options.BlockedExtensions.Contains(ext))
                {
                    throw new InvalidOperationException($"ZIP entry contains forbidden executable/script extension '{ext}': '{rawName}'.");
                }

                if (options.AllowedExtensions.Count > 0 && !options.AllowedExtensions.Contains(ext))
                {
                    // Skip or reject non-asset files
                    throw new InvalidOperationException($"ZIP entry extension '{ext}' is not permitted for asset packs: '{rawName}'.");
                }

                // Size and ratio checks
                if (entry.Length > options.MaxSingleEntryBytes)
                {
                    throw new InvalidOperationException($"ZIP entry '{rawName}' uncompressed size ({entry.Length} bytes) exceeds limit ({options.MaxSingleEntryBytes} bytes).");
                }

                if (entry.CompressedLength > 0)
                {
                    double ratio = (double)entry.Length / entry.CompressedLength;
                    if (ratio > options.MaxCompressionRatio && entry.Length > 1024 * 100)
                    {
                        throw new InvalidOperationException($"Suspicious compression ratio ({ratio:F1}:1) detected for '{rawName}'.");
                    }
                }

                string? parentDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Stream copy with running total check for Zip Bomb protection
                using (var entryStream = entry.Open())
                using (var outputStream = File.Create(destPath))
                {
                    byte[] buffer = new byte[8192];
                    int read;
                    while ((read = entryStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        totalExtractedBytes += read;
                        if (totalExtractedBytes > options.MaxTotalDecompressedBytes)
                        {
                            outputStream.Dispose();
                            try { File.Delete(destPath); } catch { }
                            throw new InvalidOperationException($"ZIP archive exceeds maximum decompressed size quota ({options.MaxTotalDecompressedBytes} bytes).");
                        }
                        outputStream.Write(buffer, 0, read);
                    }
                }

                extractedFileCount++;
            }

            return extractedFileCount;
        }
    }
}
