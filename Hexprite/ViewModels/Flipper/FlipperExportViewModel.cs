using System;
using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hexprite.Core;
using Hexprite.Services;

namespace Hexprite.ViewModels.Flipper
{
    public partial class FlipperExportViewModel : ObservableObject, IDisposable
    {
        private readonly IFlipperExportService _exportService;
        private readonly SpriteState _spriteState;
        private readonly IFlipperWindowManager? _windowManager;
        private readonly IDialogService? _dialogService;
        private readonly int _totalFrames;
        private readonly bool _hasCycle;
        private bool _isUpdating;

        [ObservableProperty]
        private string _animationName = "MyAnimation";

        [ObservableProperty]
        private int _frameRate = 5;

        [ObservableProperty]
        private int _passiveFrames;

        [ObservableProperty]
        private int _activeFrames;

        [ObservableProperty]
        private bool _preserveCycle = true;

        [ObservableProperty]
        private bool _isPassiveFramesEditable = true;

        [ObservableProperty]
        private int _minLevel = 1;

        [ObservableProperty]
        private int _maxLevel = 3;

        [ObservableProperty]
        private int _minButthurt;

        [ObservableProperty]
        private int _maxButthurt = 14;

        [ObservableProperty]
        private int _weight = 1;

        [ObservableProperty]
        private int _activeCycles = 1;

        [ObservableProperty]
        private int _duration = 3600;

        [ObservableProperty]
        private int _activeCooldown;

        [ObservableProperty]
        private string _speechBubbleText = string.Empty;

        [ObservableProperty]
        private int _speechBubbleX = 14;

        [ObservableProperty]
        private int _speechBubbleY = 4;

        [ObservableProperty]
        private int _selectedTailPositionIndex;

        [ObservableProperty]
        private int _selectedTargetModeIndex;

        [ObservableProperty]
        private string _validationStatus = string.Empty;

        [ObservableProperty]
        private bool _hasValidationError;

        [ObservableProperty]
        private string _manifestSnippet = string.Empty;

        public int TotalFrames => _totalFrames;

        public bool HasCycle => _hasCycle;

        public Action<bool?>? RequestClose { get; set; }

        public FlipperExportViewModel(
            SpriteState spriteState,
            IFlipperExportService? exportService = null,
            IFlipperWindowManager? windowManager = null,
            IDialogService? dialogService = null)
        {
            ArgumentNullException.ThrowIfNull(spriteState);

            _spriteState = spriteState;
            _exportService = exportService ?? new FlipperExportService();
            _windowManager = windowManager;
            _dialogService = dialogService;

            _totalFrames = spriteState.Frames.Count;
            _hasCycle = spriteState.FlipperCycle != null
                && Array.TrueForAll(spriteState.FlipperCycle.FramesOrder, i => i < _totalFrames);

            _isUpdating = true;
            try
            {
                if (_hasCycle && spriteState.FlipperCycle != null)
                {
                    _passiveFrames = spriteState.FlipperCycle.PassiveFrameCount;
                    _activeCycles = spriteState.FlipperCycle.ActiveCycles;
                    _duration = spriteState.FlipperCycle.Duration;
                    _activeCooldown = spriteState.FlipperCycle.ActiveCooldown;

                    var bubble = spriteState.FlipperCycle.SpeechBubble;
                    if (bubble != null)
                    {
                        _speechBubbleText = bubble.Text;
                        _speechBubbleX = bubble.X;
                        _speechBubbleY = bubble.Y;
                        _selectedTailPositionIndex = bubble.Tail switch
                        {
                            SpeechBubbleTailPosition.BottomLeft => 0,
                            SpeechBubbleTailPosition.BottomRight => 1,
                            SpeechBubbleTailPosition.TopLeft => 2,
                            SpeechBubbleTailPosition.TopRight => 3,
                            _ => 4
                        };
                    }
                }
                else
                {
                    _passiveFrames = 0;
                }

                _isPassiveFramesEditable = !_hasCycle || !PreserveCycle;
            }
            finally
            {
                _isUpdating = false;
            }

            UpdateOutputs();
        }

        public void UpdateOutputs()
        {
            if (_isUpdating) return;

            ValidationStatus = string.Empty;

            if (PassiveFrames < 0 || PassiveFrames > _totalFrames)
            {
                ValidationStatus = $"Passive frames must be between 0 and {_totalFrames}.";
                HasValidationError = true;
                return;
            }

            ActiveFrames = Math.Max(0, _totalFrames - PassiveFrames);

            string animName = FlipperExportService.SanitizeAnimationName(AnimationName);
            if (string.IsNullOrEmpty(animName))
            {
                animName = "AnimationName";
            }

            var entry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = Math.Clamp(MinLevel, 1, 30),
                MaxLevel = Math.Clamp(MaxLevel, 1, 30),
                MinButthurt = Math.Clamp(MinButthurt, 0, 14),
                MaxButthurt = Math.Clamp(MaxButthurt, 0, 14),
                Weight = Math.Max(1, Weight)
            };

