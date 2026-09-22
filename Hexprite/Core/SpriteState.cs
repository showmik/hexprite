using System.Text.Json.Serialization;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Globalization;

namespace Hexprite.Core
{
    public class SpriteState
    {
        /// <summary>Hard upper bound used by NewCanvasCommand to prevent OOM crashes.</summary>
        public const int MaxDimension = 512;

        public int Width { get; }
        public int Height { get; }
        
        /// <summary>
        /// The pixel buffer for the currently active layer.
        /// Updated automatically by <see cref="NormalizeLayerState"/> when the active layer or frame changes.
        /// </summary>
        public bool[] Pixels { get; set; }

        // ── Animation support ────────────────────────────────────────────
        public bool IsAnimationEnabled { get; set; }
        public int FrameRateFps { get; set; } = 12;
        public PlaybackDirection PlaybackDirection { get; set; } = PlaybackDirection.Forward;
        public List<FrameState> Frames { get; set; } = [];
        public int ActiveFrameIndex { get; set; }

        // ── Backward compatibility: LegacyLayers for migration ───────────
        [JsonInclude]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LayerState>? LegacyLayers { get; set; }

        public List<LayerState> Layers { get; set; } = [];
        public int ActiveLayerIndex { get; set; }

        [JsonIgnore]
        public bool[] ActiveLayerPixels
        {
            get
            {
                if (Frames != null && Frames.Count > 0 &&
                    ActiveFrameIndex >= 0 && ActiveFrameIndex < Frames.Count)
                {
                    var frame = Frames[ActiveFrameIndex];
                    if (frame.LayerPixels != null &&
                        ActiveLayerIndex >= 0 && ActiveLayerIndex < frame.LayerPixels.Count &&
                        frame.LayerPixels[ActiveLayerIndex] != null)
                    {
                        return frame.LayerPixels[ActiveLayerIndex].GetMonochromeData();
                    }
                }
                return Pixels ?? [];
            }
        }

        /// <summary>
        /// Color mode determines how pixels are interpreted during rendering and export.
        /// Monochrome: false=black (no transparency in data), true=white.
        /// Rgb: full color with alpha channel.
        /// There are exactly TWO modes - Indexed is deliberately omitted.
        /// </summary>
        public ColorMode ColorMode { get; set; } = ColorMode.Monochrome;

        /// <summary>
        /// Stored alongside pixels so that Undo/Redo can restore the visual invert
        /// state along with the pixel data.
        /// </summary>
        public bool IsDisplayInverted { get; set; }

        // ── Dynamic Link ────────────────────────────────────────────────
        public string? LinkedSourceFile { get; set; }
        public string? LinkedVariableName { get; set; }
        public ExportFormat? LinkedFormat { get; set; }

        [JsonIgnore]
        public bool IsLinked => !string.IsNullOrEmpty(LinkedSourceFile);

        [JsonIgnore]
        public string? LinkedSourceFileName => string.IsNullOrEmpty(LinkedSourceFile) ? null : System.IO.Path.GetFileName(LinkedSourceFile);

        /// <summary>
        /// Last-used export settings for this document.
        /// Nullable so that existing .hexp files (which lack this field) still
        /// deserialise cleanly; the ViewModel falls back to default settings when null.
        /// Not cloned into undo snapshots — settings changes are never undoable.
        /// </summary>
        public ExportSettings? ExportSettings { get; set; }

        /// <summary>
        /// Last-used image export settings (PNG, BMP, GIF).
        /// </summary>
        public ImageExportSettings? ImageExportSettings { get; set; }

        /// <summary>
        /// Passive/active playback-cycle metadata parsed from a real Flipper Zero
        /// "dolphin-style" animation import (Frames order / Passive frames /
        /// Active frames / Active cycles / Duration / Active cooldown / Bubble
        /// slots). Null for animations that never had this metadata (freshly
        /// created, or imported/exported without it).
        /// </summary>
        public FlipperAnimationCycle? FlipperCycle { get; set; }

