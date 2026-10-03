# Unified Persistence for Bitmap Import and Export Settings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Provide comprehensive, thread-safe persistence for code generation export settings, image/GIF export settings, bitmap monochrome import settings, and animated sequence import settings via `UserPreferencesService`.

**Architecture:** Extend `UserPreferences` with strongly typed defaults for code export, image export, bitmap import, and animation import. Ensure document-level isolation (`SpriteName` is never globally saved; open documents keep saved settings while new documents inherit preferences). Wire `MainViewModel`, `ShellViewModel`, and `ExportDialog` into `UserPreferencesService`, preserve full animation import properties, migrate legacy `bitmap-import-settings.json`, and provide factory reset actions.

**Tech Stack:** C# 10 / .NET 10, WPF, System.Text.Json, xUnit.

**Spec:** [`docs/superpowers/specs/2026-10-03-import-export-settings-persistence-design.md`](file:///H:/Repositories/hexprite/docs/superpowers/specs/2026-10-03-import-export-settings-persistence-design.md)

## Global Constraints

- Storage file: `%APPDATA%\Hexprite\user-preferences.json` managed exclusively through `UserPreferencesService`.
- Atomic writes: All disk persistence must utilize `SafeFileIo.WriteAllTextAtomic` and honor `UserPreferencesService` thread synchronization.
- Document Isolation: `SpriteName` must never be overwritten in global user preferences; `ExportAsAnimation` must be driven by `IsAnimationEnabled`.
- Animation Import Fidelity: `TargetFps` (1–24), `MaxFrames` (1–256), `UniformSampling`, and `TrimTrailingBlankFrames` must be preserved across sessions.
- Backward Compatibility: Zero regressions across existing test suites (`dotnet test`).

## Review Focus

1. Corrupted or invalid JSON in `user-preferences.json` falls back to `.bak` or defaults without crashing the application.
2. Modifying returned settings objects from `GetDefault*` must never mutate internal cached preferences (strict deep cloning).
3. Opening an existing `.hexp` document with saved custom export settings preserves those settings and does not overwrite them with global preferences.
4. Rapid slider or textbox changes in the export sidebar must not trigger excessive disk writes (debounced persistence).
5. Importing an animated GIF must retain user adjustments to `TargetFps` and `MaxFrames` on the next import dialog open.

---

### Task 1: Extend `UserPreferences` Schema & Accessors with Deep Cloning

**Files:**
- Modify: `Hexprite/Services/BitmapToMonochromeConverter.cs:36-56`
- Modify: `Hexprite/Services/AnimationImportSettings.cs:8-41`
- Modify: `Hexprite/Services/UserPreferencesService.cs:31-110, 360-450`
- Test: `Hexprite.Tests/UserPreferencesServiceTests.cs`

**Interfaces:**
- Consumes: `ExportSettings`, `ImageExportSettings`, `BitmapImportSettings`, `AnimationImportSettings`
- Produces:
  - `UserPreferences.DefaultExportSettings`
  - `UserPreferences.DefaultImageExportSettings`
  - `UserPreferences.DefaultBitmapImportSettings`
  - `UserPreferences.DefaultAnimationImportSettings`
  - `UserPreferencesService.GetDefaultExportSettings() -> ExportSettings`
  - `UserPreferencesService.GetDefaultImageExportSettings() -> ImageExportSettings`
  - `UserPreferencesService.GetDefaultBitmapImportSettings() -> BitmapImportSettings`
  - `UserPreferencesService.GetDefaultAnimationImportSettings() -> AnimationImportSettings`
  - `UserPreferencesService.SaveImportSettings(BitmapImportSettings settings) -> void`
  - `UserPreferencesService.ResetDefaultExportSettings() -> void`
  - `UserPreferencesService.ResetDefaultImageExportSettings() -> void`
  - `UserPreferencesService.ResetDefaultImportSettings() -> void`

- [ ] **Step 1: Write the failing tests in `UserPreferencesServiceTests.cs`**

```csharp
using Hexprite.Core;
using Hexprite.Services;
using System;
using System.IO;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public class UserPreferencesServiceTests : IDisposable
{
    private readonly string _tempFile;

    public UserPreferencesServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"user_prefs_test_{Guid.NewGuid():N}.json");
        UserPreferencesService.SetCustomSettingsPath(_tempFile);
    }

    public void Dispose()
    {
        UserPreferencesService.SetCustomSettingsPath(null);
        try { if (File.Exists(_tempFile)) File.Delete(_tempFile); } catch { }
        try { if (File.Exists(_tempFile + ".bak")) File.Delete(_tempFile + ".bak"); } catch { }
    }

    [Fact]
    public void GetDefaultSettings_ReturnsIsolatedClones()
    {
        var exp1 = UserPreferencesService.GetDefaultExportSettings();
        exp1.BytesPerLine = 32;
        var exp2 = UserPreferencesService.GetDefaultExportSettings();
        Assert.NotEqual(32, exp2.BytesPerLine);

        var img1 = UserPreferencesService.GetDefaultImageExportSettings();
        img1.Scale = 8;
        var img2 = UserPreferencesService.GetDefaultImageExportSettings();
        Assert.NotEqual(8, img2.Scale);

        var bmp1 = UserPreferencesService.GetDefaultBitmapImportSettings();
        bmp1.Threshold = 200;
        var bmp2 = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.NotEqual(200, bmp2.Threshold);

        var anim1 = UserPreferencesService.GetDefaultAnimationImportSettings();
        anim1.TargetFps = 20;
        var anim2 = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.NotEqual(20, anim2.TargetFps);
    }

    [Fact]
    public void SaveImportSettings_PreservesAnimationPropertiesAndSyncsBase()
    {
        var anim = new AnimationImportSettings
        {
            Threshold = 180,
            DitheringAlgorithm = BitmapDitheringAlgorithm.Atkinson,
            TargetFps = 15,
            MaxFrames = 64,
            UniformSampling = false,
            TrimTrailingBlankFrames = false,
        };

        UserPreferencesService.SaveImportSettings(anim);

        var savedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.Equal(180, savedAnim.Threshold);
        Assert.Equal(BitmapDitheringAlgorithm.Atkinson, savedAnim.DitheringAlgorithm);
        Assert.Equal(15, savedAnim.TargetFps);
        Assert.Equal(64, savedAnim.MaxFrames);
        Assert.False(savedAnim.UniformSampling);
        Assert.False(savedAnim.TrimTrailingBlankFrames);

        var savedBmp = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.Equal(180, savedBmp.Threshold);
        Assert.Equal(BitmapDitheringAlgorithm.Atkinson, savedBmp.DitheringAlgorithm);
    }

    [Fact]
    public void ResetDefaults_RestoresFactorySettings()
    {
        UserPreferencesService.Update(p =>
        {
            p.DefaultExportSettings.BytesPerLine = 32;
            p.DefaultImageExportSettings.Scale = 8;
            p.DefaultBitmapImportSettings.Threshold = 40;
            p.DefaultAnimationImportSettings.TargetFps = 24;
        });

        UserPreferencesService.ResetDefaultExportSettings();
        Assert.Equal(16, UserPreferencesService.GetDefaultExportSettings().BytesPerLine);

        UserPreferencesService.ResetDefaultImageExportSettings();
        Assert.Equal(4, UserPreferencesService.GetDefaultImageExportSettings().Scale);

        UserPreferencesService.ResetDefaultImportSettings();
        Assert.Equal(128, UserPreferencesService.GetDefaultBitmapImportSettings().Threshold);
        Assert.Equal(8, UserPreferencesService.GetDefaultAnimationImportSettings().TargetFps);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~UserPreferencesServiceTests`
Expected: FAIL with compilation errors (methods `GetDefaultExportSettings`, `SaveImportSettings`, etc. not defined).

- [ ] **Step 3: Implement model helpers and `UserPreferencesService` accessors**

In `BitmapToMonochromeConverter.cs`:
Add `Clone()` and `CopyBaseFrom()` to `BitmapImportSettings`:
```csharp
public BitmapImportSettings Clone() => (BitmapImportSettings)MemberwiseClone();
public void CopyBaseFrom(BitmapImportSettings source)
{
    Preset = source.Preset;
    MaxDimension = source.MaxDimension;
    Threshold = source.Threshold;
    AlphaThreshold = source.AlphaThreshold;
    Invert = source.Invert;
    DitheringAlgorithm = source.DitheringAlgorithm;
    ScalingMode = source.ScalingMode;
    UseSerpentineScanning = source.UseSerpentineScanning;
    UseGammaCorrection = source.UseGammaCorrection;
    UseAdaptiveThresholding = source.UseAdaptiveThresholding;
    PreserveEdges = source.PreserveEdges;
    Sharpen = source.Sharpen;
    Brightness = source.Brightness;
    Contrast = source.Contrast;
    DitherAmount = source.DitherAmount;
}
```

In `AnimationImportSettings.cs`:
Add `Clone()` to `AnimationImportSettings`:
```csharp
public new AnimationImportSettings Clone() => (AnimationImportSettings)MemberwiseClone();
```

In `UserPreferencesService.cs`:
Add properties to `UserPreferences`:
```csharp
public ExportSettings DefaultExportSettings { get; set; } = new();
public ImageExportSettings DefaultImageExportSettings { get; set; } = new();
public BitmapImportSettings DefaultBitmapImportSettings { get; set; } = new();
public AnimationImportSettings DefaultAnimationImportSettings { get; set; } = new();
```

Add helper methods in `UserPreferencesService`:
```csharp
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
        sanitizedBase.MaxDimension = SpriteState.MaxDimension;
        sanitizedBase.Threshold = Math.Clamp(sanitizedBase.Threshold, 0, 255);
        sanitizedBase.AlphaThreshold = Math.Clamp(sanitizedBase.AlphaThreshold, 0, 255);
        sanitizedBase.Brightness = Math.Clamp(sanitizedBase.Brightness, -100, 100);
        sanitizedBase.Contrast = Math.Clamp(sanitizedBase.Contrast, -100, 100);
        sanitizedBase.DitherAmount = Math.Clamp(sanitizedBase.DitherAmount, 0, 100);

        if (settings is AnimationImportSettings anim)
        {
            var animClone = anim.Clone();
            animClone.MaxDimension = SpriteState.MaxDimension;
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
        p.DefaultBitmapImportSettings = new BitmapImportSettings();
        p.DefaultAnimationImportSettings = new AnimationImportSettings();
    });
}
```

Update `CloneAndNormalize` and `Normalize` in `UserPreferencesService.cs` to defensively copy and clamp the four settings objects.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~UserPreferencesServiceTests`
Expected: PASS (all 3 tests pass).

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Services/BitmapToMonochromeConverter.cs Hexprite/Services/AnimationImportSettings.cs Hexprite/Services/UserPreferencesService.cs Hexprite.Tests/UserPreferencesServiceTests.cs
git commit -m "feat: add import and export settings defaults to UserPreferencesService"
```

---

### Task 2: Implement Legacy `bitmap-import-settings.json` Migration

**Files:**
- Modify: `Hexprite/Services/UserPreferencesService.cs:280-328`
- Test: `Hexprite.Tests/UserPreferencesServiceTests.cs`

**Interfaces:**
- Consumes: `%APPDATA%\Hexprite\bitmap-import-settings.json` (if present)
- Produces: `UserPreferencesService.MigrateLegacyBitmapImportSettings(string legacyFilePath) -> bool`

- [ ] **Step 1: Write the failing test in `UserPreferencesServiceTests.cs`**

```csharp
[Fact]
public void MigrateLegacyBitmapImportSettings_ImportsValuesAndRemovesLegacyFile()
{
    string legacyFile = Path.Combine(Path.GetTempPath(), $"legacy_import_{Guid.NewGuid():N}.json");
    try
    {
        var legacy = new BitmapImportSettings
        {
            Threshold = 142,
            DitheringAlgorithm = BitmapDitheringAlgorithm.Bayer,
            Contrast = 25,
            Invert = true
        };
        File.WriteAllText(legacyFile, System.Text.Json.JsonSerializer.Serialize(legacy));

        bool migrated = UserPreferencesService.MigrateLegacyImportSettings(legacyFile);
        Assert.True(migrated);

        var loadedBmp = UserPreferencesService.GetDefaultBitmapImportSettings();
        Assert.Equal(142, loadedBmp.Threshold);
        Assert.Equal(BitmapDitheringAlgorithm.Bayer, loadedBmp.DitheringAlgorithm);
        Assert.Equal(25, loadedBmp.Contrast);
        Assert.True(loadedBmp.Invert);

        var loadedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
        Assert.Equal(142, loadedAnim.Threshold);
        Assert.True(loadedAnim.Invert);

        Assert.False(File.Exists(legacyFile));
    }
    finally
    {
        try { if (File.Exists(legacyFile)) File.Delete(legacyFile); } catch { }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MigrateLegacyBitmapImportSettings`
Expected: FAIL (`MigrateLegacyImportSettings` method does not exist).

- [ ] **Step 3: Implement migration logic in `UserPreferencesService`**

In `UserPreferencesService.cs`:
```csharp
public static bool MigrateLegacyImportSettings(string legacyFilePath)
{
    if (!File.Exists(legacyFilePath)) return false;
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
```
In `LoadFromDisk()`:
```csharp
string legacyImportFile = Path.Combine(EffectiveSettingsDir, "bitmap-import-settings.json");
if (File.Exists(legacyImportFile))
{
    MigrateLegacyImportSettings(legacyImportFile);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MigrateLegacyBitmapImportSettings`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Services/UserPreferencesService.cs Hexprite.Tests/UserPreferencesServiceTests.cs
git commit -m "feat: add legacy bitmap-import-settings.json migration into UserPreferences"
```

---

### Task 3: Integrate Bitmap & Animation Import Persistence in `ShellViewModel`

**Files:**
- Modify: `Hexprite/ViewModels/ShellViewModel.cs:2100-2105, 2400-2450, 2500-2570`
- Test: `Hexprite.Tests/ShellViewModelTests.cs`

**Interfaces:**
- Consumes: `UserPreferencesService.GetDefaultBitmapImportSettings()`, `UserPreferencesService.GetDefaultAnimationImportSettings()`, `UserPreferencesService.SaveImportSettings(settings)`
- Produces: `ShellViewModel.LoadBitmapImportSettings()`, `ShellViewModel.SaveBitmapImportSettings(settings)` redirecting to `UserPreferencesService`

- [ ] **Step 1: Write the failing test in `ShellViewModelTests.cs`**

```csharp
[Fact]
public void ShellViewModel_ImportSettings_RoutesThroughUserPreferencesService()
{
    var settings = new AnimationImportSettings
    {
        Threshold = 175,
        TargetFps = 18,
        MaxFrames = 48,
    };

    ShellViewModel.SaveBitmapImportSettings(settings);

    var loadedBmp = ShellViewModel.LoadBitmapImportSettings();
    Assert.Equal(175, loadedBmp.Threshold);

    var loadedAnim = UserPreferencesService.GetDefaultAnimationImportSettings();
    Assert.Equal(175, loadedAnim.Threshold);
    Assert.Equal(18, loadedAnim.TargetFps);
    Assert.Equal(48, loadedAnim.MaxFrames);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~ShellViewModel_ImportSettings_RoutesThroughUserPreferencesService`
Expected: FAIL (`loadedAnim.TargetFps` is 8 instead of 18 because `SaveBitmapImportSettings` sliced it).

- [ ] **Step 3: Update `ShellViewModel.cs` import workflows**

In `ShellViewModel.cs`:
1. Redirect `LoadBitmapImportSettings` and `SaveBitmapImportSettings`:
```csharp
internal static BitmapImportSettings LoadBitmapImportSettings(string? settingsFilePath = null)
{
    if (settingsFilePath != null)
    {
        // Custom path fallback for existing test harnesses
        ...
    }
    return UserPreferencesService.GetDefaultBitmapImportSettings();
}

internal static void SaveBitmapImportSettings(BitmapImportSettings settings, string? settingsFilePath = null)
{
    if (settingsFilePath != null)
    {
        // Custom path fallback for existing test harnesses
        ...
    }
    UserPreferencesService.SaveImportSettings(settings);
}
```
2. In `ExecuteImportBitmap`:
```csharp
var baseSettings = UserPreferencesService.GetDefaultBitmapImportSettings();
var importSettings = _dialogService.ShowImportBitmapDialog(selectedPath, baseSettings);
if (importSettings == null) return;
UserPreferencesService.SaveImportSettings(importSettings);
```
3. In `ExecuteImportAnimatedGif`:
```csharp
var animSettings = UserPreferencesService.GetDefaultAnimationImportSettings();
var importSettings = _dialogService.ShowImportAnimationDialog(selectedPath, animSettings);
if (importSettings == null) return;
UserPreferencesService.SaveImportSettings(importSettings);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~ShellViewModel_ImportSettings_RoutesThroughUserPreferencesService`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/ViewModels/ShellViewModel.cs Hexprite.Tests/ShellViewModelTests.cs
git commit -m "refactor: route ShellViewModel import settings through UserPreferencesService"
```

---

### Task 4: Integrate Code Generation Export Persistence in `MainViewModel`

**Files:**
- Modify: `Hexprite/ViewModels/MainViewModel.Export.cs:25-80, 135-180`
- Modify: `Hexprite/ViewModels/MainViewModel.cs:250-280`
- Modify: `Hexprite/ViewModels/ShellViewModel.cs:2820-2850`
- Test: `Hexprite.Tests/ViewModels/MainViewModelExportTests.cs`

**Interfaces:**
- Consumes: `UserPreferencesService.GetDefaultExportSettings()`, `UserPreferencesService.Update(...)`
- Produces:
  - `MainViewModel.ResetExportSettingsCommand`
  - Automatic debounced persistence of `ExportSettings` to `UserPreferencesService`

- [ ] **Step 1: Write the failing tests in `MainViewModelExportTests.cs`**

```csharp
[Fact]
public void MainViewModel_NewDocument_InheritsUserPreferenceExportSettings()
{
    UserPreferencesService.Update(p =>
    {
        p.DefaultExportSettings.Format = ExportFormat.U8g2DrawBitmap;
        p.DefaultExportSettings.BytesPerLine = 8;
        p.DefaultExportSettings.UppercaseHex = true;
    });

    var vm = CreateMainViewModel();

    Assert.Equal(ExportFormat.U8g2DrawBitmap, vm.ExportFormat);
    Assert.Equal(8, vm.BytesPerLine);
    Assert.True(vm.UppercaseHex);
    // Document sprite name must remain isolated
    Assert.Equal("mySprite", vm.SpriteName);
}

[Fact]
public void MainViewModel_ModifyingExportSettings_UpdatesUserPreferencesWithoutMutatingSpriteName()
{
    var vm = CreateMainViewModel();
    vm.SpriteName = "customShip";
    vm.ExportFormat = ExportFormat.MicroPython;
    vm.BytesPerLine = 12;

    // Trigger update
    var saved = UserPreferencesService.GetDefaultExportSettings();
    Assert.Equal(ExportFormat.MicroPython, saved.Format);
    Assert.Equal(12, saved.BytesPerLine);
    Assert.Equal("mySprite", saved.SpriteName);
}

[Fact]
public void MainViewModel_ResetExportSettings_RestoresDefaultsPreservingSpriteName()
{
    var vm = CreateMainViewModel();
    vm.SpriteName = "keepMyName";
    vm.ExportFormat = ExportFormat.RawHex;
    vm.BytesPerLine = 32;

    vm.ResetExportSettingsCommand.Execute(null);

    Assert.Equal(ExportFormat.AdafruitGfx, vm.ExportFormat);
    Assert.Equal(16, vm.BytesPerLine);
    Assert.Equal("keepMyName", vm.SpriteName);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter FullyQualifiedName~MainViewModelExportTests`
Expected: FAIL (`ResetExportSettingsCommand` does not exist; new document has default `AdafruitGfx` instead of `U8g2DrawBitmap`).

- [ ] **Step 3: Implement export preference inheritance, debounced persistence, and reset command**

1. In `MainViewModel.Export.cs`:
Initialize `_exportSettings` from `UserPreferencesService.GetDefaultExportSettings()`:
```csharp
private ExportSettings _exportSettings = UserPreferencesService.GetDefaultExportSettings();
```
In `SetExportSetting`:
Alongside `TriggerDebouncedUpdate()`, persist to `UserPreferencesService`:
```csharp
UserPreferencesService.Update(prefs =>
{
    var snapshot = _exportSettings.Clone();
    snapshot.SpriteName = "mySprite";
    prefs.DefaultExportSettings = snapshot;
});
```
Add RelayCommand `ResetExportSettingsCommand`:
```csharp
public IRelayCommand ResetExportSettingsCommand => new RelayCommand(ResetExportSettings);

public void ResetExportSettings()
{
    string currentName = SpriteName;
    UserPreferencesService.ResetDefaultExportSettings();
    ApplyExportSettings(UserPreferencesService.GetDefaultExportSettings());
    SpriteName = currentName;
}
```
2. In `ShellViewModel.CreateDocument`:
Seed new document from `UserPreferencesService.GetDefaultExportSettings()`.
When opening a document: if `loaded.ExportSettings != null`, apply document settings.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --filter FullyQualifiedName~MainViewModelExportTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/ViewModels/MainViewModel.Export.cs Hexprite/ViewModels/MainViewModel.cs Hexprite/ViewModels/ShellViewModel.cs Hexprite.Tests/ViewModels/MainViewModelExportTests.cs
git commit -m "feat: persist Code Generation export settings to UserPreferences with document isolation"
```

---

### Task 5: Integrate Image / GIF Export Persistence & Reset in `ExportDialog`

**Files:**
- Modify: `Hexprite/ViewModels/MainViewModel.cs:3400-3450`
- Modify: `Hexprite/Views/ExportDialog.xaml:170-200`
- Modify: `Hexprite/Views/ExportDialog.xaml.cs:20-50, 150-180`
- Modify: `Hexprite/ViewModels/SpriteSheetSlicerViewModel.cs:1610-1630`
- Test: `Hexprite.Tests/ViewModels/MainViewModelExportTests.cs`

**Interfaces:**
- Consumes: `UserPreferencesService.GetDefaultImageExportSettings()`, `UserPreferencesService.ResetDefaultImageExportSettings()`
- Produces:
  - `ExportDialog` pre-population from preferences
  - `ExportDialog` "Reset to Defaults" button
  - `UserPreferencesService.Update(...)` on image export confirmation

- [ ] **Step 1: Write the failing tests in `MainViewModelExportTests.cs`**

```csharp
[Fact]
public void ImageExport_PrepopulatesFromUserPreferences_AndSavesOnConfirm()
{
    UserPreferencesService.Update(p =>
    {
        p.DefaultImageExportSettings.Scale = 6;
        p.DefaultImageExportSettings.Format = ImageExportFormat.Bmp;
        p.DefaultImageExportSettings.GifFps = 20;
    });

    var vm = CreateMainViewModel();
    Assert.Null(vm.SpriteState.ImageExportSettings);

    // Initial resolution must yield saved preferences
    var initial = vm.SpriteState.ImageExportSettings 
        ?? UserPreferencesService.GetDefaultImageExportSettings();
    Assert.Equal(6, initial.Scale);
    Assert.Equal(ImageExportFormat.Bmp, initial.Format);
    Assert.Equal(20, initial.GifFps);
}
```

- [ ] **Step 2: Run test to verify it passes/fails**

Run: `dotnet test --filter FullyQualifiedName~ImageExport_PrepopulatesFromUserPreferences`
Expected: PASS (or fail if accessor behavior differs).

- [ ] **Step 3: Update `MainViewModel.cs` and `ExportDialog` UI**

1. In `MainViewModel.cs`:
```csharp
var settings = SpriteState.ImageExportSettings 
    ?? UserPreferencesService.GetDefaultImageExportSettings();

var newSettings = _dialogService.ShowExportImageDialog(settings, SpriteState);
if (newSettings != null)
{
    SpriteState.ImageExportSettings = newSettings;
    UserPreferencesService.Update(p => p.DefaultImageExportSettings = newSettings.Clone());
    ...
}
```
2. In `ExportDialog.xaml`:
Add a "Reset to Defaults" button in the bottom bar:
```xaml
<Button x:Name="BtnResetDefaults" Content="Reset to Defaults" 
        Click="BtnResetDefaults_Click" HorizontalAlignment="Left"
        Style="{StaticResource SubtleButtonStyle}" Margin="0,0,10,0"/>
```
3. In `ExportDialog.xaml.cs`:
```csharp
private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
{
    Result = new ImageExportSettings();
    LoadSettings(Result);
    UpdateContextualState();
    UpdatePreviewText();
}
```
4. In `SpriteSheetSlicerViewModel.cs`:
Use `UserPreferencesService.GetDefaultImageExportSettings()` when initializing image exports.

- [ ] **Step 4: Run tests to verify all tests pass**

Run: `dotnet test --filter FullyQualifiedName~MainViewModelExportTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/ViewModels/MainViewModel.cs Hexprite/Views/ExportDialog.xaml Hexprite/Views/ExportDialog.xaml.cs Hexprite/ViewModels/SpriteSheetSlicerViewModel.cs Hexprite.Tests/ViewModels/MainViewModelExportTests.cs
git commit -m "feat: persist Image and GIF export settings and add reset button in ExportDialog"
```

---

### Task 6: Add Reset Action to Code Export Sidebar & Full Test Suite Verification

**Files:**
- Modify: `Hexprite/Views/SidebarPanel.xaml:770-810`
- Test: Full solution regression test suite (`dotnet test`)

**Interfaces:**
- Consumes: `MainViewModel.ResetExportSettingsCommand`
- Produces: User-facing "Reset to Defaults" button in the Code Generation sidebar header

- [ ] **Step 1: Add Reset Button in `SidebarPanel.xaml`**

In `SidebarPanel.xaml`, inside the Code Generation expander header or top action bar:
Add a tooltip-enabled icon/button bound to `ResetExportSettingsCommand`:
```xaml
<Button Command="{Binding ResetExportSettingsCommand}"
        ToolTip="Reset Export Settings to Factory Defaults"
        Style="{StaticResource IconButtonStyle}"
        Width="22" Height="22" Margin="4,0,0,0">
    <Path Data="M12,5V1L7,6L12,11V7C15.31,7 18,9.69 18,13C18,16.31 15.31,19 12,19C8.69,19 6,16.31 6,13H4C4,17.42 7.58,21 12,21C16.42,21 20,17.42 20,13C20,8.58 16.42,5 12,5Z"
          Fill="{DynamicResource Brush.Foreground.Secondary}"
          Stretch="Uniform" Width="12" Height="12"/>
</Button>
```

- [ ] **Step 2: Run all tests in `Hexprite.Tests`**

Run: `dotnet test`
Expected: PASS across all 3,127+ tests (plus the new persistence tests).

- [ ] **Step 3: Commit**

```bash
git add Hexprite/Views/SidebarPanel.xaml
git commit -m "feat: add Reset to Defaults button to Code Generation export sidebar"
```
