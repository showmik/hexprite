using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Hexprite.Core;
using Hexprite.Services.Compression;
using Serilog;

namespace Hexprite.Services
{
    public partial class FlipperImportService : IFlipperImportService
    {
        private static readonly Serilog.ILogger Logger = Log.ForContext<FlipperImportService>();

        [GeneratedRegex(@"(\d+)[^\d]*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
        private static partial Regex TrailingNumberRegex { get; }

        public static int NaturalCompare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    int startX = ix;
                    while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                    int startY = iy;
                    while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                    var numSpanX = x.AsSpan(startX, ix - startX);
                    var numSpanY = y.AsSpan(startY, iy - startY);

                    if (ulong.TryParse(numSpanX, NumberStyles.None, CultureInfo.InvariantCulture, out ulong numX) &&
                        ulong.TryParse(numSpanY, NumberStyles.None, CultureInfo.InvariantCulture, out ulong numY))
                    {
                        int cmp = numX.CompareTo(numY);
                        if (cmp != 0) return cmp;
                    }
                    else
                    {
                        int cmp = numSpanX.CompareTo(numSpanY, StringComparison.Ordinal);
                        if (cmp != 0) return cmp;
                    }
                }
                else
                {
                    int cmp = char.ToUpperInvariant(x[ix]).CompareTo(char.ToUpperInvariant(y[iy]));
                    if (cmp != 0) return cmp;
                    ix++;
                    iy++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }

        public static int ExtractFrameNumber(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) return int.MaxValue;
            string name = Path.GetFileNameWithoutExtension(filename);
            var match = System.Text.RegularExpressions.Regex.Match(name, @"\d+$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(200));
            if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int num))
            {
                return num;
            }
            return int.MaxValue;
        }

        public SpriteState ImportAnimation(string metaTxtPath)
        {
            if (string.IsNullOrWhiteSpace(metaTxtPath))
                throw new ArgumentException("Path cannot be empty.", nameof(metaTxtPath));

            string dir;
            string? metaFile = null;

            if (File.Exists(metaTxtPath))
            {
                metaFile = metaTxtPath;
                dir = Path.GetDirectoryName(metaTxtPath) ?? string.Empty;
            }
            else if (Directory.Exists(metaTxtPath))
            {
                dir = metaTxtPath;
                string candidateMeta = Path.Combine(metaTxtPath, "meta.txt");
                if (File.Exists(candidateMeta))
                {
                    metaFile = candidateMeta;
                }
            }
            else
            {
                throw new FileNotFoundException($"Animation directory or meta.txt not found: {metaTxtPath}");
            }

            int frameRate = 5;
            int width = 128;
            int height = 64;
            FlipperAnimationMeta? meta = null;

            if (metaFile != null && File.Exists(metaFile))
            {
                string text = SafeFileIo.ReadAllTextWithRetry(metaFile);
                meta = FlipperAnimationMeta.Parse(text);
                frameRate = meta.FrameRate > 0 ? meta.FrameRate : 5;
                if (meta.Width > 0) width = meta.Width;
                if (meta.Height > 0) height = meta.Height;
            }

            var sprite = new SpriteState(width, height)
            {
                ColorMode = ColorMode.Monochrome,
                FrameRateFps = frameRate,
            };
            sprite.Frames.Clear(); // Remove default frame

            // Discover animation frame files in order of priority:
            // 1. *.bm (Flipper native)
            // 2. *.png, *.bmp, *.jpg, *.jpeg
            // 3. *.gif
            // 4. *.hexp
            var frameFiles = new List<string>();
            if (Directory.Exists(dir))
            {
                var allBm = Directory.GetFiles(dir, "*.bm");
                if (allBm.Length > 0)
                {
                    var sortedBm = allBm.ToList();
                    sortedBm.Sort((a, b) => NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));
                    frameFiles.AddRange(sortedBm);
                }
                else
                {
                    // Check for image sequences
                    var imageExtensions = new[] { "*.png", "*.bmp", "*.jpg", "*.jpeg", "*.hexp" };
                    var discoveredImages = new List<string>();
                    foreach (var ext in imageExtensions)
                    {
                        discoveredImages.AddRange(Directory.GetFiles(dir, ext));
                    }

                    if (discoveredImages.Count > 0)
                    {
                        discoveredImages.Sort((a, b) => NaturalCompare(Path.GetFileName(a), Path.GetFileName(b)));
                        frameFiles.AddRange(discoveredImages);
                    }
                    else
                    {
                        // Check for animated gif
                        var gifs = Directory.GetFiles(dir, "*.gif");
                        if (gifs.Length > 0)
                        {
                            var gifFrames = DecomposeGif(gifs[0], width, height);
                            if (gifFrames.Count > 0)
                            {
                                foreach (var f in gifFrames) sprite.Frames.Add(f);
                            }
                        }
                    }
                }
            }

