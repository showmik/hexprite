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
