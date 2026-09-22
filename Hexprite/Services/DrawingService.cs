using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Hexprite.Core;

namespace Hexprite.Services
{
    public class DrawingService : IDrawingService
    {
        /// <summary>
        /// Returns true if the pixel at (x, y) should NOT be written because
        /// it falls outside the provided selection clip.
        /// </summary>
        private static bool IsClipped(int x, int y, IPixelClip? clip)
        {
            return clip != null && !clip.IsPixelInClip(x, y);
        }

        public static OverflowPixelBuffer? GetActiveOverflowBuffer(SpriteState state)
        {
            return state.ActivePixelBuffer as OverflowPixelBuffer;
        }

        internal static (int minX, int minY, int maxX, int maxY, int width, int height) GetPixelDomain(SpriteState state)
        {
            if (state.ActivePixelBuffer is OverflowPixelBuffer ovf)
            {
                return (
                    -ovf.MarginX,
                    -ovf.MarginY,
                    ovf.ExtendedWidth - ovf.MarginX - 1,
                    ovf.ExtendedHeight - ovf.MarginY - 1,
                    ovf.ExtendedWidth,
                    ovf.ExtendedHeight);
            }

            return (0, 0, state.Width - 1, state.Height - 1, state.Width, state.Height);
        }

        private static void WritePixel(SpriteState state, OverflowPixelBuffer? ovf, int x, int y, bool value, IPixelClip? clip)
        {
            if (IsClipped(x, y, clip)) return;
            if (ovf != null)
            {
                ovf.SetPixelNoInvalidate(x, y, value);
                if (x >= 0 && x < state.Width && y >= 0 && y < state.Height)
                    state.Pixels[(y * state.Width) + x] = value;
            }
            else
            {
                if (x >= 0 && x < state.Width && y >= 0 && y < state.Height)
                    state.Pixels[(y * state.Width) + x] = value;
            }
        }

        private static void WritePixelNoClip(SpriteState state, OverflowPixelBuffer? ovf, int x, int y, bool value)
        {
            if (ovf != null)
            {
                ovf.SetPixelNoInvalidate(x, y, value);
                if (x >= 0 && x < state.Width && y >= 0 && y < state.Height)
                    state.Pixels[(y * state.Width) + x] = value;
            }
            else
            {
                if (x >= 0 && x < state.Width && y >= 0 && y < state.Height)
                    state.Pixels[(y * state.Width) + x] = value;
            }
        }

        /// <summary>
        /// Returns true if the pixel at absolute canvas position (x, y) should
        /// be drawn given the active dither pattern. Patterns tile across the
        /// full canvas so overlapping strokes produce consistent results.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool ShouldDitherPixel(int x, int y, DitherPattern pattern)
        {
            return pattern switch
            {
                DitherPattern.Checkerboard => ((x + y) & 1) == 0,
                DitherPattern.Light => ((x | y) & 1) == 0,
                DitherPattern.Dense => ((x & y) & 1) == 0,
                DitherPattern.DiagonalLines => ((x + y) & 2) == 0,
                DitherPattern.CrossHatch => ((x + y) & 2) == 0 || ((x - y) & 2) == 0,
                _ => true,
            };
        }

        // ── Flood fill ────────────────────────────────────────────────────

        public bool ApplyFloodFill(SpriteState state, int startX, int startY, bool newState, bool isContiguous = true, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            if (startX < 0 || startX >= state.Width || startY < 0 || startY >= state.Height) return false;
            if (IsClipped(startX, startY, clip)) return false;

            int startIndex = (startY * state.Width) + startX;
            bool targetState = state.Pixels[startIndex];
            if (targetState == newState) return false;

            if (!isContiguous)
            {
                bool changed = false;
                if (ovf != null)
                {
                    var ext = ovf.GetExtendedData();
                    for (int ey = 0; ey < ovf.ExtendedHeight; ey++)
                    {
                        for (int ex = 0; ex < ovf.ExtendedWidth; ex++)
                        {
                            int extIdx = ey * ovf.ExtendedWidth + ex;
                            if (ext[extIdx] == targetState)
                            {
                                int canvasX = ex - ovf.MarginX;
                                int canvasY = ey - ovf.MarginY;
                                if (!IsClipped(canvasX, canvasY, clip))
                                {
                                    ovf.SetPixelNoInvalidate(canvasX, canvasY, newState);
                                    if (canvasX >= 0 && canvasX < state.Width && canvasY >= 0 && canvasY < state.Height)
                                    {
                                        int i = (canvasY * state.Width) + canvasX;
                                        state.Pixels[i] = newState;
                                    }
                                    changed = true;
                                }
                            }
                        }
                    }
                }
                else
                {
                    for (int y = 0; y < state.Height; y++)
                    {
                        for (int x = 0; x < state.Width; x++)
                        {
                            int i = (y * state.Width) + x;
                            if (state.Pixels[i] == targetState && !IsClipped(x, y, clip))
                            {
                                state.Pixels[i] = newState;
                                changed = true;
                            }
                        }
                    }
                }
                return changed;
            }

            int total = state.Width * state.Height;
            // FIX: Rent a buffer from the shared pool to eliminate Garbage Collection spikes.
            bool[] visited = System.Buffers.ArrayPool<bool>.Shared.Rent(total);

            try
            {
                // CRITICAL: Rented arrays can contain garbage data from previous operations.
                Array.Clear(visited, 0, total);

                var queue = new Queue<int>();

                visited[startIndex] = true;
                queue.Enqueue(startIndex);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    state.Pixels[current] = newState;

                    int x = current % state.Width;
                    int y = current / state.Width;
                    
                    ovf?.SetPixelNoInvalidate(x, y, newState);

                    TryEnqueue(queue, visited, state, targetState, x - 1, y, clip);
                    TryEnqueue(queue, visited, state, targetState, x + 1, y, clip);
                    TryEnqueue(queue, visited, state, targetState, x, y - 1, clip);
                    TryEnqueue(queue, visited, state, targetState, x, y + 1, clip);
                }
            }
            finally
            {
                // Guarantee the buffer is returned to the pool
                System.Buffers.ArrayPool<bool>.Shared.Return(visited);
            }

            return true;
        }

