using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Hexprite.Controllers;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Services.Compression;
using Hexprite.ViewModels;
using Serilog;

namespace Hexprite
{
    public partial class App : Application
    {
        private IServiceProvider _serviceProvider = null!;
        public IServiceProvider Services => _serviceProvider;

        protected override void OnStartup(StartupEventArgs e)
        {
            LoggingService.Initialize();
            Log.Information("Application startup invoked. ArgsCount={ArgsCount}", e.Args?.Length ?? 0);

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            base.OnStartup(e);

            try
            {
                var services = new ServiceCollection();
                ConfigureServices(services);
                _serviceProvider = services.BuildServiceProvider();

                // Apply the persisted theme before showing any UI
                // FIX: Use the interface directly without downcasting to the concrete type.
                var themeService = _serviceProvider.GetRequiredService<IThemeService>();
                themeService.Initialize();

                var shortcutManager = _serviceProvider.GetRequiredService<IHexpriteShortcutManager>();
                shortcutManager.Initialize();

                var shell = _serviceProvider.GetRequiredService<ShellViewModel>();
                if (e.Args != null && e.Args.Length > 0)
                {
                    foreach (var arg in e.Args)
                    {
                        if (System.IO.File.Exists(arg))
                        {
                            shell.OpenFile(arg);
                        }
                    }
                }

                _serviceProvider.GetRequiredService<MainWindow>().Show();

                // Check for updates in the background (fire-and-forget)
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        var prefs = UserPreferencesService.Get();
                        if (!prefs.AutoCheckForUpdates)
                        {
                            return;
                        }

                        if (prefs.LastUpdateCheckUtc.HasValue &&
                            DateTimeOffset.UtcNow - prefs.LastUpdateCheckUtc.Value < TimeSpan.FromHours(24))
                        {
                            return;
                        }

                        var updateService = _serviceProvider.GetRequiredService<IUpdateService>();
                        var update = await updateService.CheckForUpdateAsync().ConfigureAwait(false);

                        UserPreferencesService.Update(p => p.LastUpdateCheckUtc = DateTimeOffset.UtcNow);

                        if (update != null)
                        {
                            await Current.Dispatcher.InvokeAsync(() =>
                            {
                                var shellVm = _serviceProvider.GetRequiredService<ShellViewModel>();
                                shellVm.NotifyUpdateAvailable(update);
                            });
                        }
                    }
                    catch (Exception updateEx)
                    {
                        Log.Warning(updateEx, "Background update check failed");
                    }
                });

