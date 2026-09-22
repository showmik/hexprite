using System;
using System.IO;
using System.Linq;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Integration")]
    public class AssetsPathServiceTests
    {
        [Fact]
        public void ResolveHexpritePreviewLibraryPath_FindsSourceTreeOrAppDataAssets()
        {
            string? resolved = AssetsPathService.ResolveHexpritePreviewLibraryPath();
            Assert.NotNull(resolved);
            Assert.True(AssetsPathService.IsValidHexpritePreviewLibrary(resolved));
        }

        [Fact]
        public void ResolveHexpritePreviewStandalonePath_FindsStandaloneSketch()
        {
            string? resolved = AssetsPathService.ResolveHexpritePreviewStandalonePath();
            Assert.NotNull(resolved);
            Assert.True(AssetsPathService.IsValidHexpritePreviewStandalone(resolved));
        }

        [Fact]
        public void ResolveHexpritePreviewPlatformIOPath_FindsPlatformIOProject()
        {
            string? resolved = AssetsPathService.ResolveHexpritePreviewPlatformIOPath();
            Assert.NotNull(resolved);
            Assert.True(AssetsPathService.IsValidHexpritePreviewPlatformIO(resolved));
        }

        [Fact]
        public void EnumerateHexpritePreviewCandidates_IncludesAppDataAssetsPath()
        {
            string expected = Path.Combine(AssetsPathService.AppDataAssetsDirectory, AssetsPathService.HexpritePreviewFolderName);

            bool containsAppDataCandidate = AssetsPathService.EnumerateHexpritePreviewCandidates()
                .Where(c => c != null)
                .Any(c => string.Equals(Path.GetFullPath(c!), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase));

            Assert.True(containsAppDataCandidate);
        }

        [Fact]
        public void EnumerateHexpritePreviewCandidates_IncludesBaseDirectoryAssetsPath()
        {
            string expected = Path.Combine(AppContext.BaseDirectory, "Assets", AssetsPathService.HexpritePreviewFolderName);

            bool containsBaseCandidate = AssetsPathService.EnumerateHexpritePreviewCandidates()
                .Where(c => c != null)
                .Any(c => string.Equals(Path.GetFullPath(c!), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase));

            Assert.True(containsBaseCandidate);
        }

        [Fact]
        public void EnsureAssetsExtracted_ExtractsEmbeddedAssetsSuccessfully()
        {
            AssetsPathService.EnsureAssetsExtracted(force: true);

            string libraryPath = Path.Combine(AssetsPathService.AppDataAssetsDirectory, AssetsPathService.HexpritePreviewFolderName);
            string standalonePath = Path.Combine(AssetsPathService.AppDataAssetsDirectory, AssetsPathService.HexpritePreviewStandaloneFolderName);
            string platformIOPath = Path.Combine(AssetsPathService.AppDataAssetsDirectory, AssetsPathService.HexpritePreviewPlatformIOFolderName);

            Assert.True(AssetsPathService.IsValidHexpritePreviewLibrary(libraryPath));
            Assert.True(AssetsPathService.IsValidHexpritePreviewStandalone(standalonePath));
            Assert.True(AssetsPathService.IsValidHexpritePreviewPlatformIO(platformIOPath));
        }

        [Fact]
        public void IsValidHexpritePreviewLibrary_RejectsMissingFolder()
        {
            Assert.False(AssetsPathService.IsValidHexpritePreviewLibrary(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        }

        [Fact]
        public void IsValidHexpritePreviewStandalone_RejectsMissingFolder()
        {
            Assert.False(AssetsPathService.IsValidHexpritePreviewStandalone(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        }

        [Fact]
        public void IsValidHexpritePreviewPlatformIO_RejectsMissingFolder()
        {
            Assert.False(AssetsPathService.IsValidHexpritePreviewPlatformIO(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
        }
    }
}