        /// <summary>
        /// [JsonConstructor] tells System.Text.Json to use this constructor when
        /// deserializing. Parameter names must match property names (case-insensitive).
        /// Without this, get-only Width/Height come back as 0 and Pixels is null.
        /// </summary>
        [JsonConstructor]
        public SpriteState(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new bool[width * height];

            // Initialize with one frame containing one layer
            Layers.Add(new LayerState
            {
                Name = "Layer 1",
                IsVisible = true,
            });
            Frames.Add(new FrameState
            {
                Name = "Frame 1",
                LayerPixels = [new MonochromePixelBuffer(Pixels)],
            });
            ActiveLayerIndex = 0;
            ActiveFrameIndex = 0;
        }

        [JsonIgnore]
        public SelectionSnapshot? SelectionSnapshot { get; set; }

        public bool NormalizeLayerState()
        {
            bool changed = false;

            // 0. Sanitize framerate
            int clampedFps = Math.Clamp(FrameRateFps, 1, 60);
            if (FrameRateFps != clampedFps)
            {
                FrameRateFps = clampedFps;
                changed = true;
            }

            int safeW = Math.Clamp(Width, 1, 512);
            int safeH = Math.Clamp(Height, 1, 512);
            int pixelCount = safeW * safeH;

            // 1. Migrate LegacyLayers to global Layers
            if (LegacyLayers != null && LegacyLayers.Count > 0)
            {
                Layers = LegacyLayers;
                foreach (var l in Layers)
                {
                    if (string.IsNullOrWhiteSpace(l.Name)) l.Name = "Layer";
                }
                LegacyLayers = null;
                changed = true;
            }

            // 2. Ensure Frames list is not empty and has no null entries
            if (Frames == null)
            {
                Frames = [];
                changed = true;
            }
            else if (Frames.Count > 0)
            {
                int initialCount = Frames.Count;
                Frames.RemoveAll(f => f == null);
                if (Frames.Count != initialCount) changed = true;
            }

            if (Frames.Count == 0)
            {
                Frames = [new() { Name = "Frame 1", LayerPixels = [new MonochromePixelBuffer(pixelCount)] }];
                changed = true;
            }

            // 3. Migrate Frame-specific Layers to global Layers and LayerPixels
            bool legacyLayersAdopted = false;
            foreach (var frame in Frames)
            {
                if (frame.Layers != null && frame.Layers.Count > 0)
                {
                    if (!legacyLayersAdopted)
                    {
                        // Adopt metadata from the first frame that has it
                        Layers = frame.Layers.ConvertAll(l => new LayerState { Name = l.Name, IsVisible = l.IsVisible, IsLocked = l.IsLocked });
                        legacyLayersAdopted = true;
                        changed = true;
                    }

                    // Extract pixels
                    frame.LayerPixels = [];
                    foreach (var l in frame.Layers)
                    {
                        frame.LayerPixels.Add(new MonochromePixelBuffer(l.Pixels?.Length == pixelCount ? (bool[])l.Pixels.Clone() : new bool[pixelCount]));
                    }
                    frame.Layers = null; // Remove legacy data
                    changed = true;
                }
            }

            // 4. Ensure we have at least one layer globally and no null layers
            if (Layers == null)
            {
                Layers = [];
                changed = true;
            }
            else if (Layers.Count > 0)
            {
                int initialLayerCount = Layers.Count;
                Layers.RemoveAll(l => l == null);
                if (Layers.Count != initialLayerCount) changed = true;
            }

            if (Layers.Count == 0)
            {
                Layers.Add(new LayerState { Name = "Layer 1", IsVisible = true });
                changed = true;
            }

            // 5. Ensure all layers have valid names
            for (int i = 0; i < Layers.Count; i++)
            {
                string fallbackName = string.Create(CultureInfo.InvariantCulture, $"Layer {i + 1}");
                string normalizedName = string.IsNullOrWhiteSpace(Layers[i].Name) ? fallbackName : Layers[i].Name.Trim();
                if (Layers[i].Name != normalizedName)
                {
                    Layers[i].Name = normalizedName;
                    changed = true;
                }
            }

            // 6. Ensure every frame has exactly the right number of LayerPixels
            foreach (var frame in Frames)
            {
                if (frame.LayerPixels == null)
                {
                    frame.LayerPixels = [];
                    changed = true;
                }
                while (frame.LayerPixels.Count < Layers.Count)
                {
                    frame.LayerPixels.Add(new MonochromePixelBuffer(pixelCount));
                    changed = true;
                }
                if (frame.LayerPixels.Count > Layers.Count)
                {
                    frame.LayerPixels.RemoveRange(Layers.Count, frame.LayerPixels.Count - Layers.Count);
                    changed = true;
                }
                // Ensure array size
                for (int i = 0; i < frame.LayerPixels.Count; i++)
                {
                    if (frame.LayerPixels[i] == null)
                    {
                        frame.LayerPixels[i] = new MonochromePixelBuffer(pixelCount);
                        changed = true;
                    }
                    else if (frame.LayerPixels[i] is not OverflowPixelBuffer && frame.LayerPixels[i].GetMonochromeData().Length != pixelCount)
                    {
                        frame.LayerPixels[i] = new MonochromePixelBuffer(pixelCount);
                        changed = true;
                    }
                }
            }

            // Sync global layers
            for (int i = 0; i < Layers.Count; i++)
            {
                if (Layers[i].IsGlobal && Frames.Count > 0)
                {
                    var globalBuffer = Frames[0].LayerPixels[i];
                    for (int f = 1; f < Frames.Count; f++)
                    {
                        if (!ReferenceEquals(Frames[f].LayerPixels[i], globalBuffer))
                        {
                            Frames[f].LayerPixels[i] = globalBuffer;
                            changed = true;
                        }
                    }
                }
            }

            // Clamp ActiveFrameIndex
            int clampedFrame = Math.Clamp(ActiveFrameIndex, 0, Math.Max(0, Frames.Count - 1));
            if (ActiveFrameIndex != clampedFrame)
            {
                ActiveFrameIndex = clampedFrame;
                changed = true;
            }

            // Clamp ActiveLayerIndex
            int clampedActiveLayer = Math.Clamp(ActiveLayerIndex, 0, Math.Max(0, Layers.Count - 1));
            if (ActiveLayerIndex != clampedActiveLayer)
            {
                ActiveLayerIndex = clampedActiveLayer;
                changed = true;
            }

            // Sanitize FlipperCycle if present
            if (FlipperCycle != null)
            {
                if (FlipperCycle.FramesOrder != null && FlipperCycle.FramesOrder.Length > 0)
                {
                    var validOrder = FlipperCycle.FramesOrder
                        .Where(idx => idx >= 0 && idx < Frames.Count)
                        .ToArray();
                    if (validOrder.Length != FlipperCycle.FramesOrder.Length)
                    {
                        FlipperCycle.FramesOrder = validOrder;
                        changed = true;
                    }
                }
            }

            // Sync Pixels reference to current layer
            if (Frames.Count > 0 && Layers.Count > 0)
            {
                var activeBuffer = Frames[ActiveFrameIndex].LayerPixels[ActiveLayerIndex].GetMonochromeData();
                if (!ReferenceEquals(Pixels, activeBuffer))
                {
                    Pixels = activeBuffer;
                    changed = true;
                }
            }

            return changed;
        }

