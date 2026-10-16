# Changelog

All notable changes to **Hexprite** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [v0.4.0-beta] - 2026-10-16

### 🚀 Highlights & What's New

- **Delta Patches Animation Export (`DeltaPatches`)**:
  - Export only the pixels that change between animation frames instead of redrawing whole screens.
  - Drastically shrinks generated code size and saves microcontroller flash memory.
  - Includes ready-to-run sketches for both Adafruit GFX and U8g2 display libraries.

- **Non-Blocking Animation Timing**:
  - Added a non-blocking `millis()` timing option alongside standard `delay()`.
  - Animations play smoothly on Arduino and microcontrollers without freezing other tasks or sensor loops.

- **Vertical Sprite Sheets**:
  - Added vertical layout options for sprite sheets in addition to horizontal strips.

- **Unified Settings Persistence & Reset**:
  - Hexprite now automatically remembers your export formats, dimensions, and import presets across restarts.
  - Added a 1-click "Reset to Defaults" button in the Code Generation sidebar to restore original settings at any time.

- **Rock-Solid GIF Import**:
  - Fixed out-of-memory errors when importing large or long animated GIFs with safe compositing.
  - Automatically trims empty trailing frames when loading GIF files.

- **UI Polish & Smooth Loading**:
  - Added a native indeterminate progress bar and loading spinner that prevents UI freezes during exports and imports.
  - Cleaner sidebar controls and improved chip-style preset selectors.

- **Code Generation & Export Bug Fixes**:
  - Cleaned up syntax and frame rates for U8g2, Flipper Zero, MicroPython, and LVGL outputs.
  - Fixed XBM bit order and memory alignment edge cases.

---

## [v0.3.0-beta] - 2026-09-21

### 🚀 Highlights & Major Features

- **LiquidCrystal 1602/2004 Custom Character Subsystem (`createChar`)**:
  - **HD44780 5×8 Character Generation**: Added `ExportFormat.LiquidCrystalChar` supporting single-frame and multi-frame custom character generation for standard 1602 and 2004 LCD modules.
  - **Arduino Binary Literals**: Generated code emits `B00000`–`B11111` binary literals for intuitive visual representation of character pixels, with fallback to hex when configured.
  - **Multi-Frame CGRAM Bundling**: Automatically packages up to 8 animation frames across HD44780 CGRAM slots 0–7 with frame delay arrays and animation registration snippets.
  - **Full Arduino Sketch Generator**: Generates complete `.ino` sketches with `LiquidCrystal_I2C` initialization, character registration in `setup()`, and animation playback loops, including parallel wiring fallback comments.
  - **Direct Sketch Folder Export**: Added `ExportArduinoSketchFolderCommand` enabling 1-click export of complete `.ino` + `sprites.h` Arduino sketch bundles to disk.
  - **Bi-Directional Code Import Detection**: Extended `ImportFromCodeDetector` and `CodeGeneratorService` to detect, parse, and import LiquidCrystal binary byte arrays and multi-frame declarations.
  - **Binary Literal Syntax Highlighting**: Updated monospace code viewers to highlight Arduino `B[01]{5}` and `B[01]{8}` literals.
  - **CGRAM Memory Telemetry**: Export telemetry displays exact CGRAM byte consumption (`(CGRAM)`).

- **Hardened File I/O Architecture (`SafeFileIo`)**:
  - **Atomic File Operations**: Introduced `SafeFileIo` utility with staging files (`.tmp`) and atomic replacement, eliminating corrupted or truncated files on interrupted writes.
  - **Directory Creation & Read-Only Handling**: Automatically ensures parent directories exist and clears read-only attributes before writing.
  - **Transient Lock Retries**: Implemented a 3-stage exponential backoff retry loop to handle transient file locks from antivirus, cloud drives, and external IDEs.
  - **Path Traversal Protection**: Guarded font descriptor parsers (BMFont `.fnt`) against directory traversal attacks (`../`) when resolving referenced texture atlas images.
  - **Security & DoS Defenses**: Added a 10MB file size safety cap across image, font, and code import pipelines; clamped character code points (`0`..`65535`) and frame counts; sanitized animation and sprite identifiers.

- **Expanded Code Generation & Adaptive Microcontroller Sketch Engine**:
  - **Indexed 2D Array Format**: Added `ExportFormat.Indexed2D` emitting 2D matrix arrays (`name[HEIGHT][WIDTH]`) with ready-to-run C/C++ harnesses supporting both Arduino (`setup()`/`loop()`) and desktop C (`main()`) entry points. Exposed in format dropdowns across both `SidebarPanel.xaml` and `CodeOutputWindow.xaml`.
  - **Adaptive OLED Resolutions**: `BuildAdafruitGfxSketch` inspects canvas dimensions and adapts `SCREEN_HEIGHT` to 32 when sprite height is $\le 32$, halving buffer RAM allocation to 512 bytes and preventing ATmega328P SRAM exhaustion.
  - **Standalone Sketch Dimension Enforcement**: Enforced `{NAME}_WIDTH` and `{NAME}_HEIGHT` inclusion in standalone sketch headers regardless of user preferences, eliminating undefined identifier compile errors.
  - **Active Frame Export**: Single-frame sketch exports correctly extract the active visible frame composite rather than hardcoding frame 0.
  - **MicroPython Syntax Correction**: Fixed row comments in MicroPython exports to use `# row {N}` instead of `// row {N}`, resolving Python `SyntaxError` failures.
  - **Memory Target Breakdown & Savings**: Export status bar now displays target memory categories (`Flash (PROGMEM)`, `RAM/Flash`, `CGRAM`) and compression savings percentages.

- **Hardware Preview Studio Robustness & Auto-Detect Polish**:
  - **Flexible Pin Configurations**: Removed strict AVR I2C hardware pin checks from sketch generators, enabling software I2C and custom pin routing across all boards.
  - **Serial Receive Buffer Overflow Guard**: Added ring buffer capacity checks in the serial receive loop to prevent memory corruption on high-throughput or noisy links.
  - **Baud Rate Auto-Detection**: Reset connection states prior to baud probing, increased ACK handshake response tolerance to 600ms, and ensured baud rate restoration on failed probes.
  - **Scaled Frame Boundary Checks**: Frame dimension calculations now account for canvas scaling and display resolution limits during transmission.

- **UI Ergonomics & State Persistence**:
  - **Clean Borderless Panels**: Redesigned sidebar cards in `SidebarPanel.xaml` with flat, borderless styling, rounded corners, and consistent panel backgrounds (`Brush.Surface.Panel`).
  - **Separated Hardware Cards**: Split hardware configuration into dedicated "Display Mapping" and "Firmware & Sketch" cards with live target display resolution badges.
  - **Expander State Persistence**: All sidebar Expander expand/collapse states are now saved to `UserPreferencesService` and restored across application restarts.
  - **Resilient Clipboard Export**: Added retry loops for clipboard copying in `ClipboardService` and `CodeOutputWindow` to survive transient locks by external clipboard managers, displaying clear visual status (`⚠ Locked` / `✓ Copied!`).

---

## [v0.2.1-beta] - 2026-09-20

### 🚀 Highlights & Major Features

