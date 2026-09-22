using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperPackArchiverServiceTests
    {
        [Fact]
        public void ValidatePack_EmptyList_ReturnsError()
        {
            var animations = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();
            var report = FlipperPackArchiverService.ValidatePack(animations);

            Assert.False(report.IsValid);
            Assert.Contains(report.Issues, i => i.Severity == "Error");
        }

        [Fact]
        public void ValidatePack_ValidAnimations_ReportsCorrectCoverage()
        {
            var sprite1 = new SpriteState(128, 64);
            sprite1.Frames[0].Name = "F1";

            var sprite2 = new SpriteState(128, 64);
            sprite2.Frames[0].Name = "F1";

            var animations = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("anim_1", sprite1, new FlipperManifestEntry { Name = "anim_1", MinLevel = 1, MaxLevel = 15, MinButthurt = 0, MaxButthurt = 14, Weight = 1 }),
                ("anim_2", sprite2, new FlipperManifestEntry { Name = "anim_2", MinLevel = 16, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            var report = FlipperPackArchiverService.ValidatePack(animations);

            Assert.True(report.IsValid);
            Assert.Equal(2, report.TotalAnimations);
            Assert.Equal(2, report.TotalFrames);
            Assert.Equal(100.0, report.MatrixCoveragePercent);
        }

        [Fact]
        public void CreateZipArchive_GeneratesValidZipWithManifest()
        {
            var sprite = new SpriteState(128, 64);
            sprite.Frames.Add(new FrameState { Name = "F1" });

            var animations = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
            {
                ("test_anim", sprite, new FlipperManifestEntry { Name = "test_anim", MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 })
            };

            string zipPath = Path.Combine(Path.GetTempPath(), $"test_flipper_pack_{Guid.NewGuid():N}.zip");
            try
            {
                FlipperPackArchiverService.CreateZipArchive(animations, "TestPack", zipPath);

                Assert.True(File.Exists(zipPath));

                using var zip = ZipFile.OpenRead(zipPath);
                Assert.Contains(zip.Entries, e => e.FullName.Contains("manifest.txt"));
                Assert.Contains(zip.Entries, e => e.FullName.Contains("meta.txt"));
            }
            finally
            {
                if (File.Exists(zipPath)) File.Delete(zipPath);
            }
        }
    }
}
