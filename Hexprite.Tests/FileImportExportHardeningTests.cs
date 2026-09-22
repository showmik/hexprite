using System;
using System.IO;
using System.Text;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FileImportExportHardeningTests : IDisposable
    {
        private readonly string _tempDir;

        public FileImportExportHardeningTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_HardeningTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                try { Directory.Delete(_tempDir, true); } catch { /* best effort */ }
            }
        }

        [Fact]
        public void SafeFileIo_ReadAllBytesWithRetry_ReadsBytesSuccessfully()
        {
            string filePath = Path.Combine(_tempDir, "sample.bin");
            byte[] expected = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03];
            File.WriteAllBytes(filePath, expected);

            byte[] actual = SafeFileIo.ReadAllBytesWithRetry(filePath);
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void SafeFileIo_ReadAllBytesWithRetry_MissingFile_ThrowsFileNotFoundException()
        {
            string filePath = Path.Combine(_tempDir, "non_existent.bin");
            Assert.Throws<FileNotFoundException>(() => SafeFileIo.ReadAllBytesWithRetry(filePath));
        }

        [Fact]
        public void SafeFileIo_MoveAtomic_CreatesSubdirectoryAndOverwritesReadOnlyTarget()
        {
            string source = Path.Combine(_tempDir, "src.dat");
            string destDir = Path.Combine(_tempDir, "sub", "folder");
            string dest = Path.Combine(destDir, "dst.dat");

            File.WriteAllText(source, "hello world");
            Directory.CreateDirectory(destDir);
            File.WriteAllText(dest, "old data");
            File.SetAttributes(dest, FileAttributes.ReadOnly);

            SafeFileIo.MoveAtomic(source, dest, overwrite: true);

            Assert.False(File.Exists(source));
            Assert.True(File.Exists(dest));
            Assert.Equal("hello world", File.ReadAllText(dest));
            Assert.False(File.GetAttributes(dest).HasFlag(FileAttributes.ReadOnly));
        }

        [Fact]
        public void SafeFileIo_CopyAtomic_CreatesSubdirectoryAndOverwritesReadOnlyTarget()
        {
            string source = Path.Combine(_tempDir, "copy_src.dat");
            string dest = Path.Combine(_tempDir, "nested", "copy_dst.dat");

            File.WriteAllText(source, "copy test content");

            SafeFileIo.CopyAtomic(source, dest, overwrite: true);

            Assert.True(File.Exists(source));
            Assert.True(File.Exists(dest));
            Assert.Equal("copy test content", File.ReadAllText(dest));
        }

        [Fact]
        public void XbmService_ParseFile_SupportsHexAndUppercaseDimensions()
        {
            var service = new XbmService();
            string xbmPath = Path.Combine(_tempDir, "test_hex.xbm");
            File.WriteAllText(xbmPath,
                "#define TEST_WIDTH 0x20\n" +
                "#define TEST_HEIGHT 0x10\n" +
                "static unsigned char test_bits[] = { 0x00 };\n");

            var result = service.ParseFile(xbmPath);
            Assert.Equal(32, result.Width);
            Assert.Equal(16, result.Height);
        }

        [Fact]
        public void XbmService_ParseFile_SupportsShortDimensionNames()
        {
            var service = new XbmService();
            string xbmPath = Path.Combine(_tempDir, "test_short.xbm");
            File.WriteAllText(xbmPath,
                "#define icon_w 24\n" +
                "#define icon_h 12\n" +
                "static unsigned char icon_bits[] = { 0x00 };\n");

            var result = service.ParseFile(xbmPath);
            Assert.Equal(24, result.Width);
            Assert.Equal(12, result.Height);
        }

        [Fact]
        public void XbmService_ParseFile_MissingFile_ThrowsFileNotFoundException()
        {
            var service = new XbmService();
            Assert.Throws<FileNotFoundException>(() => service.ParseFile(Path.Combine(_tempDir, "missing.xbm")));
        }

        [Fact]
        public void XbmService_ExportImage_InvalidDimensions_ThrowsArgumentOutOfRangeException()
        {
            var service = new XbmService();
            var sprite = new SpriteState(0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.ExportImage(sprite, 0, Path.Combine(_tempDir, "out.xbm")));
        }

        [Fact]
        public void FontImportService_MissingFiles_ThrowFileNotFoundException()
        {
            var service = new FontImportService();
            var options = new FontImportOptions();

            Assert.Throws<FileNotFoundException>(() => service.ImportFromFontFile(Path.Combine(_tempDir, "missing.ttf"), options));
            Assert.Throws<FileNotFoundException>(() => service.ImportFromSpriteSheet(Path.Combine(_tempDir, "missing.png"), 16, 16, options));
            Assert.Throws<FileNotFoundException>(() => service.ImportFromBMFont(Path.Combine(_tempDir, "missing.fnt"), options));
        }

        [Fact]
        public void FontImportService_BMFont_PathTraversal_IsConfined()
        {
            var service = new FontImportService();
            string fntPath = Path.Combine(_tempDir, "traversal.fnt");
            File.WriteAllText(fntPath,
                "info face=\"Test\" size=16\n" +
                "common lineHeight=16 base=12\n" +
                "page id=0 file=\"../../secret.png\"\n" +
                "char id=65 x=0 y=0 width=8 height=16 xoffset=0 yoffset=0 xadvance=8\n");

            // Should look for secret.png inside _tempDir (confined), not outside in parent directories
            var ex = Assert.Throws<FileNotFoundException>(() => service.ImportFromBMFont(fntPath, new FontImportOptions()));
            Assert.Contains(Path.Combine(_tempDir, "secret.png"), ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CodeGeneratorService_LargeInput_ClampsFrameCount()
        {
            var service = new CodeGeneratorService();
            var sprite = new SpriteState(8, 8);

            // 1000 frames worth of bytes: 1000 * 8 = 8000 bytes
            var sb = new StringBuilder();
            sb.Append("const uint8_t data[] = {");
            for (int i = 0; i < 8000; i++)
            {
                sb.Append("0xAA,");
            }
            sb.Append("};");

            service.ParseAdafruitGfxToState(sb.ToString(), sprite);

            // Clamped to 512 frames max
            Assert.True(sprite.Frames.Count <= 512);
            Assert.Equal(512, sprite.Frames.Count);
        }

        [Fact]
        public void CodeGeneratorService_ZeroDimensions_DoesNotCrash()
        {
            var service = new CodeGeneratorService();
            var sprite = new SpriteState(0, 0);

            service.ParseAdafruitGfxToState("0xFF", sprite);
            service.ParseXbmToState("0xFF", sprite);
            service.ParseBinaryToState("10101010", sprite);
            service.ParseIndexed2DToState("1,0,1,0", sprite);

            Assert.Equal(0, sprite.Width);
            Assert.Equal(0, sprite.Height);
        }

        [Fact]
        public void FileImportExportService_MissingOrTooLargeFile_ThrowsAppropriateException()
        {
            var service = new FileImportExportService();
            Assert.Throws<FileNotFoundException>(() => service.ExtractSpritesFromFile(Path.Combine(_tempDir, "missing.c")));
        }

        [Fact]
        public void FlipperExportService_SanitizeAnimationName_ReplacesWindowsDeviceNames()
        {
            string sanitizedCon = FlipperExportService.SanitizeAnimationName("CON");
            Assert.Equal("Anim_CON", sanitizedCon);

            string sanitizedNul = FlipperExportService.SanitizeAnimationName("NUL");
            Assert.Equal("Anim_NUL", sanitizedNul);

            string sanitizedCom1 = FlipperExportService.SanitizeAnimationName("com1");
            Assert.Equal("Anim_com1", sanitizedCom1);

            string sanitizedNormal = FlipperExportService.SanitizeAnimationName("Normal_Anim");
            Assert.Equal("Normal_Anim", sanitizedNormal);
        }

        [Fact]
        public void FlipperExportService_NonPositiveDimensions_ThrowsArgumentOutOfRangeException()
        {
            var service = new FlipperExportService();
            var sprite = new SpriteState(0, 64);
            var settings = new FlipperExportSettings { TargetFolder = _tempDir, AnimationName = "test" };

            Assert.Throws<ArgumentOutOfRangeException>(() => service.ExportAnimation(sprite, settings));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.ExportImage(sprite, 0, Path.Combine(_tempDir, "test.bm")));
        }
    }
}