        public void EnsureLayers()
        {
            NormalizeLayerState();
        }

        /// <summary>
        /// Converts a layer to a shared global layer. The active/source frame's
        /// buffer becomes the shared buffer, while all previous frame buffers are
        /// retained for an optional later restore.
        /// </summary>
        public void GlobalizeLayer(int layerIndex, int sourceFrameIndex)
        {
            EnsureLayers();
            if (layerIndex < 0 || layerIndex >= Layers.Count || Frames == null || Frames.Count == 0)
                return;
            sourceFrameIndex = Math.Clamp(sourceFrameIndex, 0, Frames.Count - 1);
            var layer = Layers[layerIndex];
            if (layer.IsGlobal)
                return;

            layer.PreGlobalFramePixels = Frames
                .ConvertAll(frame => (frame.LayerPixels != null && layerIndex < frame.LayerPixels.Count && frame.LayerPixels[layerIndex] != null)
                    ? frame.LayerPixels[layerIndex].Clone()
                    : new MonochromePixelBuffer(Width * Height));

            var sourceFrame = Frames[sourceFrameIndex];
            var sharedBuffer = (sourceFrame.LayerPixels != null && layerIndex < sourceFrame.LayerPixels.Count && sourceFrame.LayerPixels[layerIndex] != null)
                ? sourceFrame.LayerPixels[layerIndex]
                : new MonochromePixelBuffer(Width * Height);

            foreach (var frame in Frames)
            {
                if (frame.LayerPixels != null && layerIndex < frame.LayerPixels.Count)
                {
                    frame.LayerPixels[layerIndex] = sharedBuffer;
                }
            }

            layer.IsGlobal = true;
            SetActiveLayer(ActiveLayerIndex);
        }

