using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Hexprite.Core;

namespace Hexprite.Services
{
    public static class FlipperPackArchiverService
    {
        public static FlipperPackIntegrityReport ValidatePack(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations)
        {
            ArgumentNullException.ThrowIfNull(animations);

            var report = new FlipperPackIntegrityReport
            {
                TotalAnimations = animations.Count,
            };

            if (animations.Count == 0)
            {
                report.Issues.Add(new FlipperPackIntegrityIssue
                {
                    AnimationName = "-",
                    Severity = "Error",
                    Message = "Asset pack contains zero animations.",
                });
                return report;
            }

            int totalFrames = 0;
            var manifestEntries = new List<FlipperManifestEntry>();

            foreach (var (name, sprite, manifestEntry) in animations)
            {
                manifestEntries.Add(manifestEntry);
                int frameCount = sprite?.Frames.Count ?? 0;
                totalFrames += frameCount;

                if (string.IsNullOrWhiteSpace(name))
                {
                    report.Issues.Add(new FlipperPackIntegrityIssue
                    {
                        AnimationName = "-",
                        Severity = "Error",
                        Message = "Animation has an empty or invalid directory name.",
                    });
                }

                if (sprite == null || frameCount == 0)
                {
                    report.Issues.Add(new FlipperPackIntegrityIssue
                    {
                        AnimationName = name,
                        Severity = "Error",
                        Message = "Animation contains zero frames.",
                    });
                    continue;
                }

                // Check frame dimensions
                if (sprite.Width != 128 || sprite.Height != 64)
                {
                    report.Issues.Add(new FlipperPackIntegrityIssue
                    {
                        AnimationName = name,
                        Severity = "Warning",
                        Message = string.Create(CultureInfo.InvariantCulture, $"Frame dimensions ({sprite.Width}x{sprite.Height}) differ from standard Flipper LCD (128x64)."
),
                    });
                }

                // Check level and mood ranges
                if (manifestEntry.MinLevel < 1 || manifestEntry.MaxLevel > 30 || manifestEntry.MinLevel > manifestEntry.MaxLevel)
                {
                    report.Issues.Add(new FlipperPackIntegrityIssue
                    {
                        AnimationName = name,
                        Severity = "Error",
                        Message = string.Create(CultureInfo.InvariantCulture, $"Invalid Level range (L{manifestEntry.MinLevel}-{manifestEntry.MaxLevel}). Must be between 1 and 30."
),
                    });
                }

                if (manifestEntry.MinButthurt < 0 || manifestEntry.MaxButthurt > 14 || manifestEntry.MinButthurt > manifestEntry.MaxButthurt)
                {
                    report.Issues.Add(new FlipperPackIntegrityIssue
                    {
                        AnimationName = name,
                        Severity = "Error",
                        Message = string.Create(CultureInfo.InvariantCulture, $"Invalid Mood range (M{manifestEntry.MinButthurt}-{manifestEntry.MaxButthurt}). Must be between 0 and 14."
),
                    });
                }
            }

            report.TotalFrames = totalFrames;

            // Check Matrix Coverage
            int maxLvl = (manifestEntries.Count > 0 && manifestEntries.TrueForAll(e => e.MaxLevel <= 3)) ? 3 : 30;
            var matrix = new FlipperScheduleMatrix(manifestEntries, maxLvl);
            report.MatrixCoveredCells = matrix.CoveredCellsCount;
            report.MatrixCoveragePercent = matrix.CoveragePercentage;

            if (matrix.CoveragePercentage < 100.0)
            {
                int gaps = matrix.GetUncoveredCells().Count;
                report.Issues.Add(new FlipperPackIntegrityIssue
                {
                    AnimationName = "Schedule Matrix",
                    Severity = "Warning",
                    Message = string.Create(CultureInfo.InvariantCulture, $"{gaps} out of {matrix.TotalCells} Level/Mood states have no animation scheduled ({matrix.CoveragePercentage:F0}% coverage)."
),
                });
            }

            return report;
        }

        public static void CreateZipArchive(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName,
            string zipOutputPath)
        {
            ArgumentNullException.ThrowIfNull(animations);
            ArgumentException.ThrowIfNullOrWhiteSpace(zipOutputPath);

            string tempDir = Path.Combine(Path.GetTempPath(), "Hexprite_Pack_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                string safePackName = FlipperExportService.SanitizeAnimationName(packName);
                if (string.IsNullOrWhiteSpace(safePackName)) safePackName = "Flipper_Asset_Pack";

                string animsRoot = Path.Combine(tempDir, "Anims", safePackName);
                Directory.CreateDirectory(animsRoot);

                var exportService = new FlipperExportService();
                var manifestEntries = new List<FlipperManifestEntry>();
                var usedAnimNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var (Name, Sprite, ManifestEntry) in animations)
                {
                    string safeAnimName = FlipperExportService.SanitizeAnimationName(Name);
                    if (string.IsNullOrWhiteSpace(safeAnimName)) safeAnimName = "anim";

                    string uniqueAnimName = safeAnimName;
                    int disambiguation = 1;
                    while (usedAnimNames.Contains(uniqueAnimName))
                    {
                        uniqueAnimName = string.Create(CultureInfo.InvariantCulture, $"{safeAnimName}_{disambiguation++}");
                    }
                    usedAnimNames.Add(uniqueAnimName);

                    var entryClone = ManifestEntry.Clone();
                    entryClone.Name = uniqueAnimName;
                    manifestEntries.Add(entryClone);

                    var settings = new FlipperExportSettings
                    {
                        TargetFolder = animsRoot,
                        AnimationName = uniqueAnimName,
                        FrameRate = Sprite != null ? Math.Clamp(Sprite.FrameRateFps, 1, 60) : 5,
                        PassiveFrames = Sprite?.Frames.Count ?? 1,
                        ActiveFrames = 0,
                        MinLevel = ManifestEntry.MinLevel,
                        MaxLevel = ManifestEntry.MaxLevel,
                        MinButthurt = ManifestEntry.MinButthurt,
                        MaxButthurt = ManifestEntry.MaxButthurt,
                        Weight = ManifestEntry.Weight,
                        CreateManifestTxt = false,
                    };
                    var effectiveSprite = Sprite ?? new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 };
                    effectiveSprite.NormalizeLayerState();
                    exportService.ExportAnimation(effectiveSprite, settings);
                }

                // Write manifest.txt
                string manifestPath = Path.Combine(animsRoot, "manifest.txt");
                var manifest = new FlipperManifest { Entries = manifestEntries };
                SafeFileIo.WriteAllTextAtomic(manifestPath, manifest.Serialize(), encoding: Encoding.UTF8);

                // Write Readme / metadata
                string readmePath = Path.Combine(animsRoot, "README.txt");
                SafeFileIo.WriteAllTextAtomic(readmePath, $"Flipper Zero Asset Pack: {packName}\nGenerated with Hexprite Studio\nTotal Animations: {animations.Count}\nReady for SD Card /ext/dolphin/ deployment.", encoding: Encoding.UTF8);

                // Package ZIP atomically
                string tempZip = Path.Combine(Path.GetTempPath(), "Hexprite_Zip_" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    ZipFile.CreateFromDirectory(tempDir, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);
                    SafeFileIo.MoveAtomic(tempZip, zipOutputPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempZip))
                    {
                        try { File.Delete(tempZip); } catch { /* best effort */ }
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
                }
            }
        }
    }
}
