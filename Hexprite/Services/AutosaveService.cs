using Hexprite.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hexprite.Services
{
    public sealed class AutosaveService : IAutosaveService, IDisposable
    {
        private static string? _customAutosaveDir;
        public static void SetCustomAutosaveDirectory(string? dir) => _customAutosaveDir = dir;
        public static string AutosaveDir => _customAutosaveDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite", "Autosaves");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private readonly Lock _lock = new();
        private Timer? _timer;
        private Func<AutosaveEnvelope?>? _envelopeProvider;
        private Func<bool>? _isDirtyProvider;
        private bool _isSaving;
        private bool _isDisposed;
        private CancellationTokenSource? _saveCts;
        private string? _autosaveFile;
        private int _intervalSeconds = 30;

        public int IntervalSeconds
        {
            get => _intervalSeconds;
            set
            {
                int val = Math.Clamp(value, 5, 300);
                if (_intervalSeconds != val)
                {
                    _intervalSeconds = val;
                    if (_timer != null && !_isDisposed)
                    {
                        _timer.Change(TimeSpan.FromSeconds(_intervalSeconds), TimeSpan.FromSeconds(_intervalSeconds));
                    }
                }
            }
        }

        public void StartAutosaveLoop(string documentId, Func<SpriteState?> stateProvider, Func<bool> isDirtyProvider, Func<AutosaveMetadata?>? metadataProvider = null)
        {
            StartEnvelopeAutosaveLoop(documentId, () =>
            {
                var state = stateProvider();
                if (state == null) return null;
                var meta = metadataProvider?.Invoke();
                return new AutosaveEnvelope
                {
                    DocumentId = documentId,
                    Mode = DocumentMode.Sprite,
                    Title = meta?.Title ?? state.ExportSettings?.SpriteName ?? "Sprite",
                    FilePath = meta?.FilePath,
                    TabIndex = meta?.TabIndex ?? 0,
                    IsActiveTab = meta?.IsActiveTab ?? false,
                    ParentPackName = meta?.ParentPackName,
                    ParentPackPath = meta?.ParentPackPath,
                    PackEntryName = meta?.PackEntryName,
                    SpriteState = state.Clone(),
                };
            }, isDirtyProvider);
        }

        public void StartAssetPackAutosaveLoop(string documentId, Func<AssetPackDocument?> stateProvider, Func<bool> isDirtyProvider, Func<AutosaveMetadata?>? metadataProvider = null)
        {
            StartEnvelopeAutosaveLoop(documentId, () =>
            {
                var doc = stateProvider();
                if (doc == null) return null;
                var meta = metadataProvider?.Invoke();
                return new AutosaveEnvelope
                {
                    DocumentId = documentId,
                    Mode = DocumentMode.AssetPack,
                    Title = meta?.Title ?? doc.PackName,
                    FilePath = meta?.FilePath,
                    TabIndex = meta?.TabIndex ?? 0,
                    IsActiveTab = meta?.IsActiveTab ?? false,
                    AssetPackDocument = doc,
                };
            }, isDirtyProvider);
        }

        public void StartFontAutosaveLoop(string documentId, Func<FontDocument?> stateProvider, Func<bool> isDirtyProvider, Func<AutosaveMetadata?>? metadataProvider = null)
        {
            StartEnvelopeAutosaveLoop(documentId, () =>
            {
                var doc = stateProvider();
                if (doc == null) return null;
                var meta = metadataProvider?.Invoke();
                return new AutosaveEnvelope
                {
                    DocumentId = documentId,
                    Mode = DocumentMode.Font,
                    Title = meta?.Title ?? doc.FontName,
                    FilePath = meta?.FilePath,
                    TabIndex = meta?.TabIndex ?? 0,
                    IsActiveTab = meta?.IsActiveTab ?? false,
                    FontDocument = doc.Clone(),
                };
            }, isDirtyProvider);
        }

        public void StartEnvelopeAutosaveLoop(string documentId, Func<AutosaveEnvelope?> envelopeProvider, Func<bool> isDirtyProvider)
        {
            lock (_lock)
            {
                _envelopeProvider = envelopeProvider;
                _isDirtyProvider = isDirtyProvider;
                _autosaveFile = Path.Combine(AutosaveDir, $"recovery_{documentId}.json");
                _isDisposed = false;
                _saveCts?.Dispose();
                _saveCts = new CancellationTokenSource();

                if (!Directory.Exists(AutosaveDir))
                {
                    Directory.CreateDirectory(AutosaveDir);
                }

                _timer?.Dispose();
                _timer = new Timer(OnTimerTick, state: null, TimeSpan.FromSeconds(_intervalSeconds), TimeSpan.FromSeconds(_intervalSeconds));
            }
        }

        public void StopAutosaveLoop()
        {
            lock (_lock)
            {
                _timer?.Change(Timeout.Infinite, 0);
            }
        }

        public void TriggerImmediateAutosave()
        {
            _ = SaveImmediatelyAsync();
        }

        public async Task SaveImmediatelyAsync()
        {
            if (_isDisposed) return;

            bool isDirty = _isDirtyProvider?.Invoke() ?? false;
            if (!isDirty) return;

            var envelope = CaptureCurrentEnvelope();
            if (envelope == null) return;

            await WriteEnvelopeAtomicAsync(envelope).ConfigureAwait(false);
        }

        private AutosaveEnvelope? CaptureCurrentEnvelope()
        {
            AutosaveEnvelope? currentEnvelope = null;
            try
            {
                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        currentEnvelope = _envelopeProvider?.Invoke();
                    });
                }
                else
                {
                    currentEnvelope = _envelopeProvider?.Invoke();
                }
            }
            catch
            {
                return null;
            }

            if (currentEnvelope != null)
            {
                currentEnvelope.IsDirty = true;
                currentEnvelope.Timestamp = DateTime.UtcNow;
            }

            return currentEnvelope;
        }

        private async Task WriteEnvelopeAtomicAsync(AutosaveEnvelope currentEnvelope)
        {
            if (_isDisposed || _autosaveFile == null) return;

            CancellationToken ct;
            lock (_lock)
            {
                if (_isSaving || _isDisposed) return;
                _isSaving = true;
                _saveCts ??= new CancellationTokenSource();
                ct = _saveCts.Token;
            }

            try
            {
                string targetFile = _autosaveFile;
                string tempFile = targetFile + ".tmp";
                string json = JsonSerializer.Serialize(currentEnvelope, JsonOptions);

                await Task.Run(async () =>
                {
                    if (_isDisposed || ct.IsCancellationRequested) return;
                    await File.WriteAllTextAsync(tempFile, json, ct).ConfigureAwait(false);

                    lock (_lock)
                    {
                        if (_isDisposed || ct.IsCancellationRequested || (_isDirtyProvider != null && !_isDirtyProvider()))
                        {
                            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
                            return;
                        }
                        File.Move(tempFile, targetFile, overwrite: true);
                    }
                }, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // In-flight write was cancelled (e.g. document saved or cleared)
                try
                {
                    if (_autosaveFile != null)
                    {
                        string tempFile = _autosaveFile + ".tmp";
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                }
                catch { }
            }
            catch (InvalidOperationException)
            {
                // Collection was modified during serialization, skip this tick
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "AutosaveService.WriteEnvelopeAtomicAsync", new { _autosaveFile });
            }
            finally
            {
                lock (_lock)
                {
                    _isSaving = false;
                }
            }
        }

        private async void OnTimerTick(object? state)
        {
            if (_isDisposed) return;

            bool isDirty = _isDirtyProvider?.Invoke() ?? false;
            if (!isDirty) return;

            var currentEnvelope = CaptureCurrentEnvelope();
            if (currentEnvelope == null) return;

            await WriteEnvelopeAtomicAsync(currentEnvelope).ConfigureAwait(false);
        }

        public IEnumerable<string> GetAvailableAutosaves()
        {
            if (Directory.Exists(AutosaveDir))
            {
                // Recover orphaned .tmp files left by a crash during write.
                foreach (var tmpFile in Directory.GetFiles(AutosaveDir, "*.json.tmp"))
                {
                    string jsonPath = tmpFile[..^4]; // strip ".tmp"
                    if (!File.Exists(jsonPath) && new FileInfo(tmpFile).Length > 0)
                    {
                        try
                        {
                            File.Move(tmpFile, jsonPath);
                        }
                        catch (IOException)
                        {
                        }
                    }
                    else
                    {
                        try { File.Delete(tmpFile); } catch (IOException) { }
                    }
                }

                var files = Directory.GetFiles(AutosaveDir, "*.json");
                return files.Where(f => new FileInfo(f).Length > 0);
            }
            return [];
        }

        public AutosaveEnvelope? LoadAutosaveEnvelope(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;

                using var jsonDoc = JsonDocument.Parse(json);
                var root = jsonDoc.RootElement;

                // Detect if this is an AutosaveEnvelope (v2) or legacy raw SpriteState (v1)
                if (root.TryGetProperty("Mode", out _) || root.TryGetProperty("SchemaVersion", out _) || root.TryGetProperty("DocumentId", out _))
                {
                    var envelope = JsonSerializer.Deserialize<AutosaveEnvelope>(json, JsonOptions);
                    if (envelope != null)
                    {
                        envelope.SpriteState?.EnsureLayers();
                        envelope.FontDocument?.NormalizeGlyphs();
                        envelope.AssetPackDocument?.EnsureCaseInsensitiveAnimations();
                        return envelope;
                    }
                }

                // Fallback: Legacy raw SpriteState
                if (root.TryGetProperty("Width", out _) && (root.TryGetProperty("Pixels", out _) || root.TryGetProperty("Frames", out _)))
                {
                    var legacyState = JsonSerializer.Deserialize<SpriteState>(json, JsonOptions);
                    if (legacyState != null)
                    {
                        legacyState.EnsureLayers();
                        return new AutosaveEnvelope
                        {
                            SchemaVersion = 1,
                            Mode = DocumentMode.Sprite,
                            Title = legacyState.ExportSettings?.SpriteName ?? "Recovered Sprite",
                            SpriteState = legacyState,
                            IsDirty = true,
                        };
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "AutosaveService.LoadAutosaveEnvelope", new { path });
                QuarantineCorruptedFile(path);
                return null;
            }
        }

        private static void QuarantineCorruptedFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string quarantineDir = Path.Combine(AutosaveDir, "Corrupted");
                    Directory.CreateDirectory(quarantineDir);
                    string dest = Path.Combine(quarantineDir, Path.GetFileName(path));
                    File.Move(path, dest, overwrite: true);
                }
            }
            catch { }
        }

        public SpriteState? LoadAutosave(string path)
        {
            return LoadAutosaveEnvelope(path)?.SpriteState;
        }

        public void ClearCurrentAutosave()
        {
            lock (_lock)
            {
                try
                {
                    _saveCts?.Cancel();
                    _saveCts?.Dispose();
                    _saveCts = new CancellationTokenSource();
                }
                catch { }

                if (_autosaveFile != null)
                {
                    try
                    {
                        if (File.Exists(_autosaveFile)) File.Delete(_autosaveFile);
                        string tempFile = _autosaveFile + ".tmp";
                        if (File.Exists(tempFile)) File.Delete(tempFile);
                    }
                    catch (Exception ex)
                    {
                        HandledErrorReporter.Warning(ex, "AutosaveService.ClearCurrentAutosave", new { _autosaveFile });
                    }
                }
            }
        }

        public void ClearAllAutosaves()
        {
            if (Directory.Exists(AutosaveDir))
            {
                var files = Directory.GetFiles(AutosaveDir, "*.*")
                    .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        HandledErrorReporter.Warning(ex, "AutosaveService.ClearAllAutosaves", new { file });
                    }
                }
            }
        }

        public void ArchiveAllAutosaves(string reason = "Archive")
        {
            if (!Directory.Exists(AutosaveDir)) return;

            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string archiveDir = Path.Combine(AutosaveDir, "Archive", $"{reason}_{timestamp}");

            try
            {
                var files = Directory.GetFiles(AutosaveDir, "*.*")
                    .Where(f => f.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (files.Count > 0)
                {
                    Directory.CreateDirectory(archiveDir);
                    foreach (var file in files)
                    {
                        try
                        {
                            string dest = Path.Combine(archiveDir, Path.GetFileName(file));
                            File.Move(file, dest, overwrite: true);
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "AutosaveService.ArchiveAllAutosaves", new { archiveDir });
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _isDisposed = true;
                _saveCts?.Cancel();
                _timer?.Dispose();
                _timer = null;
                _saveCts?.Dispose();
                _saveCts = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
