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
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Globalization;

namespace Hexprite.ViewModels
{
    public partial class MainViewModel
    {
        // ── Export settings ───────────────────────────────────────────────
        private ExportSettings _exportSettings = new();
        public ExportSettings ExportSettings
        {
            get => _exportSettings;
            private set => SetProperty(ref _exportSettings, value);
        }

        private DispatcherTimer? _exportDebounceTimer;
        private CancellationTokenSource? _textUpdateCts;
        private readonly Lock _textUpdateCtsLock = new();

        private void TriggerDebouncedUpdate()
        {
            if (_exportDebounceTimer == null)
            {
                _exportDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(150),
                };
                _exportDebounceTimer.Tick += (s, e) =>
                {
                    _exportDebounceTimer.Stop();
                    UpdateTextOutputs();
                };
            }
            _exportDebounceTimer.Stop();
            _exportDebounceTimer.Start();
        }

        private CancellationToken CancelAndCreateNewTextUpdateCts()
        {
            lock (_textUpdateCtsLock)
            {
                _textUpdateCts?.Cancel();
                _textUpdateCts?.Dispose();
                _textUpdateCts = new CancellationTokenSource();
                return _textUpdateCts.Token;
            }
        }

        /// <summary>
        /// Helper to set an ExportSettings property with change notification and code regeneration.
        /// </summary>
        private void SetExportSetting<T>(System.Func<T> getter, System.Action<T> setter, string propertyName, T value, string[]? additionalProperties = null)
        {
            if (!EqualityComparer<T>.Default.Equals(getter(), value))
            {
                setter(value);
                OnPropertyChanged(propertyName);
                additionalProperties?.ToList().ForEach(OnPropertyChanged);
                TriggerDebouncedUpdate();
            }
        }

        // Convenience proxy: binds directly in the sidebar without deep binding paths
        public ExportFormat ExportFormat
        {
            get => _exportSettings.Format;
            set => SetExportSetting(() => _exportSettings.Format, v => _exportSettings.Format = v, nameof(ExportFormat), value, [nameof(IsCommaSeparatorEnabled), nameof(IsCompressionVisible), nameof(IsArduinoSketchExportable)]);
        }

        public bool IsCommaSeparatorEnabled => ExportFormat == ExportFormat.RawHex || ExportFormat == ExportFormat.RawBinary;

        public bool IsArduinoSketchExportable =>
            ExportFormat is ExportFormat.AdafruitGfx or
                            ExportFormat.U8g2DrawBitmap or
                            ExportFormat.U8g2DrawXBM or
                            ExportFormat.LiquidCrystalChar;

        public string SpriteName
        {
            get => _exportSettings.SpriteName;
            set => SetExportSetting(() => _exportSettings.SpriteName, v => _exportSettings.SpriteName = v, nameof(SpriteName), value, [nameof(Title)]);
        }

        /// <summary>
        /// Sets the sprite name without triggering export code generation.
        /// Used by import paths to avoid UI stalls for large canvases.
        /// </summary>
        public void SetSpriteNameWithoutGenerating(string name)
        {
            if (_exportSettings.SpriteName != name)
            {
                _exportSettings.SpriteName = name;
                OnPropertyChanged(nameof(SpriteName));
                OnPropertyChanged(nameof(Title));
            }
        }

        public bool IncludeUsageComment
        {
            get => _exportSettings.IncludeUsageComment;
            set => SetExportSetting(() => _exportSettings.IncludeUsageComment, v => _exportSettings.IncludeUsageComment = v, nameof(IncludeUsageComment), value);
        }

        public bool IncludeDimensionConstants
        {
            get => _exportSettings.IncludeDimensionConstants;
            set => SetExportSetting(() => _exportSettings.IncludeDimensionConstants, v => _exportSettings.IncludeDimensionConstants = v, nameof(IncludeDimensionConstants), value);
        }

        public AnimationExportLayout AnimationLayout
        {
            get => _exportSettings.AnimationLayout;
            set => SetExportSetting(() => _exportSettings.AnimationLayout, v => _exportSettings.AnimationLayout = v, nameof(AnimationLayout), value);
        }

        public bool UseCommaSeparator
        {
            get => _exportSettings.UseCommaSeparator;
            set => SetExportSetting(() => _exportSettings.UseCommaSeparator, v => _exportSettings.UseCommaSeparator = v, nameof(UseCommaSeparator), value);
        }

        public int BytesPerLine
        {
            get => _exportSettings.BytesPerLine;
            set => SetExportSetting(() => _exportSettings.BytesPerLine, v => _exportSettings.BytesPerLine = v, nameof(BytesPerLine), value);
        }

