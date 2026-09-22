using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Hexprite.Core
{
    /// <summary>
    /// Provides atomic, resilient file I/O operations with retry backoff,
    /// read-only attribute stripping, directory auto-creation, and safety backups.
    /// </summary>
    public static class SafeFileIo
    {
        private static string GetBackupPath(string originalPath)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string backupDir = string.IsNullOrEmpty(appData)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups")
                : Path.Combine(appData, "Hexprite", "Backups");

            string safeName = Path.GetFileName(originalPath);
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(originalPath)))[..12];
            return Path.Combine(backupDir, $"{safeName}.{hash}.bak");
        }

        /// <summary>
        /// Determines whether a file begins with the 3-byte UTF-8 Byte Order Mark (0xEF, 0xBB, 0xBF).
        /// </summary>
        public static bool HasUtf8Bom(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length >= 3)
                {
                    byte[] bom = new byte[3];
                    int read = stream.Read(bom, 0, 3);
                    return read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Writes text to a file atomically by writing to a temporary file first,
        /// then moving/replacing the target. Handles retries on transient locks,
        /// strips read-only attributes, and creates a safety backup of existing files.
        /// Preserves the existing file's UTF-8 BOM or no-BOM encoding if <paramref name="encoding"/> is not specified.
        /// </summary>
        public static void WriteAllTextAtomic(string filePath, string content, int maxRetries = 5, bool createBackup = true, Encoding? encoding = null)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Create safety backup if requested and destination exists
            if (createBackup && File.Exists(filePath))
            {
                try
                {
                    string backupPath = GetBackupPath(filePath);
                    string backupDir = Path.GetDirectoryName(backupPath) ?? string.Empty;
                    if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                    {
                        Directory.CreateDirectory(backupDir);
                    }
                    File.Copy(filePath, backupPath, overwrite: true);
                }
                catch
                {
                    // Non-fatal: backup creation failure should not block the primary save
                }
            }

            // If encoding is not explicitly provided, preserve existing BOM (or lack thereof).
            // Default to UTF-8 without BOM for new files to avoid polluting embedded C/Python codebases.
            Encoding targetEncoding = encoding ?? (File.Exists(filePath) && HasUtf8Bom(filePath)
                ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            string tempFilePath = Path.Combine(dir, $".hexp_tmp_{Guid.NewGuid():N}");

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    File.WriteAllText(tempFilePath, content, targetEncoding);

                    if (File.Exists(filePath))
                    {
                        var attrs = File.GetAttributes(filePath);
                        if (attrs.HasFlag(FileAttributes.ReadOnly))
                        {
                            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                        }
                    }

                    File.Move(tempFilePath, filePath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);
                    }
                    catch { }

                    if (attempt == maxRetries - 1)
                        throw;

                    int delay = Math.Min(300, 25 * (1 << attempt));
                    Thread.Sleep(delay);
                }
            }
        }

        /// <summary>
        /// Reads all text from a file with retry backoff and shared read/write/delete access.
        /// Transparently decompresses GZip-compressed files if the 0x1F 0x8B magic header is detected.
        /// </summary>
        public static string ReadAllTextWithRetry(string filePath, int maxRetries = 5)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    
                    if (stream.Length >= 2)
                    {
                        int b1 = stream.ReadByte();
                        int b2 = stream.ReadByte();
                        stream.Position = 0; // Rewind

                        if (b1 == 0x1F && b2 == 0x8B)
                        {
                            using var gz = new GZipStream(stream, System.IO.Compression.CompressionMode.Decompress, leaveOpen: true);
                            using var gzReader = new StreamReader(gz, Encoding.UTF8);
                            return gzReader.ReadToEnd();
                        }
                    }

                    using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                    return reader.ReadToEnd();
                }
                catch (IOException) when (attempt < maxRetries - 1)
                {
                    int delay = Math.Min(300, 25 * (1 << attempt));
                    Thread.Sleep(delay);
                }
            }

            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"Could not read file after {maxRetries} attempts: {filePath}"));
        }

        /// <summary>
        /// Writes text to a file using GZip compression atomically via a temporary file with retry backoff.
        /// </summary>
        public static void WriteCompressedTextAtomic(string filePath, string content, int maxRetries = 5, bool createBackup = true)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            ArgumentNullException.ThrowIfNull(content);

            byte[] utf8Bytes = Encoding.UTF8.GetBytes(content);
            WriteStreamAtomic(filePath, stream =>
            {
                using var gz = new GZipStream(stream, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true);
                gz.Write(utf8Bytes, 0, utf8Bytes.Length);
            }, maxRetries, createBackup);
        }

        /// <summary>
        /// Writes raw bytes to a file atomically via a temporary file with retry backoff.
        /// </summary>
        public static void WriteBytesAtomic(string filePath, byte[] bytes, int maxRetries = 5, bool createBackup = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            ArgumentNullException.ThrowIfNull(bytes);

            WriteStreamAtomic(filePath, stream => stream.Write(bytes, 0, bytes.Length), maxRetries, createBackup);
        }

        /// <summary>
        /// Writes to a file stream atomically via a temporary file with retry backoff.
        /// </summary>
        public static void WriteStreamAtomic(string filePath, Action<Stream> writeAction, int maxRetries = 5, bool createBackup = false)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
            ArgumentNullException.ThrowIfNull(writeAction);

            string dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (createBackup && File.Exists(filePath))
            {
                try
                {
                    string backupPath = GetBackupPath(filePath);
                    string backupDir = Path.GetDirectoryName(backupPath) ?? string.Empty;
                    if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
                    {
                        Directory.CreateDirectory(backupDir);
                    }
                    File.Copy(filePath, backupPath, overwrite: true);
                }
                catch { }
            }

            string tempFilePath = Path.Combine(dir, $".hexp_tmp_{Guid.NewGuid():N}");

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using (var fs = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        writeAction(fs);
                        fs.Flush(flushToDisk: true);
                    }

                    if (File.Exists(filePath))
                    {
                        var attrs = File.GetAttributes(filePath);
                        if (attrs.HasFlag(FileAttributes.ReadOnly))
                        {
                            File.SetAttributes(filePath, attrs & ~FileAttributes.ReadOnly);
                        }
                    }

                    File.Move(tempFilePath, filePath, overwrite: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    try
                    {
                        if (File.Exists(tempFilePath))
                            File.Delete(tempFilePath);
                    }
                    catch { }

                    if (attempt == maxRetries - 1)
                        throw;

                    int delay = Math.Min(300, 25 * (1 << attempt));
                    Thread.Sleep(delay);
                }
            }
        }

        /// <summary>
        /// Reads all bytes from a file with retry backoff and shared read/write/delete access.
        /// </summary>
        public static byte[] ReadAllBytesWithRetry(string filePath, int maxRetries = 5)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    int length = (int)stream.Length;
                    byte[] bytes = new byte[length];
                    int totalRead = 0;
                    while (totalRead < length)
                    {
                        int read = stream.Read(bytes, totalRead, length - totalRead);
                        if (read == 0) break;
                        totalRead += read;
                    }
                    if (totalRead != length)
                    {
                        Array.Resize(ref bytes, totalRead);
                    }
                    return bytes;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt == maxRetries - 1)
                        throw;

                    int delay = Math.Min(300, 25 * (1 << attempt));
                    Thread.Sleep(delay);
                }
            }

            throw new IOException(string.Create(CultureInfo.InvariantCulture, $"Could not read file after {maxRetries} attempts: {filePath}"));
        }

        /// <summary>
        /// Moves a file with retry backoff, directory auto-creation, and read-only attribute stripping.
        /// </summary>
        public static void MoveAtomic(string sourcePath, string destPath, int maxRetries = 5, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(destPath))
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(destPath));

            string destDir = Path.GetDirectoryName(destPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    if (File.Exists(destPath) && overwrite)
                    {
                        var attrs = File.GetAttributes(destPath);
                        if (attrs.HasFlag(FileAttributes.ReadOnly))
                        {
                            File.SetAttributes(destPath, attrs & ~FileAttributes.ReadOnly);
                        }
                    }

                    File.Move(sourcePath, destPath, overwrite);
                    return;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    if (attempt == maxRetries - 1)
                        throw;

                    int delay = Math.Min(300, 25 * (1 << attempt));
                    Thread.Sleep(delay);
                }
            }
        }

        /// <summary>
        /// Copies a file atomically using a temporary file with retry backoff, directory auto-creation, and read-only attribute stripping.
        /// </summary>
        public static void CopyAtomic(string sourcePath, string destPath, int maxRetries = 5, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
            if (string.IsNullOrWhiteSpace(destPath))
                throw new ArgumentException("Destination path cannot be null or empty.", nameof(destPath));

            string destDir = Path.GetDirectoryName(destPath) ?? string.Empty;
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            string tempDestPath = Path.Combine(destDir, $".hexp_tmp_{Guid.NewGuid():N}");

            try
            {
                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        using var src = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var dst = new FileStream(tempDestPath, FileMode.Create, FileAccess.Write, FileShare.None);
                        src.CopyTo(dst);
                        dst.Flush(flushToDisk: true);
                        break;
                    }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                    {
                        try { if (File.Exists(tempDestPath)) File.Delete(tempDestPath); } catch { }

                        if (attempt == maxRetries - 1)
                            throw;

                        int delay = Math.Min(300, 25 * (1 << attempt));
                        Thread.Sleep(delay);
                    }
                }

                MoveAtomic(tempDestPath, destPath, maxRetries, overwrite);
            }
            finally
            {
                try { if (File.Exists(tempDestPath)) File.Delete(tempDestPath); } catch { }
            }
        }

        /// <summary>
        /// Ensures that the file path ends with the given default extension (e.g. ".hexp").
        /// </summary>
        public static string EnsureExtension(string filePath, string defaultExtension)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return filePath;
            if (string.IsNullOrEmpty(defaultExtension)) return filePath;

            if (!defaultExtension.StartsWith('.'))
                defaultExtension = "." + defaultExtension;

            string currentExt = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(currentExt))
            {
                return filePath + defaultExtension;
            }

            return filePath;
        }
    }
}