                // Cleanup stale linked-file backups in the background (fire-and-forget)
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { _serviceProvider.GetRequiredService<IFileImportExportService>().CleanupOldBackups(); }
                    catch (Exception cleanupEx) { Log.Warning(cleanupEx, "Backup cleanup failed"); }
                });

                // Ensure embedded assets (HexpritePreview library and sketches) are extracted to AppData in background
                System.Threading.Tasks.Task.Run(() =>
                {
                    try { AssetsPathService.EnsureAssetsExtracted(); }
                    catch (Exception assetEx) { Log.Warning(assetEx, "Background asset extraction failed"); }

                    // Regenerate the standalone sketch from saved preferences so it always
                    // reflects the user's configured pins and baud rate (not the embedded defaults).
                    try
                    {
                        var prefs = UserPreferencesService.Get();
                        var wiringConfig = new HardwarePreviewWiringConfig
                        {
                            BoardPreset = prefs.HardwarePreviewBoardPreset,
                            InterfaceType = prefs.HardwarePreviewInterfaceType,
                            DisplayModel = prefs.HardwarePreviewDisplayModel,
                            SdaPin = prefs.HardwarePreviewSdaPin,
                            SclPin = prefs.HardwarePreviewSclPin,
                            I2cAddress = prefs.HardwarePreviewI2cAddress,
                            UseSoftwareI2c = prefs.HardwarePreviewUseSoftwareI2c,
                            CsPin = prefs.HardwarePreviewCsPin,
                            DcPin = prefs.HardwarePreviewDcPin,
                            RstPin = prefs.HardwarePreviewRstPin,
                            ClkPin = prefs.HardwarePreviewClkPin,
                            MosiPin = prefs.HardwarePreviewMosiPin,
                        };
                        HardwarePreviewSketchGenerator.UpdateStandaloneSketchInAppData(wiringConfig, prefs.HardwarePreviewBaudRate);
                    }
                    catch (Exception sketchEx) { Log.Warning(sketchEx, "Background sketch regeneration failed"); }
                });
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Application startup failed");
                SentryCrashFlush.TryFlushPendingEvents();
                Hexprite.Views.MessageDialog.Show(
                    $"The application could not start.\n\n{ex.Message}",
                    "Hexprite",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application exiting with code {ExitCode}", e.ApplicationExitCode);
            DispatcherUnhandledException -= App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException -= TaskScheduler_UnobservedTaskException;

            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }

            LoggingService.Shutdown();
            base.OnExit(e);
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            // Shared services (stateless or app-wide)
            services.AddSingleton<IUpdateService, UpdateService>();
            services.AddSingleton<ICodeGeneratorService, CodeGeneratorService>();
            services.AddSingleton<ICompressionService, CompressionService>();
            services.AddSingleton<IDrawingService, DrawingService>();
            services.AddSingleton<IClipboardService, ClipboardService>();
            services.AddSingleton<IDialogService, DialogService>();
            services.AddSingleton<IThemeService, ThemeService>();
            services.AddSingleton<IBugReportService, BugReportService>();
            services.AddSingleton<IUserFeedbackService, UserFeedbackService>();
            services.AddSingleton<IPixelClipboardService, PixelClipboardService>();
            services.AddSingleton<IExportService, ExportService>();
            services.AddSingleton<IFlipperExportService, FlipperExportService>();
            services.AddSingleton<IFlipperImportService, FlipperImportService>();
            services.AddSingleton<IFlipperUsbDeployer, FlipperUsbDeployer>();
            services.AddSingleton<IFlipperScreenStreamService, FlipperScreenStreamService>();
            services.AddSingleton<ISpriteSheetSlicerService, SpriteSheetSlicerService>();
            services.AddSingleton<IXbmService, XbmService>();
            services.AddSingleton<IFileImportExportService, FileImportExportService>();
            services.AddSingleton<IHardwarePreviewService, HardwarePreviewService>();
            services.AddSingleton<IHexpriteShortcutManager, HexpriteShortcutManager>();
            services.AddTransient<IAutosaveService, AutosaveService>();
            
            // Font Mode Services
            services.AddSingleton<IFontCodeGeneratorService, FontCodeGeneratorService>();
            services.AddSingleton<IFontImportService, FontImportService>();
            
            // Register ViewModels that can be created dynamically
            services.AddTransient<MainViewModel>();
            services.AddTransient<FontViewModel>();
            services.AddTransient<Hexprite.ViewModels.SpriteSheetSlicerViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperSimulatorViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperScheduleMatrixViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperMediaSlicerViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperScreenMirrorViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperExportViewModel>();
            services.AddTransient<Hexprite.ViewModels.Flipper.FlipperDeployViewModel>();

            // Flipper window & tab management abstractions
            services.AddSingleton<IFlipperWindowManager>(sp => new FlipperWindowManager(sp));

            // Controller factory for creating document-scoped controllers
            services.AddSingleton<IControllerFactory, ControllerFactory>();

            // ShellViewModel is the app-level VM that manages tabs
            services.AddSingleton<ShellViewModel>();
            services.AddSingleton<IWorkspaceTabService>(sp => new WorkspaceTabService(sp));

            // MainWindow is transient (created once on startup)
            services.AddTransient<MainWindow>(sp => new MainWindow(
                sp.GetRequiredService<ShellViewModel>(),
                sp.GetRequiredService<IHexpriteShortcutManager>()));
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            // Serilog Sentry sink forwards Fatal + exception to Sentry when configured.
            Log.Fatal(e.Exception, "Unhandled UI thread exception. IsTerminating=true");
            SentryCrashFlush.TryFlushPendingEvents();

            CreateEmergencyBackups(e.Exception);

            // Keep false so unrecoverable crashes terminate cleanly.
            e.Handled = false;
        }

        private void CurrentDomain_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Fatal(ex, "Unhandled non-UI exception. IsTerminating={IsTerminating}", e.IsTerminating);
                if (e.IsTerminating)
                {
                    SentryCrashFlush.TryFlushPendingEvents();
                    CreateEmergencyBackups(ex);
                }

                return;
            }

            Log.Fatal(
                "Unhandled non-UI exception was a non-Exception object. IsTerminating={IsTerminating} Type={ExceptionType}",
                e.IsTerminating,
                e.ExceptionObject?.GetType().FullName ?? "null");
            if (e.IsTerminating)
            {
                SentryCrashFlush.TryFlushPendingEvents();
                CreateEmergencyBackups(new InvalidOperationException("Unknown non-CLS exception."));
            }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Log.Error(e.Exception, "Unobserved task exception. ObservedBeforeSet={ObservedBeforeSet}", e.Observed);
            e.SetObserved();
        }

        private void CreateEmergencyBackups(Exception ex)
        {
            try
            {
                var shell = _serviceProvider?.GetService<ShellViewModel>();
                if (shell == null || shell.OpenDocuments.Count == 0) 
                {
                    ShowFallbackCrashDialog(ex);
                    return;
                }

                string backupDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Hexprite",
                    "CrashBackups",
                    DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

                bool savedAny = false;
                foreach (var doc in shell.OpenDocuments)
                {
                    if (doc.HasUnsavedChanges)
                    {
                        if (!savedAny) 
                        {
                            System.IO.Directory.CreateDirectory(backupDir);
                        }

                        string baseName = string.IsNullOrWhiteSpace(doc.Title) ? "Untitled" : doc.Title;
                        if (baseName.EndsWith('*'))
                        {
                            baseName = baseName[..^1];
                        }
                        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
                        {
                            baseName = baseName.Replace(c, '_');
                        }
                        
                        string ext = doc.Mode switch
                        {
                            Core.DocumentMode.Font => ".hexfont",
                            Core.DocumentMode.AssetPack => ".hexpack",
                            _ => ".hexp",
                        };
                        string fullPath = System.IO.Path.Combine(backupDir, baseName + ext);
                        
                        try
                        {
                            doc.SaveAs(fullPath);
                            savedAny = true;
                        }
                        catch { }
                    }
                }

                if (savedAny)
                {
                    Hexprite.Views.MessageDialog.Show(
                        $"Hexprite has encountered a fatal error and must close.\n\n" +
                        $"Emergency backups of your unsaved work have been saved to:\n{backupDir}\n\n" +
                        $"Error: {ex.Message}",
                        "Hexprite - Fatal Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    ShowFallbackCrashDialog(ex);
                }
            }
            catch (Exception backupEx)
            {
                Log.Fatal(backupEx, "Failed to create emergency backups during crash.");
                Hexprite.Views.MessageDialog.Show(
                    $"An unexpected error occurred and the application must close.\n\n" +
                    $"We attempted to save an emergency backup but failed: {backupEx.Message}\n\n" +
                    $"Original Error: {ex.Message}",
                    "Hexprite - Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static void ShowFallbackCrashDialog(Exception ex)
        {
            Hexprite.Views.MessageDialog.Show(
                $"Hexprite has encountered a fatal error and must close.\n\n" +
                $"Error: {ex.Message}",
                "Hexprite - Fatal Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