        public bool UppercaseHex
        {
            get => _exportSettings.UppercaseHex;
            set => SetExportSetting(() => _exportSettings.UppercaseHex, v => _exportSettings.UppercaseHex = v, nameof(UppercaseHex), value);
        }

        public bool IncludeRowComments
        {
            get => _exportSettings.IncludeRowComments;
            set => SetExportSetting(() => _exportSettings.IncludeRowComments, v => _exportSettings.IncludeRowComments = v, nameof(IncludeRowComments), value);
        }

        public bool IncludeArraySize
        {
            get => _exportSettings.IncludeArraySize;
            set => SetExportSetting(() => _exportSettings.IncludeArraySize, v => _exportSettings.IncludeArraySize = v, nameof(IncludeArraySize), value);
        }

        public bool GenerateFullSketch
        {
            get => _exportSettings.GenerateFullSketch;
            set => SetExportSetting(() => _exportSettings.GenerateFullSketch, v => _exportSettings.GenerateFullSketch = v, nameof(GenerateFullSketch), value);
        }

        // Convenience proxy for compression mode
        public CompressionMode Compression
        {
            get => _exportSettings.Compression;
            set => SetExportSetting(() => _exportSettings.Compression, v => _exportSettings.Compression = v, nameof(Compression), value);
        }

        /// <summary>Compression is only available for standard C/C++ formats (not Raw, MicroPython, or Flipper formats).</summary>
        public bool IsCompressionVisible =>
            ExportFormat != ExportFormat.RawHex &&
            ExportFormat != ExportFormat.RawBinary &&
            ExportFormat != ExportFormat.MicroPython &&
            ExportFormat != ExportFormat.FlipperCompressedBitmap &&
            ExportFormat != ExportFormat.FlipperXbm &&
            ExportFormat != ExportFormat.FlipperCanvasIcon;

        // ── Export output ────────────────────────────────────────────────────
        private Task? _activeTextUpdateTask;
        private readonly Lock _textUpdateTaskLock = new();
        private volatile bool _isUpdatePending;

        private string _exportedCode = string.Empty;
        public string ExportedCode
        {
            get => _exportedCode;
            private set => SetProperty(ref _exportedCode, value);
        }

        private string _exportStats = string.Empty;
        public string ExportStats
        {
            get => _exportStats;
            private set => SetProperty(ref _exportStats, value);
        }

        /// <summary>
        /// Indicates that the canvas has changed since the last code generation.
        /// Bound to a stale indicator in the sidebar.
        /// </summary>
        private bool _isCodeStale;
        public bool IsCodeStale
        {
            get => _isCodeStale;
            private set => SetProperty(ref _isCodeStale, value);
        }

        // ── Export output ─────────────────────────────────────────────────

        /// <summary>
        /// Marks the code output as stale after a canvas-modifying operation.
        /// Does NOT regenerate — the user must click "Generate Code" to update.
        /// </summary>
        public void MarkCodeStale() 
        {
            IsCodeStale = true;
            UpdateDirtyState();
            UpdateActiveFrameThumbnail();
            if (IsPlaying && SpriteState != null)
            {
                InvalidatePlaybackFrame(SpriteState.ActiveFrameIndex);
            }
        }

        public void UpdateTextOutputs()
        {
            _exportDebounceTimer?.Stop();
            _ = UpdateTextOutputsAsync();
        }

        public System.Threading.Tasks.Task UpdateTextOutputsAsync()
        {
            _isUpdatePending = true;

            lock (_textUpdateTaskLock)
            {
                if (_activeTextUpdateTask == null || _activeTextUpdateTask.IsCompleted)
                {
                    _activeTextUpdateTask = RunTextUpdateLoopAsync();
                }
                return _activeTextUpdateTask;
            }
        }

