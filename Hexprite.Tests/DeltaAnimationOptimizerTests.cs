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
