# Delta / Bounding-Box Frame Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement delta bounding-box frame optimization (`AnimationExportLayout.DeltaPatches`) in Hexprite to drastically reduce flash footprint and transfer overhead for multi-frame microcontroller animations (e.g. 128×64 deskbot facial expressions).

**Architecture:** A standalone differential optimizer (`DeltaAnimationOptimizer`) calculates pixel difference masks between consecutive frames, clusters changed pixels into adaptive bounding-box patches using an exact byte-cost merge heuristic, and extracts row-aligned 1-bit monochrome bitmaps. `CodeGeneratorService` serializes the baseline Keyframe 0, contiguous `_DELTAS` stream, and `_FRAME_OFFSETS` table, generating flicker-free playback loops for Adafruit GFX and U8g2 under both `BlockingDelay` and `NonBlockingMillis` timing modes. UI dropdowns in `SidebarPanel.xaml` and `CodeOutputWindow.xaml` expose the new layout option.

**Tech Stack:** C# .NET 10, WPF, xUnit, C++ Arduino / PROGMEM (Adafruit GFX & U8g2).

**Spec:** [`docs/superpowers/specs/2026-09-30-delta-bounding-box-animation-design.md`](file:///H:/Repositories/hexprite/docs/superpowers/specs/2026-09-30-delta-bounding-box-animation-design.md)

## Global Constraints
- Preserve existing project behavior: `AnimationExportLayout.ArrayOfFrames` remains default.
- 100% backward compatibility for all existing export formats and settings.
- Zero runtime dynamic memory allocation (`malloc`/`free`) on the microcontroller: all delta frames stream directly from `PROGMEM`.
- Exact pixel fidelity: roundtrip application of delta patches onto Frame 0 must be 100% bit-exact with original animation frames.
- Support both `BlockingDelay` (`delay()`) and `NonBlockingMillis` (`millis()`) timing modes.

## Review Focus
- Single-frame animation: behaves cleanly as a static frame export without emitting broken delta arrays.
- Zero-change frames (idle holds): emits `numPatches = 0` (1 byte) without error.
- Full-screen change (flashes/wipes): correctly capped at a single bounding box without exceeding full-frame raw size.
- 1-pixel patch boundary alignment: byte packing $\lceil w / 8 \rceil$ handles non-byte-multiple widths (e.g., $w=5 \rightarrow 1\text{ byte per row}$, $w=9 \rightarrow 2\text{ bytes per row}$).
- Wrap-around loop continuity: looping from frame $N-1$ back to Frame 0 correctly re-blits the complete keyframe without artifacts.

---

### Task 1: Core Model & Settings (`AnimationExportLayout.DeltaPatches`)

**Files:**
- Modify: `Hexprite/Core/ExportSettings.cs:34-45`
- Test: `Hexprite.Tests/ExportSettingsTests.cs`

**Interfaces:**
- Produces: `AnimationExportLayout.DeltaPatches = 3`

- [ ] **Step 1: Write the failing test**

In `Hexprite.Tests/ExportSettingsTests.cs`:
```csharp
[Fact]
public void AnimationExportLayout_ContainsDeltaPatchesOption()
{
    var layout = AnimationExportLayout.DeltaPatches;
    Assert.Equal(3, (int)layout);

    var settings = new ExportSettings { AnimationLayout = AnimationExportLayout.DeltaPatches };
    Assert.Equal(AnimationExportLayout.DeltaPatches, settings.AnimationLayout);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~AnimationExportLayout_ContainsDeltaPatchesOption`
Expected: Compile error: `AnimationExportLayout does not contain a definition for 'DeltaPatches'`.

- [ ] **Step 3: Add `DeltaPatches` to `AnimationExportLayout`**

In `Hexprite/Core/ExportSettings.cs`:
```csharp
public enum AnimationExportLayout
{
    ArrayOfFrames = 0,
    VerticalSpriteSheet = 1,
    HorizontalSpriteSheet = 2,
    DeltaPatches = 3,
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~AnimationExportLayout_ContainsDeltaPatchesOption`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Core/ExportSettings.cs Hexprite.Tests/ExportSettingsTests.cs
git commit -m "feat(export): add DeltaPatches to AnimationExportLayout enum"
```

---

### Task 2: Differential Optimizer Service (`DeltaAnimationOptimizer`)

**Files:**
- Create: `Hexprite/Services/DeltaAnimationOptimizer.cs`
- Create: `Hexprite.Tests/DeltaAnimationOptimizerTests.cs`

**Interfaces:**
- Produces:
  ```csharp
  public sealed record DeltaPatch(int X, int Y, int Width, int Height, byte[] Data);
  public sealed record DeltaFrame(IReadOnlyList<DeltaPatch> Patches);
  public sealed record OptimizedDeltaAnimation(byte[] Keyframe0, IReadOnlyList<DeltaFrame> DeltaFrames);
  
  public static class DeltaAnimationOptimizer
  {
      public static OptimizedDeltaAnimation Optimize(
          IReadOnlyList<bool[]> frames,
          int width,
          int height,
          CancellationToken cancellationToken = default);
  }
  ```

- [ ] **Step 1: Write comprehensive unit tests for `DeltaAnimationOptimizer`**

Create `Hexprite.Tests/DeltaAnimationOptimizerTests.cs`:
```csharp
using System.Collections.Generic;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

public sealed class DeltaAnimationOptimizerTests
{
    [Fact]
    public void Optimize_IdenticalFrames_EmitsZeroPatchesForDelta()
    {
        int w = 16, h = 16;
        bool[] frame0 = new bool[w * h];
        frame0[0] = true;
        bool[] frame1 = (bool[])frame0.Clone();

        var result = DeltaAnimationOptimizer.Optimize([frame0, frame1], w, h);

        Assert.NotNull(result.Keyframe0);
        Assert.Single(result.DeltaFrames);
        Assert.Empty(result.DeltaFrames[0].Patches);
    }

    [Fact]
    public void Optimize_DistantClusters_RemainSeparatePatches()
    {
        int w = 32, h = 32;
        bool[] frame0 = new bool[w * h];
        bool[] frame1 = new bool[w * h];

        // Eye at (4, 4), size 4x4
        for (int y = 4; y < 8; y++)
            for (int x = 4; x < 8; x++)
                frame1[y * w + x] = true;

        // Mouth at (4, 24), size 4x4
        for (int y = 24; y < 28; y++)
            for (int x = 4; x < 8; x++)
                frame1[y * w + x] = true;

        var result = DeltaAnimationOptimizer.Optimize([frame0, frame1], w, h);

        Assert.Single(result.DeltaFrames);
        Assert.Equal(2, result.DeltaFrames[0].Patches.Count);
    }

    [Fact]
    public void Optimize_AdjacentClusters_MergeIntoOnePatch()
    {
        int w = 16, h = 16;
        bool[] frame0 = new bool[w * h];
        bool[] frame1 = new bool[w * h];

        // Pixel at (2, 2) and pixel at (3, 2)
        frame1[2 * w + 2] = true;
        frame1[2 * w + 3] = true;

        var result = DeltaAnimationOptimizer.Optimize([frame0, frame1], w, h);

        Assert.Single(result.DeltaFrames);
        Assert.Single(result.DeltaFrames[0].Patches);
        var patch = result.DeltaFrames[0].Patches[0];
        Assert.Equal(2, patch.X);
        Assert.Equal(2, patch.Y);
        Assert.Equal(2, patch.Width);
        Assert.Equal(1, patch.Height);
    }

    [Fact]
    public void Optimize_Reconstruction_MatchesOriginalFramesBitExact()
    {
        int w = 16, h = 16;
        var rng = new System.Random(42);
        var frames = new List<bool[]>();
        for (int f = 0; f < 5; f++)
        {
            var frame = new bool[w * h];
            for (int i = 0; i < frame.Length; i++)
            {
                if (rng.NextDouble() < 0.15) frame[i] = true;
            }
            frames.Add(frame);
        }

        var result = DeltaAnimationOptimizer.Optimize(frames, w, h);

        // Reconstruct frames
        bool[] current = new bool[w * h];
        // Decode Keyframe 0
        int bytesPerRow = (w + 7) / 8;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int byteIdx = y * bytesPerRow + (x / 8);
                int bit = 7 - (x % 8);
                current[y * w + x] = (result.Keyframe0[byteIdx] & (1 << bit)) != 0;
            }
        }
        Assert.Equal(frames[0], current);

        // Apply delta frames
        for (int f = 1; f < frames.Count; f++)
        {
            var delta = result.DeltaFrames[f - 1];
            foreach (var patch in delta.Patches)
            {
                int patchBytesPerRow = (patch.Width + 7) / 8;
                for (int py = 0; py < patch.Height; py++)
                {
                    for (int px = 0; px < patch.Width; px++)
                    {
                        int byteIdx = py * patchBytesPerRow + (px / 8);
                        int bit = 7 - (px % 8);
                        bool val = (patch.Data[byteIdx] & (1 << bit)) != 0;
                        current[(patch.Y + py) * w + (patch.X + px)] = val;
                    }
                }
            }
            Assert.Equal(frames[f], current);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~DeltaAnimationOptimizerTests`
Expected: Compile error: `DeltaAnimationOptimizer` does not exist.

- [ ] **Step 3: Implement `DeltaAnimationOptimizer`**

Create `Hexprite/Services/DeltaAnimationOptimizer.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.Threading;

namespace Hexprite.Services;

public sealed record DeltaPatch(int X, int Y, int Width, int Height, byte[] Data);
public sealed record DeltaFrame(IReadOnlyList<DeltaPatch> Patches);
public sealed record OptimizedDeltaAnimation(byte[] Keyframe0, IReadOnlyList<DeltaFrame> DeltaFrames);

public static class DeltaAnimationOptimizer
{
    private sealed record BoundingBox(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        public int ByteCost => 4 + (((Width + 7) / 8) * Height);

        public BoundingBox Union(BoundingBox other) =>
            new(
                Math.Min(MinX, other.MinX),
                Math.Min(MinY, other.MinY),
                Math.Max(MaxX, other.MaxX),
                Math.Max(MaxY, other.MaxY));
    }

    public static OptimizedDeltaAnimation Optimize(
        IReadOnlyList<bool[]> frames,
        int width,
        int height,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0)
            return new OptimizedDeltaAnimation([], []);

        byte[] keyframe0 = PackBitmap(frames[0], 0, 0, width, height, width);

        if (frames.Count == 1)
            return new OptimizedDeltaAnimation(keyframe0, []);

        var deltaFrames = new List<DeltaFrame>(frames.Count - 1);

        for (int f = 1; f < frames.Count; f++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prev = frames[f - 1];
            var curr = frames[f];

            var boxes = FindChangedBoundingBoxes(prev, curr, width, height);
            var mergedBoxes = ClusterBoundingBoxes(boxes, width, height);

            var patches = new List<DeltaPatch>(mergedBoxes.Count);
            foreach (var box in mergedBoxes)
            {
                byte[] data = PackBitmap(curr, box.MinX, box.MinY, box.Width, box.Height, width);
                patches.Add(new DeltaPatch(box.MinX, box.MinY, box.Width, box.Height, data));
            }

            deltaFrames.Add(new DeltaFrame(patches));
        }

        return new OptimizedDeltaAnimation(keyframe0, deltaFrames);
    }

    private static List<BoundingBox> FindChangedBoundingBoxes(bool[] prev, bool[] curr, int width, int height)
    {
        int totalPixels = width * height;
        bool[] visited = new bool[totalPixels];
        var boxes = new List<BoundingBox>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = y * width + x;
                if (visited[idx] || prev[idx] == curr[idx])
                    continue;

                // Flood fill connected component
                int minX = x, maxX = x, minY = y, maxY = y;
                var queue = new Queue<int>();
                queue.Enqueue(idx);
                visited[idx] = true;

                while (queue.Count > 0)
                {
                    int currIdx = queue.Dequeue();
                    int cx = currIdx % width;
                    int cy = currIdx / width;

                    if (cx < minX) minX = cx;
                    if (cx > maxX) maxX = cx;
                    if (cy < minY) minY = cy;
                    if (cy > maxY) maxY = cy;

                    // 8-way neighbors
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = cx + dx;
                            int ny = cy + dy;
                            if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                            {
                                int nIdx = ny * width + nx;
                                if (!visited[nIdx] && prev[nIdx] != curr[nIdx])
                                {
                                    visited[nIdx] = true;
                                    queue.Enqueue(nIdx);
                                }
                            }
                        }
                    }
                }

                boxes.Add(new BoundingBox(minX, minY, maxX, maxY));
            }
        }

        return boxes;
    }

    private static List<BoundingBox> ClusterBoundingBoxes(List<BoundingBox> boxes, int width, int height)
    {
        if (boxes.Count <= 1)
            return boxes;

        int fullFrameBytes = ((width + 7) / 8) * height;

        bool changed = true;
        while (changed && boxes.Count > 1)
        {
            changed = false;
            int bestI = -1, bestJ = -1;
            int bestSavings = 0;

            for (int i = 0; i < boxes.Count; i++)
            {
                for (int j = i + 1; j < boxes.Count; j++)
                {
                    var a = boxes[i];
                    var b = boxes[j];
                    var union = a.Union(b);

                    int separateCost = a.ByteCost + b.ByteCost;
                    int unionCost = union.ByteCost;
                    int savings = separateCost - unionCost;

                    if (savings >= 0 && savings > bestSavings)
                    {
                        bestSavings = savings;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            if (bestI >= 0)
            {
                var merged = boxes[bestI].Union(boxes[bestJ]);
                boxes.RemoveAt(bestJ);
                boxes.RemoveAt(bestI);
                boxes.Add(merged);
                changed = true;
            }
        }

        // Cap check: If total patch bytes exceeds raw full frame, collapse into single enclosing box
        int totalCost = 0;
        foreach (var b in boxes) totalCost += b.ByteCost;
        if (totalCost > fullFrameBytes)
        {
            int minX = width, minY = height, maxX = 0, maxY = 0;
            foreach (var b in boxes)
            {
                if (b.MinX < minX) minX = b.MinX;
                if (b.MinY < minY) minY = b.MinY;
                if (b.MaxX > maxX) maxX = b.MaxX;
                if (b.MaxY > maxY) maxY = b.MaxY;
            }
            return [new BoundingBox(minX, minY, maxX, maxY)];
        }

        return boxes;
    }

    private static byte[] PackBitmap(bool[] fullFrame, int x, int y, int w, int h, int stride)
    {
        int bytesPerRow = (w + 7) / 8;
        byte[] buffer = new byte[bytesPerRow * h];

        for (int row = 0; row < h; row++)
        {
            for (int col = 0; col < w; col++)
            {
                bool pixel = fullFrame[(y + row) * stride + (x + col)];
                if (pixel)
                {
                    int byteIndex = row * bytesPerRow + (col / 8);
                    int bitIndex = 7 - (col % 8);
                    buffer[byteIndex] |= (byte)(1 << bitIndex);
                }
            }
        }

        return buffer;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~DeltaAnimationOptimizerTests`
Expected: PASS (all 4 tests pass).

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Services/DeltaAnimationOptimizer.cs Hexprite.Tests/DeltaAnimationOptimizerTests.cs
git commit -m "feat(services): implement DeltaAnimationOptimizer service with adaptive clustering"
```

---

### Task 3: Code Generation for Adafruit GFX with `DeltaPatches`

**Files:**
- Modify: `Hexprite/Services/CodeGeneratorService.cs`
- Test: `Hexprite.Tests/CodeGeneratorServiceTests.cs`

**Interfaces:**
- Consumes: `DeltaAnimationOptimizer.Optimize`, `ExportSettings.AnimationLayout == AnimationExportLayout.DeltaPatches`
- Produces: Adafruit GFX sketch and C-array output with `{NAME}_FRAME_0`, `{NAME}_DELTAS`, `{NAME}_FRAME_OFFSETS`, and `drawDeltaFrame`.

- [ ] **Step 1: Write failing tests in `Hexprite.Tests/CodeGeneratorServiceTests.cs`**

```csharp
[Fact]
public void GenerateCode_AdafruitGfx_DeltaPatches_EmitsKeyframeAndDeltaArrays()
{
    var service = new CodeGeneratorService();
    int w = 16, h = 16;
    bool[] f0 = new bool[w * h];
    bool[] f1 = new bool[w * h];
    f1[2 * w + 2] = true; // 1 pixel change

    var settings = new ExportSettings
    {
        Format = ExportFormat.AdafruitGfx,
        ExportAsAnimation = true,
        AnimationLayout = AnimationExportLayout.DeltaPatches,
        SpriteName = "testDelta",
        FrameRateFps = 20,
    };

    string code = service.GenerateCode([f0, f1], w, h, settings, false, null, 0, 0, 0, 0);

    Assert.Contains("const uint8_t PROGMEM testDelta_FRAME_0[32] = {", code);
    Assert.Contains("const uint8_t PROGMEM testDelta_DELTAS[", code);
    Assert.Contains("const uint16_t PROGMEM testDelta_FRAME_OFFSETS[1] = {", code);
}

[Fact]
public void GenerateSketch_AdafruitGfx_DeltaPatches_NonBlockingMillis_EmitsDrawDeltaHelperAndStateLoop()
{
    var service = new CodeGeneratorService();
    int w = 16, h = 16;
    bool[] f0 = new bool[w * h];
    bool[] f1 = new bool[w * h];
    f1[2 * w + 2] = true;

    var settings = new ExportSettings
    {
        Format = ExportFormat.AdafruitGfx,
        ExportAsAnimation = true,
        AnimationLayout = AnimationExportLayout.DeltaPatches,
        GenerateFullSketch = true,
        TimingMode = AnimationTimingMode.NonBlockingMillis,
        SpriteName = "deltaSketch",
        FrameRateFps = 30,
    };

    string sketch = service.GenerateSketch([f0, f1], w, h, settings, false, null, 0, 0, 0, 0);

    Assert.Contains("void drawDeltaFrame(const uint8_t* p)", sketch);
    Assert.Contains("display.drawBitmap(px, py, p, pw, ph, SSD1306_WHITE, SSD1306_BLACK);", sketch);
    Assert.Contains("if (currentFrame == 0) {", sketch);
    Assert.Contains("display.drawBitmap(x, y, deltaSketch_FRAME_0", sketch);
    Assert.Contains("drawDeltaFrame(&deltaSketch_DELTAS[deltaSketch_FRAME_OFFSETS[currentFrame - 1]]);", sketch);
    Assert.Contains("millis()", sketch);
    Assert.DoesNotContain("delay(", sketch);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~GenerateCode_AdafruitGfx_DeltaPatches`
Expected: FAIL (arrays not emitted).

- [ ] **Step 3: Implement Adafruit GFX `DeltaPatches` in `CodeGeneratorService.cs`**

1. In `GenerateCodeAsync`: when `isAnimation && settings.AnimationLayout == AnimationExportLayout.DeltaPatches`, route to `BuildDeltaPatchesAnimation(frames, name, width, height, settings, hexFmt, frameDelays, cancellationToken)`.
2. Implement `BuildDeltaPatchesAnimation`:
   - Calls `DeltaAnimationOptimizer.Optimize(frames, width, height, cancellationToken)`.
   - Emits dimensions: `{UPPER}_WIDTH`, `{UPPER}_HEIGHT`, `{UPPER}_FRAMES`, `{UPPER}_FPS`.
   - Emits `{name}_FRAME_0[...] = { ... };`.
   - Serializes delta stream into single byte array `_DELTAS`:
     - for each delta frame:
       - `(byte)patches.Count`
       - for each patch: `(byte)patch.X`, `(byte)patch.Y`, `(byte)patch.Width`, `(byte)patch.Height`, followed by `patch.Data`.
     - tracks offset in `offsets` list.
   - Emits `{name}_DELTAS[...] = { ... };`.
   - Emits `{name}_FRAME_OFFSETS[...] = { ... };`.
3. In `BuildAdafruitGfxSketch`:
   - When `cfg.AnimationLayout == AnimationExportLayout.DeltaPatches`:
     - Emits `void drawDeltaFrame(const uint8_t* p) { ... }` helper.
     - Emits `loop()` branching on `TimingMode == BlockingDelay` vs `NonBlockingMillis`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~GenerateCode_AdafruitGfx_DeltaPatches`
Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~GenerateSketch_AdafruitGfx_DeltaPatches`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Services/CodeGeneratorService.cs Hexprite.Tests/CodeGeneratorServiceTests.cs
git commit -m "feat(codegen): implement Adafruit GFX sketch generation for DeltaPatches"
```

---

### Task 4: Code Generation for U8g2 with `DeltaPatches`

**Files:**
- Modify: `Hexprite/Services/CodeGeneratorService.cs`
- Test: `Hexprite.Tests/CodeGeneratorServiceTests.cs`

**Interfaces:**
- Consumes: `DeltaAnimationOptimizer.Optimize`, `ExportFormat.U8g2DrawBitmap`, `ExportFormat.U8g2DrawXBM`
- Produces: U8g2 sketch with `u8g2.setDrawColor(0); u8g2.drawBox(...)` then `u8g2.drawBitmap/drawXBMP` patch blitting.

- [ ] **Step 1: Write failing tests in `Hexprite.Tests/CodeGeneratorServiceTests.cs`**

```csharp
[Fact]
public void GenerateSketch_U8g2DrawBitmap_DeltaPatches_EmitsDrawDeltaHelperAndSendBuffer()
{
    var service = new CodeGeneratorService();
    int w = 16, h = 16;
    bool[] f0 = new bool[w * h];
    bool[] f1 = new bool[w * h];
    f1[2 * w + 2] = true;

    var settings = new ExportSettings
    {
        Format = ExportFormat.U8g2DrawBitmap,
        ExportAsAnimation = true,
        AnimationLayout = AnimationExportLayout.DeltaPatches,
        GenerateFullSketch = true,
        TimingMode = AnimationTimingMode.NonBlockingMillis,
        SpriteName = "u8g2Delta",
        FrameRateFps = 15,
    };

    string sketch = service.GenerateSketch([f0, f1], w, h, settings, false, null, 0, 0, 0, 0);

    Assert.Contains("void drawDeltaFrame(const uint8_t* p)", sketch);
    Assert.Contains("u8g2.drawBox(x + px, y + py, pw, ph);", sketch);
    Assert.Contains("u8g2.drawBitmap(x + px, y + py, (pw + 7) / 8, ph, p);", sketch);
    Assert.Contains("u8g2.sendBuffer();", sketch);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~GenerateSketch_U8g2DrawBitmap_DeltaPatches`
Expected: FAIL.

- [ ] **Step 3: Implement U8g2 `DeltaPatches` in `CodeGeneratorService.cs`**

In `BuildU8g2Sketch`:
- When `cfg.AnimationLayout == AnimationExportLayout.DeltaPatches`:
  - Emit `drawDeltaFrame` helper:
    ```cpp
    void drawDeltaFrame(const uint8_t* p) {
      uint8_t numPatches = pgm_read_byte(p++);
      for (uint8_t i = 0; i < numPatches; i++) {
        uint8_t px = pgm_read_byte(p++);
        uint8_t py = pgm_read_byte(p++);
        uint8_t pw = pgm_read_byte(p++);
        uint8_t ph = pgm_read_byte(p++);
        u8g2.setDrawColor(0);
        u8g2.drawBox(x + px, y + py, pw, ph);
        u8g2.setDrawColor(1);
        if (isXbm)
          u8g2.drawXBMP(x + px, y + py, pw, ph, p);
        else
          u8g2.drawBitmap(x + px, y + py, (pw + 7) / 8, ph, p);
        p += ((pw + 7) / 8) * ph;
      }
    }
    ```
  - In `loop()`:
    - On `currentFrame == 0`: `u8g2.clearBuffer(); u8g2.drawBitmap(...);`
    - On `currentFrame > 0`: `drawDeltaFrame(&{NAME}_DELTAS[{NAME}_FRAME_OFFSETS[currentFrame - 1]]);`
    - `u8g2.sendBuffer();`
    - Support both `TimingMode.BlockingDelay` and `TimingMode.NonBlockingMillis`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~GenerateSketch_U8g2DrawBitmap_DeltaPatches`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Services/CodeGeneratorService.cs Hexprite.Tests/CodeGeneratorServiceTests.cs
git commit -m "feat(codegen): implement U8g2 sketch generation for DeltaPatches"
```

---

### Task 5: UI Integration (`SidebarPanel.xaml` & `CodeOutputWindow.xaml`)

**Files:**
- Modify: `Hexprite/Views/SidebarPanel.xaml:800-810`
- Modify: `Hexprite/Views/CodeOutputWindow.xaml:165-180`
- Modify: `Hexprite/Views/CodeOutputWindow.xaml.cs`
- Test: `Hexprite.Tests/ViewModels/MainViewModelExportTests.cs`

**Interfaces:**
- Produces: UI selectable option "Delta Patches (Bounding Box)" with SelectedIndex `3`.

- [ ] **Step 1: Write test in `MainViewModelExportTests.cs`**

```csharp
[Fact]
public void AnimationLayout_CanSelectDeltaPatches()
{
    var vm = CreateViewModel();
    vm.IsAnimationMode = true;

    vm.AnimationLayout = (int)AnimationExportLayout.DeltaPatches;

    Assert.Equal(3, vm.AnimationLayout);
    Assert.Equal(AnimationExportLayout.DeltaPatches, vm.CurrentExportSettings.AnimationLayout);
}
```

- [ ] **Step 2: Run test to verify it passes (or fails if bounds check exists)**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~AnimationLayout_CanSelectDeltaPatches`

- [ ] **Step 3: Update `SidebarPanel.xaml` and `CodeOutputWindow.xaml`**

In `SidebarPanel.xaml`:
```xaml
<ComboBox x:Name="CboAnimationLayout"
          Style="{StaticResource ModernComboBoxStyle}"
          Height="26"
          Margin="0,0,0,12"
          SelectedIndex="{Binding AnimationLayout}"
          Visibility="{Binding IsAnimationEnabled, Converter={StaticResource BooleanToVisibilityConverter}}">
    <ComboBoxItem Content="Array of frames (pointers)"/>
    <ComboBoxItem Content="Vertical Sprite Sheet (tall)"/>
    <ComboBoxItem Content="Horizontal Sprite Sheet (wide)"/>
    <ComboBoxItem Content="Delta Patches (Bounding Box)"/>
</ComboBox>
```

In `CodeOutputWindow.xaml`:
Add `<ComboBoxItem Content="Delta Patches (Bounding Box)"/>` to `CboLayout`.

In `CodeOutputWindow.xaml.cs`:
Ensure layout index mapping covers index `3` $\rightarrow$ `AnimationExportLayout.DeltaPatches`.

- [ ] **Step 4: Run tests to verify ViewModel bindings and UI logic**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~MainViewModelExportTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Hexprite/Views/SidebarPanel.xaml Hexprite/Views/CodeOutputWindow.xaml Hexprite/Views/CodeOutputWindow.xaml.cs Hexprite.Tests/ViewModels/MainViewModelExportTests.cs
git commit -m "feat(ui): add Delta Patches option to animation layout dropdowns"
```

---

### Task 6: Chaos Fuzzer Extension & Full Suite Verification

**Files:**
- Modify: `Hexprite.Tests/CodeGeneratorChaosFuzzerTests.cs`
- Run: Full test suite

- [ ] **Step 1: Extend `CodeGeneratorChaosFuzzerTests.cs`**

Ensure `AnimationLayout` random sampling in the 10,000-iteration fuzzer includes `AnimationExportLayout.DeltaPatches`:
```csharp
s.AnimationLayout = (AnimationExportLayout)rng.Next(0, 4);
```

- [ ] **Step 2: Run Chaos Fuzzer**

Run: `dotnet test Hexprite.Tests --filter FullyQualifiedName~CodeGeneratorChaosFuzzerTests`
Expected: 10,000 iterations pass with zero exceptions.

- [ ] **Step 3: Run Full Test Suite**

Run: `dotnet test Hexprite.Tests`
Expected: All tests pass with 0 failures, 0 regressions.

- [ ] **Step 4: Commit**

```bash
git add Hexprite.Tests/CodeGeneratorChaosFuzzerTests.cs
git commit -m "test(fuzzer): add DeltaPatches layout to chaos fuzzer permutations"
```