            var diagnostics = entry.Validate(isMomentum: true);
            var errors = diagnostics.FindAll(d => d.Severity == FlipperValidationSeverity.Error);
            if (errors.Count > 0)
            {
                ValidationStatus = errors[0].Message;
            }

            HasValidationError = !string.IsNullOrEmpty(ValidationStatus);
            ManifestSnippet = entry.ToManifestBlock();
        }

        public bool TryValidateInputs(string targetFolder, out FlipperExportSettings settings, out FlipperManifestEntry entry, out SpriteState exportSprite)
        {
            settings = null!;
            entry = null!;
            exportSprite = null!;

            if (PassiveFrames < 0 || PassiveFrames > _totalFrames)
            {
                _dialogService?.ShowMessage("Please enter a valid number of passive frames.", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
            if (FrameRate <= 0)
            {
                _dialogService?.ShowMessage("Please enter a valid frame rate.", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }

            string animName = FlipperExportService.SanitizeAnimationName(AnimationName);

            entry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = Math.Clamp(MinLevel, 1, 30),
                MaxLevel = Math.Clamp(MaxLevel, 1, 30),
                MinButthurt = Math.Clamp(MinButthurt, 0, 14),
                MaxButthurt = Math.Clamp(MaxButthurt, 0, 14),
                Weight = Math.Max(1, Weight)
            };

            var errors = entry.Validate(isMomentum: true).FindAll(d => d.Severity == FlipperValidationSeverity.Error);
            if (errors.Count > 0)
            {
                _dialogService?.ShowMessage(errors[0].Message, "Manifest Validation Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }

            var targetMode = SelectedTargetModeIndex switch
            {
                1 => FlipperExportTargetMode.MomentumAssetPack,
                2 => FlipperExportTargetMode.StockDolphin,
                _ => FlipperExportTargetMode.SingleAnimation
            };

            int activeFrames = _totalFrames - PassiveFrames;
            settings = new FlipperExportSettings(
                targetFolder,
                animName,
                FrameRate,
                PassiveFrames,
                activeFrames,
                MinLevel,
                MaxLevel,
                MinButthurt,
                MaxButthurt,
                Weight,
                createManifestTxt: true,
                targetMode: targetMode
            );

            bool preserving = _hasCycle && PreserveCycle;
            exportSprite = _spriteState;
            if (_hasCycle && !preserving)
            {
                exportSprite = _spriteState.Clone();
                exportSprite.FlipperCycle = null;
            }

            if (exportSprite.FlipperCycle != null)
            {
                exportSprite.FlipperCycle.ActiveCycles = Math.Max(1, ActiveCycles);
                exportSprite.FlipperCycle.Duration = Math.Max(0, Duration);
                exportSprite.FlipperCycle.ActiveCooldown = Math.Max(0, ActiveCooldown);

                string bubbleText = SpeechBubbleText?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(bubbleText))
                {
                    var tail = SelectedTailPositionIndex switch
                    {
                        0 => SpeechBubbleTailPosition.BottomLeft,
                        1 => SpeechBubbleTailPosition.BottomRight,
                        2 => SpeechBubbleTailPosition.TopLeft,
                        3 => SpeechBubbleTailPosition.TopRight,
                        _ => SpeechBubbleTailPosition.None
                    };

                    var bubble = new FlipperSpeechBubble(1, SpeechBubbleX, SpeechBubbleY, bubbleText, tail);
                    exportSprite.FlipperCycle.SpeechBubble = bubble;
                    if (exportSprite.FlipperCycle.SpeechBubbles.Count > 0)
                    {
                        exportSprite.FlipperCycle.SpeechBubbles[0] = bubble;
                    }
                    else
                    {
                        exportSprite.FlipperCycle.SpeechBubbles = [bubble];
                    }
                    exportSprite.FlipperCycle.BubbleSlots = exportSprite.FlipperCycle.SpeechBubbles.Count;
                }
            }

            return true;
        }

        partial void OnAnimationNameChanged(string value) => UpdateOutputs();
        partial void OnFrameRateChanged(int value) => UpdateOutputs();
        partial void OnPassiveFramesChanged(int value) => UpdateOutputs();
        partial void OnMinLevelChanged(int value) => UpdateOutputs();
        partial void OnMaxLevelChanged(int value) => UpdateOutputs();
        partial void OnMinButthurtChanged(int value) => UpdateOutputs();
        partial void OnMaxButthurtChanged(int value) => UpdateOutputs();
        partial void OnWeightChanged(int value) => UpdateOutputs();
        partial void OnSelectedTargetModeIndexChanged(int value) => UpdateOutputs();
        partial void OnSelectedTailPositionIndexChanged(int value) => UpdateOutputs();
        partial void OnSpeechBubbleTextChanged(string value) => UpdateOutputs();
        partial void OnSpeechBubbleXChanged(int value) => UpdateOutputs();
        partial void OnSpeechBubbleYChanged(int value) => UpdateOutputs();

        partial void OnPreserveCycleChanged(bool value)
        {
            IsPassiveFramesEditable = !_hasCycle || !value;
            UpdateOutputs();
        }

        [RelayCommand]
        public async Task ExportAsync()
        {
            string? folder = _dialogService?.ShowOpenFolderDialog(
                SelectedTargetModeIndex == 0
                    ? "Select Destination Folder for Animation"
                    : "Select Asset Pack Root Folder (e.g., SD card /ext/asset_packs/<PackName>)");

            if (string.IsNullOrEmpty(folder))
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = SelectedTargetModeIndex == 0
                        ? "Select Destination Folder for Animation"
                        : "Select Asset Pack Root Folder (e.g., SD card /ext/asset_packs/<PackName>)"
                };
                if (dlg.ShowDialog() == true)
                {
                    folder = dlg.FolderName;
                }
            }

            if (!string.IsNullOrEmpty(folder))
            {
                if (!TryValidateInputs(folder, out var settings, out _, out var exportSprite))
                    return;

                try
                {
                    await Task.Run(() => _exportService.ExportAnimation(exportSprite, settings));
                    string targetDesc = settings.TargetMode switch
                    {
                        FlipperExportTargetMode.MomentumAssetPack => $"Momentum Asset Pack '{settings.AnimationName}'",
                        FlipperExportTargetMode.StockDolphin => $"Stock Dolphin Pack '{settings.AnimationName}'",
                        _ => $"Animation '{settings.AnimationName}'"
                    };
                    _dialogService?.ShowMessage($"Successfully exported {targetDesc} to:\n{folder}", "Export Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    RequestClose?.Invoke(true);
                }
                catch (Exception ex)
                {
                    _dialogService?.ShowMessage($"Failed to export animation:\n{ex.Message}", "Export Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            }
        }

        public void Export()
        {
            ExportAsync().GetAwaiter().GetResult();
        }

        [RelayCommand]
        public void DeployUsb()
        {
            if (!TryValidateInputs("", out var settings, out _, out var exportSprite))
                return;

            try
            {
                // In-memory deployment file generation (zero disk I/O)
                var fileEntries = _exportService.GenerateDeploymentFiles(exportSprite, settings);

                if (_windowManager != null)
                {
                    _windowManager.ShowDeploy(exportSprite);
                }
                else
                {
                    var deployer = new FlipperUsbDeployer();
                    var deployWin = new Views.FlipperDeployWindow(deployer, fileEntries, settings.AnimationName);
                    deployWin.ShowDialog();
                }
            }
            catch (Exception ex)
            {
                _dialogService?.ShowMessage($"Failed to prepare deployment files:\n{ex.Message}", "Deployment Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        public void SimulateMatrix()
        {
            string animName = FlipperExportService.SanitizeAnimationName(AnimationName);
            if (string.IsNullOrEmpty(animName)) animName = "Animation";

            var entry = new FlipperManifestEntry
            {
                Name = animName,
                MinLevel = Math.Clamp(MinLevel, 1, 30),
                MaxLevel = Math.Clamp(MaxLevel, 1, 30),
                MinButthurt = Math.Clamp(MinButthurt, 0, 14),
                MaxButthurt = Math.Clamp(MaxButthurt, 0, 14),
                Weight = Math.Max(1, Weight)
            };

            if (_windowManager != null)
            {
                _windowManager.ShowSimulator(_spriteState);
            }
            else
            {
                var list = new List<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>
                {
                    (animName, _spriteState, entry)
                };
                var simWin = new Views.FlipperMatrixSimulatorWindow(list, animName);
                simWin.ShowDialog();
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            RequestClose = null;
            GC.SuppressFinalize(this);
        }
    }
}