        /// <summary>
        /// Converts a global layer back to independent frame buffers.
        /// </summary>
        public void LocalizeLayer(int layerIndex, GlobalLayerLocalizeMode mode)
        {
            EnsureLayers();
            if (layerIndex < 0 || layerIndex >= Layers.Count || Frames.Count == 0)
                return;
            var layer = Layers[layerIndex];
            if (!layer.IsGlobal)
                return;

            var source = Frames[ActiveFrameIndex].LayerPixels[layerIndex];
            var backups = layer.PreGlobalFramePixels;
            bool canRestore = mode == GlobalLayerLocalizeMode.RestorePreviousContent &&
                backups is { Count: > 0 } && backups.Count == Frames.Count;

            if (canRestore)
            {
                for (int i = 0; i < Frames.Count; i++)
                    Frames[i].LayerPixels[layerIndex] = backups![i].Clone();
            }
            else
            {
                for (int i = 0; i < Frames.Count; i++)
                    Frames[i].LayerPixels[layerIndex] = source.Clone();
            }

            layer.IsGlobal = false;
            layer.PreGlobalFramePixels = null;
            SetActiveLayer(ActiveLayerIndex);
        }

        public void InsertGlobalBackupFromFrame(int insertIndex, int sourceFrameIndex)
        {
            if (sourceFrameIndex < 0 || sourceFrameIndex >= Frames.Count) return;
            foreach (var layer in Layers)
            {
                if (!layer.IsGlobal || layer.PreGlobalFramePixels == null) continue;
                layer.PreGlobalFramePixels.Insert(insertIndex,
                    layer.PreGlobalFramePixels[Math.Clamp(sourceFrameIndex, 0, layer.PreGlobalFramePixels.Count - 1)].Clone());
            }
        }

        public void RemoveGlobalBackupAt(int frameIndex)
        {
            foreach (var layer in Layers)
            {
                if (!layer.IsGlobal || layer.PreGlobalFramePixels == null) continue;
                if (frameIndex >= 0 && frameIndex < layer.PreGlobalFramePixels.Count)
                    layer.PreGlobalFramePixels.RemoveAt(frameIndex);
            }
        }

        /// <summary>Reorders persisted pre-global buffers to match a reordered frame list.</summary>
        public void ReorderGlobalBackups(IReadOnlyList<FrameState> previousFrameOrder)
        {
            foreach (var layer in Layers)
            {
                if (!layer.IsGlobal || layer.PreGlobalFramePixels == null ||
                    layer.PreGlobalFramePixels.Count != previousFrameOrder.Count)
                    continue;

                var byFrame = new Dictionary<FrameState, IPixelBuffer>();
                for (int i = 0; i < previousFrameOrder.Count; i++)
                    byFrame[previousFrameOrder[i]] = layer.PreGlobalFramePixels[i];

                layer.PreGlobalFramePixels = [.. Frames
                    .Where(byFrame.ContainsKey)
                    .Select(frame => byFrame[frame].Clone())];
            }
        }

