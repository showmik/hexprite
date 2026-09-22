using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Services
{
    public enum FlipperExportTargetMode
    {
        SingleAnimation,
        MomentumAssetPack,
        StockDolphin,
    }

    public class FlipperExportSettings
    {
        public string TargetFolder { get; set; } = string.Empty;
        public string AnimationName { get; set; } = "Animation";
        public int FrameRate { get; set; } = 5;
        public int PassiveFrames { get; set; }
        public int ActiveFrames { get; set; }
        public int MinLevel { get; set; } = 1;
        public int MaxLevel { get; set; } = 3;
        public int MinButthurt { get; set; }
        public int MaxButthurt { get; set; } = 14;
        public int Weight { get; set; } = 1;
        public bool CreateManifestTxt { get; set; } = true;
        public FlipperExportTargetMode TargetMode { get; set; } = FlipperExportTargetMode.SingleAnimation;

        public FlipperExportSettings() { }

        public FlipperExportSettings(
            string targetFolder,
            string animationName,
            int frameRate,
            int passiveFrames,
            int activeFrames)
        {
            TargetFolder = targetFolder;
            AnimationName = animationName;
            FrameRate = frameRate;
            PassiveFrames = passiveFrames;
            ActiveFrames = activeFrames;
        }

        public FlipperExportSettings(
            string targetFolder,
            string animationName,
            int frameRate,
            int passiveFrames,
            int activeFrames,
            int minLevel,
            int maxLevel,
            int minButthurt,
            int maxButthurt,
            int weight,
            bool createManifestTxt = true,
            FlipperExportTargetMode targetMode = FlipperExportTargetMode.SingleAnimation)
            : this(targetFolder, animationName, frameRate, passiveFrames, activeFrames)
        {
            MinLevel = minLevel;
            MaxLevel = maxLevel;
            MinButthurt = minButthurt;
            MaxButthurt = maxButthurt;
            Weight = weight;
            CreateManifestTxt = createManifestTxt;
            TargetMode = targetMode;
        }
    }

    public interface IFlipperExportService
    {
        /// <summary>
        /// Exports the sprite state to a folder containing a meta.txt, sequential .bm frames, and optional manifest.txt.
        /// </summary>
        void ExportAnimation(SpriteState spriteState, FlipperExportSettings settings);

        /// <summary>
        /// Generates the complete set of deployment files (meta.txt, frame_X.bm, manifest.txt, Icons) in memory.
        /// </summary>
        IReadOnlyList<(string RelativePath, byte[] Data)> GenerateDeploymentFiles(SpriteState sprite, FlipperExportSettings settings);

        /// <summary>
        /// Exports a complete multi-animation asset pack with Anims/, Icons/, and manifest.txt.
        /// </summary>
        void ExportAssetPack(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetAssetPackFolder,
            bool isMomentum = true);

        /// <summary>
        /// Asynchronously exports a complete multi-animation asset pack with Anims/, Icons/, and manifest.txt using background parallel compression.
        /// </summary>
        System.Threading.Tasks.Task ExportAssetPackAsync(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetAssetPackFolder,
            bool isMomentum = true,
            System.IProgress<double>? progress = null,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports a complete multi-animation asset pack compressed directly into a .zip archive.
        /// </summary>
        void ExportAssetPackZip(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetZipFilePath,
            bool isMomentum = true);

        /// <summary>
        /// Asynchronously exports a complete multi-animation asset pack compressed directly into a .zip archive.
        /// </summary>
        System.Threading.Tasks.Task ExportAssetPackZipAsync(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetZipFilePath,
            bool isMomentum = true,
            System.IProgress<double>? progress = null,
            System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates a collection of animations and manifest entries against Flipper Zero hardware limits before export.
        /// </summary>
        System.Collections.Generic.List<FlipperValidationDiagnostic> ValidateAssetPackForExport(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            bool isMomentum = true);

        /// <summary>
        /// Exports a single frame of the sprite state as a static .bm file.
        /// </summary>
        void ExportImage(SpriteState spriteState, int frameIndex, string targetFilePath);
    }
}
