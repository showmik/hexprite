using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Rendering;
using Hexprite.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel
    {
        // ── Animation / Frames ────────────────────────────────────────────
        public ObservableCollection<FrameItemViewModel> Frames { get; } = [];

        public int SelectedFrameCount => Frames.Count(frame => frame.IsSelected);
        public bool HasMultipleFramesSelected => SelectedFrameCount > 1;
        public string FrameSelectionSummary => HasMultipleFramesSelected
            ? string.Create(CultureInfo.InvariantCulture, $"{SelectedFrameCount} selected \u00b7 {CurrentFrameDisplay}"
)
            : CurrentFrameDisplay;

        private int _selectedFrameIndex;
        private bool _suppressSelectedFrameBindingToState;

        public int SelectedFrameIndex
        {
            get => _selectedFrameIndex;
            set
            {
                if (_suppressSelectedFrameBindingToState)
                    return;
                if (SetProperty(ref _selectedFrameIndex, value))
                {
                    SetActiveFrame(value);
                    OnPropertyChanged(nameof(CurrentFrameDisplay));
                    OnPropertyChanged(nameof(FrameSelectionSummary));
                }
            }
        }

        public string CurrentFrameDisplay => string.Create(CultureInfo.InvariantCulture, $"Frame {(SelectedFrameIndex >= 0 ? SelectedFrameIndex + 1 : 0)} / {Frames?.Count ?? 0}");

        public string EstimatedMemoryUsage
        {
            get
            {
                if (SpriteState == null) return "0 B";
                int bytesPerFrame = SpriteState.ColorMode == ColorMode.Rgb
                    ? SpriteState.Width * SpriteState.Height * 4
                    : ((SpriteState.Width + 7) / 8) * SpriteState.Height;
                int totalBytes = bytesPerFrame * Math.Max(1, SpriteState.Frames.Count);
                return totalBytes >= 1024 * 1024
                    ? string.Create(CultureInfo.InvariantCulture, $"{totalBytes / (1024.0 * 1024.0):F1} MB")
                    : totalBytes >= 1024
                        ? string.Create(CultureInfo.InvariantCulture, $"{totalBytes / 1024.0:F1} KB")
                        : string.Create(CultureInfo.InvariantCulture, $"{totalBytes} B");
            }
        }

        /// <summary>
        /// Synchronizes the native WPF extended ListBox selection with the view model.
        /// The active frame is the item the user most recently made current.
        /// </summary>
        public void SynchronizeFrameSelection(IEnumerable<FrameItemViewModel> selectedItems, FrameItemViewModel? activeItem)
        {
            if (SpriteState == null || _suppressSelectedFrameBindingToState) return;

            var selected = selectedItems
                .Select(Frames.IndexOf)
                .Where(index => index >= 0)
                .Distinct()
                .ToHashSet();

            int activeIndex = activeItem == null ? -1 : Frames.IndexOf(activeItem);
            if (activeIndex < 0)
                activeIndex = SpriteState.ActiveFrameIndex;
            if (selected.Count == 0)
                selected.Add(activeIndex);

            foreach (var (frame, index) in Frames.Select((frame, index) => (frame, index)))
                frame.IsSelected = selected.Contains(index);

            if (activeIndex >= 0 && activeIndex < SpriteState.Frames.Count && SpriteState.ActiveFrameIndex != activeIndex)
            {
                _suppressSelectedFrameBindingToState = true;
                try
                {
                    SetActiveFrame(activeIndex);
                }
                finally
                {
                    _suppressSelectedFrameBindingToState = false;
                }
            }

            NotifyFrameSelectionChanged();
        }

        public void SelectAllFrames()
        {
            foreach (var frame in Frames)
                frame.IsSelected = true;
            NotifyFrameSelectionChanged();
        }

        public void SelectActiveFrameOnly()
        {
            int activeIndex = SpriteState?.ActiveFrameIndex ?? 0;
            for (int i = 0; i < Frames.Count; i++)
                Frames[i].IsSelected = i == activeIndex;
            NotifyFrameSelectionChanged();
        }

        private void NotifyFrameSelectionChanged()
        {
            OnPropertyChanged(nameof(SelectedFrameCount));
            OnPropertyChanged(nameof(HasMultipleFramesSelected));
            OnPropertyChanged(nameof(FrameSelectionSummary));
            OnPropertyChanged(nameof(EstimatedMemoryUsage));
            BatchFrameOperationCommand?.NotifyCanExecuteChanged();
            DuplicateFrameCommand?.NotifyCanExecuteChanged();
            DeleteFrameCommand?.NotifyCanExecuteChanged();
            ClearFrameCommand?.NotifyCanExecuteChanged();
            SelectAllFramesCommand?.NotifyCanExecuteChanged();
            SelectActiveFrameOnlyCommand?.NotifyCanExecuteChanged();
            NextFrameCommand?.NotifyCanExecuteChanged();
            PreviousFrameCommand?.NotifyCanExecuteChanged();
        }

        private List<int> GetSelectedFrameIndicesOrActive()
        {
            var indices = Frames
                .Select((frame, index) => new { frame, index })
                .Where(item => item.frame.IsSelected)
                .Select(item => item.index)
                .Where(index => SpriteState != null && index >= 0 && index < SpriteState.Frames.Count)
                .Distinct()
                .Order()
                .ToList();

            if (indices.Count == 0 && SpriteState != null)
                indices.Add(SpriteState.ActiveFrameIndex);
            return indices;
        }

        private bool CanExecuteBatchFrameOperation(FrameBatchOperation operation)
        {
            if (!IsAnimationEnabled || SpriteState == null || IsProcessing)
                return false;

            return operation == FrameBatchOperation.ClearEntireFrame || CanModifyActiveLayer;
        }

        private void ExecuteBatchFrameOperation(FrameBatchOperation operation)
        {
            if (!CanExecuteBatchFrameOperation(operation) || SpriteState == null)
                return;

            StopTextEditing();
            if (_selectionService.IsFloating)
                _selectionInput.CommitIfActive();
            if (_selectionService.HasActiveSelection)
                _selectionService.Cancel();
            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();
            SpriteState.SyncActiveLayer();

            var targetIndices = GetSelectedFrameIndicesOrActive();
            var affectedIndices = new HashSet<int>();
            int skippedLayers = 0;
            bool changed = false;

            if (operation == FrameBatchOperation.ClearEntireFrame)
            {
                foreach (int frameIndex in targetIndices)
                {
                    var frame = SpriteState.Frames[frameIndex];
                    for (int layerIndex = 0; layerIndex < frame.LayerPixels.Count; layerIndex++)
                    {
                        // Frame-local commands must never mutate a protected or shared global layer.
                        if (SpriteState.Layers[layerIndex].IsLocked || SpriteState.Layers[layerIndex].IsGlobal)
                        {
                            skippedLayers++;
                            continue;
                        }

                        var buffer = frame.LayerPixels[layerIndex];
                        if (!BufferHasPixels(buffer))
                            continue;

                        if (!changed)
                            SaveStateForUndo();
                        changed = true;
                        ClearBuffer(buffer);
                        affectedIndices.Add(frameIndex);
                    }
                }
            }
            else
            {
                int layerIndex = SpriteState.ActiveLayerIndex;
                var processedBuffers = new HashSet<IPixelBuffer>();
                foreach (int frameIndex in targetIndices)
                {
                    var buffer = SpriteState.Frames[frameIndex].LayerPixels[layerIndex];
                    if (!processedBuffers.Add(buffer))
                        continue;

                    bool bufferWillChange = operation == FrameBatchOperation.InvertActiveLayer || BufferHasPixels(buffer);
                    if (!bufferWillChange)
                        continue;

                    if (!changed)
                        SaveStateForUndo();
                    changed = true;
                    for (int index = 0; index < SpriteState.Frames.Count; index++)
                    {
                        if (ReferenceEquals(SpriteState.Frames[index].LayerPixels[layerIndex], buffer))
                            affectedIndices.Add(index);
                    }
                    ApplyBatchOperationToBuffer(buffer, operation);
                }
            }

            if (!changed)
            {
                ShowStatus("No selected frame pixels to change");
                return;
            }

            SpriteState.SetActiveFrame(SpriteState.ActiveFrameIndex);
            RefreshFrameThumbnails(affectedIndices);
            UpdateAllLayersContentIndicator();
            UpdateOnionSkinCache();
            if (IsPlaying)
                RebuildPlaybackCache();
            RedrawGridFromMemory();
            MarkCodeStale();

            string operationName = operation switch
            {
                FrameBatchOperation.InvertActiveLayer => "inverted active layer",
                FrameBatchOperation.ClearActiveLayer => "cleared active layer",
                FrameBatchOperation.ShiftLeft => "shifted left",
                FrameBatchOperation.ShiftRight => "shifted right",
                FrameBatchOperation.ShiftUp => "shifted up",
                FrameBatchOperation.ShiftDown => "shifted down",
                FrameBatchOperation.FlipActiveLayerHorizontal => "flipped horizontally",
                FrameBatchOperation.FlipActiveLayerVertical => "flipped vertically",
                _ => "cleared frame",
            };
            string suffix = skippedLayers > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({skippedLayers} protected layer{(skippedLayers == 1 ? string.Empty : "s")} skipped)") : string.Empty;
            ShowStatus($"{targetIndices.Count} frame{(targetIndices.Count == 1 ? string.Empty : "s")} {operationName}{suffix}");
        }

        private static bool BufferHasPixels(IPixelBuffer buffer) => buffer is OverflowPixelBuffer overflow
            ? Array.IndexOf(overflow.GetExtendedData(), value: true) >= 0
            : Array.IndexOf(buffer.GetMonochromeData(), value: true) >= 0;

        private static void ClearBuffer(IPixelBuffer buffer)
        {
            if (buffer is OverflowPixelBuffer overflow)
            {
                Array.Clear(overflow.GetExtendedData(), 0, overflow.GetExtendedData().Length);
                overflow.InvalidateViewCache();
            }
            else
            {
                var pixels = buffer.GetMonochromeData();
                Array.Clear(pixels, 0, pixels.Length);
            }
        }

        private void ApplyBatchOperationToBuffer(IPixelBuffer buffer, FrameBatchOperation operation)
        {
            if (operation == FrameBatchOperation.ClearActiveLayer)
            {
                ClearBuffer(buffer);
                return;
            }

            if (operation == FrameBatchOperation.InvertActiveLayer)
            {
                if (buffer is OverflowPixelBuffer overflow)
                    overflow.Invert();
                else
                {
                    var pixels = buffer.GetMonochromeData();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = !pixels[i];
                }
                return;
            }

            if (operation is FrameBatchOperation.ShiftLeft or FrameBatchOperation.ShiftRight or FrameBatchOperation.ShiftUp or FrameBatchOperation.ShiftDown)
            {
                int dx = operation == FrameBatchOperation.ShiftLeft ? -1 : operation == FrameBatchOperation.ShiftRight ? 1 : 0;
                int dy = operation == FrameBatchOperation.ShiftUp ? -1 : operation == FrameBatchOperation.ShiftDown ? 1 : 0;
                if (buffer is OverflowPixelBuffer overflow)
                {
                    overflow.ShiftContent(dx, dy);
                    return;
                }

                var source = buffer.GetMonochromeData();
                var shifted = new bool[source.Length];
                for (int y = 0; y < SpriteState.Height; y++)
                    for (int x = 0; x < SpriteState.Width; x++)
                        if (source[y * SpriteState.Width + x])
                        {
                            int nx = (x + dx + SpriteState.Width) % SpriteState.Width;
                            int ny = (y + dy + SpriteState.Height) % SpriteState.Height;
                            shifted[ny * SpriteState.Width + nx] = true;
                        }
                buffer.WriteMonochromeData(shifted);
                return;
            }

            var direction = operation == FrameBatchOperation.FlipActiveLayerHorizontal
                ? FlipDirection.Horizontal
                : FlipDirection.Vertical;
            if (buffer is OverflowPixelBuffer overflowBuffer)
            {
                var flipped = _drawingService.FlipPixels(overflowBuffer.GetExtendedData(), overflowBuffer.ExtendedWidth, overflowBuffer.ExtendedHeight, direction);
                int marginX = direction == FlipDirection.Horizontal
                    ? overflowBuffer.ExtendedWidth - overflowBuffer.MarginX - SpriteState.Width
                    : overflowBuffer.MarginX;
                int marginY = direction == FlipDirection.Vertical
                    ? overflowBuffer.ExtendedHeight - overflowBuffer.MarginY - SpriteState.Height
                    : overflowBuffer.MarginY;
                var replacement = new OverflowPixelBuffer(flipped, SpriteState.Width, SpriteState.Height,
                    overflowBuffer.ExtendedWidth, overflowBuffer.ExtendedHeight, marginX, marginY);
                ReplaceBufferReferences(overflowBuffer, replacement);
            }
            else
            {
                buffer.WriteMonochromeData(_drawingService.FlipPixels(buffer.GetMonochromeData(), SpriteState.Width, SpriteState.Height, direction));
            }
        }

        private void ReplaceBufferReferences(IPixelBuffer original, IPixelBuffer replacement)
        {
            foreach (var frame in SpriteState.Frames)
                for (int layer = 0; layer < frame.LayerPixels.Count; layer++)
                    if (ReferenceEquals(frame.LayerPixels[layer], original))
                        frame.LayerPixels[layer] = replacement;
        }

        private bool _isAnimationEnabled;
        public bool IsAnimationEnabled
        {
            get => _isAnimationEnabled;
            set
            {
                if (SetProperty(ref _isAnimationEnabled, value))
                {
                    SpriteState.IsAnimationEnabled = value;
                    IsDirty = true;
                    MarkCodeStale();
                    UpdateOnionSkinCache();
                    if (value)
                    {
                        RebuildFrameViewModels();
                        RedrawGridFromMemory();
                    }
                    else
                    {
                        IsPlaying = false;
                        SetActiveFrame(0);
                    }
                    // Notify frame commands that their CanExecute changed
                    AddFrameCommand?.NotifyCanExecuteChanged();
                    DuplicateFrameCommand?.NotifyCanExecuteChanged();
                    DeleteFrameCommand?.NotifyCanExecuteChanged();
                    BatchFrameOperationCommand?.NotifyCanExecuteChanged();
                    SelectAllFramesCommand?.NotifyCanExecuteChanged();
                    SelectActiveFrameOnlyCommand?.NotifyCanExecuteChanged();
                }
            }
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(DisplayPreviewBitmap));
                    if (value)
                        StartPlayback();
                    else
                        StopPlayback();
                }
            }
        }

        private bool _isOnionSkinPrevEnabled;
        public bool IsOnionSkinPrevEnabled
        {
            get => _isOnionSkinPrevEnabled;
            set { if (SetProperty(ref _isOnionSkinPrevEnabled, value)) { UpdateOnionSkinCache(); RedrawGridFromMemory(); } }
        }

        private bool _isOnionSkinNextEnabled;
        public bool IsOnionSkinNextEnabled
        {
            get => _isOnionSkinNextEnabled;
            set { if (SetProperty(ref _isOnionSkinNextEnabled, value)) { UpdateOnionSkinCache(); RedrawGridFromMemory(); } }
        }

        private int _frameRateFps = 12;
        public int FrameRateFps
        {
            get => _frameRateFps;
            set
            {
                int clamped = Math.Clamp(value, 1, 60);
                if (SetProperty(ref _frameRateFps, clamped))
                {
                    SpriteState.FrameRateFps = clamped;
                    IsDirty = true;
                    MarkCodeStale();
                    UpdatePlaybackTimerIntervalForCurrentFrame();
                }
            }
        }

        private PlaybackDirection _playbackDirection = PlaybackDirection.Forward;
        public PlaybackDirection PlaybackDirection
        {
            get => _playbackDirection;
            set
            {
                if (SetProperty(ref _playbackDirection, value))
                {
                    SpriteState.PlaybackDirection = value;
                    IsDirty = true;
                    MarkCodeStale();
                }
            }
        }

        private int _playbackFrameIndex;
        private DispatcherTimer? _playbackTimer;
        // Bug 3: Stored delegate so SelectionChanged can be properly unsubscribed in Detach().
        private EventHandler? _selectionChangedHandler;
        private uint _onionSkinPrevUint;
        private uint _onionSkinNextUint;
        private uint _onionSkinBothUint;
        private bool[][]? _playbackFrameCache;

        private bool[]? _cachedPrevFramePixels;
        private bool[]? _cachedNextFramePixels;

        private void UpdateOnionSkinCache()
        {
            if (!_isAnimationEnabled || SpriteState == null || SpriteState.Frames.Count <= 1)
            {
                _cachedPrevFramePixels = null;
                _cachedNextFramePixels = null;
                return;
            }

            if (_isOnionSkinPrevEnabled)
                _cachedPrevFramePixels = SpriteState.CompositeFramePixels((SpriteState.ActiveFrameIndex - 1 + SpriteState.Frames.Count) % SpriteState.Frames.Count);
            else
                _cachedPrevFramePixels = null;

            if (_isOnionSkinNextEnabled)
                _cachedNextFramePixels = SpriteState.CompositeFramePixels((SpriteState.ActiveFrameIndex + 1) % SpriteState.Frames.Count);
            else
                _cachedNextFramePixels = null;
        }

        /// <summary>
        /// Indices of all selected layers for multi-selection operations.
        /// </summary>
        public ObservableCollection<int> SelectedLayerIndices { get; } = [];

        private int _selectedLayerIndex;
        /// <summary>
        /// While rebuilding <see cref="Layers"/>, the ListBox two-way binds <see cref="SelectedLayerIndex"/>
        /// and may push index 0 when the collection is cleared — that would incorrectly call
        /// <see cref="SetActiveLayer"/> and reset the sprite's active layer. Ignore inbound binding
        /// updates during rebuild.
        /// </summary>
        private bool _suppressSelectedLayerBindingToState;

        public int SelectedLayerIndex
        {
            get => _selectedLayerIndex;
            set
            {
                if (_suppressSelectedLayerBindingToState)
                    return;
                if (SetProperty(ref _selectedLayerIndex, value))
                {
                    SetActiveLayer(value, shouldRedraw: true);
                    // Clear multi-selection when ListBox updates SelectedIndex
                    // This ensures single-click selection doesn't leave multi-selection state
                    SetSingleLayerSelection(value);
                }
            }
        }

        public bool IsActiveLayerLocked =>
            SpriteState.Layers.Count > 0 &&
            SpriteState.ActiveLayerIndex >= 0 &&
            SpriteState.ActiveLayerIndex < SpriteState.Layers.Count &&
            SpriteState.Layers[SpriteState.ActiveLayerIndex].IsLocked;

        public bool IsMultipleLayersSelected => SelectedLayerIndices.Count > 1;

        public bool IsActiveLayerVisible =>
            SpriteState != null &&
            SpriteState.Layers.Count > 0 &&
            SpriteState.ActiveLayerIndex >= 0 &&
            SpriteState.ActiveLayerIndex < SpriteState.Layers.Count &&
            SpriteState.Layers[SpriteState.ActiveLayerIndex].IsVisible;

        public bool CanModifyActiveLayer =>
            !IsProcessing && !IsActiveLayerLocked && IsActiveLayerVisible && !IsMultipleLayersSelected;

        public bool IsAllLayersSelected => Layers.Count > 0 && SelectedLayerIndices.Count == Layers.Count;

        private bool _isDisplayInverted;
        public bool IsDisplayInverted
        {
            get => _isDisplayInverted;
            set
            {
                if (SetProperty(ref _isDisplayInverted, value) && SpriteState != null)
                    SpriteState.IsDisplayInverted = value;
            }
        }

        private bool _showGridLines = true;
        public bool ShowGridLines
        {
            get => _showGridLines;
            set
            {
                if (SetProperty(ref _showGridLines, value))
                    SaveEditorPreferences();
            }
        }

        private bool _isBrushCursorVisible = true;
        public bool IsBrushCursorVisible
        {
            get => _isBrushCursorVisible;
            set
            {
                if (SetProperty(ref _isBrushCursorVisible, value))
                    SaveEditorPreferences();
            }
        }

        // ── Animation playback helpers ─────────────────────────────────────

        private void UpdatePlaybackTimerInterval()
        {
            if (_playbackTimer == null) return;
            int intervalMs = (int)Math.Round(1000.0 / Math.Clamp(FrameRateFps, 1, 60), MidpointRounding.AwayFromZero);
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        public void RebuildPlaybackCache()
        {
            if (SpriteState == null) return;
            int frameCount = SpriteState.Frames.Count;
            _playbackFrameCache = new bool[frameCount][];
            for (int i = 0; i < frameCount; i++)
                _playbackFrameCache[i] = SpriteState.CompositeFramePixels(i);
        }

        private bool _isPlaybackReverseDirection;

        private void StartPlayback()
        {
            if (_playbackTimer == null) return;
            if (_selectionService.IsFloating)
                _selectionInput.CommitIfActive();
            _selectionInput.ResetControllerState();
            _toolInput.CancelInProgressDrawing();

            _playbackFrameIndex = SpriteState.ActiveFrameIndex;
            _isPlaybackReverseDirection = PlaybackDirection == PlaybackDirection.Reverse;

            RebuildPlaybackCache();

            UpdatePlaybackTimerIntervalForCurrentFrame();
            for (int i = 0; i < Frames.Count; i++)
                Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
            _playbackTimer.Start();
        }

        private void UpdatePlaybackTimerIntervalForCurrentFrame()
        {
            if (_playbackTimer == null || SpriteState == null || SpriteState.Frames.Count == 0) return;
            _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, Math.Max(0, SpriteState.Frames.Count - 1));
            int baseIntervalMs = (int)Math.Round(1000.0 / Math.Clamp(FrameRateFps, 1, 60), MidpointRounding.AwayFromZero);
            int multiplier = Math.Max(1, SpriteState.Frames[_playbackFrameIndex].DelayMultiplier);
            _playbackTimer.Interval = TimeSpan.FromMilliseconds(baseIntervalMs * multiplier);
        }

        private void StopPlayback()
        {
            _playbackTimer?.Stop();
            _playbackFrameCache = null;
            for (int i = 0; i < Frames.Count; i++)
                Frames[i].IsPlayingBack = false;

            _playbackFrameIndex = SpriteState.ActiveFrameIndex;
            UpdatePreviewSimulation(force: true);
            if (IsHardwarePreviewEnabled)
            {
                TriggerHardwarePreviewUpdate();
            }
        }

        private void PlaybackTimer_Tick(object? sender, EventArgs e)
        {
            if (SpriteState == null || SpriteState.Frames.Count <= 1) return;

            _playbackFrameIndex = Math.Clamp(_playbackFrameIndex, 0, SpriteState.Frames.Count - 1);

            if (PlaybackDirection == PlaybackDirection.PingPong)
            {
                if (_isPlaybackReverseDirection)
                {
                    _playbackFrameIndex--;
                    if (_playbackFrameIndex < 0)
                    {
                        _playbackFrameIndex = Math.Min(1, SpriteState.Frames.Count - 1);
                        _isPlaybackReverseDirection = false;
                    }
                }
                else
                {
                    _playbackFrameIndex++;
                    if (_playbackFrameIndex >= SpriteState.Frames.Count)
                    {
                        _playbackFrameIndex = Math.Max(0, SpriteState.Frames.Count - 2);
                        _isPlaybackReverseDirection = true;
                    }
                }
            }
            else
            {
                if (PlaybackDirection == PlaybackDirection.Reverse)
                {
                    _playbackFrameIndex--;
                    if (_playbackFrameIndex < 0)
                    {
                        _playbackFrameIndex = SpriteState.Frames.Count - 1;
                    }
                }
                else // Forward
                {
                    _playbackFrameIndex = (_playbackFrameIndex + 1) % SpriteState.Frames.Count;
                }
            }

            UpdatePlaybackTimerIntervalForCurrentFrame();
            for (int i = 0; i < Frames.Count; i++)
                Frames[i].IsPlayingBack = (i == _playbackFrameIndex);
            UpdatePlaybackPreview();
        }

        private void UpdatePlaybackPreview()
        {
            if (SpriteState == null) return;
            EnsurePreviewSimBitmap();
            if (_previewSimBitmap == null) return;
            // Bug 7: Guard against divide-by-zero if the bitmap was allocated before
            // SpriteState was ready (PixelWidth/Height guaranteed ≥ 1 by EnsurePreviewSimBitmap,
            // but an early call path could still produce 0 before that runs).
            if (_previewSimBitmap.PixelWidth <= 0 || _previewSimBitmap.PixelHeight <= 0) return;

            // Use cached frame data if available, otherwise composite on the fly
            bool[] frameData = _playbackFrameCache != null && _playbackFrameIndex >= 0 && _playbackFrameIndex < _playbackFrameCache.Length
                ? _playbackFrameCache[_playbackFrameIndex]
                : SpriteState.CompositeFramePixels(_playbackFrameIndex);

            if (IsHardwarePreviewEnabled && !IsFrameTooLargeForHardware())
            {
                _hardwarePreview.SendFrame(frameData, SpriteState.Width, SpriteState.Height);
            }

            int w = SpriteState.Width;
            int h = SpriteState.Height;

            // Simple preview rendering without selection overlay
            for (int y = 0; y < _previewSimBitmap.PixelHeight; y++)
            {
                for (int x = 0; x < _previewSimBitmap.PixelWidth; x++)
                {
                    int srcX = (int)((x / (double)_previewSimBitmap.PixelWidth) * w);
                    int srcY = (int)((y / (double)_previewSimBitmap.PixelHeight) * h);
                    srcX = Math.Clamp(srcX, 0, w - 1);
                    srcY = Math.Clamp(srcY, 0, h - 1);
                    bool isOn = frameData[srcY * w + srcX];

                    // If this is the active frame being edited, overlay the floating selection
                    if (_playbackFrameIndex == SpriteState.ActiveFrameIndex && _selectionService.IsFloating && _selectionService.FloatingPixels != null)
                    {
                        var eff = _selectionService.GetEffectiveFloating();
                        int fx = srcX - eff.x;
                        int fy = srcY - eff.y;
                        if (fx >= 0 && fx < eff.w && fy >= 0 && fy < eff.h)
                        {
                            if (eff.mask == null || eff.mask[fx, fy])
                            {
                                bool floatingPixel = eff.pixels[fx, fy];
                                if (_floatingPasteMode == FloatingPasteMode.Transparent)
                                {
                                    if (floatingPixel) isOn = true;
                                }
                                else
                                {
                                    isOn = floatingPixel;
                                }
                            }
                        }
                    }
                    _previewSimBuffer[y * _previewSimBitmap.PixelWidth + x] = isOn ? _previewOnUint : _previewOffUint;
                }
            }

            var rect = new Int32Rect(0, 0, _previewSimBitmap.PixelWidth, _previewSimBitmap.PixelHeight);
            _previewSimBitmap.WritePixels(rect, _previewSimBuffer, _previewSimBitmap.PixelWidth * 4, 0);
            OnPropertyChanged(nameof(DisplayPreviewBitmap));
        }

        /// <summary>
        /// The frame index currently being shown in the preview — during animation playback
        /// this is the playback frame, otherwise the active editing frame.
        /// </summary>
        internal int CurrentDisplayFrameIndex => _isPlaying ? _playbackFrameIndex : SpriteState.ActiveFrameIndex;

        public void InvalidatePlaybackFrame(int frameIndex)
        {
            if (_playbackFrameCache != null && frameIndex >= 0 && frameIndex < _playbackFrameCache.Length && SpriteState != null && frameIndex < SpriteState.Frames.Count)
            {
                _playbackFrameCache[frameIndex] = SpriteState.CompositeFramePixels(frameIndex);
            }
        }

        public void NextFrame()
        {
            if (SpriteState == null || SpriteState.Frames.Count <= 1) return;
            if (IsPlaying) IsPlaying = false;
            int next = (SpriteState.ActiveFrameIndex + 1) % SpriteState.Frames.Count;
            SetActiveFrame(next);
        }

        public void PreviousFrame()
        {
            if (SpriteState == null || SpriteState.Frames.Count <= 1) return;
            if (IsPlaying) IsPlaying = false;
            int prev = (SpriteState.ActiveFrameIndex - 1 + SpriteState.Frames.Count) % SpriteState.Frames.Count;
            SetActiveFrame(prev);
        }
    }
}
