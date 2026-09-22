using System;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IFlipperImportService
    {
        /// <summary>
        /// Imports a full Flipper Zero animation by parsing its meta.txt and loading all associated .bm frames.
        /// </summary>
        SpriteState ImportAnimation(string metaTxtPath);

        /// <summary>
        /// Imports a single .bm file as a single-frame SpriteState.
        /// Assumes the standard 128x64 Flipper resolution unless otherwise specified.
        /// </summary>
        SpriteState ImportFrame(string bmFilePath, int width = 128, int height = 64);

        /// <summary>
        /// Imports all animations and metadata from a Flipper Asset Pack directory or manifest.txt.
        /// </summary>
        System.Collections.Generic.List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> ImportAssetPack(string assetPackDirOrManifestPath);

        /// <summary>
        /// Asynchronously imports all animations and metadata from a Flipper Asset Pack directory, ZIP archive, or manifest.txt.
        /// </summary>
        System.Threading.Tasks.Task<System.Collections.Generic.List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>> ImportAssetPackAsync(
            string assetPackDirOrManifestPath,
            System.IProgress<double>? progress = null,
            System.Threading.CancellationToken cancellationToken = default);
    }
}
