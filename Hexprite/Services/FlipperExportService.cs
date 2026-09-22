using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class FlipperExportService : IFlipperExportService
    {
        private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        public static string SanitizeAnimationName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Animation";
            var sb = new StringBuilder();
            bool lastWasUnderscore = false;

            foreach (char c in name.Trim())
            {
                if (char.IsAsciiLetterOrDigit(c) || c == '-')
                {
                    sb.Append(c);
                    lastWasUnderscore = false;
                }
                else
                {
                    if (!lastWasUnderscore)
                    {
                        sb.Append('_');
                        lastWasUnderscore = true;
                    }
                }
            }
            string result = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(result)) return "Animation";
            if (ReservedDeviceNames.Contains(result)) return $"Anim_{result}";
            return result;
        }

        public void ExportAnimation(SpriteState spriteState, FlipperExportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(spriteState);
            ArgumentNullException.ThrowIfNull(settings);

            if (spriteState.Width <= 0 || spriteState.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(spriteState), "Sprite dimensions must be greater than zero.");

            settings.AnimationName = SanitizeAnimationName(settings.AnimationName);
            string animFolder;
            string manifestDir;

            if (settings.TargetMode == FlipperExportTargetMode.MomentumAssetPack)
            {
                string animsDir = Path.Combine(settings.TargetFolder, "Anims");
                string iconsDir = Path.Combine(settings.TargetFolder, "Icons");
                Directory.CreateDirectory(animsDir);
                Directory.CreateDirectory(iconsDir);
                animFolder = Path.Combine(animsDir, settings.AnimationName);
                manifestDir = animsDir;
            }
            else if (settings.TargetMode == FlipperExportTargetMode.StockDolphin)
            {
                string dolphinDir = Path.Combine(settings.TargetFolder, "dolphin");
                Directory.CreateDirectory(dolphinDir);
                animFolder = Path.Combine(dolphinDir, settings.AnimationName);
                manifestDir = dolphinDir;
            }
            else
            {
                animFolder = Path.Combine(settings.TargetFolder, settings.AnimationName);
                manifestDir = settings.TargetFolder;
            }

            Directory.CreateDirectory(animFolder);

            int totalFrames = spriteState.Frames.Count;

            // Generate meta.txt
            string metaPath = Path.Combine(animFolder, "meta.txt");
            string metaContent = BuildMetaTxt(spriteState, settings);
            SafeFileIo.WriteAllTextAtomic(metaPath, metaContent, maxRetries: 3, createBackup: false);

            // Generate frame_X.bm files
            for (int i = 0; i < totalFrames; i++)
            {
                bool[] pixels = spriteState.CompositeFramePixels(i, isExport: true);
                byte[] bmData = ConvertToFlipperBm(pixels, spriteState.Width, spriteState.Height);
                string framePath = Path.Combine(animFolder, string.Create(CultureInfo.InvariantCulture, $"frame_{i}.bm"));
                SafeFileIo.WriteBytesAtomic(framePath, bmData, maxRetries: 3, createBackup: false);
            }

            // Generate/Update manifest.txt if requested
            if (settings.CreateManifestTxt && (settings.TargetMode != FlipperExportTargetMode.SingleAnimation || !string.IsNullOrEmpty(manifestDir)))
            {
                string manifestPath = Path.Combine(manifestDir, "manifest.txt");
                var manifestEntry = new FlipperManifestEntry
                {
                    Name = settings.AnimationName,
                    MinLevel = settings.MinLevel,
                    MaxLevel = settings.MaxLevel,
                    MinButthurt = settings.MinButthurt,
                    MaxButthurt = settings.MaxButthurt,
                    Weight = settings.Weight,
                };

                FlipperManifest manifest;
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        manifest = FlipperManifest.Parse(SafeFileIo.ReadAllTextWithRetry(manifestPath));
                        // Replace existing or add new
                        int existingIdx = manifest.Entries.FindIndex(e => e.Name.Equals(settings.AnimationName, StringComparison.OrdinalIgnoreCase));
                        if (existingIdx >= 0)
                            manifest.Entries[existingIdx] = manifestEntry;
                        else
                            manifest.Entries.Add(manifestEntry);
                    }
                    catch
                    {
                        manifest = new FlipperManifest { Entries = [manifestEntry] };
                    }
                }
                else
                {
                    manifest = new FlipperManifest { Entries = [manifestEntry] };
                }

                SafeFileIo.WriteAllTextAtomic(manifestPath, manifest.Serialize(), maxRetries: 3, createBackup: false);
            }

            // Generate Momentum Asset Pack preview icon in Icons/ if needed for standalone animation export
            if (settings.TargetMode == FlipperExportTargetMode.MomentumAssetPack && totalFrames > 0 && settings.CreateManifestTxt)
            {
                string iconsDir = Path.Combine(settings.TargetFolder, "Icons");
                string iconPath = Path.Combine(iconsDir, $"I_{settings.AnimationName}_10x10.bm");
                if (!File.Exists(iconPath))
                {
                    GeneratePackIcon(spriteState, iconPath);
                }
            }
        }

        public IReadOnlyList<(string RelativePath, byte[] Data)> GenerateDeploymentFiles(SpriteState sprite, FlipperExportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(sprite);
            ArgumentNullException.ThrowIfNull(settings);

            string animName = SanitizeAnimationName(settings.AnimationName);
            var files = new List<(string RelativePath, byte[] Data)>();

            string animFolderPrefix = settings.TargetMode switch
            {
                FlipperExportTargetMode.MomentumAssetPack => $"Anims/{animName}",
                FlipperExportTargetMode.StockDolphin => $"dolphin/{animName}",
                _ => animName,
            };

            string manifestPrefix = settings.TargetMode switch
            {
                FlipperExportTargetMode.MomentumAssetPack => "Anims",
                FlipperExportTargetMode.StockDolphin => "dolphin",
                _ => "",
            };

            // 1. meta.txt
            string metaTxt = BuildMetaTxt(sprite, settings);
            files.Add(($"{animFolderPrefix}/meta.txt", Encoding.UTF8.GetBytes(metaTxt)));

            // 2. frame_X.bm files
            int totalFrames = sprite.Frames.Count;
            for (int i = 0; i < totalFrames; i++)
            {
                bool[] pixels = sprite.CompositeFramePixels(i, isExport: true);
                byte[] bmData = ConvertToFlipperBm(pixels, sprite.Width, sprite.Height);
                files.Add((string.Create(CultureInfo.InvariantCulture, $"{animFolderPrefix}/frame_{i}.bm"), bmData));
            }

            // 3. manifest.txt
            if (settings.CreateManifestTxt)
            {
                var manifestEntry = new FlipperManifestEntry
                {
                    Name = animName,
                    MinLevel = settings.MinLevel,
                    MaxLevel = settings.MaxLevel,
                    MinButthurt = settings.MinButthurt,
                    MaxButthurt = settings.MaxButthurt,
                    Weight = settings.Weight,
                };

                var manifest = new FlipperManifest { Entries = [manifestEntry] };
                string manifestPath = string.IsNullOrEmpty(manifestPrefix) ? "manifest.txt" : $"{manifestPrefix}/manifest.txt";
                files.Add((manifestPath, Encoding.UTF8.GetBytes(manifest.Serialize())));
            }

            // 4. Momentum 10x10 pack icon in Icons/
            if (settings.TargetMode == FlipperExportTargetMode.MomentumAssetPack && totalFrames > 0)
            {
                byte[] iconData = GeneratePackIconBytes(sprite);
                files.Add(($"Icons/I_{animName}_10x10.bm", iconData));
            }

            return files;
        }

        public List<FlipperValidationDiagnostic> ValidateAssetPackForExport(
            IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            bool isMomentum = true)
        {
            var diagnostics = new List<FlipperValidationDiagnostic>();

            if (animations == null || animations.Count == 0)
            {
                diagnostics.Add(new FlipperValidationDiagnostic(
                    FlipperValidationSeverity.Error,
                    "FZ010",
                    "Asset pack contains 0 animations."));
                return diagnostics;
            }

            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < animations.Count; i++)
            {
                var (sprite, entry, _) = animations[i];

                if (entry == null || string.IsNullOrWhiteSpace(entry.Name))
                {
                    diagnostics.Add(new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Error,
                        "FZ001",
                        string.Create(CultureInfo.InvariantCulture, $"Animation at index {i} has an empty or missing name."),
                        nameof(FlipperManifestEntry.Name)));
                }
                else
                {
                    if (!seenNames.Add(entry.Name.Trim()))
                    {
                        diagnostics.Add(new FlipperValidationDiagnostic(
                            FlipperValidationSeverity.Error,
                            "FZ011",
                            $"Duplicate animation name '{entry.Name}' found in asset pack.",
                            nameof(FlipperManifestEntry.Name)));
                    }

                    diagnostics.AddRange(entry.Validate(isMomentum));
                }

                string animName = entry?.Name ?? string.Create(CultureInfo.InvariantCulture, $"Animation_{i}");

                if (sprite == null)
                {
                    diagnostics.Add(new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Error,
                        "FZS001",
                        $"Sprite for animation '{animName}' is null."));
                    continue;
                }

                if (sprite.Frames == null || sprite.Frames.Count == 0)
                {
                    diagnostics.Add(new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Error,
                        "FZS002",
                        $"Animation '{animName}' contains 0 frames. Minimum 1 frame is required."));
                }

                if (sprite.Width <= 0 || sprite.Height <= 0 || sprite.Width > 128 || sprite.Height > 64)
                {
                    diagnostics.Add(new FlipperValidationDiagnostic(
                        FlipperValidationSeverity.Error,
                        "FZM001",
                        string.Create(CultureInfo.InvariantCulture, $"Animation '{animName}' dimensions ({sprite.Width}x{sprite.Height}) exceed maximum Flipper screen limits (128x64).")));
                }

                if (sprite.FlipperCycle?.FramesOrder != null && sprite.FlipperCycle.FramesOrder.Length > 0)
                {
                    int physicalCount = sprite.Frames?.Count ?? 0;
                    for (int f = 0; f < sprite.FlipperCycle.FramesOrder.Length; f++)
                    {
                        int frameIdx = sprite.FlipperCycle.FramesOrder[f];
                        if (frameIdx < 0 || frameIdx >= physicalCount)
                        {
                            diagnostics.Add(new FlipperValidationDiagnostic(
                                FlipperValidationSeverity.Error,
                                "FZM004",
                                string.Create(CultureInfo.InvariantCulture, $"Animation '{animName}' frames order references frame index {frameIdx} which does not exist (available frames: 0..{physicalCount - 1}).")));
                            break;
                        }
                    }
                }
            }

            return diagnostics;
        }

        public void ExportAssetPack(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetAssetPackFolder,
            bool isMomentum = true)
        {
            ArgumentNullException.ThrowIfNull(animations);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetAssetPackFolder);

            var diagnostics = ValidateAssetPackForExport(animations, isMomentum);
            var errors = diagnostics.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                string errorDetails = string.Join('\n', errors.Select(e => $"• [{e.Code}] {e.Message}"));
                throw new InvalidOperationException($"Asset pack export failed pre-flight validation:\n{errorDetails}");
            }

            string animsDir = isMomentum ? Path.Combine(targetAssetPackFolder, "Anims") : Path.Combine(targetAssetPackFolder, "dolphin");
            Directory.CreateDirectory(animsDir);

            string? iconsDir = null;
            if (isMomentum)
            {
                iconsDir = Path.Combine(targetAssetPackFolder, "Icons");
                Directory.CreateDirectory(iconsDir);
            }

            var manifest = new FlipperManifest();

            foreach (var (sprite, entry, animSettings) in animations)
            {
                animSettings.TargetFolder = targetAssetPackFolder;
                animSettings.TargetMode = isMomentum ? FlipperExportTargetMode.MomentumAssetPack : FlipperExportTargetMode.StockDolphin;
                animSettings.CreateManifestTxt = false; // We write the batch manifest below

                ExportAnimation(sprite, animSettings);
                manifest.Entries.Add(entry.Clone());
            }

            string manifestPath = Path.Combine(animsDir, "manifest.txt");
            SafeFileIo.WriteAllTextAtomic(manifestPath, manifest.Serialize(), maxRetries: 3, createBackup: false);

            // Generate asset pack main icon in Icons/ for Momentum
            if (isMomentum && iconsDir != null && animations.Count > 0 && animations[0].Sprite.Frames.Count > 0)
            {
                string firstAnimName = SanitizeAnimationName(animations[0].ManifestEntry.Name);
                string iconPath = Path.Combine(iconsDir, $"I_{firstAnimName}_10x10.bm");
                if (!File.Exists(iconPath))
                {
                    GeneratePackIcon(animations[0].Sprite, iconPath);
                }
            }
        }

        public async Task ExportAssetPackAsync(
            IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetAssetPackFolder,
            bool isMomentum = true,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(animations);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetAssetPackFolder);

            var diagnostics = ValidateAssetPackForExport(animations, isMomentum);
            var errors = diagnostics.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                string errorDetails = string.Join('\n', errors.Select(e => $"• [{e.Code}] {e.Message}"));
                throw new InvalidOperationException($"Asset pack export failed pre-flight validation:\n{errorDetails}");
            }

            await Task.Run(() =>
            {
                string animsDir = isMomentum ? Path.Combine(targetAssetPackFolder, "Anims") : Path.Combine(targetAssetPackFolder, "dolphin");
                Directory.CreateDirectory(animsDir);

                string? iconsDir = null;
                if (isMomentum)
                {
                    iconsDir = Path.Combine(targetAssetPackFolder, "Icons");
                    Directory.CreateDirectory(iconsDir);
                }

                var manifest = new FlipperManifest();
                int totalAnims = animations.Count;

                for (int animIdx = 0; animIdx < totalAnims; animIdx++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var (sprite, entry, animSettings) = animations[animIdx];
                    animSettings.TargetFolder = targetAssetPackFolder;
                    animSettings.TargetMode = isMomentum ? FlipperExportTargetMode.MomentumAssetPack : FlipperExportTargetMode.StockDolphin;
                    animSettings.CreateManifestTxt = false;

                    ExportAnimationParallel(sprite, animSettings, cancellationToken);
                    manifest.Entries.Add(entry.Clone());

                    progress?.Report((double)(animIdx + 1) / totalAnims);
                }

                string manifestPath = Path.Combine(animsDir, "manifest.txt");
                SafeFileIo.WriteAllTextAtomic(manifestPath, manifest.Serialize(), maxRetries: 3, createBackup: false);

                if (isMomentum && iconsDir != null && animations.Count > 0 && animations[0].Sprite.Frames.Count > 0)
                {
                    string firstAnimName = SanitizeAnimationName(animations[0].ManifestEntry.Name);
                    string iconPath = Path.Combine(iconsDir, $"I_{firstAnimName}_10x10.bm");
                    if (!File.Exists(iconPath))
                    {
                        GeneratePackIcon(animations[0].Sprite, iconPath);
                    }
                }
            }, cancellationToken);
        }

        private static void ExportAnimationParallel(SpriteState spriteState, FlipperExportSettings settings, CancellationToken cancellationToken = default)
        {
            string animName = SanitizeAnimationName(settings.AnimationName);
            string baseFolder = settings.TargetMode switch
            {
                FlipperExportTargetMode.MomentumAssetPack => Path.Combine(settings.TargetFolder, "Anims"),
                FlipperExportTargetMode.StockDolphin => Path.Combine(settings.TargetFolder, "dolphin"),
                _ => settings.TargetFolder,
            };

            string animFolder = Path.Combine(baseFolder, animName);
            Directory.CreateDirectory(animFolder);

            int totalFrames = spriteState.Frames.Count;

            // Generate meta.txt
            string metaPath = Path.Combine(animFolder, "meta.txt");
            string metaContent = BuildMetaTxt(spriteState, settings);
            SafeFileIo.WriteAllTextAtomic(metaPath, metaContent, maxRetries: 3, createBackup: false);

            // Generate frame_X.bm files with parallel compression
            Parallel.For(0, totalFrames, new ParallelOptions { CancellationToken = cancellationToken }, i =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var span = new bool[spriteState.Width * spriteState.Height];
                spriteState.CompositeFramePixels(i, span.AsSpan(), isExport: true);
                byte[] bmData = ConvertToFlipperBm(span, spriteState.Width, spriteState.Height);
                string framePath = Path.Combine(animFolder, string.Create(CultureInfo.InvariantCulture, $"frame_{i}.bm"));
                SafeFileIo.WriteBytesAtomic(framePath, bmData, maxRetries: 3, createBackup: false);
            });
        }

        public void ExportAssetPackZip(
            System.Collections.Generic.IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetZipFilePath,
            bool isMomentum = true)
        {
            ArgumentNullException.ThrowIfNull(animations);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetZipFilePath);

            var diagnostics = ValidateAssetPackForExport(animations, isMomentum);
            var errors = diagnostics.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                string errorDetails = string.Join('\n', errors.Select(e => $"• [{e.Code}] {e.Message}"));
                throw new InvalidOperationException($"Asset pack export failed pre-flight validation:\n{errorDetails}");
            }

            string stagingDir = Path.Combine(Path.GetTempPath(), "HexpritePackZipStaging_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDir);
            string tempZip = Path.Combine(Path.GetTempPath(), "HexpritePackZip_" + Guid.NewGuid().ToString("N") + ".zip");

            try
            {
                ExportAssetPack(animations, stagingDir, isMomentum);
                System.IO.Compression.ZipFile.CreateFromDirectory(stagingDir, tempZip);
                SafeFileIo.MoveAtomic(tempZip, targetZipFilePath, maxRetries: 5, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
                try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
            }
        }

        public async Task ExportAssetPackZipAsync(
            IReadOnlyList<(SpriteState Sprite, FlipperManifestEntry ManifestEntry, FlipperExportSettings Settings)> animations,
            string targetZipFilePath,
            bool isMomentum = true,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(animations);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetZipFilePath);

            var diagnostics = ValidateAssetPackForExport(animations, isMomentum);
            var errors = diagnostics.Where(d => d.Severity == FlipperValidationSeverity.Error).ToList();
            if (errors.Count > 0)
            {
                string errorDetails = string.Join('\n', errors.Select(e => $"• [{e.Code}] {e.Message}"));
                throw new InvalidOperationException($"Asset pack export failed pre-flight validation:\n{errorDetails}");
            }

            await Task.Run(async () =>
            {
                string? targetDir = Path.GetDirectoryName(targetZipFilePath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                string stagingZip = Path.Combine(targetDir ?? Path.GetTempPath(), $".hexp_tmp_{Guid.NewGuid():N}.zip");
                try
                {
                    await using (var fileStream = new FileStream(stagingZip, FileMode.Create, FileAccess.Write, FileShare.None))
                    await using (var archive = new System.IO.Compression.ZipArchive(fileStream, System.IO.Compression.ZipArchiveMode.Create))
                    {
                        string animsPrefix = isMomentum ? "Anims" : "dolphin";
                        var manifest = new FlipperManifest();
                        int totalAnims = animations.Count;

                        for (int animIdx = 0; animIdx < totalAnims; animIdx++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var (sprite, entry, animSettings) = animations[animIdx];
                            string animName = SanitizeAnimationName(animSettings.AnimationName);
                            string animFolderPrefix = $"{animsPrefix}/{animName}";

                            // 1. meta.txt
                            string metaTxt = BuildMetaTxt(sprite, animSettings);
                            var metaEntry = archive.CreateEntry($"{animFolderPrefix}/meta.txt", System.IO.Compression.CompressionLevel.Fastest);
                            await using (var entryStream = await metaEntry.OpenAsync(cancellationToken).ConfigureAwait(false))
                            await using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                            {
                                await writer.WriteAsync(metaTxt.AsMemory(), cancellationToken).ConfigureAwait(false);
                            }

                            // 2. frame_X.bm files - compress in parallel into in-memory byte arrays
                            int totalFrames = sprite.Frames.Count;
                            byte[][] frameBytes = new byte[totalFrames][];
                            Parallel.For(0, totalFrames, new ParallelOptions { CancellationToken = cancellationToken }, i =>
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                var span = new bool[sprite.Width * sprite.Height];
                                sprite.CompositeFramePixels(i, span.AsSpan(), isExport: true);
                                frameBytes[i] = ConvertToFlipperBm(span, sprite.Width, sprite.Height);
                            });

                            for (int i = 0; i < totalFrames; i++)
                            {
                                var frameEntry = archive.CreateEntry(string.Create(CultureInfo.InvariantCulture, $"{animFolderPrefix}/frame_{i}.bm"), System.IO.Compression.CompressionLevel.Fastest);
                                await using var entryStream = await frameEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                                await entryStream.WriteAsync(frameBytes[i].AsMemory(0, frameBytes[i].Length), cancellationToken).ConfigureAwait(false);
                            }

                            manifest.Entries.Add(entry.Clone());
                            progress?.Report((double)(animIdx + 1) / (totalAnims + 1));
                        }

                        // 3. manifest.txt
                        var manifestZipEntry = archive.CreateEntry($"{animsPrefix}/manifest.txt", System.IO.Compression.CompressionLevel.Fastest);
                        await using (var entryStream = await manifestZipEntry.OpenAsync(cancellationToken).ConfigureAwait(false))
                        await using (var writer = new StreamWriter(entryStream, Encoding.UTF8))
                        {
                            await writer.WriteAsync(manifest.Serialize().AsMemory(), cancellationToken).ConfigureAwait(false);
                        }

                        // 4. Momentum Icons
                        if (isMomentum && animations.Count > 0 && animations[0].Sprite.Frames.Count > 0)
                        {
                            string firstAnimName = SanitizeAnimationName(animations[0].ManifestEntry.Name);
                            byte[] iconData = GeneratePackIconBytes(animations[0].Sprite);
                            var iconEntry = archive.CreateEntry($"Icons/I_{firstAnimName}_10x10.bm", System.IO.Compression.CompressionLevel.Fastest);
                            await using var entryStream = await iconEntry.OpenAsync(cancellationToken).ConfigureAwait(false);
                            await entryStream.WriteAsync(iconData.AsMemory(0, iconData.Length), cancellationToken).ConfigureAwait(false);
                        }
                    }

                    SafeFileIo.MoveAtomic(stagingZip, targetZipFilePath, maxRetries: 5, overwrite: true);
                    progress?.Report(1.0);
                }
                finally
                {
                    try { if (File.Exists(stagingZip)) File.Delete(stagingZip); } catch { }
                }
            }, cancellationToken).ConfigureAwait(false);
        }

        public void ExportImage(SpriteState spriteState, int frameIndex, string targetFilePath)
        {
            ArgumentNullException.ThrowIfNull(spriteState);
            ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);

            if (spriteState.Width <= 0 || spriteState.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(spriteState), "Sprite dimensions must be greater than zero.");

            bool[] pixels = spriteState.CompositeFramePixels(frameIndex, isExport: true);
            byte[] bmData = ConvertToFlipperBm(pixels, spriteState.Width, spriteState.Height);
            SafeFileIo.WriteBytesAtomic(targetFilePath, bmData, maxRetries: 3, createBackup: false);
        }

        private static string BuildMetaTxt(SpriteState spriteState, FlipperExportSettings settings)
        {
            int totalFrames = spriteState.Frames.Count;

            // A real Flipper "dolphin-style" animation cycle survives round-trip
            // only if it's still consistent with the current (possibly edited
            // since import) physical frame list.
            var cycle = spriteState.FlipperCycle;
            bool cycleValid = cycle != null && Array.TrueForAll(cycle.FramesOrder, i => i < totalFrames);

            var sb = new StringBuilder();
            sb.AppendLine("Filetype: Flipper Animation");
            sb.AppendLine("Version: 1");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Width: {spriteState.Width}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Height: {spriteState.Height}");

            if (cycleValid)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Passive frames: {cycle!.PassiveFrameCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Active frames: {cycle.ActiveFrameCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Frames order: {string.Join(' ', cycle.FramesOrder)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Active cycles: {cycle.ActiveCycles}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Frame rate: {settings.FrameRate}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Duration: {cycle.Duration}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Active cooldown: {cycle.ActiveCooldown}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Bubble slots: {cycle.BubbleSlots}");

                var bubbles = cycle.SpeechBubbles.Count > 0
                    ? cycle.SpeechBubbles
                    : (cycle.SpeechBubble != null ? [cycle.SpeechBubble] : new List<FlipperSpeechBubble>());

                foreach (var bubble in bubbles)
                {
                    sb.AppendLine();
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Slot: {bubble.SlotIndex}");
                    if (bubble.StartFrame > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"StartFrame: {bubble.StartFrame}");
                    if (bubble.EndFrame > 0) sb.AppendLine(CultureInfo.InvariantCulture, $"EndFrame: {bubble.EndFrame}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Text: {bubble.Text}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"X: {bubble.X}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"Y: {bubble.Y}");
                    if (!string.IsNullOrEmpty(bubble.AlignH)) sb.AppendLine(CultureInfo.InvariantCulture, $"AlignH: {bubble.AlignH}");
                    if (!string.IsNullOrEmpty(bubble.AlignV)) sb.AppendLine(CultureInfo.InvariantCulture, $"AlignV: {bubble.AlignV}");
                }
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Passive frames: {settings.PassiveFrames}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Active frames: {settings.ActiveFrames}");

                // Frame order: 0 1 2 ... N-1
                sb.Append("Frames order: ");
                for (int i = 0; i < totalFrames; i++)
                {
                    sb.Append(i);
                    if (i < totalFrames - 1) sb.Append(' ');
                }
                sb.AppendLine();

                sb.AppendLine("Active cycles: 1");
                sb.AppendLine(CultureInfo.InvariantCulture, $"Frame rate: {settings.FrameRate}");
                sb.AppendLine("Duration: 3600");
                sb.AppendLine("Active cooldown: 0");
                sb.AppendLine("Bubble slots: 0");
            }

            return sb.ToString();
        }

        private static void GeneratePackIcon(SpriteState sprite, string targetIconPath)
        {
            byte[] bmData = GeneratePackIconBytes(sprite);
            SafeFileIo.WriteBytesAtomic(targetIconPath, bmData, maxRetries: 3, createBackup: false);
        }

        private static byte[] GeneratePackIconBytes(SpriteState sprite)
        {
            const int iconW = 10;
            const int iconH = 10;
            bool[] iconPixels = new bool[iconW * iconH];

            bool[] srcPixels = sprite.CompositeFramePixels(0, isExport: true);
            int srcW = sprite.Width;
            int srcH = sprite.Height;

            int minX = srcW, minY = srcH, maxX = -1, maxY = -1;
            for (int y = 0; y < srcH; y++)
            {
                for (int x = 0; x < srcW; x++)
                {
                    if (srcPixels[y * srcW + x])
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            if (maxX >= minX && maxY >= minY)
            {
                int boxW = maxX - minX + 1;
                int boxH = maxY - minY + 1;

                for (int ty = 0; ty < iconH; ty++)
                {
                    for (int tx = 0; tx < iconW; tx++)
                    {
                        int sx = minX + (tx * boxW) / iconW;
                        int sy = minY + (ty * boxH) / iconH;
                        if (sx < srcW && sy < srcH && srcPixels[sy * srcW + sx])
                        {
                            iconPixels[ty * iconW + tx] = true;
                        }
                    }
                }
            }

            return ConvertToFlipperBm(iconPixels, iconW, iconH);
        }

        private static byte[] ConvertToFlipperBm(bool[] pixels, int width, int height)
        {
            int rowBytes = (width + 7) / 8;
            byte[] buffer = new byte[rowBytes * height];
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (pixels[y * width + x])
                    {
                        int byteIndex = y * rowBytes + (x / 8);
                        int bit = x % 8;
                        buffer[byteIndex] |= (byte)(1 << bit);
                    }
                }
            }
            // Apply Heatshrink compression
            byte[] compressed = Hexprite.Services.Compression.HeatshrinkCompressor.Compress(buffer);
            
            // Only use compressed data if it actually saves space (including the 4-byte header)
            if (compressed.Length + 4 < buffer.Length)
            {
                byte[] finalBuffer = new byte[compressed.Length + 4];
                finalBuffer[0] = 0x01; // Heatshrink magic
                finalBuffer[1] = 0x00;
                
                // 2-byte little endian compressed length
                byte[] lenBytes = BitConverter.GetBytes((ushort)compressed.Length);
                finalBuffer[2] = lenBytes[0];
                finalBuffer[3] = lenBytes[1];
                
                Array.Copy(compressed, 0, finalBuffer, 4, compressed.Length);
                return finalBuffer;
            }

            // Otherwise, fall back to raw uncompressed
            return buffer;
        }
    }
}
