using System;
using System.Collections.Generic;
using System.Windows;
using Hexprite.Core;
using Hexprite.ViewModels;
using Hexprite.ViewModels.Flipper;
using Hexprite.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hexprite.Services
{
    /// <summary>
    /// Concrete window manager implementation for presenting Flipper Zero tool windows and dialogs.
    /// Resolves ViewModels and services from DI or falls back to standard implementations.
    /// </summary>
    public class FlipperWindowManager : IFlipperWindowManager
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly IFlipperExportService? _exportService;
        private readonly IFlipperImportService? _importService;
        private readonly IFlipperUsbDeployer? _deployer;
        private readonly IFlipperScreenStreamService? _streamService;
        private readonly IWorkspaceTabService? _tabService;
        private readonly IDialogService? _dialogService;
        private readonly IClipboardService? _clipboardService;
        private readonly IUserFeedbackService? _feedbackService;

        public FlipperWindowManager(IServiceProvider? serviceProvider = null)
        {
            _serviceProvider = serviceProvider;
        }

        public FlipperWindowManager(
            IFlipperExportService? exportService,
            IFlipperImportService? importService,
            IFlipperUsbDeployer? deployer,
            IFlipperScreenStreamService? streamService,
            IWorkspaceTabService? tabService,
            IDialogService? dialogService,
            IClipboardService? clipboardService = null,
            IUserFeedbackService? feedbackService = null,
            IServiceProvider? serviceProvider = null)
        {
            _exportService = exportService;
            _importService = importService;
            _deployer = deployer;
            _streamService = streamService;
            _tabService = tabService;
            _dialogService = dialogService;
            _clipboardService = clipboardService;
            _feedbackService = feedbackService;
            _serviceProvider = serviceProvider;
        }

        private T GetService<T>(T? directInstance, Func<T> fallbackFactory) where T : class
        {
            if (directInstance != null) return directInstance;
            if (_serviceProvider != null)
            {
                var resolved = _serviceProvider.GetService<T>();
                if (resolved != null) return resolved;
            }
            return fallbackFactory();
        }

        private static void SafeSetOwner(Window window)
        {
            try
            {
                var mainWin = Application.Current?.MainWindow;
                if (mainWin != null && mainWin != window && mainWin.IsLoaded)
                {
                    window.Owner = mainWin;
                }
            }
            catch
            {
                // Ignore owner assignment issues in testing/headless environments
            }
        }

        public void ShowSimulator(SpriteState? initialSprite = null)
        {
            var exportService = GetService(_exportService, () => new FlipperExportService());
            var importService = GetService(_importService, () => new FlipperImportService());
            var tabService = GetService(_tabService, () => _serviceProvider?.GetService<IWorkspaceTabService>() ?? new WorkspaceTabService(_serviceProvider ?? new ServiceCollection().BuildServiceProvider()));
            var dialogService = GetService(_dialogService, () => new DialogService());
            var feedbackService = GetService(_feedbackService, () => new UserFeedbackService());

            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations;
            string packName = "AssetPack";

            if (initialSprite != null)
            {
                string animName = "Animation";
                var entry = new FlipperManifestEntry
                {
                    Name = animName,
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1,
                };

                animations = [(animName, initialSprite, entry)];
                packName = animName;
            }
            else
            {
                animations = [];
            }

            var vm = new FlipperSimulatorViewModel(
                animations,
                packName,
                importService,
                exportService,
                this,
                tabService,
                dialogService,
                feedbackService);

            var win = new FlipperMatrixSimulatorWindow(vm);
            SafeSetOwner(win);
            win.ShowDialog();
        }

        public void ShowSimulator(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName = "AssetPack")
        {
            ShowSimulator(animations, packName, settings: null, onSettingsChanged: null);
        }

        public void ShowSimulator(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName,
            FlipperSimulatorSettings? settings,
            Action<FlipperSimulatorSettings>? onSettingsChanged = null)
        {
            var exportService = GetService(_exportService, () => new FlipperExportService());
            var importService = GetService(_importService, () => new FlipperImportService());
            var tabService = GetService(_tabService, () => _serviceProvider?.GetService<IWorkspaceTabService>() ?? new WorkspaceTabService(_serviceProvider ?? new ServiceCollection().BuildServiceProvider()));
            var dialogService = GetService(_dialogService, () => new DialogService());
            var feedbackService = GetService(_feedbackService, () => new UserFeedbackService());

            var vm = new FlipperSimulatorViewModel(
                animations ?? [],
                packName,
                importService,
                exportService,
                this,
                tabService,
                dialogService,
                feedbackService);

            if (settings != null)
            {
                vm.ApplySimulatorSettings(settings);
            }

            if (onSettingsChanged != null)
            {
                vm.DocumentModified += (s, e) =>
                {
                    var updated = vm.GetCurrentSimulatorSettings();
                    if (updated != null)
                    {
                        onSettingsChanged(updated);
                    }
                };
            }

            var win = new FlipperMatrixSimulatorWindow(vm);
            SafeSetOwner(win);
            win.ShowDialog();

            if (onSettingsChanged != null)
            {
                var finalSettings = vm.GetCurrentSimulatorSettings();
                if (finalSettings != null)
                {
                    onSettingsChanged(finalSettings);
                }
            }
        }

        public void ShowMediaSlicer(SpriteState? initialSprite = null)
        {
            var exportService = GetService(_exportService, () => new FlipperExportService());
            var tabService = GetService(_tabService, () => _serviceProvider?.GetService<IWorkspaceTabService>() ?? new WorkspaceTabService(_serviceProvider ?? new ServiceCollection().BuildServiceProvider()));
            var dialogService = GetService(_dialogService, () => new DialogService());
            var slicerService = _serviceProvider?.GetService<ISpriteSheetSlicerService>() ?? new SpriteSheetSlicerService();
            var codeGenService = _serviceProvider?.GetService<ICodeGeneratorService>() ?? new CodeGeneratorService();

            var flipperSettings = new SpriteSheetSliceSettings
            {
                CanvasMode = SliceCanvasMode.FixedCanvas,
                CanvasWidth = 128,
                CanvasHeight = 64,
                Alignment = SliceCanvasAlignment.Center,
                ScalingMode = SliceScalingMode.Crop1To1,
                Layout = SpriteSheetLayout.HorizontalStrip,
                Fps = 12
            };

            var vm = new SpriteSheetSlicerViewModel(
                slicerService: slicerService,
                tabService: tabService,
                dialogService: dialogService,
                exportService: new ExportService(),
                flipperExportService: exportService,
                codeGeneratorService: codeGenService,
                initialSprite: initialSprite,
                initialSettings: flipperSettings);
            vm.SelectedPresetIndex = 1; // Flipper Zero preset

            var win = new SpriteSheetSlicerWindow(vm);
            SafeSetOwner(win);
            win.ShowDialog();
        }

        public void ShowScheduleMatrix(
            IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null,
            string packName = "Flipper Asset Pack")
        {
            var tabService = GetService(_tabService, () => _serviceProvider?.GetService<IWorkspaceTabService>() ?? new WorkspaceTabService(_serviceProvider ?? new ServiceCollection().BuildServiceProvider()));
            tabService.OpenAssetPackInTab(pack, packName);
        }

        public void ShowScreenMirror()
        {
            var streamService = GetService(_streamService, () => new FlipperScreenStreamService());
            var tabService = GetService(_tabService, () => _serviceProvider?.GetService<IWorkspaceTabService>() ?? new WorkspaceTabService(_serviceProvider ?? new ServiceCollection().BuildServiceProvider()));
            var dialogService = GetService(_dialogService, () => new DialogService());

            var vm = new FlipperScreenMirrorViewModel(
                streamService,
                tabService,
                dialogService);

            var win = new FlipperScreenMirrorWindow(vm);
            SafeSetOwner(win);
            win.Show();
        }

        public void ShowDeploy(SpriteState? sprite = null)
        {
            var exportService = GetService(_exportService, () => new FlipperExportService());

            IReadOnlyList<(string RelativePath, byte[] Data)> files;
            string animName = "MyAssetPack";

            if (sprite != null)
            {
                animName = "MyAnimation";
                var settings = new FlipperExportSettings
                {
                    AnimationName = animName,
                    FrameRate = sprite.FrameRateFps > 0 ? sprite.FrameRateFps : 10,
                    PassiveFrames = sprite.FlipperCycle?.PassiveFrameCount ?? 0,
                    ActiveFrames = sprite.Frames.Count,
                    MinLevel = 1,
                    MaxLevel = 30,
                    MinButthurt = 0,
                    MaxButthurt = 14,
                    Weight = 1,
                    CreateManifestTxt = true,
                    TargetMode = FlipperExportTargetMode.MomentumAssetPack,
                };

                files = exportService.GenerateDeploymentFiles(sprite, settings);
            }
            else
            {
                files = [];
            }

            ShowDeploy(files, animName);
        }

        public void ShowDeploy(IReadOnlyList<(string RelativePath, byte[] Data)> files, string packName = "AssetPack")
        {
            var deployer = GetService(_deployer, () => new FlipperUsbDeployer());
            var dialogService = GetService(_dialogService, () => new DialogService());

            var vm = new FlipperDeployViewModel(
                deployer,
                files,
                packName,
                dialogService);

            var win = new FlipperDeployWindow(vm);
            SafeSetOwner(win);
            win.ShowDialog();
        }

        public void ShowDeploy(IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, string packName = "AssetPack")
        {
            var exportService = GetService(_exportService, () => new FlipperExportService());
            var fileEntries = new List<(string RelativePath, byte[] Data)>();
            var manifestEntries = new List<FlipperManifestEntry>();

            if (animations != null)
            {
                foreach (var (name, sprite, entry) in animations)
                {
                    if (sprite == null) continue;
                    string safeName = FlipperExportService.SanitizeAnimationName(name);
                    var animEntry = new FlipperManifestEntry
                    {
                        Name = safeName,
                        MinLevel = entry?.MinLevel ?? 1,
                        MaxLevel = entry?.MaxLevel ?? 30,
                        MinButthurt = entry?.MinButthurt ?? 0,
                        MaxButthurt = entry?.MaxButthurt ?? 14,
                        Weight = entry?.Weight ?? 1,
                    };
                    manifestEntries.Add(animEntry);

                    var settings = new FlipperExportSettings
                    {
                        AnimationName = safeName,
                        FrameRate = sprite.FrameRateFps > 0 ? sprite.FrameRateFps : 10,
                        PassiveFrames = sprite.FlipperCycle?.PassiveFrameCount ?? 0,
                        ActiveFrames = sprite.Frames.Count,
                        MinLevel = animEntry.MinLevel,
                        MaxLevel = animEntry.MaxLevel,
                        MinButthurt = animEntry.MinButthurt,
                        MaxButthurt = animEntry.MaxButthurt,
                        Weight = animEntry.Weight,
                        CreateManifestTxt = false,
                        TargetMode = FlipperExportTargetMode.MomentumAssetPack,
                    };

                    var singleAnimFiles = exportService.GenerateDeploymentFiles(sprite, settings);
                    foreach (var f in singleAnimFiles)
                    {
                        fileEntries.Add(f);
                    }
                }
            }

            var manifest = new FlipperManifest { Entries = manifestEntries };
            fileEntries.Add(("Anims/manifest.txt", System.Text.Encoding.UTF8.GetBytes(manifest.Serialize())));

            ShowDeploy(fileEntries, packName);
        }

        public bool? ShowExportDialog(SpriteState sprite, FlipperExportSettings settings)
        {
            ArgumentNullException.ThrowIfNull(sprite);

            var exportService = GetService(_exportService, () => new FlipperExportService());
            var dialogService = GetService(_dialogService, () => new DialogService());

            var vm = new FlipperExportViewModel(
                sprite,
                exportService,
                this,
                dialogService);

            if (settings != null)
            {
                if (!string.IsNullOrWhiteSpace(settings.AnimationName))
                    vm.AnimationName = settings.AnimationName;
                if (settings.FrameRate > 0)
                    vm.FrameRate = settings.FrameRate;
                if (settings.PassiveFrames > 0)
                    vm.PassiveFrames = settings.PassiveFrames;
                if (settings.ActiveFrames > 0)
                    vm.ActiveFrames = settings.ActiveFrames;
                vm.MinLevel = settings.MinLevel;
                vm.MaxLevel = settings.MaxLevel;
                vm.MinButthurt = settings.MinButthurt;
                vm.MaxButthurt = settings.MaxButthurt;
                vm.Weight = settings.Weight;
                vm.SelectedTargetModeIndex = (int)settings.TargetMode;
            }

            var dlg = new FlipperExportDialog(vm);
            SafeSetOwner(dlg);
            return dlg.ShowDialog();
        }
    }
}