            if (sprite.Frames.Count == 0 && frameFiles.Count > 0)
            {
                int displayIndex = 0;
                foreach (var file in frameFiles)
                {
                    displayIndex++;
                    sprite.Frames.Add(ReadAnyFrame(file, width, height, displayIndex));
                }
            }

            if (sprite.Frames.Count == 0)
            {
                // If totally empty folder
                var emptyFrame = new FrameState { Name = "Frame 1" };
                emptyFrame.LayerPixels.Add(new MonochromePixelBuffer(new bool[width * height]));
                sprite.Frames.Add(emptyFrame);
            }

            // Configure FlipperCycle
            if (meta != null)
            {
                if (meta.FramesOrder.Length > 0
                    && Array.TrueForAll(meta.FramesOrder, i => i >= 0 && i < sprite.Frames.Count)
                    && meta.PassiveFrames + meta.ActiveFrames == meta.FramesOrder.Length)
                {
                    sprite.FlipperCycle = new FlipperAnimationCycle
                    {
                        FramesOrder = meta.FramesOrder,
                        PassiveFrameCount = meta.PassiveFrames,
                        ActiveFrameCount = meta.ActiveFrames,
                        ActiveCycles = meta.ActiveCycles,
                        Duration = meta.Duration,
                        ActiveCooldown = meta.ActiveCooldown,
                        BubbleSlots = meta.BubbleSlots,
                        SpeechBubbles = meta.SpeechBubbles,
                        SpeechBubble = meta.SpeechBubbles.Count > 0 ? meta.SpeechBubbles[0] : null,
                    };
                }
            }
            else
            {
                // Default cycle for imported folder without meta
                int frameCount = sprite.Frames.Count;
                int[] defaultOrder = Enumerable.Range(0, frameCount).ToArray();
                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    FramesOrder = defaultOrder,
                    PassiveFrameCount = frameCount,
                    ActiveFrameCount = 0,
                    ActiveCycles = 1,
                    Duration = Math.Max(1, frameCount / Math.Max(1, frameRate)),
                    ActiveCooldown = 0,
                };
            }

            return sprite;
        }

        public SpriteState ImportFrame(string bmFilePath, int width = 128, int height = 64)
        {
            var sprite = new SpriteState(width, height) { ColorMode = ColorMode.Monochrome };
            sprite.Frames.Clear();
            sprite.Frames.Add(ReadAnyFrame(bmFilePath, width, height, 1));
            return sprite;
        }

        public List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> ImportAssetPack(string assetPackDirOrManifestPath)
        {
            if (string.IsNullOrWhiteSpace(assetPackDirOrManifestPath))
                throw new ArgumentException("Path cannot be empty.", nameof(assetPackDirOrManifestPath));

            if (assetPackDirOrManifestPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(assetPackDirOrManifestPath))
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "HexpritePackZip_" + Guid.NewGuid().ToString("N"));
                try
                {
                    try
                    {
                        SafeZipExtractor.SafeExtractToDirectory(assetPackDirOrManifestPath, tempDir);
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new IOException(ex.Message, ex);
                    }
                    return ImportAssetPack(tempDir);
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, recursive: true);
                        }
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }

            var results = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();

            if (File.Exists(assetPackDirOrManifestPath))
            {
                string fileName = Path.GetFileName(assetPackDirOrManifestPath);
                if (fileName.Equals("meta.txt", StringComparison.OrdinalIgnoreCase))
                {
                    string animDir = Path.GetDirectoryName(assetPackDirOrManifestPath) ?? string.Empty;
                    string animName = Path.GetFileName(animDir);
                    var sprite = ImportAnimation(assetPackDirOrManifestPath);
                    var entry = new FlipperManifestEntry { Name = animName, MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
                    results.Add((animName, sprite, entry));
                    return results;
                }

                // If pointing to a manifest.txt
                string manifestPath = assetPackDirOrManifestPath;
                string baseDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;
                return ImportFromManifest(manifestPath, baseDir);
            }

            if (Directory.Exists(assetPackDirOrManifestPath))
            {
                string baseDir = assetPackDirOrManifestPath;

                // 1. Check if directory is itself a single animation folder
                string directMeta = Path.Combine(baseDir, "meta.txt");
                if (File.Exists(directMeta))
                {
                    string animName = Path.GetFileName(baseDir);
                    var sprite = ImportAnimation(directMeta);
                    var entry = new FlipperManifestEntry { Name = animName, MinLevel = 1, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 };
                    results.Add((animName, sprite, entry));
                    return results;
                }

                // 2. Look for manifest.txt at standard locations or subdirectories
                string? manifestPath = FindManifestPath(baseDir);
                if (manifestPath != null && File.Exists(manifestPath))
                {
                    string animsBaseDir = Path.GetDirectoryName(manifestPath) ?? baseDir;
                    return ImportFromManifest(manifestPath, animsBaseDir);
                }

                // 3. Fallback: Scan for all folders containing meta.txt
                var metaFiles = Directory.GetFiles(baseDir, "meta.txt", SearchOption.AllDirectories);
                if (metaFiles.Length > 0)
                {
                    foreach (var metaPath in metaFiles)
                    {
                        try
                        {
                            string animDir = Path.GetDirectoryName(metaPath) ?? string.Empty;
                            string animName = Path.GetFileName(animDir);
                            var sprite = ImportAnimation(metaPath);
                            var entry = new FlipperManifestEntry
                            {
                                Name = animName,
                                MinLevel = 1,
                                MaxLevel = 30,
                                MinButthurt = 0,
                                MaxButthurt = 14,
                                Weight = 1,
                            };
                            results.Add((animName, sprite, entry));
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "Failed to import discovered meta.txt at {Path}", metaPath);
                        }
                    }

                    if (results.Count > 0) return results;
                }

                // 4. Loose Folder Auto-Discovery: Scan subdirectories for image sequences
                var subDirs = Directory.GetDirectories(baseDir);
                foreach (var sub in subDirs)
                {
                    if (HasAnimationFrames(sub))
                    {
                        try
                        {
                            string animName = Path.GetFileName(sub);
                            var sprite = ImportAnimation(sub);
                            var entry = new FlipperManifestEntry
                            {
                                Name = animName,
                                MinLevel = 1,
                                MaxLevel = 30,
                                MinButthurt = 0,
                                MaxButthurt = 14,
                                Weight = 1,
                            };
                            results.Add((animName, sprite, entry));
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "Failed to import discovered frame folder at {Path}", sub);
                        }
                    }
                }

                if (results.Count > 0) return results;

                // 5. If root directory itself contains frames directly
                if (HasAnimationFrames(baseDir))
                {
                    string rootAnimName = Path.GetFileName(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (string.IsNullOrWhiteSpace(rootAnimName)) rootAnimName = "Animation_1";
                    var sprite = ImportAnimation(baseDir);
                    var entry = new FlipperManifestEntry
                    {
                        Name = rootAnimName,
                        MinLevel = 1,
                        MaxLevel = 30,
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        Weight = 1,
                    };
                    results.Add((rootAnimName, sprite, entry));
                    return results;
                }

                throw new FileNotFoundException($"No manifest.txt or animation meta.txt found in asset pack directory: {assetPackDirOrManifestPath}");
            }

            throw new DirectoryNotFoundException($"Asset pack path does not exist: {assetPackDirOrManifestPath}");
        }

        private static bool HasAnimationFrames(string dir)
        {
            if (!Directory.Exists(dir)) return false;
            string[] patterns = ["*.bm", "*.png", "*.bmp", "*.jpg", "*.jpeg", "*.gif", "*.hexp"];
            foreach (var p in patterns)
            {
                if (Directory.EnumerateFiles(dir, p).Any()) return true;
            }
            return false;
        }

        public async Task<List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>> ImportAssetPackAsync(
            string assetPackDirOrManifestPath,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(assetPackDirOrManifestPath))
                throw new ArgumentException("Path cannot be empty.", nameof(assetPackDirOrManifestPath));

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = ImportAssetPack(assetPackDirOrManifestPath);
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(1.0);
                return result;
            }, cancellationToken);
        }

        private List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> ImportFromManifest(string manifestPath, string baseDir)
        {
            var manifest = FlipperManifest.Parse(SafeFileIo.ReadAllTextWithRetry(manifestPath));
            var results = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>();

            foreach (var entry in manifest.Entries)
            {
                string? animFolder = ResolveAnimationDirectory(baseDir, entry.Name);
                if (animFolder != null)
                {
                    try
                    {
                        var sprite = ImportAnimation(animFolder);
                        results.Add((entry.Name, sprite, entry));
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to import animation '{Anim}' from manifest", entry.Name);
                    }
                }
                else
                {
                    Logger.Warning("Animation folder for '{Anim}' not found in {BaseDir}; inserting placeholder sprite", entry.Name, baseDir);
                }

                // Fallback: Retain entry with default 128x64 placeholder SpriteState
                var placeholderSprite = CreatePlaceholderSprite();
                results.Add((entry.Name, placeholderSprite, entry));
            }

            return results;
        }

        private static SpriteState CreatePlaceholderSprite()
        {
            var sprite = new SpriteState(128, 64)
            {
                ColorMode = ColorMode.Monochrome,
                FrameRateFps = 5,
            };
            sprite.Frames.Clear();
            var frame = new FrameState { Name = "Frame 1" };
            frame.LayerPixels.Add(new MonochromePixelBuffer(new bool[128 * 64]));
            sprite.Frames.Add(frame);
            return sprite;
        }

        private static string? FindManifestPath(string baseDir)
        {
            string[] directCandidates =
            [
                Path.Combine(baseDir, "Anims", "manifest.txt"),
                Path.Combine(baseDir, "anims", "manifest.txt"),
                Path.Combine(baseDir, "dolphin", "manifest.txt"),
                Path.Combine(baseDir, "manifest.txt"),
            ];

            foreach (var c in directCandidates)
            {
                if (File.Exists(c)) return c;
            }

            // Recursive search
            var allManifests = Directory.GetFiles(baseDir, "manifest.txt", SearchOption.AllDirectories);
            if (allManifests.Length > 0)
            {
                // Prefer Anims/manifest.txt if multiple exist
                foreach (var m in allManifests)
                {
                    if (m.Contains("Anims", StringComparison.OrdinalIgnoreCase)) return m;
                }
                return allManifests[0];
            }

            return null;
        }

        private static string? ResolveAnimationDirectory(string baseDir, string animName)
        {
            // 1. Direct path
            string direct = Path.Combine(baseDir, animName);
            if (Directory.Exists(direct)) return direct;

            // 2. Case-insensitive lookup in baseDir
            if (Directory.Exists(baseDir))
            {
                foreach (var dir in Directory.GetDirectories(baseDir))
                {
                    if (Path.GetFileName(dir).Equals(animName, StringComparison.OrdinalIgnoreCase))
                    {
                        return dir;
                    }
                }
            }

            // 3. Search in subdirectories
            var matching = Directory.GetDirectories(baseDir, animName, SearchOption.AllDirectories);
            if (matching.Length > 0) return matching[0];

            return null;
        }

        private static List<FrameState> DecomposeGif(string gifPath, int width, int height)
        {
            var frames = new List<FrameState>();
            try
            {
                using var stream = File.Open(gifPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);

                int frameIndex = 0;
                foreach (var gifFrame in decoder.Frames)
                {
                    frameIndex++;
                    var frame = ConvertBitmapSourceToFrame(gifFrame, width, height, frameIndex);
                    frames.Add(frame);
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to decompose animated GIF at {Path}", gifPath);
            }
            return frames;
        }

        private static FrameState ReadAnyFrame(string path, int width, int height, int index)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".bm")
            {
                return ReadFrame(path, width, height, index);
            }

            if (ext is ".png" or ".bmp" or ".jpg" or ".jpeg")
            {
                try
                {
                    var (pixels, imgW, imgH, _) = BitmapToMonochromeConverter.ConvertTo1Bit(path, new BitmapImportSettings
                    {
                        MaxDimension = Math.Max(width, height),
                        Threshold = 128
                    });

                    // Center/fit pixels into target width x height
                    bool[] finalPixels = new bool[width * height];
                    int offsetX = Math.Max(0, (width - imgW) / 2);
                    int offsetY = Math.Max(0, (height - imgH) / 2);
                    int copyW = Math.Min(width, imgW);
                    int copyH = Math.Min(height, imgH);

                    for (int y = 0; y < copyH; y++)
                    {
                        for (int x = 0; x < copyW; x++)
                        {
                            int srcIdx = y * imgW + x;
                            int dstIdx = (y + offsetY) * width + (x + offsetX);
                            if (srcIdx < pixels.Length && dstIdx < finalPixels.Length)
                            {
                                finalPixels[dstIdx] = pixels[srcIdx];
                            }
                        }
                    }

                    var frame = new FrameState { Name = string.Create(CultureInfo.InvariantCulture, $"Frame {index}") };
                    frame.LayerPixels.Add(new MonochromePixelBuffer(finalPixels));
                    return frame;
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to convert image frame '{Path}' to 1-bit; inserting blank", Path.GetFileName(path));
                    return BlankFrame(width, height, index);
                }
            }

            return ReadFrame(path, width, height, index);
        }

        private static FrameState ConvertBitmapSourceToFrame(System.Windows.Media.Imaging.BitmapSource source, int width, int height, int index)
        {
            var formatted = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            int srcW = formatted.PixelWidth;
            int srcH = formatted.PixelHeight;
            int stride = srcW * 4;
            byte[] bgra = new byte[stride * srcH];
            formatted.CopyPixels(bgra, stride, 0);

            bool[] pixels = new bool[width * height];
            int offsetX = Math.Max(0, (width - srcW) / 2);
            int offsetY = Math.Max(0, (height - srcH) / 2);
            int copyW = Math.Min(width, srcW);
            int copyH = Math.Min(height, srcH);

            for (int y = 0; y < copyH; y++)
            {
                for (int x = 0; x < copyW; x++)
                {
                    int bgraIdx = (y * stride) + (x * 4);
                    byte b = bgra[bgraIdx];
                    byte g = bgra[bgraIdx + 1];
                    byte r = bgra[bgraIdx + 2];
                    byte a = bgra[bgraIdx + 3];

                    bool isOn = a > 64 && (0.299 * r + 0.587 * g + 0.114 * b) < 128;
                    int dstIdx = (y + offsetY) * width + (x + offsetX);
                    if (dstIdx < pixels.Length)
                    {
                        pixels[dstIdx] = isOn;
                    }
                }
            }

            var frame = new FrameState { Name = string.Create(CultureInfo.InvariantCulture, $"Frame {index}") };
            frame.LayerPixels.Add(new MonochromePixelBuffer(pixels));
            return frame;
        }

        private static FrameState ReadFrame(string path, int width, int height, int index)
        {
            if (width <= 0 || height <= 0)
            {
                return BlankFrame(Math.Max(1, width), Math.Max(1, height), index);
            }

            int expectedBytes = (width * height) / 8;
            if (expectedBytes <= 0)
            {
                return BlankFrame(width, height, index);
            }

            byte[] buffer;
            try
            {
                buffer = SafeFileIo.ReadAllBytesWithRetry(path);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to read frame file '{Frame}'; inserting blank placeholder", Path.GetFileName(path));
                return BlankFrame(width, height, index);
            }

            // Flipper .bm format (from flipperzero-firmware lib/toolbox/compress.c):
            //   CompressHeader { uint8 is_compressed; uint8 reserved; uint16 compressed_buff_size; }
            //   is_compressed == 0x01 -> heatshrink-compressed payload follows (little-endian len)
            //   is_compressed == 0x00 -> raw pixel bytes follow immediately (1 header byte)
            // A file whose length exactly equals expectedBytes is treated as raw with no
            // header (Hexprite's own headerless raw export path).
            byte[] rawPixels;

            try
            {
                if (buffer.Length == expectedBytes)
                {
                    // Headerless raw (Hexprite export). Use as-is.
                    rawPixels = buffer;
                }
                else if (buffer.Length >= 4 && buffer[0] == 0x01 && buffer[1] == 0x00)
                {
                    // Flipper compressed: 4-byte header + heatshrink payload.
                    int compLen = BitConverter.ToUInt16(buffer, 2);
                    if (compLen <= 0 || 4 + compLen > buffer.Length)
                    {
                        Logger.Warning("Frame '{Frame}' declares {CompLen} compressed bytes but file only has {Avail} available; inserting blank placeholder",
                            Path.GetFileName(path), compLen, buffer.Length - 4);
                        return BlankFrame(width, height, index);
                    }

                    byte[] compData = new byte[compLen];
                    Array.Copy(buffer, 4, compData, 0, compLen);
                    try
                    {
                        rawPixels = HeatshrinkCompressor.Decompress(compData, expectedBytes);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to decompress frame '{Frame}'; inserting blank placeholder", Path.GetFileName(path));
                        return BlankFrame(width, height, index);
                    }
                }
                else if (buffer.Length == expectedBytes + 1 && buffer[0] == 0x00)
                {
                    // Flipper uncompressed: 0x00 header + raw pixels.
                    rawPixels = new byte[expectedBytes];
                    Array.Copy(buffer, 1, rawPixels, 0, expectedBytes);
                }
                else
                {
                    // Unexpected content — best effort, bounds-checked in BuildFrameFromRaw.
                    rawPixels = buffer;
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "Failed to process frame '{Frame}'; inserting blank placeholder", Path.GetFileName(path));
                return BlankFrame(width, height, index);
            }

            return BuildFrameFromRaw(rawPixels, width, height, index);
        }

        private static FrameState BlankFrame(int width, int height, int index)
        {
            var frame = new FrameState { Name = string.Create(CultureInfo.InvariantCulture, $"Frame {index}") };
            frame.LayerPixels.Add(new MonochromePixelBuffer(new bool[width * height]));
            return frame;
        }

        private static FrameState BuildFrameFromRaw(byte[] buffer, int width, int height, int index)
        {
            var frame = new FrameState { Name = string.Create(CultureInfo.InvariantCulture, $"Frame {index}") };
            bool[] pixels = new bool[width * height];
            
            int rowBytes = (width + 7) / 8;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int byteIndex = y * rowBytes + (x / 8);
                    if (byteIndex >= buffer.Length) continue; // safety against corrupt files
                    
                    int bit = x % 8;
                    pixels[y * width + x] = (buffer[byteIndex] & (1 << bit)) != 0;
                }
            }
            
            frame.LayerPixels.Add(new MonochromePixelBuffer(pixels));
            return frame;
        }
    }
}