- **Interactive Hardware Preview Studio & Dynamic Wiring Configuration**:
  - **Visual Wiring Configuration Dialog**: Added [`HardwarePreviewWiringDialog.xaml`](file:///H:/dev/hexprite/Hexprite/Views/HardwarePreviewWiringDialog.xaml) with live pinout guidance, pin conflict validation, and support for both I2C and SPI buses.
  - **Built-In Microcontroller Presets**: Pre-configured board profiles with authentic default pinouts for Arduino Uno/Nano, ESP32 DevKit, ESP8266 NodeMCU, Raspberry Pi Pico (RP2040), STM32 BlackPill, Teensy 4.0, and Custom Board.
  - **Auto-Detect Baud Rate (`⚡`)**: Added one-click serial baud rate auto-detection with ACK handshaking across standard baud rates up to 1,000,000 baud.
  - **Dual Standalone Firmware Generators**: Added automated generation for standalone Arduino sketches (`.ino`) and complete PlatformIO projects (`platformio.ini` + `src/main.cpp`) matching active pinouts.
  - **RAM Buffer Overflow Guard**: Added pre-flight calculations evaluating canvas dimensions against MCU SRAM limits with helpful warning banners.
  - **Non-Blocking Asynchronous Serial Engine**: Refactored [`HardwarePreviewService`](file:///H:/dev/hexprite/Hexprite/Services/HardwarePreviewService.cs) with `SemaphoreSlim` synchronization, non-blocking reconfiguration loops, generation tracking, and thread-safe error recovery.
  - **Interactive Troubleshooting Guide**: Added contextual troubleshooting panel in the sidebar diagnosing disconnected ports, baud mismatches, I2C address conflicts, and pin wiring faults.

- **Canvas Selection & Layer Workflow Enhancements**:
  - **Reselect (`Ctrl+Shift+D`)**: Instantly restore the previous selection mask after deselecting or performing canvas edits.
  - **New Layer from Selection (`Ctrl+J` / `Ctrl+Shift+J`)**: Lift and copy selected pixels directly onto a brand new layer in a single command, preserving layer hierarchy and pixel alignment.
  - **Empty Selection Guardrails**: Actions on empty selection masks are intercepted with helpful status bar notifications.

- **External File Sync & Linked Source Hardening**:
  - **Multi-Document External Change Detection**: Per-document SHA256 hash tracking ensures external edits are detected accurately across multiple open tabs.
  - **Atomic Save Resilience**: Enhanced file watchers reliably handle atomic save cycles (file delete + recreate) used by modern IDEs.
  - **Hexadecimal Constant Support**: Parser supports hexadecimal constants (e.g., `0x80`, `0x40`) in C/C++, Python, and Rust headers without syntax errors.
  - **UTF-8 BOM Preservation**: Transparently preserves Byte Order Marks on external file sync.

- **Font Export Studio & Universal C Code Generator Subsystem**:
  - **Modern Card-Based `FontExportDialog`**: Replaced generic file picker workflows with an integrated export studio modal ([`FontExportDialog.xaml`](file:///H:/dev/hexprite/Hexprite/Views/FontExportDialog.xaml) & [`FontExportDialog.xaml.cs`](file:///H:/dev/hexprite/Hexprite/Views/FontExportDialog.xaml.cs)) featuring a custom title bar, real-time code generation preview with monospace syntax styling, quick format switching, one-click clipboard copying with animated visual confirmation, and direct file export with automatic extension and filter pairing.
  - **Universal Microcontroller Multi-Engine Support**: Full code generation pipeline across Adafruit GFX (`GFXfont`), LVGL v8/v9 (`lv_font_t` with `LV_FONT_DECLARE` declaration), u8g2 BDF (`.bdf`), Raw C Array (`uint8_t* const`), and Flipper Zero column-major bit-packed formats via [`FontCodeGeneratorService`](file:///H:/dev/hexprite/Hexprite/Services/FontCodeGeneratorService.cs).
  - **Deterministic Ascending Code Point Ordering**: Guaranteed strictly ascending code point sort order across all generated glyph records, ensuring embedded binary search routines (e.g. Adafruit GFX and LVGL cmap tables) execute correctly without runtime crashes.
  - **Identifier & Character Comment Sanitization**: Introduced [`SanitiseFontName`](file:///H:/dev/hexprite/Hexprite/Services/FontCodeGeneratorService.cs#L17-L22) to enforce valid C identifier rules (defaulting safely to `myFont`), and [`GetPrintableCharDisplay`](file:///H:/dev/hexprite/Hexprite/Services/FontCodeGeneratorService.cs#L28-L45) to safely escape control characters, spaces (`'space'`), tabs (`'\t'`), newlines (`'\n'`), and single quotes (`\'`) within C metric comment blocks.
  - **Sticky Export Directory Memory**: Maintained `InitialDirectory` state across consecutive export operations so subsequent exports open directly in the user's active project asset folder.

- **Modernized Font Import Studio UX**:
  - **Card-Based `FontImportDialog`**: Refactored [`FontImportDialog.xaml`](file:///H:/dev/hexprite/Hexprite/Views/FontImportDialog.xaml) and [`FontImportDialog.xaml.cs`](file:///H:/dev/hexprite/Hexprite/Views/FontImportDialog.xaml.cs) into a modern card layout with custom title bar, resizable container, and theme-adaptive styling.
  - **Drag-and-Drop & Interactive Visual Preview**: Added native drag-and-drop file ingestion for `.hexfont` and `.hexp` sprite documents, interactive pan-and-zoom glyph preview with preset scaling chips, font baseline and ascent guides, and live background inversion toggles.
  - **Resilient File Parsing & Validation**: Added robust validation error handling, clear contextual error banners, and defensive memory disposal on malformed files.

- **Font Editor Studio UX & Flat Minimalist Design**:
  - **Distraction-Free Flat Theme Styling**: Redesigned [`FontEditorPanel.xaml`](file:///H:/dev/hexprite/Hexprite/Views/FontEditorPanel.xaml) and sidebars with modern, borderless flat styles for `TextBox`, `Button`, and `ToggleButton` controls, removing visual noise and harsh wireframe borders—particularly in the Dim theme via [`Dim.xaml`](file:///H:/dev/hexprite/Hexprite/Themes/Dim.xaml).
  - **Category Filter Pills & Fast Glyph Search**: Introduced intuitive filter pills (*All*, *ASCII*, *Digits*, *Uppercase*, *Lowercase*, *Punctuation*, *Symbols*, *Custom*) and instant search with one-click clear for effortless navigation through large character maps.
  - **Rich Glyph List Cards & Canvas Controls**: Added miniature glyph thumbnail previews, hexadecimal code point badges (`0x41`), and custom edit status indicators in the glyph list; added SVG-based canvas nudge buttons (up/down/left/right) for precise 1px pixel shifting.
  - **Bresenham Drawing & Input Safety**: Implemented Bresenham's line algorithm for smooth continuous freehand drawing, bounded pointer clipping to prevent buffer corruption, and automatic drag state resets on canvas pointer exit or focus loss.

- **Drawing Engine & Brush Performance Optimization**:
  - **Brush Stamp Offset Caching**: Pre-calculated stamp coordinate lookup tables cached by `(size, shape, angle)`, eliminating per-pixel trigonometric math and memory allocations during brush stamping in [`DrawingService.cs`](file:///H:/dev/hexprite/Hexprite/Services/DrawingService.cs).
  - **Swept Capsule Line Rasterization**: Accelerated large brush stroke interpolation using swept capsule algorithms, ensuring smooth, anti-aliased, gap-free continuous strokes at high pointer velocities.
  - **Branchless Dithering & Direct Buffer Writes**: Optimized pattern dithering with branchless logic and added `SetPixelFast` in [`OverflowPixelBuffer.cs`](file:///H:/dev/hexprite/Hexprite/Core/OverflowPixelBuffer.cs) with dirty tracking, cutting redundant cache invalidations and memory overhead on canvas operations.
  - **Brush Outline Toggle & Pixel-Perfect Scoping**: Added user preference and UI toggle for brush outline visibility, persisted across sessions, and scoped pixel-perfect drawing strictly to 1px brush sizes in [`ToolInputController.cs`](file:///H:/dev/hexprite/Hexprite/Controllers/ToolInputController.cs).

- **Centralized Keyboard Shortcut Subsystem & Registry (Strangler Fig Architecture)**:
  - **Unified Shortcut Routing Engine**: Introduced [`HexpriteShortcutManager`](file:///H:/dev/hexprite/Hexprite/Services/HexpriteShortcutManager.cs) and [`IHexpriteShortcutManager`](file:///H:/dev/hexprite/Hexprite/Services/IHexpriteShortcutManager.cs), migrating all hardcoded procedural `PreviewKeyDown` handlers and XAML `Window.InputBindings` into a single, testable, dependency-injected subsystem.
  - **Scope-Based Preemption & Routing**: Implemented 3 distinct shortcut scopes (`EditorCanvas`, `Window`, `Global`). Keyboard shortcuts are now resolved in strict priority order, with intelligent context suppression when focused inside text input fields, modal dialogs, or secondary windows.
  - **Conflict-Free Context Awareness**:
    - *Timeline Focus Preemption*: Keystrokes (`Delete`, `Backspace`, `Ctrl+A`) are automatically bypassed when the `TimelinePanel` has focus, preventing canvas pixel manipulation from hijacking frame selection or deletion.
    - *Font Editor Zoom Scoping*: Canvas zoom shortcuts (`Ctrl++`, `Ctrl+-`, `Ctrl+0`) are scoped strictly to active sprite documents, allowing the embedded font editor to handle cell resizing without interference.
    - *Layer Conflict Hardening*: Strangled dangerous layer deletion on plain `Delete` keypresses, moving layer removal to explicit `Ctrl+Shift+Delete` to eliminate accidental layer loss while editing.
  - **Expanded Coverage to 63 Actions**: Added comprehensive shortcut support across all 16 editor tools (including Gradient and Elliptical Marquee), layer operations (Add `Ctrl+Shift+N`, Duplicate `Ctrl+J`, Rename `F2`, Delete `Ctrl+Shift+Delete`), animation frame insertion (`Alt+N`), canvas inversion (`Ctrl+I`), universal redo (`Ctrl+Shift+Z`), and scoped theme refreshing (`F5`).

- **Searchable Keyboard Shortcuts Cheat Sheet Dialog**:
  - **Interactive Modal & Hotkey**: Added [`KeyboardShortcutsDialog`](file:///H:/dev/hexprite/Hexprite/Views/KeyboardShortcutsDialog.xaml) and [`KeyboardShortcutsViewModel`](file:///H:/dev/hexprite/Hexprite/ViewModels/KeyboardShortcutsViewModel.cs), accessible globally via `Ctrl+/` (or `Ctrl+NumPad/`) and the Help menu.
  - **Live Search & Category Grouping**: Instant real-time filtering across all registered shortcuts, grouped into intuitive categories (Tools, Canvas & Editing, Selection, Layers, Animation, View & Zoom, Hardware & Display, System & Files) with modern badge-styled key combinations (`KbdBadge`).
  - **Automated Metadata Parity Enforcement**: Implemented bidirectional automated parity tests ensuring 100% synchronization between active registered shortcuts and cheat sheet documentation metadata.

- **Display Simulation Studio Enhancements & White OLED Preset**:
  - **New Display Preset (`Ssd1306OledWhite`)**: Added native simulation preset for white SSD1306 OLED displays with specialized bloom, blur, and diode diffusion profiles.
  - **Theme-Immune Hardware Palettes**: Refactored the display simulation rendering pipeline to use hardcoded hardware-accurate color profiles, decoupling simulated LCD/OLED displays from the application's active UI theme.
  - **Studio UX & Rendering Polish**: Rebuilt [`DisplaySimulationWindow`](file:///H:/dev/hexprite/Hexprite/Views/DisplaySimulationWindow.xaml) with single-instance enforcement (`ShowOrActivate`), mouse middle-click panning, status bar canvas telemetry (zoom level & dimensions), thread-safe asynchronous rendering cancellation, and a 1-click "Copy Image" clipboard exporter.
  - **E-Paper & Flipper Zero Simulation Fidelity**: Added capsule clustering simulation and warmer color balance for e-paper displays; added realistic backlight gradients, drop shadows, and authentic pixel grid spacing for Flipper Zero LCD simulations.

- **Selection Tool Hardening & Drag Cancellation**:
  - **Non-Destructive Drag Cancellation**: Pressing `Escape` or right-clicking during an active selection transform/drag now aborts the transformation while cleanly preserving the original base selection.
  - **Transform Dimension Clamping**: Clamped selection transformation matrices and scaling bounds to prevent out-of-memory errors on extreme resize maneuvers.
  - **State & Lifecycle Resilience**: Introduced `ResetControllerState` for deterministic lifecycle teardowns, hardened lasso hit-testing, and encapsulated rotation and resampling algorithms.

- **Large File Syntax Highlighting & Code Output Protection**:
  - **Buffer Truncation Defense**: Introduced `MaxSpansToFormat` and `MaxCharactersToFormat` safety thresholds in [`SyntaxHighlightBox`](file:///H:/dev/hexprite/Hexprite/Rendering/SyntaxHighlightBox.cs) to prevent UI thread starvation and out-of-memory crashes on massive exported code files.
  - **Tokenization Performance**: Optimized [`SidebarPanel.TokenizeCode`](file:///H:/dev/hexprite/Hexprite/Views/SidebarPanel.xaml.cs) with early-exit conditions and zero-allocation merging of consecutive numeric and string literals across array punctuation.
  - **Lazy Text Population**: Made [`CodeOutputWindow`](file:///H:/dev/hexprite/Hexprite/Views/CodeOutputWindow.xaml.cs) lazily populate selectable text blocks only when requested.

- **Animation Timeline Visual Consistency**:
  - Replaced custom drawing brush checkerboards with `Brush.Surface.Panel` in [`TimelinePanel.xaml`](file:///H:/dev/hexprite/Hexprite/Views/TimelinePanel.xaml) for seamless theme integration.
  - Unified playhead borders and icons to use `Brush.Accent.Base` during active playback.

---

### 📋 Detailed Change Log by Component

#### 🔤 Font Editor Studio & Code Generation
- **`FontExportDialog` & `FontExportSettings`**:
  - Created modern modal export studio with custom title bar, code generation preview, and live syntax formatting.
  - Integrated copy-to-clipboard button with visual checkmark notification timer.
  - Added save-to-file dialog with intelligent format extension filters (`.h`, `.c`, `.bdf`).
  - Added `InitialDirectory` property ensuring sequential saves default to the last chosen export path.
  - Bound format switching to dynamic engine usage snippets (`LV_FONT_DECLARE`, `u8g2.setFont`, `display.setFont`).
  - Added UI toggles for ASCII glyph previews, metric comments, and uppercase/lowercase hex casing.
- **`FontImportDialog`**:
  - Overhauled dialog with modern card layout, custom resizable window chrome, and Dim/Dark styling.
  - Added drag-and-drop file ingestion support for `.hexfont` and `.hexp` documents.
  - Added live preview viewport with zoom controls (1x–8x), size preset chips, invert background toggle, and baseline/ascent line guides.
  - Hardened error handling and added user-facing error indicators for invalid or corrupt font files.
- **`FontCodeGeneratorService` & `IFontCodeGeneratorService`**:
  - Implemented multi-format C code generation for Adafruit GFX, LVGL (sparse and contiguous cmaps), u8g2 BDF, Raw C Array, and Flipper Zero.
  - Enforced ascending code point sorting on all glyph collections prior to code generation.
  - Added [`SanitiseFontName`](file:///H:/dev/hexprite/Hexprite/Services/FontCodeGeneratorService.cs#L17-L22) to enforce C/C++ identifier rules, defaulting empty or invalid strings to `myFont`.
  - Added [`GetPrintableCharDisplay`](file:///H:/dev/hexprite/Hexprite/Services/FontCodeGeneratorService.cs#L28-L45) to safely encode non-printable, whitespace, and control characters in metric comments.
  - Updated LVGL export integration snippet to recommend `LV_FONT_DECLARE(font_name)`.
- **`FontEditorPanel` & `FontViewModel`**:
  - Applied flat minimalist styling to `FontEditorPanel.xaml`, removing redundant borders in the Dim theme.
  - Added category filter pills (*All*, *ASCII*, *Digits*, *Uppercase*, *Lowercase*, *Punctuation*, *Symbols*, *Custom*).
  - Added real-time glyph search box with one-click clear button.
  - Enhanced glyph list cards with mini preview bitmaps, hex code labels, and custom modification indicators.
  - Added SVG nudge buttons (up/down/left/right) for 1px canvas pixel adjustments.
  - Added live flash memory calculation and export format selection in property cards.
  - Enforced stride-preserving resizing and collection bounds protection in `FontDocument` and `GlyphState`.
  - Switched freehand drawing to Bresenham's line algorithm with automatic drag reset on canvas mouse leave.
  - Hardened atomic autosave pipeline with cancellation token support.

#### 🎨 Drawing Engine & Brush Performance
- **`DrawingService`**:
  - Added stamp offset caching keyed by `(size, shape, angle)`, eliminating per-pixel trigonometric calculations on repetitive brush stamps.
  - Implemented swept capsule line rasterization for large brush strokes, preventing gaps and stepping artifacts during fast pointer movement.
  - Inlined hot drawing loops and converted dither calculations to branchless bitwise logic.
  - Refined hardware preview dirty region invalidation and throttle timing.
- **`OverflowPixelBuffer`**:
  - Added `SetPixelFast` for direct writes bypassing redundant cache invalidations.
  - Added dirty flag tracking and pre-allocated buffer margins, eliminating memory allocations on repeated buffer reads.
- **`ToolInputController`**:
  - Throttled input events with `DispatcherTimer` for responsive UI rendering under heavy drawing loads.
  - Scoped pixel-perfect mode strictly to 1px brush sizes.
  - Added brush outline visibility preference and toggle, persisted across application launches.

#### 🖼️ UI Theme & Styling Refinements
- **Flat Control Styling**:
  - Introduced flat styles for `TextBox`, `Button`, and `ToggleButton` in [`FontEditorPanel.xaml`](file:///H:/dev/hexprite/Hexprite/Views/FontEditorPanel.xaml), removing wireframe outlines for a modern appearance.
  - Removed borders from sidebar `<Border>` containers for a seamless flat layout.
  - Harmonized border color tokens in [`Dim.xaml`](file:///H:/dev/hexprite/Hexprite/Themes/Dim.xaml) (`Palette.Border.Base`, `Palette.Border.Separator`, `Palette.Border.StatusBar`).

#### ⌨️ Keyboard Shortcut Subsystem & Routing
- **`HexpriteShortcutManager` & `IHexpriteShortcutManager`**:
  - Created singleton shortcut manager hooking WPF `InputManager.Current.PreProcessInput`.
  - Implemented `ShortcutScope` hierarchy (`EditorCanvas` $\to$ `Window` $\to$ `Global`).
  - Added smart focus detection suppressing shortcuts in `TextBox`, `PasswordBox`, and editable `ComboBox` controls.
  - Added secondary window and modal dialog awareness to prevent main window shortcuts from intercepting child dialog keys.
  - Added `TimelineFocusPredicate` to dynamically bypass canvas shortcuts when the animation timeline has input focus.
  - Added tracer bullet diagnostic shortcut (`Ctrl+Alt+Shift+F12`) for runtime health verification.
- **Registrar Architecture**:
  - Created [`EditorCanvasShortcutRegistrar`](file:///H:/dev/hexprite/Hexprite/Services/EditorCanvasShortcutRegistrar.cs) registering all 16 tool hotkeys, canvas navigation, grid shifting, and animation playback.
  - Created [`WindowShortcutRegistrar`](file:///H:/dev/hexprite/Hexprite/Services/WindowShortcutRegistrar.cs) registering file, document, layer, view, and dialog operations.
  - Removed procedural `PreviewKeyDown` key-dispatch switches from [`MainWindow.xaml.cs`](file:///H:/dev/hexprite/Hexprite/MainWindow.xaml.cs) and cleaned up redundant XAML `Window.InputBindings`.
  - Removed duplicate startup registration calls in [`App.xaml.cs`](file:///H:/dev/hexprite/Hexprite/App.xaml.cs).

#### 📖 Keyboard Shortcuts Dialog & Cheat Sheet
- **`KeyboardShortcutsDialog` & `KeyboardShortcutsViewModel`**:
  - Created categorized modal dialog displaying all 63 registered actions.
  - Implemented real-time search filtering across action names, descriptions, categories, and key combinations.
  - Added [`BooleanToInverseVisibilityConverter`](file:///H:/dev/hexprite/Hexprite/Converters/BooleanToInverseVisibilityConverter.cs) for dynamic empty search result banners.
  - Synchronized menu `InputGestureText` and tooltips across `MainWindow.xaml` and `LayersPanel.xaml`.
  - Added automated parity test validating 1:1 bidirectional mapping between registered shortcuts and metadata entries.

#### 🖥️ Display Simulation Studio
- **Display Presets & Palettes**:
  - Added `Ssd1306OledWhite` enum value and simulation profile.
  - Removed subpixel RGB striping on monochrome OLED displays for true black-and-white rendering.
  - Hardcoded canonical display palettes in [`DisplaySimulationRenderer.cs`](file:///H:/dev/hexprite/Hexprite/Rendering/DisplaySimulationRenderer.cs) to ensure hardware preview accuracy regardless of active app theme.
  - Enhanced e-paper capsule clustering and Flipper Zero backlight gradients.
- **Window Architecture & Controls**:
  - Implemented `ShowOrActivate` single-instance window lifecycle.
  - Added mouse middle-click canvas panning and keyboard navigation.
  - Added "Copy Image" button with visual feedback timer.
  - Integrated `CancellationTokenSource` for thread-safe asynchronous rendering cancellation on rapid slider/tab updates.

#### 🎯 Selection Tools & Canvas Safety
- **`SelectionService` & `SelectionInputController`**:
  - Added drag cancellation preserving base selection on `Escape` or right-click.
  - Added `ResetControllerState` to clear ephemeral state on tool switches or canvas loss of focus.
  - Clamped selection scaling dimensions to guard against arithmetic overflow and out-of-memory allocations.
  - Hardened lasso lifecycle, floating selection hit-testing, and internal resampling routines.

#### ⚡ Syntax Highlighting & Performance
- **`SyntaxHighlightBox` & `SidebarPanel`**:
  - Added `MaxSpansToFormat` and `MaxCharactersToFormat` safety clamping.
  - Optimized tokenizer with early exit and consolidated literal token merging.
  - Made `CodeOutputWindow` selectable text generation lazy.
  - Added `SyntaxHighlightingStressTests` verifying UI responsiveness under extreme code payload volumes.

#### ⏱️ Timeline & UI Polish
- **`TimelinePanel.xaml`**:
  - Replaced thumbnail checkerboard with `Brush.Surface.Panel`.
  - Updated playback playhead indicator to `Brush.Accent.Base`.

---

### 🧪 Test Coverage & Verification

Over **2,948 automated tests** passing with 0 warnings (+129 new tests):
- **`HardwarePreviewServiceTests` & `HardwarePreviewPersistenceTests`**: Asynchronous port reconfiguration, baud rate auto-detection, streaming/ACK protocol handshakes, watchdog timeouts, and thread-safe cancellation.
- **`HardwarePreviewWiringConfigTests`**: Pin validation, SPI pin uniqueness, platform-specific I2C mappings, board preset switching, and sketch code generation.
- **`SelectionHardeningTests`**: Reselect, New Layer from Selection, empty selection guardrails, transform dimension clamping, and non-destructive drag cancellation.
- **`LinkedSourceHardeningTests` & `LinkedSourceRegressionTests`**: Multi-document file synchronization, atomic save recovery, hexadecimal dimension constants, and UTF-8 BOM preservation.
- **`MainViewModelAnimationTests`**: Playback direction serialization, PingPong bounds clamping, and frame duplication integrity.
- **`FontCodeGeneratorCorrectnessTests`** (13 tests): Adafruit GFX, LVGL, u8g2 BDF, Raw C Array, and Flipper Zero generation correctness, sorted code point guarantees, character escape handling, and comment syntax.
- **`FontExportDialogTests`** (12 tests): Dialog initialization, format selection, live preview updating, snippet generation, hex casing, initial directory retention, and sanitization.
- **`FontImportDialogTests`**: Import validation, file drag-and-drop, zoom controls, and format error handling.
- **`FontEditorPanelTests` & `FontViewModelTests`**: Glyph filtering, category filtering, search, undo/redo stacks, and property notifications.
- **`DrawingServiceTests` & `PerformanceAllocationTests`**: Brush stamping allocation budgets, branchless dither correctness, swept capsule performance, and pixel buffer throughput.
- **`HexpriteShortcutManagerTests`** (108 unit & integration tests): Routing hierarchy, scope preemption, timeline focus bypass, text box suppression, secondary window isolation, duplicate collision prevention, tracer bullet, and 16-tool registry coverage.
- **`KeyboardShortcutsViewModelTests`** (15 tests): Category grouping, live search filtering, clear search, and bidirectional ActionId parity enforcement.
- **`DisplaySimulationWindowTests`**: Single-instance lifecycle, hardware palette isolation, white OLED preset, zoom/pan bounds, and thread-safe cancellation.
- **`SyntaxHighlightingStressTests`**: Large export file stress tests, character/span threshold clamping, and lazy text population.

---

## [v0.2.0-beta] - 2026-09-05

### 🚀 Highlights & Major Features

- **Animation Timeline Studio & Playback Engine**:
  - **Zero-Flicker In-Place Reconciliation**: Overhauled [`RebuildFrameViewModels()`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.HardwarePreview.cs) with a 3-phase reconciliation pattern (in-place update, append, trim) using [`SyncFrameViewModel`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.HardwarePreview.cs). Completely eliminated destructive `Frames.Clear()` resets and visual container teardowns when adding, deleting, duplicating, or reordering frames.
  - **Redesigned Frame Thumbnail Cards (70×80)**: Structured 3-row layout with distinct visual state hierarchy (Active with 2px indigo border, Selected multi-select tint, Hover elevation, and Playing amber playhead glow), embedded 10×10 transparency checkerboard, delay multiplier badge pills (`⏱ 2x`), and empty frame ghost indicators.
  - **Interactive Inline "Add Frame" (+) Action Card**: Interactive ghost action card appended directly to the end of the frame strip for immediate 1-click frame creation without toolbar travel.
  - **3-Zone Responsive Toolbar with Tabular Centering**: Balanced 3-zone layout (Status, Transport, Actions). Features a fixed 100px summary pill with tabular numeral alignment (`Typography.NumeralAlignment="Tabular"`) that completely eliminates horizontal layout shift/jitter during playback and frame stepping.
  - **Batch ToggleButton Interaction**: Refactored the Batch button to a `ToggleButton` with `ClickMode="Press"`, `Placement="Bottom"` ContextMenu, and automatic uncheck on menu closure, enabling intuitive open/close dropdown toggling without mouse-capture trapping.
  - **Live Playhead Tracking & Auto-Scroll Navigation**: Real-time [`IsPlayingBack`](file:///H:/dev/hexprite/Hexprite/ViewModels/FrameItemViewModel.cs) binding with amber playhead indicator; automatic `ScrollIntoView` keeping active frames centered; dedicated Next/Previous Frame commands with toolbar buttons and keyboard shortcuts (`,` and `.`).
  - **Collapsible Compact Mode for Static Sprites**: When animation is disabled, the frame strip automatically collapses to a minimal 34px bar, reclaiming **~106px of vertical drawing canvas space**.
  - **Frame Delay Multiplier Presets & History**: Context menu presets (`1x` through `10x`) and stepping with full undo/redo snapshotting and dirty state tracking via [`SetFrameDelayCommand`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.cs).
  - **Engine Safety & Accurate Footprint Calculation**: Clamped timer interval index calculations preventing `ArgumentOutOfRangeException` during live playback mutations; preserved animation mode when deleting down to 1 frame; and modernized `EstimatedMemoryUsage` to accurately calculate 32-bit ARGB footprints (`Width * Height * 4 bytes`) vs 1-bit monochrome, removing misleading memory UI clutter.

- **Flipper Zero Studio Suite**:
  - **Asset Pack & Animation Manifest Studio**: Complete `.hexpack` project architecture, native import/export of Flipper animation manifests (`meta.txt` and animation directory trees), support for both Stock (30 levels) and Extended (100 levels) modes, bulk level/mood range shifting, and automatic matrix balancing across Happy/Neutral/Sad mood tiers.
  - **Flipper Dolphin Schedule Matrix Editor**: Interactive schedule matrix grid with visual stage indicators, cell status badges, animation previewing, cell validation, undo/redo, region inspect mode, and virtualized high-performance rendering.
  - **Flipper Matrix & Mood Simulator**: Embedded virtual Flipper Zero device emulator with live animation playback, interactive mood transitions, HUD overlay (`FlipperHudRenderer`), drag-and-drop speech bubble positioning with tail knockout clipping, and selectable LCD color palettes (Orange Backlit, White Backlit, High Contrast, Custom LCDs).
  - **Flipper Screen Mirror & Virtual LCD**: Real-time USB serial screen streaming and virtual LCD mirroring, auto-reconnect logic, zero-allocation frame normalization to 128×64, snapshot captures, frame dumping, speed multiplier chips, and pause/resume controls.
  - **Flipper USB Deployer**: Direct one-click USB deployment to connected Flipper Zero devices via serial/RPC (`FlipperUsbDeployer`), micro-SD directory verification, 512-byte binary-safe chunked upload, post-upload verification, and real-time transfer progress.

- **Sprite Sheet & Atlas Slicer Studio**:
  - Comprehensive interactive slicing studio ([`SpriteSheetSlicerWindow`](file:///H:/dev/hexprite/Hexprite/Views/SpriteSheetSlicerWindow.xaml)) featuring uniform grid slicing, custom margins/padding, and intelligent Alpha Island (auto-detect sprite boundaries) detection.
  - Smooth pan-and-zoom interactive preview pane with pixel grid snapping and reset capabilities.
  - Direct drag-and-drop source image loading and file-based slicing workflows.
  - Consolidated export dropdown menu to individual frames, animated sequences, C/C++ source code arrays, XBM bitmaps, animated GIFs, or multi-frame `.hexp` documents.

- **Flipper Media & Animation Slicer**:
  - Dedicated video and animated GIF slicing studio ([`FlipperMediaSlicerWindow`](file:///H:/dev/hexprite/Hexprite/Views/FlipperMediaSlicerWindow.xaml)) designed for converting animations directly into Flipper Zero-compatible frame sequences.
  - Temporal subsampling, frame-rate control, thresholding, and dithering modes tailored for 128×64 monochrome displays.

- **Theme System, High-DPI & Workspace Polish**:
  - Added `Canvas.Workspace` and `Surface.Elevated` semantic color and brush resources across Dark, Dim, Flipper, and Light themes.
  - Unified panel and ScrollViewer backgrounds using `Brush.Surface.Base`.
  - Raised Layers panel minimum width from 100px to **150px** in both UI and view models with layout bounds persistence clamping.
  - Resolved 8px `WindowChrome` overhang on maximized dialogs via automated triggers in [`Styles.xaml`](file:///H:/dev/hexprite/Hexprite/Themes/Styles.xaml), enabled `UseLayoutRounding` and `SnapsToDevicePixels` across all dialog and companion windows, and eliminated button clipping on high-DPI scaling.
  - Right sidebar panel width clamped to an ergonomic default width of 300px with min/max width constraints (240px–460px) and flexible layout handling.

- **Core Engine, Concurrency & Persistence Resilience**:
  - **Optimized Pixel Storage & GZip Compression**: Highly optimized `.hexp` document format using bit-packed Base64 pixel serialization and transparent GZip stream compression, drastically reducing project file sizes while maintaining backwards compatibility.
  - **Unified Autosave Envelope & Crash Recovery**: Unified autosave envelope across standard canvases and asset pack projects. Automatic crash recovery prompts on launch with automatic timestamp preservation and stale autosave cleanup on deliberate user dismissal.
  - **File Watcher Concurrency Hardening**: Thread-safe file watcher change handling with 64 KB buffer size and optimized regex scanners for syntax highlighters.

---

### 📋 Detailed Change Log by Component

#### ⏱️ Animation & Timeline
- **Zero-Flicker Collection Reconciliation**:
  - Implemented [`SyncFrameViewModel`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.HardwarePreview.cs) to mutate existing `FrameItemViewModel` properties in-place without triggering collection resets.
  - Refactored [`RebuildFrameViewModels`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.HardwarePreview.cs) to use a 3-phase reconciliation pattern (in-place sync of overlap items, targeted append for new frames, targeted trim for removed frames).
- **Frame Strip Cards & Layout**:
  - Expanded card dimensions to 70×80 with 3-row vertical card layout in [`TimelinePanel.xaml`](file:///H:/dev/hexprite/Hexprite/Views/TimelinePanel.xaml).
  - Added 10×10 tiling transparency checkerboard (`TimelineCheckerboardBrush`).
  - Added distinct visual state brushes for Hover, Multi-Selection, Active Canvas Focus (2px indigo border), and Playhead (glowing amber border).
  - Appended interactive ghost card at the end of the frame strip bound to `AddFrameCommand`.
  - Added collapsible compact mode collapsing `FrameListClipHost` when `IsAnimationEnabled == false`.
- **Toolbar & Batch Interactions**:
  - Reorganized toolbar into 3-zone grid with responsive spacing.
  - Added fixed 100px summary pill with tabular numerals to eliminate horizontal layout jitter during stepping or playback.
  - Refactored Batch button to `ToggleButton` with `ClickMode="Press"`, `Placement="Bottom"` ContextMenu, and automatic uncheck on menu closure.
  - Set Enter key handling on FPS input box to commit frame rate changes.
- **Commands & Navigation**:
  - Added `NextFrameCommand` and `PreviousFrameCommand` with toolbar buttons and keyboard shortcuts (`,` and `.`).
  - Refactored `DuplicateFrameCommand` and `DeleteFrameCommand` to accept explicit `targetFrame` parameters from context menus.
  - Added `FrameList_PreviewMouseRightButtonDown` in [`TimelinePanel.xaml.cs`](file:///H:/dev/hexprite/Hexprite/Views/TimelinePanel.xaml.cs) to auto-select right-clicked items while preserving multi-selections.
  - Added [`SetFrameDelayCommand`](file:///H:/dev/hexprite/Hexprite/ViewModels/MainViewModel.cs) with quick multiplier presets (`1x`–`10x`) and full undo/redo history.
  - Preserved animation mode when deleting down to 1 frame.
- **Engine & Safety**:
  - Added [`IsPlayingBack`](file:///H:/dev/hexprite/Hexprite/ViewModels/FrameItemViewModel.cs) observable property and synchronized it on playback timer ticks.
  - Added auto-scroll via `FrameList.ScrollIntoView(activeItem)` on selection change.
  - Clamped playback index in `UpdatePlaybackTimerIntervalForCurrentFrame` to prevent out-of-bounds crashes during live deletions.
  - Implemented `InvalidatePlaybackFrame` to refresh playback caches immediately when editing the canvas during live playback.
  - Modernized `EstimatedMemoryUsage` for both monochrome (1-bit packed) and RGB (32-bit ARGB) modes, and removed clutter from the timeline toolbar.

#### 🐬 Flipper Zero Integration
- **Asset Pack Management**:
  - Implemented [`FlipperPackArchiverService`](file:///H:/dev/hexprite/Hexprite/Services/FlipperPackArchiverService.cs) supporting `.hexpack` archive packaging, extraction, and validation.
  - Implemented [`FlipperImportService`](file:///H:/dev/hexprite/Hexprite/Services/FlipperImportService.cs) and [`FlipperExportService`](file:///H:/dev/hexprite/Hexprite/Services/FlipperExportService.cs) with robust culture-invariant parsing and manifest formatting.
  - Added support for lossless Stock/Extended mode toggling for Flipper levels (1–30 vs 1–100).
  - Added Region Inspect Mode to Flipper matrix editor for inspecting active animation ranges and stage boundaries.
- **Hardware Communication & Simulation**:
  - [`FlipperUsbDeployer`](file:///H:/dev/hexprite/Hexprite/Services/FlipperUsbDeployer.cs): Added USB serial connection detection, auto-handshake, SD card directory checking, 512-byte binary-safe chunked manifest and asset transmission, and post-upload verification.
  - [`FlipperScreenStreamService`](file:///H:/dev/hexprite/Hexprite/Services/FlipperScreenStreamService.cs): Implemented zero-allocation byte normalization to 128×64, automated reconnect on disconnect, and frame buffer caching.
  - [`FlipperMatrixSimulatorWindow`](file:///H:/dev/hexprite/Hexprite/Views/FlipperMatrixSimulatorWindow.xaml): Added multi-palette rendering, `FlipperHudRenderer` overlay, speech bubble drag-and-drop, and tail knockout clipping.

#### ✂️ Slicers & Companion Tools
- **`SpriteSheetSlicerWindow` & `SpriteSheetSlicerViewModel`**:
  - Added auto-detect sprites using alpha island flood fill and bounding box detection.
  - Added pan-and-zoom preview box with mouse wheel zooming, middle-click panning, and zoom reset buttons.
  - Added dropdown export menu consolidating frame exports, animation exports, and C/C++ code generation.
  - Added full keyboard shortcuts and help overlay for slicer navigation.
- **`FlipperMediaSlicerWindow` & `FlipperMediaSlicerViewModel`**:
  - Added GIF and media frame extraction with preview scrubbing, start/end trimming, and playback controls.
  - Added 1-bit monochrome thresholding and dithering adjustments with instant preview update.

#### 🖌️ Canvas, Layers & Documents
- **Layer Synchronization & Dimensions**:
  - Increased `LayersColumn` `MinWidth` from 100 to 150 in [`MainWindow.xaml`](file:///H:/dev/hexprite/Hexprite/MainWindow.xaml) and [`ShellViewModel.cs`](file:///H:/dev/hexprite/Hexprite/ViewModels/ShellViewModel.cs) to prevent control compression.
  - Centralized layer item index resolution into `ResolveLayerItemIndex` in [`LayersPanel.xaml.cs`](file:///H:/dev/hexprite/Hexprite/Views/LayersPanel.xaml.cs).
  - Ensured frame thumbnails update dynamically when layer visibility changes.
  - Hardened layer index resolution logic during reordering, merging, and deletions.
- **Document Management**:
  - Implemented bit-packed Base64 serialization for monochrome and indexed frames, slashing `.hexp` storage overhead.
  - Integrated transparent GZip read/write streams in document persistence pipelines.
  - Synchronized document dirty states via `DocumentModified` events.
  - Ensured unique, deduplicated tab names and synchronized tab titles with file save paths.

#### 🖥️ UI, Styling & Window Resilience
- **WindowChrome Overhang Mitigation**:
  - Added `WindowState == Maximized` trigger in `DialogWindowBorderStyle` (`Hexprite/Themes/Styles.xaml`) applying an 8px padding and 0px border to counter the WPF `WindowChrome` maximized client overhang.
- **High-DPI Alignment**:
  - Enabled `UseLayoutRounding="True"` and `SnapsToDevicePixels="True"` on all dialog and tool windows (`FlipperMediaSlicerWindow`, `SpriteSheetSlicerWindow`, `FlipperScreenMirrorWindow`, `CodeOutputWindow`, `DisplaySimulationWindow`, `ImportFromCodeDialog`, `ImportFromFileDialog`).
  - Added explicit zero-vertical padding (`Padding="12,0"`) and minimum heights to dialog Close buttons to prevent button truncation at any display DPI scale.
- **Theme Palettes & Workspace Backgrounds**:
  - Added `Canvas.Workspace` and `Surface.Elevated` brush definitions across Dark, Dim, Flipper, and Light themes.
  - Unified `CanvasPanel`, `DisplaySimulationWindow`, and `FontEditorPanel` backgrounds to use `Brush.Canvas.Workspace` and `Brush.Surface.Base`.
- **Workspace Layout**:
  - Adjusted right sidebar panel default width to 300px with min/max width clamping (240px–460px) to optimize canvas viewport space on laptop and 1080p displays.

#### ⚙️ Concurrency & Resilience
- **File Watcher & Scanner Concurrency**:
  - Hardened `FileSystemWatcher` concurrency with 64 KB buffer size and non-locking file sharing.
  - Optimized regex scanners in syntax highlighters.
  - Hardened `MainWindow` XAML converters and decoupled experimental Community Hub code from the release branch.
- **Test Infrastructure Isolation**:
  - Refactored autosave test suites to run against isolated temporary directories (`AutosaveService.SetCustomAutosaveDirectory`), preventing developer environment pollution.

---

### 🧪 Test Coverage & Verification

Over **2,367 automated tests** passing with 0 warnings (100% pass rate):
- **`MainViewModelAnimationTests`**: Tests verifying zero-flicker in-place `Frames` collection updates (no `Reset` action on Add/Delete, reference equality preservation), playback playhead state (`IsPlayingBack`), frame delay presets and undo, memory usage calculations (monochrome vs RGB), out-of-bounds playback safety, and targeted context menu frame deletion/duplication.
- **`TimelinePanelTests`**: Tests covering mouse wheel horizontal scrolling, Batch ToggleButton open/close state transitions, and ContextMenu interactions.
- **`CanvasThemeContrastTests`**: Theme key completeness, color contrast verification across Dark, Dim, Flipper, and Light palettes, and viewport background consistency.
- **`ShellViewModelWindowLayoutPersistenceTests`**: Bounds clamping tests verifying undersized layer panel widths (< 150px) and sidebar widths (< 240px) clamp gracefully without throwing.
- **`WindowChromeConsistencyTests`**: 6 unit tests verifying `DialogWindowBorderStyle` maximized overhang triggers, layout rounding, device pixel snapping, and Close button heights/padding across all companion windows.
- **`WindowLaunchIntegrityTests`**: Launch and rendering integrity across all companion windows and dialogs.
- **`SpriteSheetSlicerTests`**: Comprehensive unit and integration tests covering grid slicing, alpha island detection, zoom/pan bounds, and multi-format exports.
- **`FlipperAssetPackTests` & `FlipperScheduleMatrixTests`**: Tests for matrix balancing, stock/extended level transitions, manifest parsing, and culture invariance.
- **`FlipperSimulatorTests`**: Tests for mood state machines, HUD overlay rendering, and speech bubble coordinate calculations.
- **`AutosaveRecoveryServiceTests`**: Tests verifying isolated temporary directories, atomic file writes, corrupt envelope handling, and user prompt dismissals.
- **`SafeFileIoTests`**: Atomic replace, backup file creation, and disk cache flush validation.

---

## [v0.1.0-beta] - 2026-08-22

### 🚀 Highlights & Major Features

- **Hardened GIF & Bitmap Export Engine**:
  - Rebuilt [`GifEncoder`](file:///H:/dev/hexprite/Hexprite/Services/GifEncoder.cs) with `IDisposable` support, preallocated LZW compression tables (zero per-frame heap allocation overhead), and browser-safe frame delay clamping ($[1, 65535]$ centiseconds).
  - Implemented [`ColorQuantizer`](file:///H:/dev/hexprite/Hexprite/Services/ColorQuantizer.cs) supporting exact lossless palette reduction and Median-Cut quantization with 2D Floyd-Steinberg error diffusion dithering.
  - Added sub-rectangle delta-frame compression (`Disposal Method 1`), reducing animation GIF file sizes by up to 50%–80%.
  - Added explicit background transparency toggle (Transparent vs Opaque Theme Background) and dynamic 3-color grid overlay rendering.
  - Refined [`ExportDialog`](file:///H:/dev/hexprite/Hexprite/Views/ExportDialog.xaml) with instant auto-focus on custom scales, non-numeric character filtering, single-frame contextual disabling with tooltips, and titlebar window dragging.

- **Intelligent Two-Way Source Code Synchronization**:
  - Proactive external file change detection on window activation (`MainWindow.Activated`) and document tab switching with adaptive retries and non-locking file sharing (`FileShare.ReadWrite | FileShare.Delete`).
  - Increased `FileSystemWatcher` buffer size to 64 KB and synchronized conflict resolution dialogs to eliminate deadlocks and duplicate prompts.
  - Modernized regex scanners for C/C++/Python code: supports `constexpr`, `static const`, typed Python buffers (`bytes`, `bytearray`), and multi-frame animation macros (`_FRAME_COUNT`, `_NUM_FRAMES`, 2D arrays).
  - Contextual dimension constant extraction prevents cross-sprite dimension macro bleed, and in-line row comments and line endings (CRLF/LF) are strictly preserved during two-way updates.
  - Standardized XBM dimension constant updating by stripping variable name suffixes (e.g. `_bits`).

- **Atomic File I/O & Autosave Resilience**:
  - Implemented [`SafeFileIo`](file:///H:/dev/hexprite/Hexprite/Core/SafeFileIo.cs) utility performing atomic file writes via temporary files, disk cache flushing, and atomic replacement with backup fallback.
  - Integrated persistent Recent Files tracking in `UserPreferencesService` and `ShellViewModel` with clean MRU list management.
  - Multi-file drag-and-drop support on the main window honoring maximum open tab limits.
  - Hardened autosave recovery against corrupt, truncated, or zero-byte `.hexp` files.

- **Layer Studio & Performance Optimization**:
  - Replaced complete layer collection rebuilds with in-place synchronization (`SyncLayerViewModel`), drastically reducing WPF UI container churn and eliminating UI flickering.
  - Added rich context menu to the Layers panel (Duplicate, Merge Down, Delete, Rename, Toggle Visibility, Toggle Reference Layer) and keyboard shortcuts (`F2` to rename, `Delete` to delete).
  - Excluded draft and reference layers from composite export operations across all formats (PNG, BMP, GIF, XBM, Flipper Zero) by default.
  - Improved drag-and-drop layer reordering with pixel-accurate drop target clip hosts.

- **Smart Update Services & Telemetry Hardening**:
  - Implemented standard `SemanticVersion` struct conforming to SemVer 2.0.0 precedence rules (major, minor, patch, pre-release tags, and build metadata).
  - Added HTTP `If-None-Match` ETag conditional request caching for 0 rate-limit token consumption on unchanged GitHub releases.
  - Installer candidate prioritization (`.exe` setup over `.zip` portable) with human-readable size extraction and direct in-app download button.
  - Added thread-safe token bucket error throttling in `HandledErrorReporter` and automated PII redaction (usernames, IPs, secrets, file paths) in `LoggingService.SanitizeForTelemetry`.

---

### 📋 Detailed Change Log by Component

#### 🎨 Export & Rendering
- **`GifEncoder` Refactoring**:
  - Implemented `IDisposable` with idempotent `Finish()` and auto-flushing on disposal.
  - Preallocated and reused `int[5003]` LZW hash and code tables across frames, eliminating heap allocations during multi-frame exports.
  - Added sub-rectangle frame encoding (`left`, `top`, `frameWidth`, `frameHeight`) and configurable disposal methods (`1 = Keep`, `2 = Restore to background`).
  - Added support for per-frame 256-color Local Color Tables (LCT).
  - Clamped centisecond delays to $\ge 1\text{cs}$ (10ms) to eliminate 0-delay browser freeze bugs.
- **`ColorQuantizer`**:
  - Added exact lossless palette extraction for images with $\le 256$ unique colors.
  - Added Median-Cut volume splitting for higher color counts with perceptually weighted RGB distance metrics.
  - Added 2D Floyd-Steinberg error diffusion dithering.
- **`ExportService`**:
  - Added `ComputeDirtyBounds` and `ExtractSubRegion` static helpers for delta-frame optimization.
  - Added animated GIF grid overlay rendering for scales $\ge 2\text{x}$ using a dynamic 3-color palette (`[bgColor, fgColor, gridColor]`).
  - Added `GifTransparentBackground` and `GifEnableDeltaOptimization` options.
  - Excluded draft and reference layers from composite exports across all export targets.
- **`ExportDialog` UI**:
  - Added auto-focus and text selection when clicking `Custom:` scale chips and `Play [N] times` loop radios.
  - Attached digit-only input filtering on all custom scale, framerate, and loop count text boxes.
  - Added contextual disabling with tooltips for single-frame documents, 1x grid overlay, and dithering dependencies.
  - Added titlebar mouse dragging (`MouseLeftButtonDown`) and unselected tab item hover highlights.
  - Updated result summary with real-time dimensions, frame count, total files, and GIF duration telemetry.

#### ⚡ Source Code Synchronization & Sprite Extraction
- **`FileImportExportService`**:
  - Enhanced C/C++/Python regexes to recognize modern qualifiers (`constexpr`, `static const`, typed Python buffers).
  - Implemented contextual dimension macro extraction to prevent cross-sprite dimension bleed.
  - Preserved line endings (CRLF vs LF) and in-line row comments when writing back to source files.
  - Added multi-frame animation detection from frame count macros (`_FRAME_COUNT`, `_NUM_FRAMES`), 2D C array brackets (`[NUM_FRAMES][BYTES]`), or byte-length inference.
  - Standardized XBM dimension constant updating by stripping variable suffixes (e.g. `_bits`).
  - Cleared read-only file attributes before restoring backups.
  - Relocated backup files to `%AppData%\Hexprite\Backups`.
- **`ImportFromFileDialog`**:
  - Added dedicated "Frames" column to sprite import table.
  - Added tri-state bulk selection checkbox in table header.
  - Added placeholder banner when no sprites are detected and automatic import button disabling when 0 sprites are selected.
- **External Change Detection**:
  - Added `CheckForExternalChanges` on `MainViewModel`, hooking `MainWindow.Activated` and tab selection changes.
  - Increased `FileSystemWatcher` buffer size to 64 KB to prevent dropped file change events.
  - Synchronized conflict resolution dialogs to prevent deadlocks and duplicate prompts.
  - Handled file hashing with adaptive retries and non-locking file sharing (`FileShare.ReadWrite | FileShare.Delete`).

#### 📁 File I/O & Core Infrastructure
- **`SafeFileIo`**:
  - Added atomic file write methods (`WriteAllTextAtomic`, `WriteAllBytesAtomic`, `WriteStreamAtomic`) writing to unique temporary files with disk flush and atomic replacement.
  - Added retry loops and user-friendly error alerts.
- **Recent Files & Drag-and-Drop**:
  - Added persistent recent files list in `UserPreferencesService` and `ShellViewModel`.
  - Added multi-file drag-and-drop support on `MainWindow` honoring maximum open tab limits.
- **Autosave Recovery**:
  - Hardened autosave crash recovery against corrupt, truncated, or zero-byte `.hexp` files.

#### 📑 Layers Panel
- **Collection Performance**:
  - Implemented `SyncLayerViewModel` for in-place differential collection updates, eliminating UI container churn and visual flickering.
- **Commands & Context Menus**:
  - Added context menu for layer operations (Duplicate, Merge Down, Delete, Rename, Toggle Visibility, Toggle Reference Layer).
  - Added keyboard shortcuts (`F2` to rename, `Delete` to delete).
  - Added `NotifyLayerPanelCommandsChanged` to synchronize command `CanExecute` states.
  - Improved drag-and-drop reordering using `LayerListClipHost` for accurate drop target indicators.

#### 🔄 Update Services & Logging
- **`UpdateService`**:
  - Implemented `SemanticVersion` struct conforming to SemVer 2.0.0 precedence rules.
  - Added HTTP `If-None-Match` ETag conditional request caching returning `304 Not Modified` with 0 GitHub API rate-limit cost.
  - Prioritized installer candidate assets (`.exe` setup over `.zip` portable) with human-readable size extraction (`FormattedSize`).
  - Added direct "Download" action button in `MainWindow.xaml` update banner.
  - Added user preferences for `AutoCheckForUpdates` and `IncludePrereleases`.
- **`LoggingService` & `HandledErrorReporter`**:
  - Added thread-safe token bucket error throttling to prevent log floods.
  - Added automated PII redaction (usernames, IP addresses, secrets, file paths) in `SanitizeForTelemetry`.
  - Added Serilog self-log size rollover and `ITimedOperation` for timing slow dev ops.

---

### 🧪 Test Coverage & Verification

Over **945 automated tests** passing with 0 warnings:
- **`GifEncoderTests`**: 18 unit tests covering stream validation, dimensions, palette mapping, LCT, sub-rectangles, delay clamping, and `IDisposable` auto-flushing.
- **`ColorQuantizerTests`**: 7 unit tests covering lossless mapping, Median-Cut box volume splitting, and Floyd-Steinberg error diffusion dithering.
- **`ExportServiceTests`**: 35 integration tests covering PNG, BMP, GIF, PNG Sequence, grid overlays, playback directions (Forward, Reverse, PingPong), delta optimization, and display preview modes.
- **`GifEncoderFuzzTests`**: FsCheck property roundtrip tests verifying decoder compatibility across 100 randomized multi-frame runs.
- **`UpdateServiceTests`**: Comprehensive tests for SemVer 2.0.0 precedence, ETag 304 handling, rate limiting, and asset selection.
- **`LinkedSourceHardeningTests`**: Multi-frame detection, comment preservation, external change detection, and read-only file restore.
- **`FileSaveOpenHardeningTests` & `FileSaveOpenRefinementTests`**: Atomic file writes, recent files tracking, and corrupt autosave recovery.
- **`LoggingAndPrivacyServiceTests` & `HandledErrorReporterTests`**: Telemetry sanitization and error throttling.
- **`MainViewModelTests`**: Layer command states, in-place synchronization, and draft layer export exclusion.