        public void SetActiveLayerPixels(bool[] pixels)
        {
            EnsureLayers();
            if (pixels == null || pixels.Length != Width * Height)
                throw new ArgumentException("Active layer pixels must match canvas dimensions.", nameof(pixels));

            if (Frames == null || Frames.Count == 0 ||
                ActiveFrameIndex < 0 || ActiveFrameIndex >= Frames.Count)
                return;

            var currentFrame = Frames[ActiveFrameIndex];
            if (currentFrame.LayerPixels == null || ActiveLayerIndex < 0 || ActiveLayerIndex >= currentFrame.LayerPixels.Count)
                return;

            var existingBuffer = currentFrame.LayerPixels[ActiveLayerIndex];
            if (existingBuffer is OverflowPixelBuffer ovf)
            {
                ovf.WriteMonochromeData(pixels);
            }
            else
            {
                currentFrame.LayerPixels[ActiveLayerIndex] = new MonochromePixelBuffer(pixels);
            }
            
            if (Layers != null && ActiveLayerIndex < Layers.Count && Layers[ActiveLayerIndex].IsGlobal)
            {
                for (int f = 0; f < Frames.Count; f++)
                {
                    if (Frames[f].LayerPixels != null && ActiveLayerIndex < Frames[f].LayerPixels.Count)
                    {
                        Frames[f].LayerPixels[ActiveLayerIndex] = currentFrame.LayerPixels[ActiveLayerIndex];
                    }
                }
            }
            
            Pixels = currentFrame.LayerPixels[ActiveLayerIndex].GetMonochromeData();
        }

        public void SetActiveLayer(int index)
        {
            EnsureLayers();
            ActiveLayerIndex = Math.Clamp(index, 0, Math.Max(0, Layers.Count - 1));
            if (Frames != null && ActiveFrameIndex >= 0 && ActiveFrameIndex < Frames.Count)
            {
                var frame = Frames[ActiveFrameIndex];
                if (frame.LayerPixels != null && ActiveLayerIndex >= 0 && ActiveLayerIndex < frame.LayerPixels.Count && frame.LayerPixels[ActiveLayerIndex] != null)
                {
                    Pixels = frame.LayerPixels[ActiveLayerIndex].GetMonochromeData();
                }
            }
        }

        public void SetActiveFrame(int index)
        {
            EnsureLayers();
            ActiveFrameIndex = Math.Clamp(index, 0, Math.Max(0, Frames.Count - 1));
            if (Frames != null && ActiveFrameIndex >= 0 && ActiveFrameIndex < Frames.Count)
            {
                var frame = Frames[ActiveFrameIndex];
                if (frame.LayerPixels != null && ActiveLayerIndex >= 0 && ActiveLayerIndex < frame.LayerPixels.Count && frame.LayerPixels[ActiveLayerIndex] != null)
                {
                    Pixels = frame.LayerPixels[ActiveLayerIndex].GetMonochromeData();
                }
            }
        }

        [JsonIgnore]
        public IPixelBuffer? ActivePixelBuffer
        {
            get
            {
                if (Frames == null || Frames.Count == 0 || ActiveFrameIndex < 0 || ActiveFrameIndex >= Frames.Count)
                    return null;
                var frame = Frames[ActiveFrameIndex];
                if (frame.LayerPixels == null || ActiveLayerIndex < 0 || ActiveLayerIndex >= frame.LayerPixels.Count)
                    return null;
                return frame.LayerPixels[ActiveLayerIndex];
            }
        }

        public void SyncActiveLayer()
        {
            if (Frames == null || Frames.Count == 0 || Layers == null || Layers.Count == 0) return;
            if (ActiveFrameIndex < 0 || ActiveFrameIndex >= Frames.Count) return;
            var frame = Frames[ActiveFrameIndex];
            if (frame.LayerPixels == null || ActiveLayerIndex < 0 || ActiveLayerIndex >= frame.LayerPixels.Count) return;
            var activeBuffer = frame.LayerPixels[ActiveLayerIndex];
            if (activeBuffer != null && activeBuffer.PreserveOverflow)
            {
                activeBuffer.WriteMonochromeData(Pixels);
            }
        }

