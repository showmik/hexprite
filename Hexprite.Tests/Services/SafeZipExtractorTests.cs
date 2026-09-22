using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests.Services
{
    [Trait("Category", "Unit")]
    public class SafeZipExtractorTests : IDisposable
    {
        private readonly string _testRoot;

        public SafeZipExtractorTests()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "HexpriteSafeZipTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testRoot);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_testRoot))
                {
                    Directory.Delete(_testRoot, recursive: true);
                }
            }
            catch
            {
                // Best effort
            }
        }

        private byte[] CreateZipArchive(params (string Name, string Content)[] entries)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    var entry = zip.CreateEntry(name);
                    using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                    writer.Write(content);
                }
            }
            return ms.ToArray();
        }

        [Fact]
        public void SafeExtract_ExtractsValidAssets_Successfully()
        {
            byte[] zipData = CreateZipArchive(
                ("manifest.txt", "name=TestPack"),
                ("anims/idle/meta.txt", "fps=5"),
                ("anims/idle/frame_0.bm", "001122")
            );

            string targetDir = Path.Combine(_testRoot, "ValidPack");
            using var ms = new MemoryStream(zipData);

            int extracted = SafeZipExtractor.SafeExtractToDirectory(ms, targetDir);

            Assert.Equal(3, extracted);
            Assert.True(File.Exists(Path.Combine(targetDir, "manifest.txt")));
            Assert.True(File.Exists(Path.Combine(targetDir, "anims", "idle", "meta.txt")));
            Assert.True(File.Exists(Path.Combine(targetDir, "anims", "idle", "frame_0.bm")));
        }

        [Fact]
        public void SafeExtract_ThrowsOnZipSlip_PathTraversal()
        {
            byte[] zipData = CreateZipArchive(
                ("../../outside.txt", "malicious content")
            );

            string targetDir = Path.Combine(_testRoot, "SlipPack");
            using var ms = new MemoryStream(zipData);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir));

            Assert.Contains("Zip Slip", ex.Message);
        }

        [Fact]
        public void SafeExtract_ThrowsOnZipSlip_SiblingPrefixMatch()
        {
            // Sibling prefix bypass attempt:
            // Target is ".../Pack", entry tries to escape to ".../Pack_evil/test.txt"
            byte[] zipData = CreateZipArchive(
                ("../SlipPack_sibling/escape.txt", "escape content")
            );

            string targetDir = Path.Combine(_testRoot, "SlipPack");
            using var ms = new MemoryStream(zipData);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir));

            Assert.Contains("Zip Slip", ex.Message);
        }

        [Theory]
        [InlineData("payload.exe")]
        [InlineData("script.bat")]
        [InlineData("setup.cmd")]
        [InlineData("exploit.ps1")]
        [InlineData("macro.vbs")]
        [InlineData("inject.dll")]
        public void SafeExtract_ThrowsOnForbiddenExtension(string forbiddenName)
        {
            byte[] zipData = CreateZipArchive(
                (forbiddenName, "binary payload")
            );

            string targetDir = Path.Combine(_testRoot, "BlockedExt");
            using var ms = new MemoryStream(zipData);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir));

            Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SafeExtract_ThrowsOnExceedingTotalSizeLimit()
        {
            string bigChunk = new string('A', 1024); // 1 KB
            (string Name, string Content)[] entries = new (string, string)[15];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = ($"frame_{i}.txt", bigChunk);
            }

            byte[] zipData = CreateZipArchive(entries);
            string targetDir = Path.Combine(_testRoot, "BombPack");

            var options = new SafeZipOptions
            {
                MaxTotalDecompressedBytes = 10 * 1024 // 10 KB limit
            };

            using var ms = new MemoryStream(zipData);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir, options));

            Assert.Contains("exceeds maximum decompressed size quota", ex.Message);
        }

        [Fact]
        public void SafeExtract_ThrowsOnExceedingEntryCountLimit()
        {
            (string Name, string Content)[] entries = new (string, string)[12];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = ($"frame_{i}.txt", "data");
            }

            byte[] zipData = CreateZipArchive(entries);
            string targetDir = Path.Combine(_testRoot, "ManyEntries");

            var options = new SafeZipOptions
            {
                MaxEntries = 5 // Max 5 entries
            };

            using var ms = new MemoryStream(zipData);
            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir, options));

            Assert.Contains("exceeds maximum entry limit", ex.Message);
        }

        [Theory]
        [InlineData("CON.txt")]
        [InlineData("sub/AUX.json")]
        [InlineData("NUL.txt")]
        public void SafeExtract_ThrowsOnReservedWindowsDeviceNames(string reservedName)
        {
            byte[] zipData = CreateZipArchive(
                (reservedName, "reserved device name payload")
            );

            string targetDir = Path.Combine(_testRoot, "ReservedDevice");
            using var ms = new MemoryStream(zipData);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                SafeZipExtractor.SafeExtractToDirectory(ms, targetDir));

            Assert.Contains("reserved device name", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }
}