        private async System.Threading.Tasks.Task RunTextUpdateLoopAsync()
        {
            while (_isUpdatePending)
            {
                _isUpdatePending = false;
                    var token = CancelAndCreateNewTextUpdateCts();

                    // Snapshot settings to avoid reading them from a different thread
                    var settingsSnapshot = new ExportSettings
                    {
                        Format = _exportSettings.Format,
                        SpriteName = _exportSettings.SpriteName,
                        IncludeUsageComment = _exportSettings.IncludeUsageComment,
                        IncludeDimensionConstants = _exportSettings.IncludeDimensionConstants,
                        UseCommaSeparator = _exportSettings.UseCommaSeparator,
                        BytesPerLine = _exportSettings.BytesPerLine,
                        UppercaseHex = _exportSettings.UppercaseHex,
                        IncludeRowComments = _exportSettings.IncludeRowComments,
                        IncludeArraySize = _exportSettings.IncludeArraySize,
                        GenerateFullSketch = _exportSettings.GenerateFullSketch,
                        ExportAsAnimation = _exportSettings.ExportAsAnimation,
                        AnimationLayout = _exportSettings.AnimationLayout,
                        FrameRateFps = SpriteState.FrameRateFps,
                        Compression = _exportSettings.Compression,
                    };

                    try
                    {
                        int w = SpriteState.Width;
                        int h = SpriteState.Height;

                        var frames = GetExportFrames(settingsSnapshot.ExportAsAnimation);
                        var delays = settingsSnapshot.ExportAsAnimation ? SpriteState.Frames.Select(f => f.DelayMultiplier).ToList() : null;

                        token.ThrowIfCancellationRequested();

                        string code = await _codeGen.GenerateCodeAsync(
                            frames, w, h, settingsSnapshot, isFloating: false,
                            floatingPixels: null,
                            0, 0, 0, 0,
                            _floatingPasteMode, delays, token);

                        token.ThrowIfCancellationRequested();

                        int byteCount = _codeGen.CalculateByteCount(frames, w, h, settingsSnapshot);

                        string memDetails = "";
                        if (settingsSnapshot.Compression != CompressionMode.None)
                        {
                            var uncompressedSettings = settingsSnapshot.Clone();
                            uncompressedSettings.Compression = CompressionMode.None;
                            int uncompressedBytes = _codeGen.CalculateByteCount(frames, w, h, uncompressedSettings);
                            if (byteCount < uncompressedBytes && uncompressedBytes > 0)
                            {
                                double savedPct = (1.0 - ((double)byteCount / uncompressedBytes)) * 100.0;
                                memDetails = $" (saved {savedPct:F0}% via {settingsSnapshot.Compression})";
                            }
                        }

                        string targetMem = settingsSnapshot.Format switch
                        {
                            ExportFormat.AdafruitGfx or ExportFormat.U8g2DrawBitmap or ExportFormat.U8g2DrawXBM => " · Flash (PROGMEM)",
                            ExportFormat.LiquidCrystalChar => " (CGRAM)",
                            ExportFormat.PlainCArray => " · RAM/Flash",
                            _ => "",
                        };

                        string stats = string.Create(CultureInfo.InvariantCulture, $"{byteCount} byte{(byteCount != 1 ? "s" : "")}{memDetails}{targetMem}  ·  {code.Length:N0} chars");

                        void ApplyResults()
                        {
                            if (token.IsCancellationRequested) return;
                            _exportedCode = code;
                            OnPropertyChanged(nameof(ExportedCode));
                            _exportStats = stats;
                            OnPropertyChanged(nameof(ExportStats));
                            IsCodeStale = false;
                        }

                        if (SynchronizationContext.Current == _uiContext || _uiContext == null || _uiContext.GetType() == typeof(SynchronizationContext))
                        {
                            ApplyResults();
                        }
                        else
                        {
                            _uiContext.Post(_ => ApplyResults(), state: null);
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        if (!token.IsCancellationRequested)
                        {
                            HandledErrorReporter.Error(ex, "MainViewModel.UpdateTextOutputsAsync");
                            _uiContext.Post(_ => ShowStatus("⚠ Could not generate export code"), state: null);
                        }
                    }
                }
        }

        private List<bool[]> GetExportFrames(bool exportAsAnimation)
        {
            int w = SpriteState.Width;
            int h = SpriteState.Height;
            var frames = new List<bool[]>();
            
            if (exportAsAnimation && (IsAnimationEnabled || (SpriteState.Frames != null && SpriteState.Frames.Count > 1)))
            {
                for (int i = 0; i < SpriteState.Frames.Count; i++)
                {
                    var composite = SpriteState.CompositeFramePixels(i, isExport: true);
                    if (i == SpriteState.ActiveFrameIndex && _selectionService.IsFloating && _selectionService.FloatingPixels != null)
                    {
                        var eff = _selectionService.GetEffectiveFloating();
                        for (int floatY = 0; floatY < eff.h; floatY++)
                        {
                            for (int floatX = 0; floatX < eff.w; floatX++)
                            {
                                if (eff.mask != null && !eff.mask[floatX, floatY]) continue;

                                int gx = eff.x + floatX;
                                int gy = eff.y + floatY;
                                if (gx >= 0 && gx < w && gy >= 0 && gy < h)
                                {
                                    bool isFloatingPixelOn = eff.pixels[floatX, floatY];
                                    if (_floatingPasteMode == FloatingPasteMode.Transparent)
                                    {
                                        if (isFloatingPixelOn) composite[gy * w + gx] = true;
                                    }
                                    else
                                    {
                                        composite[gy * w + gx] = isFloatingPixelOn;
                                    }
                                }
                            }
                        }
                    }
                    frames.Add(composite);
                }
            }
            else
            {
                var composite = SpriteState.CompositeVisiblePixels(isExport: true);
                if (_selectionService.IsFloating && _selectionService.FloatingPixels != null)
                {
                    var eff = _selectionService.GetEffectiveFloating();
                    for (int floatY = 0; floatY < eff.h; floatY++)
                    {
                        for (int floatX = 0; floatX < eff.w; floatX++)
                        {
                            if (eff.mask != null && !eff.mask[floatX, floatY]) continue;

                            int gx = eff.x + floatX;
                            int gy = eff.y + floatY;
                            if (gx >= 0 && gx < w && gy >= 0 && gy < h)
                            {
                                bool isFloatingPixelOn = eff.pixels[floatX, floatY];
                                if (_floatingPasteMode == FloatingPasteMode.Transparent)
                                {
                                    if (isFloatingPixelOn) composite[gy * w + gx] = true;
                                }
                                else
                                {
                                    composite[gy * w + gx] = isFloatingPixelOn;
                                }
                            }
                        }
                    }
                }
                frames.Add(composite);
            }
            return frames;
        }

        /// <summary>
        /// Applies export settings loaded from a .hexp file.
        /// Call after InitializeGrid so the VM proxy properties broadcast correctly.
        /// </summary>
        public void ApplyExportSettings(ExportSettings? saved)
        {
            if (saved == null) return;
            _exportSettings = saved;
            OnPropertyChanged(nameof(ExportFormat));
            OnPropertyChanged(nameof(SpriteName));
            OnPropertyChanged(nameof(IncludeUsageComment));
            OnPropertyChanged(nameof(IncludeDimensionConstants));
            OnPropertyChanged(nameof(UseCommaSeparator));
            OnPropertyChanged(nameof(BytesPerLine));
            OnPropertyChanged(nameof(UppercaseHex));
            OnPropertyChanged(nameof(IncludeRowComments));
            OnPropertyChanged(nameof(IncludeArraySize));
            OnPropertyChanged(nameof(GenerateFullSketch));
            OnPropertyChanged(nameof(Compression));
            OnPropertyChanged(nameof(IsCompressionVisible));
            OnPropertyChanged(nameof(IsArduinoSketchExportable));
            UpdateTextOutputs();
        }

        /// <summary>
        /// Sets the sprite name from the filename (without extension).
        /// Called after Save So As to update the default name.
        /// </summary>
        public void UpdateSpriteNameFromFile()
        {
            if (FilePath == null) return;
            string baseName = System.IO.Path.GetFileNameWithoutExtension(FilePath);
            string sanitised = Services.CodeGeneratorService.SanitiseName(baseName);
            if (SpriteName == "mySprite" || SpriteName == "sprite")
                SpriteName = sanitised;
        }

        public async System.Threading.Tasks.Task ExecuteExportArduinoSketchFolderAsync()
        {
            try
            {
                if (!IsArduinoSketchExportable)
                {
                    _dialogService.ShowMessage(
                        "Standalone Arduino sketch export is only supported for Arduino formats:\n" +
                        "• Adafruit GFX (SSD1306)\n" +
                        "• U8g2 (drawBitmap / drawXBM)\n" +
                        "• LiquidCrystal (HD44780)\n\n" +
                        "Please switch to an Arduino format to export a sketch folder.",
                        "Export Arduino Sketch",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                string? destFolder = _dialogService.ShowOpenFolderDialog("Select Destination Folder for Arduino Sketch");
                if (string.IsNullOrWhiteSpace(destFolder))
                    return;

                var files = _codeGen.GenerateStandaloneSketchFiles(SpriteState, ExportSettings);
                string sketchFolder = Path.Combine(destFolder, files.SketchName);
                Directory.CreateDirectory(sketchFolder);

                string inoPath = Path.Combine(sketchFolder, $"{files.SketchName}.ino");
                string headerPath = Path.Combine(sketchFolder, "sprites.h");

                SafeFileIo.WriteAllTextAtomic(inoPath, files.InoContent, maxRetries: 3, createBackup: false);
                SafeFileIo.WriteAllTextAtomic(headerPath, files.HeaderContent, maxRetries: 3, createBackup: false);

                ShowStatus($"✓ Arduino sketch exported to {files.SketchName}/");

                bool openIde = _dialogService.ShowConfirmation(
                    $"Arduino sketch successfully exported to:\n{sketchFolder}\n\nWould you like to open it in Arduino IDE?",
                    "Export Sketch Folder");

                if (openIde)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(inoPath) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        HandledErrorReporter.Error(ex, "MainViewModel.ExecuteExportArduinoSketchFolderAsync.Launch");
                        ShowStatus("⚠ Could not launch Arduino IDE");
                    }
                }
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "MainViewModel.ExecuteExportArduinoSketchFolderAsync");
                _dialogService.ShowMessage($"Failed to export Arduino sketch folder:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    }
}