        /// <summary>
        /// Enqueues a neighbor pixel if it is in bounds, inside the selection clip,
        /// hasn't been visited, and matches the target state.
        /// </summary>
        private static void TryEnqueue(Queue<int> queue, bool[] visited, SpriteState state, bool targetState, int x, int y, IPixelClip? clip)
        {
            if (x < 0 || x >= state.Width || y < 0 || y >= state.Height) return;
            int index = (y * state.Width) + x;
            if (visited[index] || state.Pixels[index] != targetState) return;
            if (IsClipped(x, y, clip)) return;
            visited[index] = true;
            queue.Enqueue(index);
        }

        public bool[,] GetFloodFillMask(SpriteState state, int startX, int startY, ISelectionService? selection, FloatingPasteMode pasteMode, out int minX, out int minY, out int maxX, out int maxY)
        {
            var domain = GetPixelDomain(state);
            if (startX < domain.minX || startX > domain.maxX || startY < domain.minY || startY > domain.maxY)
            {
                minX = 0; maxX = -1; minY = 0; maxY = -1;
                return new bool[domain.width, domain.height];
            }

            minX = startX; minY = startY; maxX = startX; maxY = startY;

            int total = checked(domain.width * domain.height);
            bool[] pooledMask = System.Buffers.ArrayPool<bool>.Shared.Rent(total);
            var ovf = GetActiveOverflowBuffer(state);
            bool[]? extendedPixels = ovf?.GetExtendedData();

            try
            {
                Array.Clear(pooledMask, 0, total);

                // Helper to read pixels from the canvas, seamlessly overlaying floating pixels if they exist
                bool GetPixelComposite(int px, int py)
                {
                    if (selection != null && selection.IsFloating && selection.FloatingPixels != null)
                    {
                        int fx = px - selection.FloatingX;
                        int fy = py - selection.FloatingY;
                        if (fx >= 0 && fx < selection.FloatingWidth && fy >= 0 && fy < selection.FloatingHeight)
                        {
                            bool fp = selection.FloatingPixels[fx, fy];
                            if (pasteMode == FloatingPasteMode.Transparent)
                            {
                                if (fp) return true;
                            }
                            else
                            {
                                return fp;
                            }
                        }
                    }
                    if (ovf != null && extendedPixels != null)
                    {
                        int ex = px + ovf.MarginX;
                        int ey = py + ovf.MarginY;
                        return extendedPixels[(ey * ovf.ExtendedWidth) + ex];
                    }

                    return state.Pixels[(py * state.Width) + px];
                }

                bool targetState = GetPixelComposite(startX, startY);
                int startIndex = ((startY - domain.minY) * domain.width) + (startX - domain.minX);

                var queue = new System.Collections.Generic.Queue<int>();
                queue.Enqueue(startIndex);
                pooledMask[startIndex] = true;

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int x = (current % domain.width) + domain.minX;
                    int y = (current / domain.width) + domain.minY;

                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;

                    if (x > domain.minX)
                    {
                        int idx = current - 1;
                        if (!pooledMask[idx] && GetPixelComposite(x - 1, y) == targetState) { pooledMask[idx] = true; queue.Enqueue(idx); }
                    }
                    if (x < domain.maxX)
                    {
                        int idx = current + 1;
                        if (!pooledMask[idx] && GetPixelComposite(x + 1, y) == targetState) { pooledMask[idx] = true; queue.Enqueue(idx); }
                    }
                    if (y > domain.minY)
                    {
                        int idx = current - domain.width;
                        if (!pooledMask[idx] && GetPixelComposite(x, y - 1) == targetState) { pooledMask[idx] = true; queue.Enqueue(idx); }
                    }
                    if (y < domain.maxY)
                    {
                        int idx = current + domain.width;
                        if (!pooledMask[idx] && GetPixelComposite(x, y + 1) == targetState) { pooledMask[idx] = true; queue.Enqueue(idx); }
                    }
                }

                // Guard: protects callers that consume out-params without checking maxX >= minX.
                if (maxX < minX || maxY < minY)
                    return new bool[0, 0];

                int w = maxX - minX + 1;
                int h = maxY - minY + 1;
                var croppedMask = new bool[w, h];
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        int domainIndex = ((y - domain.minY) * domain.width) + (x - domain.minX);
                        croppedMask[x - minX, y - minY] = pooledMask[domainIndex];
                    }
                }

                return croppedMask;
            }
            finally
            {
                System.Buffers.ArrayPool<bool>.Shared.Return(pooledMask);
            }
        }

        // ── Brush stamp ───────────────────────────────────────────────────

        private static readonly (int dx, int dy)[] s_singlePixelOffset = new[] { (0, 0) };
        private static readonly ConcurrentDictionary<(int size, BrushShape shape, int angle), (int dx, int dy)[]> s_stampOffsetsCache = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBrushMargin(int brushSize, BrushShape shape, int angleDeg)
        {
            if (shape == BrushShape.Circle || angleDeg % 90 == 0)
            {
                return ((brushSize + 1) / 2) + 1;
            }
            return (int)Math.Ceiling(brushSize * 0.70710678) + 2;
        }

        /// <summary>
        /// Returns a cached array of pixel offsets (dx, dy) relative to the brush center.
        /// Zero allocations on repeated calls with the same parameters.
        /// </summary>
        public static (int dx, int dy)[] GetStampOffsetsArray(int brushSize, BrushShape shape, int angleDeg)
        {
            if (brushSize <= 1) return s_singlePixelOffset;

            angleDeg = ((angleDeg % 360) + 360) % 360;
            var key = (brushSize, shape, angleDeg);
            if (s_stampOffsetsCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var offsets = ComputeStampOffsetsInternal(brushSize, shape, angleDeg);
            var array = offsets.ToArray();
            s_stampOffsetsCache.TryAdd(key, array);
            return array;
        }

        /// <summary>
        /// Computes the set of pixel offsets (dx, dy) relative to the brush center
        /// for the given shape, size, and rotation angle. Shared by DrawBrushStamp
        /// and the cursor preview in the View.
        /// </summary>
        public static List<(int dx, int dy)> ComputeStampOffsets(int brushSize, BrushShape shape, int angleDeg)
        {
            return new List<(int dx, int dy)>(GetStampOffsetsArray(brushSize, shape, angleDeg));
        }

        private static List<(int dx, int dy)> ComputeStampOffsetsInternal(int brushSize, BrushShape shape, int angleDeg)
        {
            var offsets = new List<(int dx, int dy)>();

            if (brushSize <= 1)
            {
                offsets.Add((0, 0));
                return offsets;
            }

            // Normalize angle to [0, 360) to prevent precision drift
            angleDeg = (angleDeg % 360 + 360) % 360;

            switch (shape)
            {
                case BrushShape.Circle:
                {
                    int half = (brushSize - 1) / 2;
                    double centerOffset = brushSize % 2 == 0 ? 0.5 : 0.0;
                    // Slightly reduce radius to avoid blocky corners and make it feel more "round" in pixel art
                    double radius = (brushSize - 0.5) / 2.0;
                    double rSq = radius * radius;
                    var set = new HashSet<(int, int)>();

                    for (int dy = -half; dy <= half + (brushSize % 2 == 0 ? 1 : 0); dy++)
                    {
                        for (int dx = -half; dx <= half + (brushSize % 2 == 0 ? 1 : 0); dx++)
                        {
                            double cx = dx - centerOffset;
                            double cy = dy - centerOffset;
                            if (cx * cx + cy * cy <= rSq)
                            {
                                set.Add((dx, dy));
                            }
                        }
                    }
                    offsets.AddRange(set);
                    break;
                }

                case BrushShape.Square:
                {
                    int half = (brushSize - 1) / 2;
                    double rad = angleDeg * Math.PI / 180.0;
                    double cos = Math.Cos(rad), sin = Math.Sin(rad);
                    double centerOffset = brushSize % 2 == 0 ? 0.5 : 0.0;
                    var set = new HashSet<(int, int)>();

                    for (int dy = -half; dy <= half + (brushSize % 2 == 0 ? 1 : 0); dy++)
                    {
                        for (int dx = -half; dx <= half + (brushSize % 2 == 0 ? 1 : 0); dx++)
                        {
                            double cx = dx - centerOffset;
                            double cy = dy - centerOffset;
                            double rotX = cx * cos - cy * sin;
                            double rotY = cx * sin + cy * cos;
                            int rx = (int)Math.Floor(rotX + centerOffset);
                            int ry = (int)Math.Floor(rotY + centerOffset);
                            set.Add((rx, ry));
                        }
                    }
                    offsets.AddRange(set);
                    break;
                }

                case BrushShape.Line:
                {
                    int half = (brushSize - 1) / 2;
                    double rad = angleDeg * Math.PI / 180.0;
                    double cos = Math.Cos(rad), sin = Math.Sin(rad);
                    double centerOffset = brushSize % 2 == 0 ? 0.5 : 0.0;
                    var set = new HashSet<(int, int)>();

                    // Horizontal line of brushSize pixels, rotated by angleDeg
                    for (int dx = -half; dx <= half + (brushSize % 2 == 0 ? 1 : 0); dx++)
                    {
                        double cx = dx - centerOffset;
                        double rotX = cx * cos;
                        double rotY = cx * sin;
                        int rx = (int)Math.Floor(rotX + centerOffset);
                        int ry = (int)Math.Floor(rotY + centerOffset);
                        set.Add((rx, ry));
                    }
                    offsets.AddRange(set);
                    break;
                }
            }

            return offsets;
        }

        public void DrawBrushStamp(SpriteState state, int cx, int cy, int brushSize, bool newState, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            if (brushSize <= 1)
            {
                WritePixel(state, ovf, cx, cy, newState, clip);
                return;
            }

            int margin = GetBrushMargin(brushSize, shape, angleDeg);
            ovf?.EnsureCapacity(cx - margin, cy - margin, cx + margin, cy + margin);

            var offsets = GetStampOffsetsArray(brushSize, shape, angleDeg);
            StampOffsets(state, ovf, cx, cy, newState, offsets, clip);
        }

        public void DrawBrushStampDithered(SpriteState state, int cx, int cy, int brushSize, bool newState, DitherPattern ditherPattern, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            if (brushSize <= 1)
            {
                if (ShouldDitherPixel(cx, cy, ditherPattern))
                    WritePixel(state, ovf, cx, cy, newState, clip);
                return;
            }

            int margin = GetBrushMargin(brushSize, shape, angleDeg);
            ovf?.EnsureCapacity(cx - margin, cy - margin, cx + margin, cy + margin);

            var offsets = GetStampOffsetsArray(brushSize, shape, angleDeg);
            StampOffsetsDithered(state, ovf, cx, cy, newState, offsets, clip, ditherPattern);
        }

        /// <summary>
        /// Fast stamp using pre-computed offsets array.
        /// </summary>
        private static void StampOffsets(SpriteState state, OverflowPixelBuffer? ovf, int cx, int cy, bool newState, (int dx, int dy)[] offsets, IPixelClip? clip)
        {
            int w = state.Width;
            int h = state.Height;
            bool[] pixels = state.Pixels;

            for (int i = 0; i < offsets.Length; i++)
            {
                int px = cx + offsets[i].dx;
                int py = cy + offsets[i].dy;

                if (IsClipped(px, py, clip)) continue;

                if ((uint)px < (uint)w && (uint)py < (uint)h)
                {
                    int idx = py * w + px;
                    if (pixels[idx] == newState) continue;
                    pixels[idx] = newState;
                    ovf?.SetPixelFast(px, py, newState);
                }
                else if (ovf != null)
                {
                    ovf.SetPixelFast(px, py, newState);
                }
            }
        }

        /// <summary>
        /// Dithered stamp using pre-computed offsets array — only writes pixels that
        /// pass the dither pattern gate, with early-exit when pixels are already set.
        /// </summary>
        private static void StampOffsetsDithered(SpriteState state, OverflowPixelBuffer? ovf, int cx, int cy, bool newState, (int dx, int dy)[] offsets, IPixelClip? clip, DitherPattern ditherPattern)
        {
            int w = state.Width;
            int h = state.Height;
            bool[] pixels = state.Pixels;

            for (int i = 0; i < offsets.Length; i++)
            {
                int px = cx + offsets[i].dx;
                int py = cy + offsets[i].dy;

                if ((uint)px < (uint)w && (uint)py < (uint)h)
                {
                    int idx = py * w + px;
                    if (pixels[idx] == newState) continue;
                    if (!ShouldDitherPixel(px, py, ditherPattern)) continue;
                    if (IsClipped(px, py, clip)) continue;

                    pixels[idx] = newState;
                    ovf?.SetPixelFast(px, py, newState);
                }
                else if (ovf != null)
                {
                    if (!ShouldDitherPixel(px, py, ditherPattern)) continue;
                    if (IsClipped(px, py, clip)) continue;

                    ovf.SetPixelFast(px, py, newState);
                }
            }
        }

        /// <summary>
        /// Rasterizes a continuous swept capsule (line segment dilated by a circular brush) over the stroke's
        /// bounding box in a single pass, replacing discrete multi-step Bresenham stamp evaluation.
        /// </summary>
        private static void RasterizeSweptCapsule(
            SpriteState state,
            OverflowPixelBuffer? ovf,
            int x0, int y0, int x1, int y1,
            bool newState,
            int brushSize,
            IPixelClip? clip,
            DitherPattern? ditherPattern)
        {
            int w = state.Width;
            int h = state.Height;
            bool[] pixels = state.Pixels;

            int half = (brushSize - 1) / 2;
            int extra = brushSize % 2 == 0 ? 1 : 0;
            float centerOffset = brushSize % 2 == 0 ? 0.5f : 0.0f;
            float radius = (brushSize - 0.5f) / 2.0f;
            float rSq = radius * radius;

            int minBoxX = Math.Min(x0, x1) - half;
            int maxBoxX = Math.Max(x0, x1) + half + extra;
            int minBoxY = Math.Min(y0, y1) - half;
            int maxBoxY = Math.Max(y0, y1) + half + extra;

            float dx = (float)(x1 - x0);
            float dy = (float)(y1 - y0);
            float lenSq = dx * dx + dy * dy;
            float invLenSq = lenSq > 0f ? 1.0f / lenSq : 0f;

            // 1. In-canvas pixels (fast path: zero canvas-bounds branching, direct array indexing)
            int inMinX = Math.Max(0, minBoxX);
            int inMaxX = Math.Min(w - 1, maxBoxX);
            int inMinY = Math.Max(0, minBoxY);
            int inMaxY = Math.Min(h - 1, maxBoxY);

            if (inMinX <= inMaxX && inMinY <= inMaxY)
            {
                for (int py = inMinY; py <= inMaxY; py++)
                {
                    float uy = (float)(py - y0) - centerOffset;
                    int rowOffset = py * w;

                    for (int px = inMinX; px <= inMaxX; px++)
                    {
                        int idx = rowOffset + px;

                        // Fast 1-cycle early exits before math calculations
                        if (pixels[idx] == newState) continue;
                        if (ditherPattern.HasValue && !ShouldDitherPixel(px, py, ditherPattern.Value)) continue;
                        if (IsClipped(px, py, clip)) continue;

                        float ux = (float)(px - x0) - centerOffset;
                        float t = lenSq > 0f ? (ux * dx + uy * dy) * invLenSq : 0f;
                        if (t < 0f) t = 0f;
                        else if (t > 1f) t = 1f;

                        float qx = ux - t * dx;
                        float qy = uy - t * dy;

                        if (qx * qx + qy * qy <= rSq)
                        {
                            pixels[idx] = newState;
                            ovf?.SetPixelFast(px, py, newState);
                        }
                    }
                }
            }

            // 2. Off-canvas pixels (only if overflow buffer is present and bounds extend beyond canvas)
            if (ovf != null && (minBoxX < 0 || maxBoxX >= w || minBoxY < 0 || maxBoxY >= h))
            {
                for (int py = minBoxY; py <= maxBoxY; py++)
                {
                    bool pyInCanvas = (uint)py < (uint)h;
                    float uy = (float)(py - y0) - centerOffset;

                    for (int px = minBoxX; px <= maxBoxX; px++)
                    {
                        if (pyInCanvas && (uint)px < (uint)w) continue;

                        if (ovf.GetPixelFast(px, py) == newState) continue;
                        if (ditherPattern.HasValue && !ShouldDitherPixel(px, py, ditherPattern.Value)) continue;
                        if (IsClipped(px, py, clip)) continue;

                        float ux = (float)(px - x0) - centerOffset;
                        float t = lenSq > 0f ? (ux * dx + uy * dy) * invLenSq : 0f;
                        if (t < 0f) t = 0f;
                        else if (t > 1f) t = 1f;

                        float qx = ux - t * dx;
                        float qy = uy - t * dy;

                        if (qx * qx + qy * qy <= rSq)
                        {
                            ovf.SetPixelFast(px, py, newState);
                        }
                    }
                }
            }
        }

        // ── Line ──────────────────────────────────────────────────────────

        public void DrawLine(SpriteState state, int x0, int y0, int x1, int y1, bool newState, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            ovf?.EnsureCapacity(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (clip == null || clip.IsPixelInClip(x0, y0))
                {
                    if ((uint)x0 < (uint)state.Width && (uint)y0 < (uint)state.Height)
                    {
                        int idx = y0 * state.Width + x0;
                        state.Pixels[idx] = newState;
                        ovf?.SetPixelFast(x0, y0, newState);
                    }
                    else if (ovf != null)
                    {
                        ovf.SetPixelFast(x0, y0, newState);
                    }
                }

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        public void DrawLine(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            if (brushSize <= 1)
            {
                DrawLine(state, x0, y0, x1, y1, newState, clip);
                return;
            }

            var ovf = GetActiveOverflowBuffer(state);
            int margin = GetBrushMargin(brushSize, shape, angleDeg);
            ovf?.EnsureCapacity(
                Math.Min(x0, x1) - margin,
                Math.Min(y0, y1) - margin,
                Math.Max(x0, x1) + margin,
                Math.Max(y0, y1) + margin);

            if (shape == BrushShape.Circle && brushSize > 2)
            {
                RasterizeSweptCapsule(state, ovf, x0, y0, x1, y1, newState, brushSize, clip, ditherPattern: null);
                return;
            }

            // Fetch cached offsets array once for the entire line
            var offsets = GetStampOffsetsArray(brushSize, shape, angleDeg);

            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                StampOffsets(state, ovf, x0, y0, newState, offsets, clip);
                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                bool stepX = false, stepY = false;
                if (e2 > -dy) { err -= dy; x0 += sx; stepX = true; }
                if (e2 < dx) { err += dx; y0 += sy; stepY = true; }
                
                if (stepX && stepY && shape == BrushShape.Line)
                {
                    StampOffsets(state, ovf, x0 - sx, y0, newState, offsets, clip);
                }
            }
        }

        public void DrawLineDithered(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize, DitherPattern ditherPattern, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            if (brushSize <= 1)
            {
                ovf?.EnsureCapacity(Math.Min(x0, x1), Math.Min(y0, y1), Math.Max(x0, x1), Math.Max(y0, y1));
                // Single-pixel Bresenham with dither gate
                int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
                int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
                int err = dx - dy;

                while (true)
                {
                    if (ShouldDitherPixel(x0, y0, ditherPattern))
                    {
                        if (clip == null || clip.IsPixelInClip(x0, y0))
                        {
                            if ((uint)x0 < (uint)state.Width && (uint)y0 < (uint)state.Height)
                            {
                                int idx = y0 * state.Width + x0;
                                state.Pixels[idx] = newState;
                                ovf?.SetPixelFast(x0, y0, newState);
                            }
                            else if (ovf != null)
                            {
                                ovf.SetPixelFast(x0, y0, newState);
                            }
                        }
                    }

                    if (x0 == x1 && y0 == y1) break;

                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
                return;
            }

            int margin = GetBrushMargin(brushSize, shape, angleDeg);
            ovf?.EnsureCapacity(
                Math.Min(x0, x1) - margin,
                Math.Min(y0, y1) - margin,
                Math.Max(x0, x1) + margin,
                Math.Max(y0, y1) + margin);

            if (shape == BrushShape.Circle && brushSize > 2)
            {
                RasterizeSweptCapsule(state, ovf, x0, y0, x1, y1, newState, brushSize, clip, ditherPattern);
                return;
            }

            var offsets = GetStampOffsetsArray(brushSize, shape, angleDeg);

            int ldx = Math.Abs(x1 - x0), ldy = Math.Abs(y1 - y0);
            int lsx = x0 < x1 ? 1 : -1, lsy = y0 < y1 ? 1 : -1;
            int lerr = ldx - ldy;

            while (true)
            {
                StampOffsetsDithered(state, ovf, x0, y0, newState, offsets, clip, ditherPattern);
                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * lerr;
                bool stepX = false, stepY = false;
                if (e2 > -ldy) { lerr -= ldy; x0 += lsx; stepX = true; }
                if (e2 < ldx) { lerr += ldx; y0 += lsy; stepY = true; }
                
                if (stepX && stepY && shape == BrushShape.Line)
                {
                    StampOffsetsDithered(state, ovf, x0 - lsx, y0, newState, offsets, clip, ditherPattern);
                }
            }
        }

        // ── Rectangle ─────────────────────────────────────────────────────

        public void DrawRectangle(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            int margin = brushSize > 1 ? GetBrushMargin(brushSize, shape, angleDeg) : 0;
            if (ovf != null)
            {
                int minBoxX = Math.Min(x0, x1) - margin;
                int maxBoxX = Math.Max(x0, x1) + margin;
                int minBoxY = Math.Min(y0, y1) - margin;
                int maxBoxY = Math.Max(y0, y1) + margin;
                ovf.EnsureCapacity(minBoxX, minBoxY, maxBoxX, maxBoxY);
            }
            var offsets = brushSize > 1 ? GetStampOffsetsArray(brushSize, shape, angleDeg) : null;
            void Stamp(int cx, int cy)
            {
                if (offsets == null)
                {
                    WritePixel(state, ovf, cx, cy, newState, clip);
                }
                else
                {
                    StampOffsets(state, ovf, cx, cy, newState, offsets, clip);
                }
            }

            if (x0 == x1 && y0 == y1)
            {
                // Degenerate case: single pixel
                Stamp(x0, y0);
                ovf?.InvalidateViewCache();
                return;
            }

            _ = state.Width;
            _ = state.Height;

            int minX = Math.Min(x0, x1), maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1), maxY = Math.Max(y0, y1);

            for (int x = minX; x <= maxX; x++)
            {
                Stamp(x, minY);
                Stamp(x, maxY);
            }
            for (int y = minY + 1; y < maxY; y++)
            {
                Stamp(minX, y);
                Stamp(maxX, y);
            }
            ovf?.InvalidateViewCache();
        }

        // ── Filled Rectangle ──────────────────────────────────────────────

        public void DrawFilledRectangle(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            if (brushSize > 1)
            {
                DrawRectangle(state, x0, y0, x1, y1, newState, brushSize, shape, angleDeg, clip);
            }
            int width = state.Width;
            int height = state.Height;

            // Math.Clamp(x, 0, width-1) throws ArgumentOutOfRangeException when width == 0.
            if (width == 0 || height == 0) return;
            var ovf = GetActiveOverflowBuffer(state);

            int minX = Math.Min(x0, x1);
            int maxX = Math.Max(x0, x1);
            int minY = Math.Min(y0, y1);
            int maxY = Math.Max(y0, y1);

            if (ovf == null)
            {
                if (maxX < 0 || minX >= width || maxY < 0 || minY >= height) return;

                minX = Math.Max(minX, 0);
                maxX = Math.Min(maxX, width - 1);
                minY = Math.Max(minY, 0);
                maxY = Math.Min(maxY, height - 1);
            }

            for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                    WritePixel(state, ovf, x, y, newState, clip);
                    
            ovf?.InvalidateViewCache();
        }

        // ── Ellipse ───────────────────────────────────────────────────────

        public void DrawEllipse(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            var ovf = GetActiveOverflowBuffer(state);
            int margin = brushSize > 1 ? GetBrushMargin(brushSize, shape, angleDeg) : 0;
            if (ovf != null)
            {
                int minBoxX = Math.Min(x0, x1) - margin;
                int maxBoxX = Math.Max(x0, x1) + margin;
                int minBoxY = Math.Min(y0, y1) - margin;
                int maxBoxY = Math.Max(y0, y1) + margin;
                ovf.EnsureCapacity(minBoxX, minBoxY, maxBoxX, maxBoxY);
            }
            var offsets = brushSize > 1 ? GetStampOffsetsArray(brushSize, shape, angleDeg) : null;
            void Stamp(int cx, int cy)
            {
                if (offsets == null)
                {
                    WritePixel(state, ovf, cx, cy, newState, clip);
                }
                else
                {
                    StampOffsets(state, ovf, cx, cy, newState, offsets, clip);
                }
            }

            if (x0 == x1 && y0 == y1)
            {
                Stamp(x0, y0);
                ovf?.InvalidateViewCache();
                return;
            }

            int a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
            long dx = 4L * (1 - a) * b * b, dy = 4L * (b1 + 1) * a * a;
            long err = dx + dy + (long)b1 * a * a, e2;

            if (x0 > x1) { x0 = x1; x1 += a; }
            if (y0 > y1) y0 = y1;
            y0 += (b + 1) / 2;
            y1 = y0 - b1;
            a *= 8 * a;
            b1 = 8 * b * b;

            do
            {
                Stamp(x1, y0); Stamp(x0, y0);
                Stamp(x0, y1); Stamp(x1, y1);
                e2 = 2 * err;
                if (e2 <= dy) { y0++; y1--; err += dy += a; }
                if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
            }
            while (x0 <= x1);

            while (y0 - y1 < b)
            {
                Stamp(x0 - 1, y0); Stamp(x1 + 1, y0);
                Stamp(x0 - 1, y1); Stamp(x1 + 1, y1);
                y0++; y1--;
            }
            ovf?.InvalidateViewCache();
        }

        // ── Filled Ellipse ────────────────────────────────────────────────

        public void DrawFilledEllipse(SpriteState state, int x0, int y0, int x1, int y1, bool newState, int brushSize = 1, BrushShape shape = BrushShape.Circle, int angleDeg = 0, IPixelClip? clip = null)
        {
            if (brushSize > 1)
            {
                DrawEllipse(state, x0, y0, x1, y1, newState, brushSize, shape, angleDeg, clip);
            }
            int width = state.Width;
            int height = state.Height;
            var ovf = GetActiveOverflowBuffer(state);

            if (x0 == x1 && y0 == y1)
            {
                WritePixel(state, ovf, x0, y0, newState, clip);
                ovf?.InvalidateViewCache();
                return;
            }

            int a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
            long dx = 4L * (1 - a) * b * b, dy = 4L * (b1 + 1) * a * a;
            long err = dx + dy + (long)b1 * a * a, e2;

            if (x0 > x1) { x0 = x1; x1 += a; }
            if (y0 > y1) y0 = y1;
            y0 += (b + 1) / 2;
            y1 = y0 - b1;
            a *= 8 * a;
            b1 = 8 * b * b;

            void FillRow(int lx, int rx, int py)
            {
                if (ovf == null && (py < 0 || py >= height)) return;
                
                int minX = Math.Min(lx, rx);
                int maxX = Math.Max(lx, rx);
                
                int clampL = minX;
                int clampR = maxX;
                
                if (ovf == null)
                {
                    if (maxX < 0 || minX >= width) return;
                    clampL = Math.Max(minX, 0);
                    clampR = Math.Min(maxX, width - 1);
                }
                
                for (int px = clampL; px <= clampR; px++)
                    WritePixel(state, ovf, px, py, newState, clip);
            }

            do
            {
                FillRow(x0, x1, y0);
                FillRow(x0, x1, y1);
                e2 = 2 * err;
                if (e2 <= dy) { y0++; y1--; err += dy += a; }
                if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
            }
            while (x0 <= x1);

            while (y0 - y1 < b)
            {
                FillRow(x0 - 1, x1 + 1, y0);
                FillRow(x0 - 1, x1 + 1, y1);
                y0++; y1--;
            }
            ovf?.InvalidateViewCache();
        }

        // ── Grid operations ───────────────────────────────────────────────

        public void ShiftGrid(SpriteState state, int offsetX, int offsetY)
        {
            int total = state.Width * state.Height;

            // FIX: Rent a buffer from the shared pool to eliminate Garbage Collection spikes.
            // This preserves thread-safety and avoids the shared mutable state problem.
            bool[] buffer = System.Buffers.ArrayPool<bool>.Shared.Rent(total);

            try
            {
                // CRITICAL: Rented arrays can contain garbage data from previous operations. 
                // We must zero it out so our shifting logic starts with a clean slate.
                Array.Clear(buffer, 0, total);

                for (int y = 0; y < state.Height; y++)
                {
                    for (int x = 0; x < state.Width; x++)
                    {
                        if (!state.Pixels[(y * state.Width) + x]) continue;

                        int newX = ((x + offsetX) % state.Width + state.Width) % state.Width;
                        int newY = ((y + offsetY) % state.Height + state.Height) % state.Height;
                        buffer[(newY * state.Width) + newX] = true;
                    }
                }

                // ArrayPool might return an array larger than requested. 
                // Copy exactly 'total' elements to avoid bounds issues.
                Array.Copy(buffer, state.Pixels, total);
            }
            finally
            {
                // Guarantee the buffer is returned to the pool
                System.Buffers.ArrayPool<bool>.Shared.Return(buffer);
            }
        }

        public static void ShiftGridOverflow(OverflowPixelBuffer buffer, int offsetX, int offsetY)
        {
            buffer.ShiftContent(offsetX, offsetY);
        }

        public void InvertGrid(SpriteState state)
        {
            var buffer = state.Frames[state.ActiveFrameIndex].LayerPixels[state.ActiveLayerIndex];
            if (buffer is Hexprite.Core.OverflowPixelBuffer ovf)
            {
                ovf.Invert();
                state.Pixels = ovf.GetMonochromeData();
            }
            else
            {
                for (int i = 0; i < state.Pixels.Length; i++)
                    state.Pixels[i] = !state.Pixels[i];
            }
        }

        /// <inheritdoc />
        public bool[] RotatePixels(bool[] src, int srcW, int srcH, RotationDirection dir)
        {
            if (src == null || src.Length != srcW * srcH)
                throw new ArgumentException("Source buffer must match srcW × srcH.", nameof(src));

            switch (dir)
            {
                case RotationDirection.OneEighty:
                {
                    var dst = new bool[srcW * srcH];
                    for (int ny = 0; ny < srcH; ny++)
                    {
                        for (int nx = 0; nx < srcW; nx++)
                        {
                            int oy = srcH - 1 - ny;
                            int ox = srcW - 1 - nx;
                            dst[ny * srcW + nx] = src[oy * srcW + ox];
                        }
                    }
                    return dst;
                }

                case RotationDirection.Clockwise90:
                {
                    int newW = srcH;
                    int newH = srcW;
                    var dst = new bool[newW * newH];
                    for (int ny = 0; ny < newH; ny++)
                    {
                        for (int nx = 0; nx < newW; nx++)
                        {
                            int ox = ny;
                            int oy = srcH - 1 - nx;
                            dst[ny * newW + nx] = src[oy * srcW + ox];
                        }
                    }
                    return dst;
                }

                case RotationDirection.CounterClockwise90:
                {
                    int newW = srcH;
                    int newH = srcW;
                    var dst = new bool[newW * newH];
                    for (int ny = 0; ny < newH; ny++)
                    {
                        for (int nx = 0; nx < newW; nx++)
                        {
                            int ox = srcW - 1 - ny;
                            int oy = nx;
                            dst[ny * newW + nx] = src[oy * srcW + ox];
                        }
                    }
                    return dst;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(dir));
            }
        }

        public bool[] FlipPixels(bool[] src, int srcW, int srcH, FlipDirection dir)
        {
            if (src == null || src.Length != srcW * srcH)
                throw new ArgumentException("Source buffer must match srcW × srcH.", nameof(src));

            var dst = new bool[srcW * srcH];

            switch (dir)
            {
                case FlipDirection.Horizontal:
                {
                    // Mirror around vertical axis: (x, y) -> (srcW-1-x, y)
                    for (int y = 0; y < srcH; y++)
                    {
                        int row = y * srcW;
                        for (int x = 0; x < srcW; x++)
                        {
                            int nx = srcW - 1 - x;
                            dst[row + nx] = src[row + x];
                        }
                    }
                    break;
                }

                case FlipDirection.Vertical:
                {
                    // Mirror around horizontal axis: (x, y) -> (x, srcH-1-y)
                    for (int y = 0; y < srcH; y++)
                    {
                        int ny = srcH - 1 - y;
                        for (int x = 0; x < srcW; x++)
                        {
                            dst[ny * srcW + x] = src[y * srcW + x];
                        }
                    }
                    break;
                }

                default:
                    throw new ArgumentOutOfRangeException(nameof(dir));
            }

            return dst;
        }

        private static readonly int[,] Bayer4x4 = new int[4, 4] {
            { 0,  8,  2, 10 },
            { 12, 4, 14, 6  },
            { 3,  11, 1, 9  },
            { 15, 7, 13, 5  },
        };

        public void DrawDitherGradient(SpriteState state, int x0, int y0, int x1, int y1, bool newState, IPixelClip? clip = null)
        {
            if (state == null || state.Pixels == null) return;
            
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lengthSq = dx * dx + dy * dy;
            var ovf = GetActiveOverflowBuffer(state);
            
            for (int y = 0; y < state.Height; y++)
            {
                for (int x = 0; x < state.Width; x++)
                {
                    float t = 1.0f;
                    if (lengthSq > 0.0001f)
                    {
                        float dot = (x - x0) * dx + (y - y0) * dy;
                        t = dot / lengthSq;
                    }
                    
                    t = Math.Clamp(t, 0.0f, 1.0f);
                    float threshold = t * 17.0f;
                    
                    int bayerValue = Bayer4x4[y % 4, x % 4];
                    bool pixelState = bayerValue < threshold - 1.0f;
                    
                    if (pixelState)
                    {
                        WritePixel(state, ovf, x, y, newState, clip);
                    }
                }
            }
            ovf?.InvalidateViewCache();
        }

        public void OutlineLayer(SpriteState state, OutlineSettings settings, IPixelClip? clip = null)
        {
            if (state == null || state.Pixels == null) return;

            int width = state.Width;
            int height = state.Height;
            bool[] original = state.Pixels;

            float innerBound = settings.Padding;
            float outerBound = settings.Padding + settings.Thickness;

            bool[] newPixels = new bool[original.Length];

            if (settings.Placement == OutlinePlacement.Outside)
            {
                // For each empty pixel, compute distance to nearest filled pixel.
                // Outline ring: innerBound < dist <= outerBound
                float[] dist = ComputeDistanceField(original, width, height, settings.Shape, towardSet: true);

                for (int i = 0; i < original.Length; i++)
                {
                    int x = i % width;
                    int y = i / width;

                    if (clip != null && !clip.IsPixelInClip(x, y))
                    {
                        newPixels[i] = original[i];
                        continue;
                    }

                    if (original[i])
                    {
                        newPixels[i] = true; // preserve original shape
                    }
                    else if (dist[i] > innerBound && dist[i] <= outerBound)
                    {
                        newPixels[i] = true; // outline ring
                    }
                    // else stays false
                }
            }
            else // Inside
            {
                // For each filled pixel, compute distance to nearest empty pixel (or canvas edge).
                // Subtract ring: innerBound < dist <= outerBound
                float[] dist = ComputeDistanceField(original, width, height, settings.Shape, towardSet: false);

                for (int i = 0; i < original.Length; i++)
                {
                    int x = i % width;
                    int y = i / width;

                    if (clip != null && !clip.IsPixelInClip(x, y))
                    {
                        newPixels[i] = original[i];
                        continue;
                    }

                    if (!original[i])
                    {
                        newPixels[i] = false; // was empty, stays empty
                    }
                    else if (dist[i] > innerBound && dist[i] <= outerBound)
                    {
                        newPixels[i] = false; // subtract the outline ring
                    }
                    else
                    {
                        newPixels[i] = original[i]; // keep as-is
                    }
                }
            }

            Array.Copy(newPixels, state.Pixels, newPixels.Length);
            
            var buffer = state.Frames[state.ActiveFrameIndex].LayerPixels[state.ActiveLayerIndex];
            buffer.WriteMonochromeData(state.Pixels);
        }

        // ── Distance field computation ────────────────────────────────

        /// <summary>
        /// Computes a distance field for the given pixel buffer.
        /// When <paramref name="towardSet"/> is true, each unset pixel gets
        /// its distance to the nearest set pixel (for outside outlines).
        /// When false, each set pixel gets its distance to the nearest unset
        /// pixel or canvas edge (for inside outlines).
        /// Circle shape uses Euclidean distance; Square shape uses Chebyshev distance.
        /// </summary>
        private static float[] ComputeDistanceField(bool[] pixels, int width, int height,
                                             OutlineShape shape, bool towardSet)
        {
            // Build the "source" mask: pixels that have distance 0
            bool[] source = new bool[pixels.Length];
            if (towardSet)
            {
                // Sources are the set pixels; we measure distance FROM unset pixels
                for (int i = 0; i < pixels.Length; i++)
                    source[i] = pixels[i];
            }
            else
            {
                // Sources are unset pixels AND canvas edges; we measure distance FROM set pixels
                // First mark all unset pixels as sources
                for (int i = 0; i < pixels.Length; i++)
                    source[i] = !pixels[i];
                // Also mark "virtual" edge sources — handled during distance computation
                // by treating out-of-bounds as distance 0 (see Chebyshev) or by padding (see EDT)
            }

            return shape == OutlineShape.Circle
                ? ComputeEuclideanDistanceField(source, width, height, !towardSet)
                : ComputeChebyshevDistanceField(source, width, height, !towardSet);
        }

        // ── Chebyshev Distance Transform (L∞) ────────────────────────

        /// <summary>
        /// Exact Chebyshev (L∞) distance transform. Returns the Chebyshev distance
        /// from each non-source pixel to the nearest source pixel.
        /// Source pixels get distance 0.
        /// When <paramref name="edgeIsSource"/> is true, canvas boundaries
        /// are treated as source (distance 0).
        /// </summary>
        private static float[] ComputeChebyshevDistanceField(bool[] source, int width, int height,
                                                       bool edgeIsSource)
        {
            int n = width * height;
            float INF = width + height + 1;
            float[] d = new float[n];

            // Initialize
            for (int i = 0; i < n; i++)
                d[i] = source[i] ? 0 : INF;

            // Forward pass (top-left to bottom-right)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int idx = y * width + x;
                    if (d[idx] == 0) continue;

                    // Check 4 neighbors: left, top-left, top, top-right
                    float best = d[idx];

                    if (x > 0) best = Math.Min(best, d[idx - 1] + 1);
                    else if (edgeIsSource) best = 1;

                    if (y > 0)
                    {
                        best = Math.Min(best, d[idx - width] + 1);
                        if (x > 0) best = Math.Min(best, d[(y - 1) * width + (x - 1)] + 1);
                        else if (edgeIsSource) best = Math.Min(best, 1);
                        if (x < width - 1) best = Math.Min(best, d[(y - 1) * width + (x + 1)] + 1);
                    }
                    else if (edgeIsSource)
                    {
                        best = 1;
                    }

                    d[idx] = best;
                }
            }

            // Backward pass (bottom-right to top-left)
            for (int y = height - 1; y >= 0; y--)
            {
                for (int x = width - 1; x >= 0; x--)
                {
                    int idx = y * width + x;
                    if (d[idx] == 0) continue;

                    float best = d[idx];

                    if (x < width - 1) best = Math.Min(best, d[idx + 1] + 1);
                    else if (edgeIsSource) best = Math.Min(best, 1);

                    if (y < height - 1)
                    {
                        best = Math.Min(best, d[idx + width] + 1);
                        if (x < width - 1) best = Math.Min(best, d[(y + 1) * width + (x + 1)] + 1);
                        if (x > 0) best = Math.Min(best, d[(y + 1) * width + (x - 1)] + 1);
                        else if (edgeIsSource) best = Math.Min(best, 1);
                    }
                    else if (edgeIsSource)
                    {
                        best = Math.Min(best, 1);
                    }

                    d[idx] = best;
                }
            }

            return d;
        }

        // ── Euclidean Distance Transform (Felzenszwalb–Huttenlocher) ─

        /// <summary>
        /// Exact Euclidean distance transform. Returns the Euclidean distance
        /// from each non-source pixel to the nearest source pixel.
        /// Uses the Felzenszwalb–Huttenlocher O(n) algorithm.
        /// When <paramref name="edgeIsSource"/> is true, a 1-pixel border of
        /// source pixels is added around the canvas before computing.
        /// </summary>
        private static float[] ComputeEuclideanDistanceField(bool[] source, int width, int height,
                                                       bool edgeIsSource)
        {
            // If edgeIsSource, pad the grid with a 1-pixel source border
            int pw, ph;
            float[] grid;

            if (edgeIsSource)
            {
                pw = width + 2;
                ph = height + 2;
                grid = new float[pw * ph];
                float INF = (float)(pw * pw + ph * ph);

                // Fill padded grid
                for (int py = 0; py < ph; py++)
                {
                    for (int px = 0; px < pw; px++)
                    {
                        if (py == 0 || py == ph - 1 || px == 0 || px == pw - 1)
                        {
                            grid[py * pw + px] = 0; // border = source
                        }
                        else
                        {
                            int ox = px - 1;
                            int oy = py - 1;
                            grid[py * pw + px] = source[oy * width + ox] ? 0 : INF;
                        }
                    }
                }
            }
            else
            {
                pw = width;
                ph = height;
                float INF = (float)(pw * pw + ph * ph);
                grid = new float[pw * ph];
                for (int i = 0; i < grid.Length; i++)
                    grid[i] = source[i] ? 0 : INF;
            }

            // Compute squared EDT in-place on grid
            ComputeSquaredEDT2D(grid, pw, ph);

            // Extract results and take square root
            float[] result = new float[width * height];
            if (edgeIsSource)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                        result[y * width + x] = (float)Math.Sqrt(grid[(y + 1) * pw + (x + 1)]);
            }
            else
            {
                for (int i = 0; i < result.Length; i++)
                    result[i] = (float)Math.Sqrt(grid[i]);
            }

            return result;
        }

        /// <summary>
        /// In-place 2D squared Euclidean distance transform using
        /// the Felzenszwalb–Huttenlocher separable parabola algorithm.
        /// Input/output: grid[y * w + x] where 0 = source, large = non-source.
        /// After this call, grid contains squared Euclidean distances.
        /// </summary>
        private static void ComputeSquaredEDT2D(float[] grid, int w, int h)
        {
            int maxDim = Math.Max(w, h);
            float[] f = new float[maxDim];
            int[] v = new int[maxDim];
            float[] z = new float[maxDim + 1];
            float[] output = new float[maxDim];

            // Phase 1: columns (along y for each x)
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    f[y] = grid[y * w + x];

                EDT1D(f, h, v, z, output);

                for (int y = 0; y < h; y++)
                    grid[y * w + x] = output[y];
            }

            // Phase 2: rows (along x for each y)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                    f[x] = grid[y * w + x];

                EDT1D(f, w, v, z, output);

                for (int x = 0; x < w; x++)
                    grid[y * w + x] = output[x];
            }
        }

        /// <summary>
        /// 1D squared distance transform using lower envelope of parabolas.
        /// Input f[0..n-1], output[0..n-1] where output[q] = min_p { (q-p)² + f[p] }.
        /// </summary>
        private static void EDT1D(float[] f, int n, int[] v, float[] z, float[] output)
        {
            v[0] = 0;
            z[0] = float.NegativeInfinity;
            z[1] = float.PositiveInfinity;
            int k = 0;

            for (int q = 1; q < n; q++)
            {
                float s;
                while (true)
                {
                    int vk = v[k];
                    // Intersection of parabola at q and parabola at v[k]
                    s = ((f[q] + (float)q * q) - (f[vk] + (float)vk * vk)) / (2.0f * q - 2.0f * vk);
                    if (s > z[k]) break;
                    k--;
                }
                k++;
                v[k] = q;
                z[k] = s;
                z[k + 1] = float.PositiveInfinity;
            }

            k = 0;
            for (int q = 0; q < n; q++)
            {
                while (z[k + 1] < q)
                    k++;
                int vk = v[k];
                output[q] = (float)(q - vk) * (q - vk) + f[vk];
            }
        }
    }
}