        public bool IsPixelOnWithOpacity(bool pixelValue, int i, LayerOpacityMode opacityMode)
        {
            if (!pixelValue) return false;
            if (opacityMode == LayerOpacityMode.Solid) return true;
            
            int x = i % Width;
            int y = i / Width;
            return opacityMode switch
            {
                LayerOpacityMode.Checkerboard => ((x + y) % 2) == 0,
                LayerOpacityMode.Sparse => (x % 2 == 0) && (y % 2 == 0),
                LayerOpacityMode.Dense => !((x % 2 != 0) && (y % 2 != 0)),
                _ => true,
            };
        }

        public static void ApplyBlendMode(Span<bool> composite, int i, bool layerOn, LayerBlendMode blendMode)
        {
            if (blendMode == LayerBlendMode.Normal)
            {
                if (layerOn) composite[i] = true;
            }
            else if (blendMode == LayerBlendMode.Xor)
            {
                if (layerOn) composite[i] = !composite[i];
            }
            else if (blendMode == LayerBlendMode.Mask)
            {
                if (!layerOn) composite[i] = false;
            }
            else if (blendMode == LayerBlendMode.Subtract)
            {
                if (layerOn) composite[i] = false;
            }
        }

        public static void ApplyBlendMode(bool[] composite, int i, bool layerOn, LayerBlendMode blendMode)
        {
            ApplyBlendMode(composite.AsSpan(), i, layerOn, blendMode);
        }

        /// <summary>
        /// Performs a flattened composite of all visible layers in the current frame directly into a destination span.
        /// </summary>
        public void CompositeVisiblePixels(Span<bool> destination, bool isExport = false)
        {
            CompositeFramePixels(ActiveFrameIndex, destination, isExport);
        }

        /// <summary>
        /// Performs a flattened composite of all visible layers in the current frame.
        /// </summary>
        public bool[] CompositeVisiblePixels(bool isExport = false)
        {
            var composite = new bool[Width * Height];
            CompositeVisiblePixels(composite.AsSpan(), isExport);
            return composite;
        }

        /// <summary>
        /// Performs a flattened composite of all visible layers in the specified frame index directly into a destination span.
        /// Zero heap allocations when destination span is provided.
        /// </summary>
        public void CompositeFramePixels(int frameIndex, Span<bool> destination, bool isExport = false)
        {
            int requiredLength = Width * Height;
            if (destination.Length < requiredLength)
            {
                throw new ArgumentException($"Destination span length ({destination.Length}) must be at least {requiredLength}.", nameof(destination));
            }

            destination.Slice(0, requiredLength).Clear();
            if (Frames == null || Frames.Count == 0) return;

            frameIndex = Math.Clamp(frameIndex, 0, Frames.Count - 1);
            var frame = Frames[frameIndex];
            if (Layers == null || frame.LayerPixels == null) return;

            int layerCount = Math.Min(Layers.Count, frame.LayerPixels.Count);
            for (int j = 0; j < layerCount; j++)
            {
                var layer = Layers[j];
                if (layer == null || !layer.IsVisible) continue;
                if (isExport && layer.ExcludeFromExport) continue;

                var pixelBuffer = frame.LayerPixels[j];
                if (pixelBuffer == null) continue;

                var layerPixels = pixelBuffer.GetMonochromeData();
                int len = Math.Min(requiredLength, layerPixels.Length);
                for (int i = 0; i < len; i++)
                {
                    bool layerOn = IsPixelOnWithOpacity(layerPixels[i], i, layer.OpacityMode);
                    ApplyBlendMode(destination, i, layerOn, layer.BlendMode);
                }
            }
        }

        /// <summary>
        /// Performs a flattened composite of all visible layers in the specified frame index.
        /// </summary>
        public bool[] CompositeFramePixels(int frameIndex, bool isExport = false)
        {
            var composite = new bool[Width * Height];
            CompositeFramePixels(frameIndex, composite.AsSpan(), isExport);
            return composite;
        }

