using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class SelectionSnapshotTests
{
    [Fact]
    public void Clone_BasicProperties_AreCopied()
    {
        var original = new SelectionSnapshot
        {
            HasActiveSelection = true,
            IsSelecting = true,
            IsFloating = true,
            IsDragging = true,
            IsTransforming = true,
            ActiveTransformHandle = TransformHandle.SE,
            MinX = 10,
            MaxX = 20,
            MinY = 5,
            MaxY = 15,
            FloatingX = 12,
            FloatingY = 8,
            FloatingWidth = 8,
            FloatingHeight = 6,
            OriginalFloatingX = 10,
            OriginalFloatingY = 5,
            OriginalFloatingWidth = 10,
            OriginalFloatingHeight = 10,
            PristineFloatingWidth = 15,
            PristineFloatingHeight = 20
        };

        var clone = original.Clone();

        Assert.Equal(original.HasActiveSelection, clone.HasActiveSelection);
        Assert.Equal(original.IsSelecting, clone.IsSelecting);
        Assert.Equal(original.IsFloating, clone.IsFloating);
        Assert.Equal(original.IsDragging, clone.IsDragging);
        Assert.Equal(original.IsTransforming, clone.IsTransforming);
        Assert.Equal(original.ActiveTransformHandle, clone.ActiveTransformHandle);
        Assert.Equal(original.MinX, clone.MinX);
        Assert.Equal(original.MaxX, clone.MaxX);
        Assert.Equal(original.MinY, clone.MinY);
        Assert.Equal(original.MaxY, clone.MaxY);
        Assert.Equal(original.FloatingX, clone.FloatingX);
        Assert.Equal(original.FloatingY, clone.FloatingY);
        Assert.Equal(original.FloatingWidth, clone.FloatingWidth);
        Assert.Equal(original.FloatingHeight, clone.FloatingHeight);
        Assert.Equal(original.PristineFloatingWidth, clone.PristineFloatingWidth);
        Assert.Equal(original.PristineFloatingHeight, clone.PristineFloatingHeight);
    }

    [Fact]
    public void Clone_Mask_IsDeepCopied()
    {
        var original = new SelectionSnapshot
        {
            Mask = new bool[,] { { true, false }, { false, true } }
        };

        var clone = original.Clone();

        Assert.NotSame(original.Mask, clone.Mask);
        Assert.Equal(original.Mask, clone.Mask);

        // Modify clone and verify original is unchanged
        Assert.NotNull(clone.Mask);
        Assert.NotNull(original.Mask);
        clone.Mask[0, 0] = false;
        Assert.True(original.Mask[0, 0]);
    }

    [Fact]
    public void Clone_FloatingPixels_IsDeepCopied()
    {
        var original = new SelectionSnapshot
        {
            FloatingPixels = new bool[,] { { true, false }, { false, true } }
        };

        var clone = original.Clone();

        Assert.NotSame(original.FloatingPixels, clone.FloatingPixels);
        Assert.Equal(original.FloatingPixels, clone.FloatingPixels);

        Assert.NotNull(clone.FloatingPixels);
        Assert.NotNull(original.FloatingPixels);
        clone.FloatingPixels[0, 0] = false;
        Assert.True(original.FloatingPixels[0, 0]);
    }

    [Fact]
    public void Clone_PristineFloatingPixels_IsDeepCopied()
    {
        var original = new SelectionSnapshot
        {
            PristineFloatingPixels = new bool[,] { { true, false }, { false, true } }
        };

        var clone = original.Clone();

        Assert.NotSame(original.PristineFloatingPixels, clone.PristineFloatingPixels);
        Assert.Equal(original.PristineFloatingPixels, clone.PristineFloatingPixels);

        Assert.NotNull(clone.PristineFloatingPixels);
        Assert.NotNull(original.PristineFloatingPixels);
        clone.PristineFloatingPixels[0, 0] = false;
        Assert.True(original.PristineFloatingPixels[0, 0]);
    }

    [Fact]
    public void Clone_OriginalFloatingPixels_AreDeepCopied_ByDefault()
    {
        var original = new SelectionSnapshot
        {
            OriginalFloatingPixels = new bool[,] { { true, false }, { false, true } }
        };

        var clone = original.Clone(includeOriginalFloatingPixels: true);

        Assert.NotSame(original.OriginalFloatingPixels, clone.OriginalFloatingPixels);
        Assert.Equal(original.OriginalFloatingPixels, clone.OriginalFloatingPixels);
    }

    [Fact]
    public void Clone_OriginalFloatingPixels_Skipped_WhenFalse()
    {
        var original = new SelectionSnapshot
        {
            OriginalFloatingPixels = new bool[,] { { true, false }, { false, true } }
        };

        var clone = original.Clone(includeOriginalFloatingPixels: false);

        Assert.Null(clone.OriginalFloatingPixels);
    }

    [Fact]
    public void Clone_LassoPoints_AreDeepCopied()
    {
        var original = new SelectionSnapshot
        {
            LassoPoints =
            [
                new PixelPoint(0, 0),
                new PixelPoint(10, 5),
                new PixelPoint(20, 10)
            ]
        };

        var clone = original.Clone();

        Assert.NotSame(original.LassoPoints, clone.LassoPoints);
        Assert.Equal(original.LassoPoints.Count, clone.LassoPoints.Count);
        Assert.Equal(original.LassoPoints[0], clone.LassoPoints[0]);
        Assert.Equal(original.LassoPoints[1], clone.LassoPoints[1]);
        Assert.Equal(original.LassoPoints[2], clone.LassoPoints[2]);

        // Modify clone's list
        clone.LassoPoints.Add(new PixelPoint(30, 15));
        Assert.Equal(3, original.LassoPoints.Count);
        Assert.Equal(4, clone.LassoPoints.Count);
    }

    [Fact]
    public void Clone_NullMask_HandledGracefully()
    {
        var original = new SelectionSnapshot
        {
            Mask = null,
            FloatingPixels = null,
            OriginalFloatingPixels = null,
            PristineFloatingPixels = null
        };

        var clone = original.Clone();

        Assert.Null(clone.Mask);
        Assert.Null(clone.FloatingPixels);
        Assert.Null(clone.OriginalFloatingPixels);
        Assert.Null(clone.PristineFloatingPixels);
    }

    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var snapshot = new SelectionSnapshot();

        Assert.False(snapshot.HasActiveSelection);
        Assert.False(snapshot.IsSelecting);
        Assert.False(snapshot.IsFloating);
        Assert.False(snapshot.IsDragging);
        Assert.False(snapshot.IsTransforming);
        Assert.Equal(TransformHandle.None, snapshot.ActiveTransformHandle);
        Assert.Equal(0, snapshot.MinX);
        Assert.Equal(0, snapshot.MaxX);
        Assert.Equal(0, snapshot.MinY);
        Assert.Equal(0, snapshot.MaxY);
        Assert.Null(snapshot.Mask);
        Assert.Null(snapshot.FloatingPixels);
        Assert.Empty(snapshot.LassoPoints);
    }
}
