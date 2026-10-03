using Hexprite.Core;
using System;
using System.IO;
using System.Text.Json;

using System.Collections.Generic;

namespace Hexprite.Services
{
    public class PerToolSettings
    {
        public int BrushSize { get; set; } = 1;
        public BrushShape BrushShape { get; set; } = BrushShape.Circle;
        public int BrushAngle { get; set; }
        public bool IsPixelPerfectEnabled { get; set; }
        public bool IsContiguousFillEnabled { get; set; } = true;
        
        public PerToolSettings Clone()
        {
            return new PerToolSettings
            {
                BrushSize = BrushSize,
                BrushShape = BrushShape,
                BrushAngle = BrushAngle,
                IsPixelPerfectEnabled = IsPixelPerfectEnabled,
                IsContiguousFillEnabled = IsContiguousFillEnabled,
            };
        }
    }

    public sealed class UserPreferences
    {
        public int NewCanvasWidth { get; set; } = 16;
        public int NewCanvasHeight { get; set; } = 16;
        public int NewCanvasPresetIndex { get; set; }
        public ColorMode NewCanvasColorMode { get; set; } = ColorMode.Monochrome;

        public int ResizePresetIndex { get; set; }
        public ResizeAnchor ResizeAnchor { get; set; } = ResizeAnchor.TopLeft;

        public int ImportFromCodeWidth { get; set; } = 16;
        public int ImportFromCodeHeight { get; set; } = 16;

        public ToolMode LastTool { get; set; } = ToolMode.Pencil;
        public bool ShowGridLines { get; set; } = true;
        public bool IsBrushCursorVisible { get; set; } = true;
        
        public Dictionary<ToolMode, PerToolSettings> ToolSettings { get; set; } = [];

        // Legacy properties preserved for backward compatibility parsing
        public int BrushSize { get; set; } = 1;
        public BrushShape BrushShape { get; set; } = BrushShape.Circle;
        public int BrushAngle { get; set; }
        public bool IsPixelPerfectEnabled { get; set; }
        public bool IsContiguousFillEnabled { get; set; } = true;
        public bool IsSymmetryHorizontalEnabled { get; set; }
        public bool IsSymmetryVerticalEnabled { get; set; }
        public int PreviewScale { get; set; } = 2;
        public int PreviewDisplayTypeIndex { get; set; }
        public bool UseRealisticPreview { get; set; }
        public int PreviewRealismStrength { get; set; } = 65;
        public int PreviewQuality { get; set; } = 1;
        public bool IsOnionSkinPrevEnabled { get; set; }
        public bool IsOnionSkinNextEnabled { get; set; }

        // Text Tool Preferences
        public string TextFontName { get; set; } = "";
        public int TextPixelScale { get; set; } = 1;
        public int TextLetterSpacing { get; set; } = 1;
        public int TextLineHeight { get; set; } = 1;
        public TextToolAlignment TextAlignment { get; set; } = TextToolAlignment.Left;

        // Update Preferences
        public bool AutoCheckForUpdates { get; set; } = true;
        public bool IncludePrereleases { get; set; }
        public string? DismissedUpdateVersion { get; set; }
        public DateTimeOffset? LastUpdateCheckUtc { get; set; }
        public string? LastUpdateCheckETag { get; set; }

        // Recent Files
        public List<string> RecentFiles { get; set; } = [];

        // Hardware Preview Wiring & Pin Preferences
        public string HardwarePreviewBoardPreset { get; set; } = HardwarePreviewWiringConfig.DefaultBoard;
        public string HardwarePreviewInterfaceType { get; set; } = HardwarePreviewWiringConfig.DefaultInterface;
        public string HardwarePreviewSdaPin { get; set; } = HardwarePreviewWiringConfig.DefaultSdaPin;
        public string HardwarePreviewSclPin { get; set; } = HardwarePreviewWiringConfig.DefaultSclPin;
        public string HardwarePreviewI2cAddress { get; set; } = HardwarePreviewWiringConfig.DefaultI2cAddress;
        public bool HardwarePreviewUseSoftwareI2c { get; set; }
        public string HardwarePreviewDisplayModel { get; set; } = HardwarePreviewWiringConfig.DefaultDisplayModel;
        public string HardwarePreviewCsPin { get; set; } = "5";
        public string HardwarePreviewDcPin { get; set; } = "16";
        public string HardwarePreviewRstPin { get; set; } = "17";
        public string HardwarePreviewClkPin { get; set; } = "18";
        public string HardwarePreviewMosiPin { get; set; } = "23";