        public SpriteState Clone(bool cloneSelectionSnapshot = true, bool includeOriginalFloatingPixels = true)
        {
            EnsureLayers();
            var clone = new SpriteState(Width, Height)
            {
                IsAnimationEnabled = IsAnimationEnabled,
                FrameRateFps = FrameRateFps,
                PlaybackDirection = PlaybackDirection,
                Layers = Layers.ConvertAll(l => l.Clone()),
                ActiveLayerIndex = ActiveLayerIndex,
                Frames = Frames.ConvertAll(f => f.Clone()),
                ActiveFrameIndex = ActiveFrameIndex,
                IsDisplayInverted = IsDisplayInverted,
                ColorMode = ColorMode,
                SelectionSnapshot = cloneSelectionSnapshot ? SelectionSnapshot?.Clone(includeOriginalFloatingPixels) : SelectionSnapshot,
                LinkedSourceFile = LinkedSourceFile,
                LinkedVariableName = LinkedVariableName,
                LinkedFormat = LinkedFormat,
                // Export settings are cloned so they are preserved across cloning operations (e.g. autosaves, canvas resizing, etc.).
                // To prevent Ctrl+Z from reverting user settings changes, RestoreState in MainViewModel explicitly preserves active settings.
                ExportSettings = ExportSettings?.Clone(),
                ImageExportSettings = ImageExportSettings?.Clone(),
                FlipperCycle = FlipperCycle?.Clone(),
            };
            clone.EnsureLayers();
            return clone;
        }

        public string ComputeHash()
        {
            using var ms = new System.IO.MemoryStream();
            using var writer = new System.IO.BinaryWriter(ms);

            writer.Write(Width);
            writer.Write(Height);
            writer.Write(IsAnimationEnabled);
            writer.Write(FrameRateFps);
            writer.Write((int)PlaybackDirection);
            writer.Write((int)ColorMode);
            writer.Write(IsDisplayInverted);

            writer.Write(LinkedSourceFile ?? string.Empty);
            writer.Write(LinkedVariableName ?? string.Empty);
            writer.Write(LinkedFormat.HasValue ? (int)LinkedFormat.Value : -1);

            foreach (var layer in Layers)
            {
                writer.Write(layer.Name ?? "");
                writer.Write(layer.IsVisible);
                writer.Write(layer.IsLocked);
                writer.Write(layer.IsGlobal);
                writer.Write(layer.PreGlobalFramePixels?.Count ?? -1);
                if (layer.PreGlobalFramePixels != null)
                {
                    foreach (var backup in layer.PreGlobalFramePixels)
                    {
                        writer.Write(backup is OverflowPixelBuffer);
                        writer.Write(backup.PreserveOverflow);
                        if (backup is OverflowPixelBuffer backupOverflow)
                        {
                            writer.Write(backupOverflow.ExtendedWidth);
                            writer.Write(backupOverflow.ExtendedHeight);
                            writer.Write(backupOverflow.MarginX);
                            writer.Write(backupOverflow.MarginY);
                            foreach (var value in backupOverflow.GetExtendedData())
                                writer.Write(value);
                        }
                        else
                        {
                            foreach (var value in backup.GetMonochromeData())
                                writer.Write(value);
                        }
                    }
                }
            }

            foreach (var frame in Frames)
            {
                writer.Write(frame.DelayMultiplier);
                foreach (var layerPixels in frame.LayerPixels)
                {
                    if (layerPixels is OverflowPixelBuffer ovf)
                    {
                        writer.Write(ovf.ExtendedWidth);
                        writer.Write(ovf.ExtendedHeight);
                        writer.Write(ovf.MarginX);
                        writer.Write(ovf.MarginY);
                        var data = ovf.GetExtendedData();
                        for (int i = 0; i < data.Length; i++)
                            writer.Write(data[i]);
                    }
                    else
                    {
                        var data = layerPixels.GetMonochromeData();
                        for (int i = 0; i < data.Length; i++)
                            writer.Write(data[i]);
                    }
                }
            }

            writer.Flush();
            return Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(ms.ToArray()));
        }
    }
}
