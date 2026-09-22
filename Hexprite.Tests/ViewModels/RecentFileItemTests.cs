using System;
using System.IO;
using Hexprite.ViewModels;
using Xunit;

namespace Hexprite.Tests.ViewModels
{
    [Trait("Category", "Unit")]
    public class RecentFileItemTests
    {
        [Theory]
        [InlineData("test.hexpack", "PACK")]
        [InlineData("test.HEXPACK", "PACK")]
        [InlineData("font.hexfont", "FONT")]
        [InlineData("font.hexpfont", "FONT")]
        [InlineData("canvas.hexp", "SPRITE")]
        [InlineData("unknown.dat", "SPRITE")]
        public void FormatBadge_IdentifiesExtensionCorrectly(string fileName, string expectedBadge)
        {
            var item = new RecentFileItem(fileName);
            Assert.Equal(expectedBadge, item.FormatBadge);
            Assert.Equal(fileName, item.FileName);
        }

        [Fact]
        public void NonExistentFile_SetsEmptyRelativeTime()
        {
            var item = new RecentFileItem("C:\\nonexistent_file_path_12345.hexp");
            Assert.Equal(string.Empty, item.RelativeTime);
        }

        [Fact]
        public void ExistingFile_GeneratesRelativeTimeString()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), $"recent_test_{Guid.NewGuid():N}.hexp");
            try
            {
                File.WriteAllText(tempFile, "test");
                var item = new RecentFileItem(tempFile);
                Assert.False(string.IsNullOrEmpty(item.RelativeTime));
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }
    }
}