        // Hardware Preview Connection Preferences
        public string? HardwarePreviewPort { get; set; }
        public int HardwarePreviewBaudRate { get; set; } = 115200;
        public bool HardwarePreviewAutoConnect { get; set; }
        public string HardwarePreviewPlacement { get; set; } = "Center";
        public string HardwarePreviewScale { get; set; } = "Scale1x";

        // Sidebar Section Expansion Preferences
        public bool IsDisplayPreviewExpanded { get; set; } = true;
        public bool IsLinkedSourceExpanded { get; set; }
        public bool IsHardwarePreviewExpanded { get; set; }
        public bool IsCodeGenerationExpanded { get; set; }
        public bool IsImportExpanded { get; set; }

        // Export Defaults
        public ExportSettings DefaultExportSettings { get; set; } = new();
        public ImageExportSettings DefaultImageExportSettings { get; set; } = new();

        // Import Defaults
        public BitmapImportSettings DefaultBitmapImportSettings { get; set; } = new() { MaxDimension = SpriteState.MaxDimension };
        public AnimationImportSettings DefaultAnimationImportSettings { get; set; } = new() { MaxDimension = SpriteState.MaxDimension };
    }

    public static class UserPreferencesService
    {
        public const int MaxRecentFiles = 15;
        private static readonly HashSet<string> AllowedRecentExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".hexp",
            ".hexpack",
            ".hexfont",
            ".hexpfont"
        };

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "S5443:Security - Insecure temporary file", Justification = "Path is only read for validation/rejection, not for file creation")]
        public static bool IsSupportedRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string fullPath = Path.GetFullPath(path);

                // Reject system temp and AppData Local Temp folders
                string tempPath = Path.GetTempPath();
                if (!string.IsNullOrEmpty(tempPath) && fullPath.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
                    return false;

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(localAppData))
                {
                    string localTemp = Path.Combine(localAppData, "Temp");
                    if (fullPath.StartsWith(localTemp, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                // Reject internal Autosaves and Backups directories
                if (fullPath.Contains(Path.DirectorySeparatorChar + "Autosaves" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Contains(Path.DirectorySeparatorChar + "Backups" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Contains(Path.AltDirectorySeparatorChar + "Autosaves" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Contains(Path.AltDirectorySeparatorChar + "Backups" + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string fileName = Path.GetFileName(fullPath);
                if (string.IsNullOrEmpty(fileName)) return false;

                // Reject temporary file prefixes and patterns
                if (fileName.StartsWith("shell_test_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith(".hexp_tmp_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("recovery_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("tmp", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith("temp_", StringComparison.OrdinalIgnoreCase) ||
                    fileName.StartsWith('~'))
                {
                    return false;
                }

                string ext = Path.GetExtension(fullPath);
                return !string.IsNullOrEmpty(ext) && AllowedRecentExtensions.Contains(ext);
            }
            catch
            {
                return false;
            }
        }

        private static readonly Lock Sync = new();
        private static string? _customSettingsPath;

        public static string EffectiveSettingsFile =>
            _customSettingsPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite", "user-preferences.json");

        public static string EffectiveBackupSettingsFile =>
            EffectiveSettingsFile + ".bak";

        public static string EffectiveSettingsDir =>
            Path.GetDirectoryName(EffectiveSettingsFile) ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite");

        public static void SetCustomSettingsPath(string? path, bool reload = true)
        {
            lock (Sync)
            {
                _customSettingsPath = path;
                if (reload)
                {
                    _cached = LoadFromDisk();
                }
            }
        }

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private static UserPreferences? _cached;

        public static UserPreferences Get()
        {
            lock (Sync)
            {
                if (_cached != null)
                    return CloneAndNormalize(_cached);

                _cached = LoadFromDisk();
                return CloneAndNormalize(_cached);
            }
        }

        public static void Update(Action<UserPreferences> apply)
        {
            if (apply == null) return;

            lock (Sync)
            {
                _cached ??= LoadFromDisk();
                apply(_cached);
                _cached = Normalize(_cached);
                SaveToDisk(_cached);
            }
        }

        public static ExportSettings GetDefaultExportSettings()
        {
            var s = Get().DefaultExportSettings.Clone();
            s.SpriteName = "mySprite";
            return s;
        }

        public static ImageExportSettings GetDefaultImageExportSettings() =>
            Get().DefaultImageExportSettings.Clone();

        public static BitmapImportSettings GetDefaultBitmapImportSettings() =>
            Get().DefaultBitmapImportSettings.Clone();

        public static AnimationImportSettings GetDefaultAnimationImportSettings() =>
            Get().DefaultAnimationImportSettings.Clone();

        public static void SaveImportSettings(BitmapImportSettings settings)
        {
            if (settings == null) return;
            Update(prefs =>
            {
                var sanitizedBase = settings.Clone();
                sanitizedBase.MaxDimension = Math.Clamp(settings.MaxDimension, 1, SpriteState.MaxDimension);
                sanitizedBase.Threshold = Math.Clamp(sanitizedBase.Threshold, 0, 255);
                sanitizedBase.AlphaThreshold = Math.Clamp(sanitizedBase.AlphaThreshold, 0, 255);
                sanitizedBase.Brightness = Math.Clamp(sanitizedBase.Brightness, -100, 100);
                sanitizedBase.Contrast = Math.Clamp(sanitizedBase.Contrast, -100, 100);
                sanitizedBase.DitherAmount = Math.Clamp(sanitizedBase.DitherAmount, 0, 100);

                if (settings is AnimationImportSettings anim)
                {
                    var animClone = anim.Clone();
                    animClone.MaxDimension = Math.Clamp(anim.MaxDimension, 1, SpriteState.MaxDimension);
                    animClone.TargetFps = Math.Clamp(animClone.TargetFps, 1, 24);
                    animClone.MaxFrames = Math.Clamp(animClone.MaxFrames, 1, 256);
                    prefs.DefaultAnimationImportSettings = animClone;
                    prefs.DefaultBitmapImportSettings = sanitizedBase;
                }
                else
                {
                    prefs.DefaultBitmapImportSettings = sanitizedBase;
                    prefs.DefaultAnimationImportSettings.CopyBaseFrom(sanitizedBase);
                }
            });
        }

        public static void ResetDefaultExportSettings()
        {
            Update(p => p.DefaultExportSettings = new ExportSettings());
        }

        public static void ResetDefaultImageExportSettings()
        {
            Update(p => p.DefaultImageExportSettings = new ImageExportSettings());
        }

        public static void ResetDefaultImportSettings()
        {
            Update(p =>
            {
                p.DefaultBitmapImportSettings = new BitmapImportSettings { MaxDimension = SpriteState.MaxDimension };
                p.DefaultAnimationImportSettings = new AnimationImportSettings { MaxDimension = SpriteState.MaxDimension };
            });
        }

        public static bool MigrateLegacyImportSettings(string legacyFilePath)
        {
            if (string.IsNullOrWhiteSpace(legacyFilePath) || !File.Exists(legacyFilePath)) return false;
            try
            {
                string json = SafeFileIo.ReadAllTextWithRetry(legacyFilePath);
                if (string.IsNullOrWhiteSpace(json)) return false;

                var legacy = JsonSerializer.Deserialize<BitmapImportSettings>(json);
                if (legacy == null) return false;

                SaveImportSettings(legacy);

                try
                {
                    File.Delete(legacyFilePath);
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Warning(ex, "UserPreferencesService.MigrateLegacyImportSettings.DeleteLegacyFailed", new { legacyFilePath });
                }
                return true;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "UserPreferencesService.MigrateLegacyImportSettings.Failed", new { legacyFilePath });
                return false;
            }
        }

        public static void AddRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsSupportedRecentFile(path)) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                Update(prefs =>
                {
                    prefs.RecentFiles.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase));
                    prefs.RecentFiles.Insert(0, fullPath);
                    if (prefs.RecentFiles.Count > MaxRecentFiles)
                    {
                        prefs.RecentFiles = [.. prefs.RecentFiles.Take(MaxRecentFiles)];
                    }
                });
            }
            catch { }
        }

        public static List<string> GetRecentFiles(bool pruneMissing = false)
        {
            var files = Get().RecentFiles;
            if (pruneMissing)
            {
                var existing = files.Where(p => File.Exists(p) || Directory.Exists(p)).ToList();
                if (existing.Count != files.Count)
                {
                    Update(prefs => prefs.RecentFiles = existing);
                    return existing;
                }
            }
            return [.. files];
        }

        public static void ClearRecentFiles()
        {
            Update(prefs => prefs.RecentFiles.Clear());
        }

        public static void RemoveRecentFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string fullPath = Path.GetFullPath(path);
                Update(prefs => prefs.RecentFiles.RemoveAll(p => string.Equals(p, fullPath, StringComparison.OrdinalIgnoreCase)));
            }
            catch { }
        }

        private static UserPreferences LoadFromDisk()
        {
            string settingsFile = EffectiveSettingsFile;
            string backupSettingsFile = EffectiveBackupSettingsFile;

            UserPreferences? result = null;

            // Primary attempt: settingsFile
            if (File.Exists(settingsFile))
            {
                try
                {
                    string json = SafeFileIo.ReadAllTextWithRetry(settingsFile);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var loaded = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions);
                        if (loaded != null)
                        {
                            result = Normalize(loaded);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Warning(ex, "UserPreferencesService.LoadFromDisk.PrimaryFailed", new { settingsFile });
                }
            }

            // Fallback attempt: backupSettingsFile
            if (result == null && File.Exists(backupSettingsFile))
            {
                try
                {
                    string bakJson = SafeFileIo.ReadAllTextWithRetry(backupSettingsFile);
                    if (!string.IsNullOrWhiteSpace(bakJson))
                    {
                        var recovered = JsonSerializer.Deserialize<UserPreferences>(bakJson, JsonOptions);
                        if (recovered != null)
                        {
                            result = Normalize(recovered);
                        }
                    }
                }
                catch (Exception ex)
                {
                    HandledErrorReporter.Warning(ex, "UserPreferencesService.LoadFromDisk.BackupFailed", new { backupSettingsFile });
                }
            }

            result ??= new UserPreferences();

            // Migrate legacy bitmap-import-settings.json if present
            try
            {
                string legacyImportFile = Path.Combine(EffectiveSettingsDir, "bitmap-import-settings.json");
                if (File.Exists(legacyImportFile))
                {
                    string json = SafeFileIo.ReadAllTextWithRetry(legacyImportFile);
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        var legacy = JsonSerializer.Deserialize<BitmapImportSettings>(json);
                        if (legacy != null)
                        {
                            result.DefaultBitmapImportSettings = legacy.Clone();
                            result.DefaultAnimationImportSettings.CopyBaseFrom(legacy);
                            Normalize(result);
                            SaveToDisk(result);
                        }
                    }
                    try { File.Delete(legacyImportFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "UserPreferencesService.LoadFromDisk.MigrateLegacyFailed");
            }

            return result;
        }

        private static void SaveToDisk(UserPreferences prefs)
        {
            string settingsFile = EffectiveSettingsFile;
            string backupSettingsFile = EffectiveBackupSettingsFile;
            string settingsDir = EffectiveSettingsDir;

            try
            {
                Directory.CreateDirectory(settingsDir);
                string json = JsonSerializer.Serialize(prefs, JsonOptions);

                // Preserve manual backup copy as extra redundancy
                if (File.Exists(settingsFile))
                {
                    try
                    {
                        File.Copy(settingsFile, backupSettingsFile, overwrite: true);
                    }
                    catch { }
                }

                SafeFileIo.WriteAllTextAtomic(settingsFile, json, maxRetries: 3, createBackup: true);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "UserPreferencesService.SaveToDisk", new { settingsFile });
            }
        }

        private static UserPreferences CloneAndNormalize(UserPreferences prefs)
        {
            return Normalize(new UserPreferences
            {
                NewCanvasWidth = prefs.NewCanvasWidth,
                NewCanvasHeight = prefs.NewCanvasHeight,
                NewCanvasPresetIndex = prefs.NewCanvasPresetIndex,
                NewCanvasColorMode = prefs.NewCanvasColorMode,
                ResizePresetIndex = prefs.ResizePresetIndex,
                ResizeAnchor = prefs.ResizeAnchor,
                ImportFromCodeWidth = prefs.ImportFromCodeWidth,
                ImportFromCodeHeight = prefs.ImportFromCodeHeight,
                LastTool = prefs.LastTool,
                ShowGridLines = prefs.ShowGridLines,
                IsBrushCursorVisible = prefs.IsBrushCursorVisible,
                ToolSettings = CloneToolSettingsMap(prefs.ToolSettings),
                BrushSize = prefs.BrushSize,
                BrushShape = prefs.BrushShape,
                BrushAngle = prefs.BrushAngle,
                IsPixelPerfectEnabled = prefs.IsPixelPerfectEnabled,
                IsContiguousFillEnabled = prefs.IsContiguousFillEnabled,
                IsSymmetryHorizontalEnabled = prefs.IsSymmetryHorizontalEnabled,
                IsSymmetryVerticalEnabled = prefs.IsSymmetryVerticalEnabled,
                PreviewScale = prefs.PreviewScale,
                PreviewDisplayTypeIndex = prefs.PreviewDisplayTypeIndex,
                UseRealisticPreview = prefs.UseRealisticPreview,
                PreviewRealismStrength = prefs.PreviewRealismStrength,
                PreviewQuality = prefs.PreviewQuality,
                IsOnionSkinPrevEnabled = prefs.IsOnionSkinPrevEnabled,
                IsOnionSkinNextEnabled = prefs.IsOnionSkinNextEnabled,
                TextFontName = prefs.TextFontName,
                TextPixelScale = prefs.TextPixelScale,
                TextLetterSpacing = prefs.TextLetterSpacing,
                TextLineHeight = prefs.TextLineHeight,
                TextAlignment = prefs.TextAlignment,
                AutoCheckForUpdates = prefs.AutoCheckForUpdates,
                IncludePrereleases = prefs.IncludePrereleases,
                DismissedUpdateVersion = prefs.DismissedUpdateVersion,
                LastUpdateCheckUtc = prefs.LastUpdateCheckUtc,
                LastUpdateCheckETag = prefs.LastUpdateCheckETag,
                RecentFiles = [.. (prefs.RecentFiles ?? [])],
                HardwarePreviewBoardPreset = prefs.HardwarePreviewBoardPreset,
                HardwarePreviewInterfaceType = prefs.HardwarePreviewInterfaceType,
                HardwarePreviewSdaPin = prefs.HardwarePreviewSdaPin,
                HardwarePreviewSclPin = prefs.HardwarePreviewSclPin,
                HardwarePreviewI2cAddress = prefs.HardwarePreviewI2cAddress,
                HardwarePreviewUseSoftwareI2c = prefs.HardwarePreviewUseSoftwareI2c,
                HardwarePreviewDisplayModel = prefs.HardwarePreviewDisplayModel,
                HardwarePreviewCsPin = prefs.HardwarePreviewCsPin,
                HardwarePreviewDcPin = prefs.HardwarePreviewDcPin,
                HardwarePreviewRstPin = prefs.HardwarePreviewRstPin,
                HardwarePreviewClkPin = prefs.HardwarePreviewClkPin,
                HardwarePreviewMosiPin = prefs.HardwarePreviewMosiPin,
                HardwarePreviewPort = prefs.HardwarePreviewPort,
                HardwarePreviewBaudRate = prefs.HardwarePreviewBaudRate,
                HardwarePreviewAutoConnect = prefs.HardwarePreviewAutoConnect,
                HardwarePreviewPlacement = prefs.HardwarePreviewPlacement,
                HardwarePreviewScale = prefs.HardwarePreviewScale,
                IsDisplayPreviewExpanded = prefs.IsDisplayPreviewExpanded,
                IsLinkedSourceExpanded = prefs.IsLinkedSourceExpanded,
                IsHardwarePreviewExpanded = prefs.IsHardwarePreviewExpanded,
                IsCodeGenerationExpanded = prefs.IsCodeGenerationExpanded,
                IsImportExpanded = prefs.IsImportExpanded,
                DefaultExportSettings = (prefs.DefaultExportSettings ?? new ExportSettings()).Clone(),
                DefaultImageExportSettings = (prefs.DefaultImageExportSettings ?? new ImageExportSettings()).Clone(),
                DefaultBitmapImportSettings = (prefs.DefaultBitmapImportSettings ?? new BitmapImportSettings { MaxDimension = SpriteState.MaxDimension }).Clone(),
                DefaultAnimationImportSettings = (prefs.DefaultAnimationImportSettings ?? new AnimationImportSettings { MaxDimension = SpriteState.MaxDimension }).Clone(),
            });
        }

        private static UserPreferences Normalize(UserPreferences prefs)
        {
            prefs.RecentFiles ??= [];
            // Filter only supported native extensions (.hexp, .hexpack, .hexfont), de-duplicate while preserving order, remove empty/whitespace, cap at MaxRecentFiles
            var deduped = new List<string>();
            foreach (var rf in prefs.RecentFiles)
            {
                if (!string.IsNullOrWhiteSpace(rf) && IsSupportedRecentFile(rf) && !deduped.Exists(d => string.Equals(d, rf, StringComparison.OrdinalIgnoreCase)))
                {
                    deduped.Add(rf);
                    if (deduped.Count >= MaxRecentFiles) break;
                }
            }
            prefs.RecentFiles = deduped;

            prefs.DefaultExportSettings ??= new ExportSettings();
            prefs.DefaultExportSettings.SpriteName = "mySprite";
            prefs.DefaultExportSettings.BytesPerLine = Math.Clamp(prefs.DefaultExportSettings.BytesPerLine, 0, 256);

            prefs.DefaultImageExportSettings ??= new ImageExportSettings();
            prefs.DefaultImageExportSettings.Scale = Math.Clamp(prefs.DefaultImageExportSettings.Scale, 1, 8);
            prefs.DefaultImageExportSettings.GifFps = Math.Clamp(prefs.DefaultImageExportSettings.GifFps, 1, 60);

            prefs.DefaultBitmapImportSettings ??= new BitmapImportSettings { MaxDimension = SpriteState.MaxDimension };
            if (prefs.DefaultBitmapImportSettings.MaxDimension <= 0 || prefs.DefaultBitmapImportSettings.MaxDimension > SpriteState.MaxDimension)
            {
                prefs.DefaultBitmapImportSettings.MaxDimension = SpriteState.MaxDimension;
            }
            prefs.DefaultBitmapImportSettings.Threshold = Math.Clamp(prefs.DefaultBitmapImportSettings.Threshold, 0, 255);
            prefs.DefaultBitmapImportSettings.AlphaThreshold = Math.Clamp(prefs.DefaultBitmapImportSettings.AlphaThreshold, 0, 255);
            prefs.DefaultBitmapImportSettings.Brightness = Math.Clamp(prefs.DefaultBitmapImportSettings.Brightness, -100, 100);
            prefs.DefaultBitmapImportSettings.Contrast = Math.Clamp(prefs.DefaultBitmapImportSettings.Contrast, -100, 100);
            prefs.DefaultBitmapImportSettings.DitherAmount = Math.Clamp(prefs.DefaultBitmapImportSettings.DitherAmount, 0, 100);

            prefs.DefaultAnimationImportSettings ??= new AnimationImportSettings { MaxDimension = SpriteState.MaxDimension };
            if (prefs.DefaultAnimationImportSettings.MaxDimension <= 0 || prefs.DefaultAnimationImportSettings.MaxDimension > SpriteState.MaxDimension)
            {
                prefs.DefaultAnimationImportSettings.MaxDimension = SpriteState.MaxDimension;
            }
            prefs.DefaultAnimationImportSettings.Threshold = Math.Clamp(prefs.DefaultAnimationImportSettings.Threshold, 0, 255);
            prefs.DefaultAnimationImportSettings.AlphaThreshold = Math.Clamp(prefs.DefaultAnimationImportSettings.AlphaThreshold, 0, 255);
            prefs.DefaultAnimationImportSettings.Brightness = Math.Clamp(prefs.DefaultAnimationImportSettings.Brightness, -100, 100);
            prefs.DefaultAnimationImportSettings.Contrast = Math.Clamp(prefs.DefaultAnimationImportSettings.Contrast, -100, 100);
            prefs.DefaultAnimationImportSettings.DitherAmount = Math.Clamp(prefs.DefaultAnimationImportSettings.DitherAmount, 0, 100);
            prefs.DefaultAnimationImportSettings.TargetFps = Math.Clamp(prefs.DefaultAnimationImportSettings.TargetFps, 1, 24);
            prefs.DefaultAnimationImportSettings.MaxFrames = Math.Clamp(prefs.DefaultAnimationImportSettings.MaxFrames, 1, 256);

            prefs.HardwarePreviewBoardPreset = string.IsNullOrWhiteSpace(prefs.HardwarePreviewBoardPreset) ? HardwarePreviewWiringConfig.DefaultBoard : prefs.HardwarePreviewBoardPreset.Trim();
            prefs.HardwarePreviewInterfaceType = string.Equals(prefs.HardwarePreviewInterfaceType, "SPI", StringComparison.OrdinalIgnoreCase) ? "SPI" : "I2C";
            prefs.HardwarePreviewSdaPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewSdaPin) ? HardwarePreviewWiringConfig.DefaultSdaPin : prefs.HardwarePreviewSdaPin.Trim();
            prefs.HardwarePreviewSclPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewSclPin) ? HardwarePreviewWiringConfig.DefaultSclPin : prefs.HardwarePreviewSclPin.Trim();
            prefs.HardwarePreviewI2cAddress = string.IsNullOrWhiteSpace(prefs.HardwarePreviewI2cAddress) ? HardwarePreviewWiringConfig.DefaultI2cAddress : prefs.HardwarePreviewI2cAddress.Trim();
            prefs.HardwarePreviewDisplayModel = string.IsNullOrWhiteSpace(prefs.HardwarePreviewDisplayModel) ? HardwarePreviewWiringConfig.DefaultDisplayModel : prefs.HardwarePreviewDisplayModel.Trim();
            prefs.HardwarePreviewCsPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewCsPin) ? "5" : prefs.HardwarePreviewCsPin.Trim();
            prefs.HardwarePreviewDcPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewDcPin) ? "16" : prefs.HardwarePreviewDcPin.Trim();
            prefs.HardwarePreviewRstPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewRstPin) ? "17" : prefs.HardwarePreviewRstPin.Trim();
            prefs.HardwarePreviewClkPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewClkPin) ? "18" : prefs.HardwarePreviewClkPin.Trim();
            prefs.HardwarePreviewMosiPin = string.IsNullOrWhiteSpace(prefs.HardwarePreviewMosiPin) ? "23" : prefs.HardwarePreviewMosiPin.Trim();
            if (prefs.HardwarePreviewBaudRate <= 0) prefs.HardwarePreviewBaudRate = 115200;
            if (string.IsNullOrWhiteSpace(prefs.HardwarePreviewPort)) prefs.HardwarePreviewPort = null;
            else prefs.HardwarePreviewPort = prefs.HardwarePreviewPort.Trim();
            if (string.IsNullOrWhiteSpace(prefs.HardwarePreviewPlacement)) prefs.HardwarePreviewPlacement = "Center";
            if (string.IsNullOrWhiteSpace(prefs.HardwarePreviewScale)) prefs.HardwarePreviewScale = "Scale1x";

            prefs.NewCanvasWidth = Math.Clamp(prefs.NewCanvasWidth, 1, SpriteState.MaxDimension);
            prefs.NewCanvasHeight = Math.Clamp(prefs.NewCanvasHeight, 1, SpriteState.MaxDimension);
            prefs.NewCanvasPresetIndex = Math.Max(0, prefs.NewCanvasPresetIndex);

            prefs.ResizePresetIndex = Math.Max(0, prefs.ResizePresetIndex);
            if (!Enum.IsDefined(prefs.ResizeAnchor))
                prefs.ResizeAnchor = ResizeAnchor.TopLeft;

            prefs.ImportFromCodeWidth = Math.Clamp(prefs.ImportFromCodeWidth, 1, 256);
            prefs.ImportFromCodeHeight = Math.Clamp(prefs.ImportFromCodeHeight, 1, 256);

            prefs.BrushSize = Math.Clamp(prefs.BrushSize, 1, 64);
            if (!Enum.IsDefined(prefs.BrushShape))
                prefs.BrushShape = BrushShape.Circle;
            prefs.BrushAngle = ((prefs.BrushAngle % 360) + 360) % 360;
            prefs.PreviewScale = Math.Max(1, prefs.PreviewScale);
            prefs.PreviewDisplayTypeIndex = Math.Clamp(prefs.PreviewDisplayTypeIndex, 0, 4);
            prefs.PreviewRealismStrength = Math.Clamp(prefs.PreviewRealismStrength, 0, 100);
            prefs.PreviewQuality = Math.Clamp(prefs.PreviewQuality, 0, 2);
            if (!Enum.IsDefined(prefs.LastTool))
                prefs.LastTool = ToolMode.Pencil;
            if (!Enum.IsDefined(prefs.NewCanvasColorMode))
                prefs.NewCanvasColorMode = ColorMode.Monochrome;

            prefs.TextPixelScale = Math.Clamp(prefs.TextPixelScale, 1, 10);
            prefs.TextLetterSpacing = Math.Clamp(prefs.TextLetterSpacing, -5, 20);
            prefs.TextLineHeight = Math.Clamp(prefs.TextLineHeight, -10, 20);
            if (!Enum.IsDefined(prefs.TextAlignment))
                prefs.TextAlignment = TextToolAlignment.Left;
            prefs.TextFontName ??= "";

            prefs.ToolSettings ??= [];
            foreach (ToolMode tool in Enum.GetValues<ToolMode>())
            {
                if (!prefs.ToolSettings.TryGetValue(tool, out var ts))
                {
                    prefs.ToolSettings[tool] = new PerToolSettings
                    {
                        BrushSize = prefs.BrushSize,
                        BrushShape = prefs.BrushShape,
                        BrushAngle = prefs.BrushAngle,
                        IsPixelPerfectEnabled = prefs.IsPixelPerfectEnabled,
                        IsContiguousFillEnabled = prefs.IsContiguousFillEnabled,
                    };
                }
                else
                {
                    ts.BrushSize = Math.Clamp(ts.BrushSize, 1, 64);
                    if (!Enum.IsDefined(ts.BrushShape))
                        ts.BrushShape = BrushShape.Circle;
                    ts.BrushAngle = ((ts.BrushAngle % 360) + 360) % 360;
                }
            }

            return prefs;
        }

        private static Dictionary<ToolMode, PerToolSettings> CloneToolSettingsMap(Dictionary<ToolMode, PerToolSettings> source)
        {
            var dest = new Dictionary<ToolMode, PerToolSettings>();
            if (source != null)
            {
                foreach (var kvp in source)
                {
                    dest[kvp.Key] = kvp.Value.Clone();
                }
            }
            return dest;
        }
    }
}
