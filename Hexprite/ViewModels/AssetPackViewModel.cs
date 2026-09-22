using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels.Flipper;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Document tab ViewModel for Flipper Zero dolphin animation asset packs.
    /// Wraps <see cref="FlipperScheduleMatrixViewModel"/> via composition to provide full
    /// workspace document lifecycle (Save, SaveAs, dirty tracking, tab switching).
    /// </summary>
    public class AssetPackViewModel : ObservableObject, IDocumentTab, IDisposable
    {
        private static readonly JsonSerializerOptions IndentedJsonOptions = new() { WriteIndented = true };

        private readonly FlipperScheduleMatrixViewModel _matrixVm;
        private readonly IAutosaveService? _autosaveService;
        private string? _filePath;
        private bool _isDirty;
        private bool _isActive;
        private bool _isSuppressingDirty;

        public FlipperScheduleMatrixViewModel MatrixViewModel => _matrixVm;

        public DocumentMode Mode => DocumentMode.AssetPack;

        public string? FilePath
        {
            get => _filePath;
            set
            {
                if (SetProperty(ref _filePath, value))
                {
                    _matrixVm.PackFilePath = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (SetProperty(ref _isDirty, value))
                {
                    OnPropertyChanged(nameof(Title));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    if (!value)
                    {
                        _autosaveService?.ClearCurrentAutosave();
                    }
                }
            }
        }

        public bool HasUnsavedChanges => _isDirty;

        public bool IsLinked => false;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (SetProperty(ref _isActive, value))
                {
                    if (value)
                    {
                        _matrixVm.SyncFromWorkspace(silent: true);
                    }
                }
            }
        }

        public string Title => _isDirty
            ? $"*{_matrixVm.PackName} (Pack)"
            : $"{_matrixVm.PackName} (Pack)";

        public IRelayCommand UndoCommand => _matrixVm.UndoCommand;
        public IRelayCommand RedoCommand => _matrixVm.RedoCommand;

        public AssetPackViewModel(
            AssetPackDocument? document = null,
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            IFlipperWindowManager? windowManager = null,
            IDialogService? dialogService = null,
            IUserFeedbackService? feedbackService = null,
            IClipboardService? clipboardService = null,
            IWorkspaceTabService? tabService = null,
            IFlipperExportService? exportService = null,
            IFlipperImportService? importService = null,
            IAutosaveService? autosaveService = null)
        {
            var doc = document ?? (pack != null && pack.Count > 0
                ? new AssetPackDocument
                {
                    PackName = "Flipper Asset Pack",
                    IsStockMode = pack.All(p => p.ManifestEntry.MaxLevel <= 3),
                    Entries = [.. pack.Select(p => p.ManifestEntry.Clone())],
                }
                : AssetPackDocument.CreateNew());

            var effectivePack = pack ?? [.. doc.Entries.Select(e => (e.Name,
                Sprite: (doc.Animations != null && doc.Animations.TryGetValue(e.Name, out var sp) && sp != null)
                    ? sp
                    : new SpriteState(128, 64) { ColorMode = ColorMode.Monochrome, FrameRateFps = 10 },
                ManifestEntry: e
            ))];

            _matrixVm = new FlipperScheduleMatrixViewModel(
                pack: effectivePack.Count > 0 ? effectivePack : null,
                packName: doc.PackName,
                windowManager: windowManager,
                dialogService: dialogService,
                feedbackService: feedbackService,
                clipboardService: clipboardService,
                tabService: tabService,
                exportService: exportService,
                importService: importService);

            if (doc.Animations != null && doc.Animations.Count > 0)
            {
                foreach (var (name, sprite) in doc.Animations)
                {
                    if (sprite != null && !string.IsNullOrWhiteSpace(name))
                    {
                        sprite.NormalizeLayerState();
                        _matrixVm.SetAnimationSprite(name, sprite);
                    }
                }
            }

            if (doc.AnimationFilePaths != null && doc.AnimationFilePaths.Count > 0)
            {
                foreach (var (name, path) in doc.AnimationFilePaths)
                {
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                    {
                        _matrixVm.SetAnimationFilePath(name, path);
                    }
                }
            }

            if (doc.IsStockMode != _matrixVm.IsStockMode)
            {
                _matrixVm.IsStockMode = doc.IsStockMode;
            }

            if (doc.SimulatorSettings != null)
            {
                _matrixVm.ApplySimulatorSettings(doc.SimulatorSettings);
            }

            _matrixVm.DocumentModified += OnDocumentModified;
            _matrixVm.PropertyChanged += OnMatrixVmPropertyChanged;

            _autosaveService = autosaveService;
            if (_autosaveService != null)
            {
                string documentId = Guid.NewGuid().ToString();
                _autosaveService.StartAssetPackAutosaveLoop(
                    documentId,
                    () => ToDocument(),
                    () => IsDirty,
                    () => new AutosaveMetadata { Title = Title, FilePath = FilePath, IsActiveTab = IsActive });
            }
        }

        public AssetPackViewModel(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> pack,
            string packName,
            IFlipperWindowManager? windowManager = null,
            IDialogService? dialogService = null,
            IUserFeedbackService? feedbackService = null,
            IClipboardService? clipboardService = null,
            IWorkspaceTabService? tabService = null,
            IFlipperExportService? exportService = null,
            IFlipperImportService? importService = null,
            IAutosaveService? autosaveService = null)
            : this(
                document: new AssetPackDocument
                {
                    PackName = packName,
                    IsStockMode = pack.All(p => p.ManifestEntry.MaxLevel <= 3),
                    Entries = [.. pack.Select(p => p.ManifestEntry.Clone())],
                },
                pack: pack,
                windowManager: windowManager,
                dialogService: dialogService,
                feedbackService: feedbackService,
                clipboardService: clipboardService,
                tabService: tabService,
                exportService: exportService,
                importService: importService,
                autosaveService: autosaveService)
        {
        }

        public AssetPackViewModel(FlipperScheduleMatrixViewModel matrixVm, IAutosaveService? autosaveService = null)
        {
            _matrixVm = matrixVm ?? throw new ArgumentNullException(nameof(matrixVm));
            _matrixVm.DocumentModified += OnDocumentModified;
            _matrixVm.PropertyChanged += OnMatrixVmPropertyChanged;
            _autosaveService = autosaveService;
            if (_autosaveService != null)
            {
                string documentId = Guid.NewGuid().ToString();
                _autosaveService.StartAssetPackAutosaveLoop(
                    documentId,
                    () => ToDocument(),
                    () => IsDirty,
                    () => new AutosaveMetadata { Title = Title, FilePath = FilePath, IsActiveTab = IsActive });
            }
        }

        private void OnDocumentModified(object? sender, EventArgs e)
        {
            if (!_isSuppressingDirty)
            {
                IsDirty = true;
            }
        }

        private void OnMatrixVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FlipperScheduleMatrixViewModel.PackName))
            {
                OnPropertyChanged(nameof(Title));
                if (!_isSuppressingDirty)
                {
                    IsDirty = true;
                }
            }
            else if (e.PropertyName == nameof(FlipperScheduleMatrixViewModel.IsStockMode))
            {
                if (!_isSuppressingDirty)
                {
                    IsDirty = true;
                }
            }
        }

        public void Save()
        {
            if (FilePath != null)
            {
                SaveToPath(FilePath);
            }
        }

        public void SaveAs(string path)
        {
            path = SafeFileIo.EnsureExtension(path, ".hexpack");
            SaveToPath(path);
        }

        public void SaveToPath(string path)
        {
            FilePath = path;
            var doc = ToDocument();
            string json = JsonSerializer.Serialize(doc, IndentedJsonOptions);
            SafeFileIo.WriteAllTextAtomic(path, json, maxRetries: 5, createBackup: true);
            MarkAsClean();
            UserPreferencesService.AddRecentFile(path);
        }

        public AssetPackDocument ToDocument()
        {
            _isSuppressingDirty = true;
            try
            {
                _matrixVm.SyncFromWorkspace(silent: true);

                var doc = new AssetPackDocument
                {
                    SchemaVersion = 2,
                    PackName = _matrixVm.PackName,
                    IsStockMode = _matrixVm.IsStockMode,
                    Entries = [.. _matrixVm.Entries.Select(e => e.Entry.Clone())],
                    SimulatorSettings = _matrixVm.GetCurrentSimulatorSettings(),
                };

                var entryNames = new HashSet<string>(
                    _matrixVm.Entries.Select(e => e.Name.Trim().TrimStart('*').Trim()),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var (name, sprite) in _matrixVm.AnimationSprites)
                {
                    if (sprite != null && !string.IsNullOrWhiteSpace(name) && entryNames.Contains(name))
                    {
                        var clone = sprite.Clone();
                        clone.NormalizeLayerState();
                        doc.Animations[name] = clone;
                    }
                }

                string packDir = !string.IsNullOrEmpty(_filePath) ? System.IO.Path.GetDirectoryName(_filePath) ?? string.Empty : string.Empty;

                foreach (var (name, filePath) in _matrixVm.AnimationFilePaths)
                {
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(filePath) && entryNames.Contains(name))
                    {
                        if (!string.IsNullOrEmpty(packDir) && filePath.StartsWith(packDir, StringComparison.OrdinalIgnoreCase))
                        {
                            string rel = System.IO.Path.GetRelativePath(packDir, filePath);
                            doc.AnimationFilePaths[name] = rel;
                        }
                        else
                        {
                            doc.AnimationFilePaths[name] = filePath;
                        }
                    }
                }

                return doc;
            }
            finally
            {
                _isSuppressingDirty = false;
            }
        }

        public void ExportFolder(string? path = null) => _matrixVm.ExportAssetPackFolder(path);

        public void ExportZip(string? path = null) => _matrixVm.ExportAssetPackZip(path);

        public void MarkAsClean()
        {
            IsDirty = false;
            _autosaveService?.ClearCurrentAutosave();
        }

        public void LoadDocument(AssetPackDocument doc)
        {
            ArgumentNullException.ThrowIfNull(doc);
            doc.EnsureCaseInsensitiveAnimations();

            _isSuppressingDirty = true;
            try
            {
                _matrixVm.PackName = doc.PackName;
                _matrixVm.IsStockMode = doc.IsStockMode;
                _matrixVm.Entries.Clear();
                foreach (var entry in doc.Entries)
                {
                    _matrixVm.Entries.Add(new FlipperScheduleEntryViewModel(entry.Clone()));
                }

                if (doc.Animations != null && doc.Animations.Count > 0)
                {
                    foreach (var (name, sprite) in doc.Animations)
                    {
                        if (sprite != null && !string.IsNullOrWhiteSpace(name))
                        {
                            var clone = sprite.Clone();
                            clone.NormalizeLayerState();
                            _matrixVm.SetAnimationSprite(name, clone);
                        }
                    }
                }

                string packDir = !string.IsNullOrEmpty(_filePath) ? System.IO.Path.GetDirectoryName(_filePath) ?? string.Empty : string.Empty;

                if (doc.AnimationFilePaths != null && doc.AnimationFilePaths.Count > 0)
                {
                    foreach (var (name, path) in doc.AnimationFilePaths)
                    {
                        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(path))
                        {
                            string resolved = path;
                            if (!string.IsNullOrEmpty(packDir) && !System.IO.Path.IsPathRooted(path))
                            {
                                resolved = System.IO.Path.GetFullPath(System.IO.Path.Combine(packDir, path));
                            }
                            _matrixVm.SetAnimationFilePath(name, resolved);
                        }
                    }
                }

                if (_matrixVm.Entries.Count > 0)
                {
                    _matrixVm.SelectedEntry = _matrixVm.Entries[0];
                }
                _matrixVm.RecalculateMatrix();
                _matrixVm.RefreshCurrentPreviewSprite();
                if (doc.SimulatorSettings != null)
                {
                    _matrixVm.ApplySimulatorSettings(doc.SimulatorSettings);
                }
                MarkAsClean();
            }
            finally
            {
                _isSuppressingDirty = false;
            }
        }

        private bool _isDisposed;

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            _autosaveService?.StopAutosaveLoop();
            _autosaveService?.ClearCurrentAutosave();
            (_autosaveService as IDisposable)?.Dispose();

            _matrixVm.DocumentModified -= OnDocumentModified;
            _matrixVm.PropertyChanged -= OnMatrixVmPropertyChanged;
            _matrixVm.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
