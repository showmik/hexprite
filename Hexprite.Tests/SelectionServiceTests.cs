using System;
using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

/// <summary>
/// Exhaustive coverage of <see cref="SelectionService"/>: the build state machine
/// (rectangle / lasso / magic-wand), boolean combine modes, floating-layer
/// lift/commit/delete, drag, clipboard, snapshot round-trip, and event fan-out.
/// </summary>
[Trait("Category", "Unit")]
    public class SelectionServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private sealed class EventCounter
    {
        public int Count;
    }

    private static (SelectionService Service, EventCounter Events) NewService()
    {
        var svc = new SelectionService();
        var ec = new EventCounter();
        svc.SelectionChanged += (_, _) => ec.Count++;
        return (svc, ec);
    }

    private static SpriteState MakeState(int w, int h, params (int x, int y)[] onPixels)
    {
        var s = new SpriteState(w, h);
        foreach (var (x, y) in onPixels)
            s.Pixels[(y * w) + x] = true;
        return s;
    }

    private static bool[,] FullMask(int w, int h)
    {
        var m = new bool[w, h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                m[x, y] = true;
        return m;
    }

    /// <summary>
    /// Parses a simple grid notation like "1 1 0 / 0 1 1" into a bool[width,height].
    /// Rows are separated by '/', cells by whitespace. '1' = true, anything else = false.
    /// </summary>
    private static bool[,] MaskFromGrid(string grid)
    {
        string[] rows = grid.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var rowCells = new List<string[]>();
        foreach (var r in rows)
        {
            string[] cells = r.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            rowCells.Add(cells);
        }
        int height = rowCells.Count;
        int width = rowCells[0].Length;
        var m = new bool[width, height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                m[x, y] = rowCells[y][x] == "1";
        return m;
    }

    // ════════════════════════════════════════════════════════════════════════
    // A. Initial state and Cancel
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NewInstance_HasIdleState()
    {
        var svc = new SelectionService();

        Assert.False(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.False(svc.IsFloating);
        Assert.False(svc.IsDragging);
        Assert.Equal(-1, svc.MinX);
        Assert.Equal(-1, svc.MaxX);
        Assert.Equal(-1, svc.MinY);
        Assert.Equal(-1, svc.MaxY);
        Assert.Null(svc.Mask);
        Assert.Null(svc.FloatingPixels);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void Cancel_FromIdle_FiresEventAndKeepsIdleState()
    {
        var (svc, ec) = NewService();

        svc.Cancel();

        Assert.Equal(1, ec.Count);
        Assert.False(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.Equal(-1, svc.MinX);
        Assert.Null(svc.Mask);
    }

    [Fact]
    public void Cancel_FromSelecting_ResetsAllState()
    {
        var (svc, ec) = NewService();
        svc.BeginRectangleSelection(2, 3);
        svc.UpdateRectangleSelection(7, 8);
        int before = ec.Count;

        svc.Cancel();

        Assert.Equal(before + 1, ec.Count);
        Assert.False(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.Equal(-1, svc.MinX);
        Assert.Equal(-1, svc.MaxX);
        Assert.Null(svc.Mask);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void Cancel_FromFloating_DropsFloatingPixelsAndResets()
    {
        var svc = new SelectionService();
        var data = new PixelClipboardData(FullMask(2, 2), 2, 2);
        svc.PasteAsFloating(data, 8, 8);
        Assert.True(svc.IsFloating);

        svc.Cancel();

        Assert.False(svc.IsFloating);
        Assert.False(svc.HasActiveSelection);
        Assert.Null(svc.FloatingPixels);
        Assert.Equal(-1, svc.MinX);
        Assert.Equal(0, svc.FloatingWidth);
    }

    // ════════════════════════════════════════════════════════════════════════
    // B. Rectangle build
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BeginRectangle_Replace_SetsAnchorAndIsSelecting_FiresEvent()
    {
        var (svc, ec) = NewService();

        svc.BeginRectangleSelection(2, 3, SelectionMode.Replace);

        Assert.True(svc.IsSelecting);
        Assert.False(svc.HasActiveSelection); // Replace + no prior: stays false until finalise
        Assert.Equal(2, svc.MinX);
        Assert.Equal(2, svc.MaxX);
        Assert.Equal(3, svc.MinY);
        Assert.Equal(3, svc.MaxY);
        Assert.Null(svc.Mask); // marquee uses null mask
        Assert.Equal(1, ec.Count);
    }

    [Theory]
    [InlineData(SelectionMode.Add)]
    [InlineData(SelectionMode.Subtract)]
    [InlineData(SelectionMode.Intersect)]
    public void BeginRectangle_NonReplace_NoPriorSelection_HasActiveStaysFalse(SelectionMode mode)
    {
        var svc = new SelectionService();

        svc.BeginRectangleSelection(2, 3, mode);

        Assert.True(svc.IsSelecting);
        // Begin sets HasActive based on whether _baseMask snapshotted; with no prior selection
        // _baseMask is null, so HasActiveSelection must be false during the build phase.
        Assert.False(svc.HasActiveSelection);
    }

    [Fact]
    public void BeginRectangle_Add_WithPriorSelection_PreservesBaseUnderUnion()
    {
        var svc = new SelectionService();
        // Prior selection: 4x4 at (0,0)-(3,3) via Replace path
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();
        Assert.True(svc.HasActiveSelection);

        // Now Add a disjoint 2x2 at (10,10)-(11,11)
        svc.BeginRectangleSelection(10, 10, SelectionMode.Add);
        svc.UpdateRectangleSelection(11, 11);
        svc.FinalizeSelection();

        // Result should be the union: bounds span both regions.
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(11, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(11, svc.MaxY);

        // Original pixels still selected.
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(3, 3));
        // New pixels also selected.
        Assert.True(svc.IsPixelInSelection(10, 10));
        Assert.True(svc.IsPixelInSelection(11, 11));
        // Pixels in the gap are not.
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void UpdateRectangle_BackwardsDrag_ProducesCorrectMinMax()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(5, 5);

        svc.UpdateRectangleSelection(1, 1);

        Assert.Equal(1, svc.MinX);
        Assert.Equal(5, svc.MaxX);
        Assert.Equal(1, svc.MinY);
        Assert.Equal(5, svc.MaxY);
    }

    [Fact]
    public void UpdateRectangle_FiresEventEachCall()
    {
        var (svc, ec) = NewService();
        svc.BeginRectangleSelection(0, 0);
        int after_begin = ec.Count;

        svc.UpdateRectangleSelection(1, 1);
        svc.UpdateRectangleSelection(2, 2);
        svc.UpdateRectangleSelection(3, 3);

        Assert.Equal(after_begin + 3, ec.Count);
    }

    [Fact]
    public void Rectangle_BackwardsDrag_FinalisesValidSelection()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(7, 7);
        svc.UpdateRectangleSelection(3, 3);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(3, svc.MinX);
        Assert.Equal(7, svc.MaxX);
        Assert.True(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void Rectangle_SinglePixel_FinalisesWith1x1Bounds()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(4, 5);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(4, svc.MinX);
        Assert.Equal(4, svc.MaxX);
        Assert.Equal(5, svc.MinY);
        Assert.Equal(5, svc.MaxY);
        Assert.True(svc.IsPixelInSelection(4, 5));
    }

    [Fact]
    public void Rectangle_BeginAfterPrior_Replace_StartsFresh()
    {
        // Replace mode should commit/discard any prior selection (HasActive becomes false).
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();
        Assert.True(svc.HasActiveSelection);

        svc.BeginRectangleSelection(10, 10, SelectionMode.Replace);

        Assert.True(svc.IsSelecting);
        Assert.False(svc.HasActiveSelection);
        Assert.Equal(10, svc.MinX);
        Assert.Equal(10, svc.MaxX);
    }

    // ════════════════════════════════════════════════════════════════════════
    // C. Lasso build
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BeginLasso_SeedsLassoPointsWithSinglePoint()
    {
        var svc = new SelectionService();

        svc.BeginLassoSelection(4, 5, SelectionMode.Replace);

        Assert.True(svc.IsSelecting);
        Assert.Single(svc.LassoPoints);
        Assert.Equal(4, svc.LassoPoints[0].X);
        Assert.Equal(5, svc.LassoPoints[0].Y);
    }

    [Fact]
    public void AddLassoPoint_SkipsConsecutiveDuplicate_AcceptsRevisitedPoint()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);

        svc.AddLassoPoint(0, 0);    // duplicate of seed → skipped
        svc.AddLassoPoint(1, 1);    // accepted
        svc.AddLassoPoint(1, 1);    // duplicate of previous → skipped
        svc.AddLassoPoint(1, 2);    // non-collinear → accepted
        svc.AddLassoPoint(0, 0);    // not a consecutive duplicate → accepted

        Assert.Equal(4, svc.LassoPoints.Count);
        Assert.Equal(new PixelPoint(0, 0), svc.LassoPoints[0]);
        Assert.Equal(new PixelPoint(1, 1), svc.LassoPoints[1]);
        Assert.Equal(new PixelPoint(1, 2), svc.LassoPoints[2]);
        Assert.Equal(new PixelPoint(0, 0), svc.LassoPoints[3]);
    }

    [Fact]
    public void AddLassoPoint_ExpandsDragBoundsAcrossAllDirections()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(5, 5);
        svc.AddLassoPoint(2, 5);   // grow left
        svc.AddLassoPoint(8, 5);   // grow right
        svc.AddLassoPoint(5, 1);   // grow up
        svc.AddLassoPoint(5, 9);   // grow down

        // Finalising will populate Mask + bounds derived from these drag bounds.
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(8, svc.MaxX);
        Assert.Equal(1, svc.MinY);
        Assert.Equal(9, svc.MaxY);
    }

    [Fact]
    public void Finalize_OnePointLasso_Replace_CancelsSelection()
    {
        var (svc, ec) = NewService();
        svc.BeginLassoSelection(3, 3, SelectionMode.Replace);
        // No more points added.

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.Equal(-1, svc.MinX);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void Finalize_OnePointLasso_Add_WithPriorBase_RestoresBase()
    {
        var svc = new SelectionService();
        // Build a 4x4 base selection at (0,0)-(3,3)
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        // Begin Add lasso with only a single point and finalise.
        svc.BeginLassoSelection(20, 20, SelectionMode.Add);
        svc.FinalizeSelection();

        // Base should be restored intact.
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(3, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(3, svc.MaxY);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void Finalize_TwoPointLasso_Replace_CancelsSelection()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0, SelectionMode.Replace);
        svc.AddLassoPoint(5, 5);

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
    }

    [Fact]
    public void Lasso_Triangle_FinalisesWithMaskMatchingPolygon()
    {
        var svc = new SelectionService();
        // Triangle: (0,0), (4,0), (0,4)
        svc.BeginLassoSelection(0, 0, SelectionMode.Replace);
        svc.AddLassoPoint(4, 0);
        svc.AddLassoPoint(0, 4);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.NotNull(svc.Mask);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(4, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(4, svc.MaxY);

        // Vertices should be inside the polygon.
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(4, 0));
        Assert.True(svc.IsPixelInSelection(0, 4));
        // Interior point of the triangle.
        Assert.True(svc.IsPixelInSelection(1, 1));
        // Exterior point (above the hypotenuse).
        Assert.False(svc.IsPixelInSelection(3, 3));
        // Far outside the bounding box.
        Assert.False(svc.IsPixelInSelection(10, 10));
    }

    // ════════════════════════════════════════════════════════════════════════
    // D. Ray-cast IsPointInPolygon / IsPointInLasso
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsPointInLasso_EmptyPolygon_ReturnsFalse()
    {
        var svc = new SelectionService();
        Assert.False(svc.IsPointInLasso(0, 0));
        Assert.False(svc.IsPointInLasso(5, 5));
    }

    [Fact]
    public void IsPointInLasso_SingleVertex_OnlyMatchesThatVertex()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(3, 4);

        Assert.True(svc.IsPointInLasso(3, 4));
        Assert.False(svc.IsPointInLasso(3, 5));
        Assert.False(svc.IsPointInLasso(0, 0));
    }

    [Fact]
    public void IsPointInLasso_TwoVertices_OnlyEndpointsMatch_NotMidpoint()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(4, 0);

        Assert.True(svc.IsPointInLasso(0, 0));
        Assert.True(svc.IsPointInLasso(4, 0));
        Assert.False(svc.IsPointInLasso(2, 0));   // midpoint along segment
        Assert.False(svc.IsPointInLasso(0, 2));
    }

    [Fact]
    public void IsPointInLasso_ConvexTriangle_InteriorInside_ExteriorOutside()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(10, 0);
        svc.AddLassoPoint(0, 10);

        Assert.True(svc.IsPointInLasso(2, 2));  // inside
        Assert.True(svc.IsPointInLasso(1, 5));  // inside
        Assert.False(svc.IsPointInLasso(8, 8));  // outside (above hypotenuse)
        Assert.False(svc.IsPointInLasso(-1, 0)); // outside bounds
    }

    [Fact]
    public void IsPointInLasso_ConcavePolygon_PointInConcavityIsOutside()
    {
        // C-shaped (concave) polygon — wraps around a notch on the right.
        //   (0,0) → (10,0) → (10,3) → (4,3) → (4,7) → (10,7) → (10,10) → (0,10)
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(10, 0);
        svc.AddLassoPoint(10, 3);
        svc.AddLassoPoint(4, 3);
        svc.AddLassoPoint(4, 7);
        svc.AddLassoPoint(10, 7);
        svc.AddLassoPoint(10, 10);
        svc.AddLassoPoint(0, 10);

        Assert.True(svc.IsPointInLasso(2, 5));  // inside the C body
        Assert.True(svc.IsPointInLasso(7, 1));  // inside the top arm
        Assert.False(svc.IsPointInLasso(7, 5)); // inside the concavity → outside polygon
    }

    [Fact]
    public void IsPointInLasso_ExactVertex_HitsFastPath()
    {
        // Hits the early "foreach v in _lassoPoints" exact match before ray-cast.
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(10, 0);
        svc.AddLassoPoint(5, 8);

        Assert.True(svc.IsPointInLasso(0, 0));
        Assert.True(svc.IsPointInLasso(10, 0));
        Assert.True(svc.IsPointInLasso(5, 8));
    }

    // ════════════════════════════════════════════════════════════════════════
    // E. RecomputeCombinedSelection + CombineWithBase boolean ops
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Recompute_RectReplace_MaskIsNull_BoundsFromDrag()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 3, SelectionMode.Replace);
        svc.UpdateRectangleSelection(7, 8);

        Assert.Null(svc.Mask);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(7, svc.MaxX);
        Assert.Equal(3, svc.MinY);
        Assert.Equal(8, svc.MaxY);
    }

    [Fact]
    public void Recompute_LassoReplace_ThreePoints_MaskPopulated()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0, SelectionMode.Replace);
        svc.AddLassoPoint(3, 0);
        svc.AddLassoPoint(0, 3);

        svc.FinalizeSelection();

        Assert.NotNull(svc.Mask);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(3, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(3, svc.MaxY);
    }

    [Fact]
    public void Combine_Add_DisjointRegions_ProducesUnion()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(2, 2);
        svc.FinalizeSelection();

        svc.BeginRectangleSelection(10, 10, SelectionMode.Add);
        svc.UpdateRectangleSelection(12, 12);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(12, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(12, svc.MaxY);
        Assert.True(svc.IsPixelInSelection(1, 1));
        Assert.True(svc.IsPixelInSelection(11, 11));
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void Combine_Add_OverlappingRegions_ProducesOR_NoDoubleCount()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(4, 4);
        svc.FinalizeSelection();

        svc.BeginRectangleSelection(2, 2, SelectionMode.Add);
        svc.UpdateRectangleSelection(6, 6);
        svc.FinalizeSelection();

        Assert.Equal(0, svc.MinX);
        Assert.Equal(6, svc.MaxX);
        Assert.Equal(0, svc.MinY);
        Assert.Equal(6, svc.MaxY);
        // Overlap region.
        Assert.True(svc.IsPixelInSelection(3, 3));
        Assert.True(svc.IsPixelInSelection(2, 2));
        // Pixel in only-original region.
        Assert.True(svc.IsPixelInSelection(0, 0));
        // Pixel in only-new region.
        Assert.True(svc.IsPixelInSelection(6, 6));
    }

    [Fact]
    public void Combine_Subtract_NewFullyContainsBase_NoActiveSelection()
    {
        var svc = new SelectionService();
        // Base is small (3x3 at (2,2))
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(4, 4);
        svc.FinalizeSelection();

        // Subtract a larger rectangle that fully contains base.
        svc.BeginRectangleSelection(0, 0, SelectionMode.Subtract);
        svc.UpdateRectangleSelection(6, 6);
        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
        Assert.Null(svc.Mask);
    }

    [Fact]
    public void Combine_Subtract_NewDisjointFromBase_BaseUnchanged()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        svc.BeginRectangleSelection(10, 10, SelectionMode.Subtract);
        svc.UpdateRectangleSelection(12, 12);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        // Base pixels still there.
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(3, 3));
        // Subtracted region was disjoint, no effect.
        Assert.False(svc.IsPixelInSelection(11, 11));
    }

    [Fact]
    public void Combine_Subtract_PartialOverlap_RemovesOverlapOnly()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(5, 5); // 6x6
        svc.FinalizeSelection();

        // Subtract a 3x3 region that overlaps the bottom-right.
        svc.BeginRectangleSelection(3, 3, SelectionMode.Subtract);
        svc.UpdateRectangleSelection(5, 5);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        // Original-only cell still selected.
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(2, 2));
        // Overlap cell removed.
        Assert.False(svc.IsPixelInSelection(3, 3));
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void Combine_Intersect_DisjointRegions_NoActiveSelection()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        svc.BeginRectangleSelection(10, 10, SelectionMode.Intersect);
        svc.UpdateRectangleSelection(12, 12);
        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
    }

    [Fact]
    public void Combine_Intersect_OverlappingRegions_BoundsAndMaskMatchOverlap()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(4, 4);
        svc.FinalizeSelection();

        svc.BeginRectangleSelection(2, 2, SelectionMode.Intersect);
        svc.UpdateRectangleSelection(6, 6);
        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(4, svc.MaxX);
        Assert.Equal(2, svc.MinY);
        Assert.Equal(4, svc.MaxY);
        // Inside intersection.
        Assert.True(svc.IsPixelInSelection(3, 3));
        // Outside intersection (only in base).
        Assert.False(svc.IsPixelInSelection(0, 0));
        // Outside intersection (only in new).
        Assert.False(svc.IsPixelInSelection(6, 6));
    }

    [Fact]
    public void Combine_Add_NoBaseMask_BehavesLikeReplaceForBuild()
    {
        // Begin Add with no prior selection, then Update + Finalize.
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 2, SelectionMode.Add);
        svc.UpdateRectangleSelection(5, 5);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(5, svc.MaxX);
        Assert.True(svc.IsPixelInSelection(3, 3));
    }

    [Fact]
    public void Combine_Subtract_NoBaseMask_NoActiveSelection()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Subtract);
        svc.UpdateRectangleSelection(3, 3);

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
    }

    [Fact]
    public void Combine_Intersect_NoBaseMask_NoActiveSelection()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Intersect);
        svc.UpdateRectangleSelection(3, 3);

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
    }

    // ════════════════════════════════════════════════════════════════════════
    // F. ApplyMask
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ApplyMask_Replace_SetsMaskBoundsAndActive_FiresEvent()
    {
        var (svc, ec) = NewService();
        var mask = MaskFromGrid("1 1 0 / 0 1 1");

        svc.ApplyMask(mask, 2, 3, 4, 4, SelectionMode.Replace);

        Assert.True(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(4, svc.MaxX);
        Assert.Equal(3, svc.MinY);
        Assert.Equal(4, svc.MaxY);
        Assert.Same(mask, svc.Mask);
        Assert.Equal(1, ec.Count);
    }

    [Fact]
    public void ApplyMask_Add_NoPrior_SetsBoundsMaskAndActive_LikeReplace()
    {
        var svc = new SelectionService();
        var mask = FullMask(3, 3);

        svc.ApplyMask(mask, 5, 5, 7, 7, SelectionMode.Add);

        Assert.True(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
        Assert.NotNull(svc.Mask);
        Assert.Equal(5, svc.MinX);
        Assert.Equal(7, svc.MaxX);
        Assert.Equal(5, svc.MinY);
        Assert.Equal(7, svc.MaxY);
        Assert.True(svc.IsPixelInSelection(6, 6));
    }

    [Theory]
    [InlineData(SelectionMode.Subtract)]
    [InlineData(SelectionMode.Intersect)]
    public void ApplyMask_NonAddNonReplace_NoPrior_Cancels(SelectionMode mode)
    {
        var svc = new SelectionService();
        var mask = FullMask(3, 3);

        svc.ApplyMask(mask, 0, 0, 2, 2, mode);

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
        Assert.Null(svc.Mask);
    }

    [Fact]
    public void ApplyMask_Add_WithPrior_Combines()
    {
        var svc = new SelectionService();
        // Establish a prior selection (4x4 at origin).
        svc.ApplyMask(FullMask(4, 4), 0, 0, 3, 3, SelectionMode.Replace);

        // Add a disjoint mask.
        svc.ApplyMask(FullMask(2, 2), 10, 10, 11, 11, SelectionMode.Add);

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(11, svc.MaxX);
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(10, 10));
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void ApplyMask_DuringHalfBuiltLasso_FinalisesIntoNewMask()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(5, 0);
        svc.AddLassoPoint(0, 5);
        Assert.True(svc.IsSelecting);

        var mask = FullMask(2, 2);
        svc.ApplyMask(mask, 20, 20, 21, 21, SelectionMode.Replace);

        Assert.False(svc.IsSelecting);
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(20, svc.MinX);
        Assert.Equal(21, svc.MaxX);
    }

    // ════════════════════════════════════════════════════════════════════════
    // G. FinalizeSelection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Finalize_LassoUnderThreePoints_Replace_Cancels()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(3, 3, SelectionMode.Replace);
        svc.AddLassoPoint(5, 5);

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
        Assert.False(svc.IsSelecting);
    }

    [Fact]
    public void Finalize_LassoUnderThreePoints_Add_WithBase_RestoresBase()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(2, 2);
        svc.FinalizeSelection();

        svc.BeginLassoSelection(20, 20, SelectionMode.Add);
        svc.AddLassoPoint(21, 20);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(0, svc.MinX);
        Assert.Equal(2, svc.MaxX);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void Finalize_LassoUnderThreePoints_Add_WithoutBase_Cancels()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(20, 20, SelectionMode.Add);
        svc.AddLassoPoint(21, 21);

        svc.FinalizeSelection();

        Assert.False(svc.HasActiveSelection);
    }

    [Fact]
    public void Finalize_LassoThreeCollinearPoints_MaskIncludesVertices_HasActiveSelection()
    {
        // Three collinear points have zero interior area, but IsPointInPolygon returns true
        // on each vertex, so the scanned mask is not empty and FinalizeSelection stays active.
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0, SelectionMode.Replace);
        svc.AddLassoPoint(10, 0);
        svc.AddLassoPoint(5, 0);

        svc.FinalizeSelection();

        Assert.True(svc.HasActiveSelection);
        Assert.NotNull(svc.Mask);
        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(10, 0));
        Assert.True(svc.IsPixelInSelection(5, 0));
        // Typical interior grid point on the segment is outside for ray-casting (not on vertex).
        Assert.False(svc.IsPixelInSelection(3, 0));
    }

    [Fact]
    public void Finalize_ValidLasso_SetsActiveAndFiresEvent()
    {
        var (svc, ec) = NewService();
        svc.BeginLassoSelection(0, 0, SelectionMode.Replace);
        svc.AddLassoPoint(5, 0);
        svc.AddLassoPoint(0, 5);
        int beforeFinalize = ec.Count;

        svc.FinalizeSelection();

        Assert.False(svc.IsSelecting);
        Assert.True(svc.HasActiveSelection);
        Assert.Equal(beforeFinalize + 1, ec.Count);
    }

    // ════════════════════════════════════════════════════════════════════════
    // H. IsPixelInSelection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void IsPixelInSelection_NoActiveSelection_AlwaysFalse()
    {
        var svc = new SelectionService();
        Assert.False(svc.IsPixelInSelection(0, 0));
        Assert.False(svc.IsPixelInSelection(5, 5));
    }

    [Fact]
    public void IsPixelInSelection_NullMaskMarquee_AllInBoundsTrue_OutOfBoundsFalse()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(5, 5);
        svc.FinalizeSelection();

        Assert.True(svc.IsPixelInSelection(2, 2));
        Assert.True(svc.IsPixelInSelection(5, 5));
        Assert.True(svc.IsPixelInSelection(3, 4));
        Assert.False(svc.IsPixelInSelection(1, 2));
        Assert.False(svc.IsPixelInSelection(6, 6));
    }

    [Fact]
    public void IsPixelInSelection_PopulatedMaskLasso_ChecksMaskCells()
    {
        var svc = new SelectionService();
        // 3x2 mask at origin: "1 1 0 / 0 1 1"
        svc.ApplyMask(MaskFromGrid("1 1 0 / 0 1 1"), 0, 0, 2, 1, SelectionMode.Replace);

        Assert.True(svc.IsPixelInSelection(0, 0));
        Assert.True(svc.IsPixelInSelection(1, 0));
        Assert.False(svc.IsPixelInSelection(2, 0));
        Assert.False(svc.IsPixelInSelection(0, 1));
        Assert.True(svc.IsPixelInSelection(1, 1));
        Assert.True(svc.IsPixelInSelection(2, 1));
    }

    [Fact]
    public void IsPixelInSelection_Floating_PixelInsideFloatingBox_UsesFloatingPixels()
    {
        var svc = new SelectionService();
        var data = new PixelClipboardData(MaskFromGrid("1 0 / 0 1"), 2, 2);
        svc.PasteAsFloating(data, 8, 8);

        int fx = svc.FloatingX;
        int fy = svc.FloatingY;

        Assert.True(svc.IsPixelInSelection(fx + 0, fy + 0));
        Assert.False(svc.IsPixelInSelection(fx + 1, fy + 0));
        Assert.False(svc.IsPixelInSelection(fx + 0, fy + 1));
        Assert.True(svc.IsPixelInSelection(fx + 1, fy + 1));
    }

    [Fact]
    public void IsPixelInSelection_Floating_PixelOutsideFloatingBox_ReturnsFalse()
    {
        var svc = new SelectionService();
        var data = new PixelClipboardData(FullMask(2, 2), 2, 2);
        svc.PasteAsFloating(data, 8, 8);

        int fx = svc.FloatingX;
        int fy = svc.FloatingY;
        Assert.False(svc.IsPixelInSelection(fx - 1, fy));
        Assert.False(svc.IsPixelInSelection(fx + 2, fy));
        Assert.False(svc.IsPixelInSelection(fx, fy - 1));
        Assert.False(svc.IsPixelInSelection(fx, fy + 2));
    }

    [Fact]
    public void IsPixelInSelection_Floating_FloatingPixelFalse_ReturnsFalse()
    {
        var svc = new SelectionService();
        // Only top-left of 2x2 is true.
        var pixels = new bool[2, 2];
        pixels[0, 0] = true;
        var data = new PixelClipboardData(pixels, 2, 2);
        svc.PasteAsFloating(data, 8, 8);

        int fx = svc.FloatingX;
        int fy = svc.FloatingY;
        Assert.True(svc.IsPixelInSelection(fx, fy));
        Assert.False(svc.IsPixelInSelection(fx + 1, fy));
        Assert.False(svc.IsPixelInSelection(fx, fy + 1));
        Assert.False(svc.IsPixelInSelection(fx + 1, fy + 1));
    }

    // ════════════════════════════════════════════════════════════════════════
    // I. LiftSelection / CommitSelection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lift_NoActiveSelection_NoOp_NoEvent()
    {
        var (svc, ec) = NewService();
        var state = MakeState(8, 8, (1, 1));

        svc.LiftSelection(state);

        Assert.False(svc.IsFloating);
        Assert.Equal(0, ec.Count);
        Assert.True(state.Pixels[(1 * 8) + 1]); // unchanged
    }

    [Fact]
    public void Lift_AlreadyFloating_Idempotent()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1));
        svc.PasteAsFloating(new PixelClipboardData(FullMask(2, 2), 2, 2), 8, 8);
        Assert.True(svc.IsFloating);

        var beforePixels = (bool[])state.Pixels.Clone();

        svc.LiftSelection(state);

        Assert.True(svc.IsFloating);
        Assert.Equal(beforePixels, state.Pixels);
    }

    [Fact]
    public void Lift_FromMarquee_MovesOnPixelsToFloating_ClearsCanvas()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1), (2, 2), (5, 5)); // 5,5 is outside the selection
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        svc.LiftSelection(state);

        Assert.True(svc.IsFloating);
        Assert.NotNull(svc.FloatingPixels);
        Assert.Equal(4, svc.FloatingWidth);
        Assert.Equal(4, svc.FloatingHeight);
        Assert.Equal(0, svc.FloatingX);
        Assert.Equal(0, svc.FloatingY);

        // Lifted on-pixels.
        Assert.True(svc.FloatingPixels[1, 1]);
        Assert.True(svc.FloatingPixels[2, 2]);
        // Off-pixels remain false.
        Assert.False(svc.FloatingPixels[0, 0]);
        // Canvas pixels in the selection are now cleared.
        Assert.False(state.Pixels[(1 * 8) + 1]);
        Assert.False(state.Pixels[(2 * 8) + 2]);
        // Pixel outside selection unchanged.
        Assert.True(state.Pixels[(5 * 8) + 5]);
    }

    [Fact]
    public void Lift_RespectsMask_OffMaskPixelsStayOnCanvas()
    {
        // Mask: 2x2 with diagonal cells true.
        // pixels[0,0]=true, [1,1]=true, [1,0]=false, [0,1]=false
        var maskPixels = new bool[2, 2];
        maskPixels[0, 0] = true;
        maskPixels[1, 1] = true;

        var svc = new SelectionService();
        svc.ApplyMask(maskPixels, 1, 1, 2, 2, SelectionMode.Replace);

        var state = MakeState(8, 8, (1, 1), (2, 1), (1, 2), (2, 2));
        svc.LiftSelection(state);

        Assert.True(svc.IsFloating);
        // Lifted (mask=true AND canvas=true): (1,1) and (2,2).
        Assert.True(svc.FloatingPixels![0, 0]); // (1,1)
        Assert.True(svc.FloatingPixels[1, 1]);  // (2,2)
        // Off-mask but canvas=true: stays on canvas, NOT in floating.
        Assert.False(svc.FloatingPixels[1, 0]); // (2,1) → off mask
        Assert.False(svc.FloatingPixels[0, 1]); // (1,2) → off mask
        Assert.True(state.Pixels[(1 * 8) + 2]); // (2,1) still on canvas
        Assert.True(state.Pixels[(2 * 8) + 1]); // (1,2) still on canvas
        // Mask=true cells cleared from canvas.
        Assert.False(state.Pixels[(1 * 8) + 1]);
        Assert.False(state.Pixels[(2 * 8) + 2]);
    }

    [Fact]
    public void Lift_SelectionExtendsPastCanvas_OnlyInBoundsLifted()
    {
        var svc = new SelectionService();
        var state = MakeState(4, 4, (3, 3));

        // Selection extends from (2,2) to (10,10) — past the 4x4 canvas.
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(10, 10);
        svc.FinalizeSelection();

        svc.LiftSelection(state);

        Assert.True(svc.IsFloating);
        Assert.Equal(9, svc.FloatingWidth);  // 10 - 2 + 1
        Assert.Equal(9, svc.FloatingHeight);
        // (3,3) lifted. Local coord = (3-2, 3-2) = (1, 1).
        Assert.True(svc.FloatingPixels![1, 1]);
        // Out-of-canvas cells in the floating layer stay false.
        Assert.False(svc.FloatingPixels[8, 8]);
        // Canvas cleared.
        Assert.False(state.Pixels[(3 * 4) + 3]);
    }

    [Fact]
    public void Commit_NotFloating_NoOp_NoEvent()
    {
        var (svc, ec) = NewService();
        var state = MakeState(8, 8);

        // Active selection but not floating.
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();
        int beforeCommit = ec.Count;

        svc.CommitSelection(state);

        Assert.Equal(beforeCommit, ec.Count); // no event
        Assert.False(svc.IsFloating);
    }

    [Fact]
    public void Commit_Floating_StampsORingOnTopOfCanvas_ClearsFloatingState()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (5, 5)); // existing pixel that should NOT be erased

        var data = new PixelClipboardData(MaskFromGrid("1 0 / 0 1"), 2, 2);
        // Force a known FloatingX/Y by manipulating after PasteAsFloating.
        svc.PasteAsFloating(data, 8, 8);

        // Move floating to overlap (5,5) so we can verify OR semantics.
        // Floating box is at FloatingX..FloatingX+1 × FloatingY..FloatingY+1.
        // We move it so that (FloatingX+1, FloatingY+1) maps to canvas (5,5).
        svc.MoveFloatingTo(4, 4);

        svc.CommitSelection(state);

        Assert.False(svc.IsFloating);
        Assert.Null(svc.FloatingPixels);
        // Floating mask had [0,0]=true and [1,1]=true at floating origin (4,4).
        // So (4,4) and (5,5) on canvas should be true.
        Assert.True(state.Pixels[(4 * 8) + 4]);
        Assert.True(state.Pixels[(5 * 8) + 5]);
        // Bounds updated to floating extents.
        Assert.Equal(4, svc.MinX);
        Assert.Equal(5, svc.MaxX);
        Assert.Equal(4, svc.MinY);
        Assert.Equal(5, svc.MaxY);
        Assert.Empty(svc.LassoPoints);
    }

    [Fact]
    public void LiftMoveCommit_RoundTrip_TranslatesPixelsOnCanvas()
    {
        var svc = new SelectionService();
        // Canvas with a single on-pixel at (1,1).
        var state = MakeState(8, 8, (1, 1));

        // Select around (1,1).
        svc.BeginRectangleSelection(1, 1, SelectionMode.Replace);
        svc.UpdateRectangleSelection(1, 1);
        svc.FinalizeSelection();

        svc.LiftSelection(state);
        Assert.False(state.Pixels[(1 * 8) + 1]); // lifted off the canvas

        svc.MoveFloatingTo(5, 5);
        svc.CommitSelection(state);

        Assert.True(state.Pixels[(5 * 8) + 5]);
        Assert.False(state.Pixels[(1 * 8) + 1]);
    }

    [Fact]
    public void Lasso_LiftMoveCommit_OverflowRoundTrip_PreservesMaskedPixel()
    {
        var state = new SpriteState(8, 8);
        var overflow = new OverflowPixelBuffer(8, 8, initialMargin: 8);
        overflow.SetPixel(2, 2, true);
        state.Frames[0].LayerPixels[0] = overflow;
        state.Layers[0].PreserveOverflow = true;
        state.Pixels = overflow.GetMonochromeData();

        var svc = new SelectionService();
        svc.BeginLassoSelection(1, 1, SelectionMode.Replace);
        svc.AddLassoPoint(4, 1);
        svc.AddLassoPoint(4, 4);
        svc.AddLassoPoint(1, 4);
        svc.FinalizeSelection();

        svc.LiftSelection(state);
        svc.MoveFloatingTo(-5, -5);
        svc.CommitSelection(state);

        bool[] extended = overflow.GetExtendedData();
        int overflowIndex = (-4 + overflow.MarginY) * overflow.ExtendedWidth + (-4 + overflow.MarginX);
        Assert.True(extended[overflowIndex]);
        Assert.False(state.Pixels[(2 * state.Width) + 2]);

        svc.LiftSelection(state);
        svc.MoveFloatingTo(1, 1);
        svc.CommitSelection(state);

        Assert.True(state.Pixels[(2 * state.Width) + 2]);
        Assert.False(overflow.GetExtendedData()[overflowIndex]);
    }

    // ════════════════════════════════════════════════════════════════════════
    // J. DeleteSelection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Delete_NoActiveSelection_NoOp()
    {
        var (svc, ec) = NewService();
        var state = MakeState(8, 8, (1, 1));

        svc.DeleteSelection(state);

        Assert.Equal(0, ec.Count);
        Assert.True(state.Pixels[(1 * 8) + 1]);
    }

    [Fact]
    public void Delete_Marquee_ClearsOnPixelsAndResetsState()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1), (2, 2), (5, 5));

        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        svc.DeleteSelection(state);

        Assert.False(svc.HasActiveSelection);
        Assert.Equal(-1, svc.MinX);
        Assert.False(state.Pixels[(1 * 8) + 1]);
        Assert.False(state.Pixels[(2 * 8) + 2]);
        Assert.True(state.Pixels[(5 * 8) + 5]); // outside selection
    }

    [Fact]
    public void Delete_LassoMask_ClearsOnlyMaskedPixels()
    {
        // Mask with diagonal cells.
        var maskPixels = new bool[2, 2];
        maskPixels[0, 0] = true;
        maskPixels[1, 1] = true;

        var svc = new SelectionService();
        svc.ApplyMask(maskPixels, 1, 1, 2, 2, SelectionMode.Replace);

        var state = MakeState(8, 8, (1, 1), (2, 1), (1, 2), (2, 2));

        svc.DeleteSelection(state);

        // Masked pixels cleared.
        Assert.False(state.Pixels[(1 * 8) + 1]);
        Assert.False(state.Pixels[(2 * 8) + 2]);
        // Off-mask pixels still on canvas.
        Assert.True(state.Pixels[(1 * 8) + 2]);
        Assert.True(state.Pixels[(2 * 8) + 1]);
        Assert.False(svc.HasActiveSelection);
    }

    [Fact]
    public void Delete_Floating_DropsFloatingPixels_ResetsState()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8); // empty canvas (lift already cleared)
        svc.PasteAsFloating(new PixelClipboardData(FullMask(2, 2), 2, 2), 8, 8);

        var beforePixels = (bool[])state.Pixels.Clone();
        svc.DeleteSelection(state);

        Assert.False(svc.IsFloating);
        Assert.False(svc.HasActiveSelection);
        Assert.Null(svc.FloatingPixels);
        // Canvas not modified (lift already cleared affected pixels in the real flow).
        Assert.Equal(beforePixels, state.Pixels);
    }

    // ════════════════════════════════════════════════════════════════════════
    // K. Drag (BeginDrag / MoveFloatingTo / EndDrag)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BeginDrag_FlipsIsDragging_FiresEvent()
    {
        var (svc, ec) = NewService();

        svc.BeginDrag();

        Assert.True(svc.IsDragging);
        Assert.Equal(1, ec.Count);
    }

    [Fact]
    public void MoveFloatingTo_NotFloating_NoOp()
    {
        var (svc, ec) = NewService();

        svc.MoveFloatingTo(3, 3);

        Assert.Equal(0, ec.Count);
        Assert.Equal(0, svc.FloatingX);
        Assert.Equal(0, svc.FloatingY);
    }

    [Fact]
    public void MoveFloatingTo_Floating_UpdatesFloatingXY_NotMinMax_FiresEvent()
    {
        var svc = new SelectionService();
        svc.PasteAsFloating(new PixelClipboardData(FullMask(2, 2), 2, 2), 16, 16);
        int originalMinX = svc.MinX;
        int originalMinY = svc.MinY;
        int originalMaxX = svc.MaxX;
        int originalMaxY = svc.MaxY;

        var ec = new EventCounter();
        svc.SelectionChanged += (_, _) => ec.Count++;

        svc.MoveFloatingTo(7, 8);

        Assert.Equal(7, svc.FloatingX);
        Assert.Equal(8, svc.FloatingY);
        Assert.Equal(originalMinX, svc.MinX);
        Assert.Equal(originalMaxX, svc.MaxX);
        Assert.Equal(originalMinY, svc.MinY);
        Assert.Equal(originalMaxY, svc.MaxY);
        Assert.Equal(1, ec.Count);
    }

    [Fact]
    public void EndDrag_ClearsIsDragging_FiresEvent()
    {
        var (svc, ec) = NewService();
        svc.BeginDrag();

        svc.EndDrag();

        Assert.False(svc.IsDragging);
        Assert.Equal(2, ec.Count); // one from begin, one from end
    }

    // ════════════════════════════════════════════════════════════════════════
    // L. Clipboard (CopySelection / PasteAsFloating)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CopySelection_NoActiveSelection_ReturnsNull()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1));

        var data = svc.CopySelection(state);

        Assert.Null(data);
    }

    [Fact]
    public void CopySelection_Marquee_CapturesOnPixelsInBounds()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1), (2, 2), (5, 5));

        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        var data = svc.CopySelection(state);

        Assert.NotNull(data);
        Assert.Equal(4, data.Width);
        Assert.Equal(4, data.Height);
        Assert.True(data.Pixels[1, 1]);
        Assert.True(data.Pixels[2, 2]);
        Assert.False(data.Pixels[0, 0]);
        // (5,5) is outside the selection bounds → not captured.
    }

    [Fact]
    public void CopySelection_LassoMask_RespectsMask()
    {
        // Diagonal mask: (0,0) and (1,1) selected; (1,0) and (0,1) not.
        var maskPixels = new bool[2, 2];
        maskPixels[0, 0] = true;
        maskPixels[1, 1] = true;

        var svc = new SelectionService();
        svc.ApplyMask(maskPixels, 1, 1, 2, 2, SelectionMode.Replace);

        var state = MakeState(8, 8, (1, 1), (2, 1), (1, 2), (2, 2));

        var data = svc.CopySelection(state);

        Assert.NotNull(data);
        Assert.True(data.Pixels[0, 0]);
        Assert.False(data.Pixels[1, 0]); // off-mask, not captured even though canvas is on
        Assert.False(data.Pixels[0, 1]);
        Assert.True(data.Pixels[1, 1]);
    }

    [Fact]
    public void CopySelection_Floating_ReturnsCloneOfFloatingPixels()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8);
        var pixels = new bool[2, 2];
        pixels[0, 0] = true;
        pixels[1, 1] = true;
        svc.PasteAsFloating(new PixelClipboardData(pixels, 2, 2), 8, 8);

        var data = svc.CopySelection(state);

        Assert.NotNull(data);
        Assert.Equal(2, data.Width);
        Assert.Equal(2, data.Height);
        Assert.True(data.Pixels[0, 0]);
        Assert.True(data.Pixels[1, 1]);

        // Mutate the copy and verify FloatingPixels is unaffected.
        data.Pixels[0, 0] = false;
        Assert.NotNull(svc.FloatingPixels);
        Assert.True(svc.FloatingPixels[0, 0]);
    }

    [Fact]
    public void PasteAsFloating_CentersOnCanvas_SetsStateAndClonesData()
    {
        var svc = new SelectionService();
        var sourcePixels = new bool[3, 3];
        sourcePixels[1, 1] = true;
        var data = new PixelClipboardData(sourcePixels, 3, 3);

        svc.PasteAsFloating(data, 8, 8);

        Assert.True(svc.IsFloating);
        Assert.True(svc.HasActiveSelection);
        // (8 - 3) / 2 = 2 (integer division)
        Assert.Equal(2, svc.FloatingX);
        Assert.Equal(2, svc.FloatingY);
        Assert.Equal(3, svc.FloatingWidth);
        Assert.Equal(3, svc.FloatingHeight);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(4, svc.MaxX);
        Assert.Equal(2, svc.MinY);
        Assert.Equal(4, svc.MaxY);

        // Verify the data was cloned: mutate the source, FloatingPixels stays the same.
        sourcePixels[1, 1] = false;
        Assert.NotNull(svc.FloatingPixels);
        Assert.True(svc.FloatingPixels[1, 1]);
    }

    // ════════════════════════════════════════════════════════════════════════
    // M. Snapshots (CreateSnapshot / RestoreSnapshot)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateSnapshot_CapturesAllFlagsAndBounds()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 3, SelectionMode.Replace);
        svc.UpdateRectangleSelection(5, 6);
        svc.FinalizeSelection();

        var snap = svc.CreateSnapshot();

        Assert.True(snap.HasActiveSelection);
        Assert.False(snap.IsSelecting);
        Assert.False(snap.IsFloating);
        Assert.False(snap.IsDragging);
        Assert.Equal(2, snap.MinX);
        Assert.Equal(5, snap.MaxX);
        Assert.Equal(3, snap.MinY);
        Assert.Equal(6, snap.MaxY);
    }

    [Fact]
    public void CreateSnapshot_DeepClonesArrays()
    {
        var svc = new SelectionService();
        // Set up a mask + floating + lasso state.
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(3, 0);
        svc.AddLassoPoint(0, 3);
        svc.FinalizeSelection();

        var snap = svc.CreateSnapshot();

        Assert.NotNull(snap.Mask);
        Assert.NotNull(svc.Mask);
        // Mutate the snapshot's mask; service's mask should be unaffected.
        bool original00 = snap.Mask[0, 0];
        snap.Mask[0, 0] = !original00;
        Assert.NotEqual(snap.Mask[0, 0], svc.Mask[0, 0]);

        // Mutate the snapshot's lasso list; service's list should be unaffected.
        snap.LassoPoints.Add(new PixelPoint(99, 99));
        Assert.DoesNotContain(new PixelPoint(99, 99), svc.LassoPoints);
    }

    [Fact]
    public void CreateSnapshot_OnIdleService_ReturnsBenignAllFalseSnapshot()
    {
        var svc = new SelectionService();

        var snap = svc.CreateSnapshot();

        Assert.False(snap.HasActiveSelection);
        Assert.False(snap.IsSelecting);
        Assert.False(snap.IsFloating);
        Assert.False(snap.IsDragging);
        Assert.Equal(-1, snap.MinX);
        Assert.Null(snap.Mask);
        Assert.Null(snap.FloatingPixels);
        Assert.Empty(snap.LassoPoints);
    }

    [Fact]
    public void RestoreSnapshot_ReinstatesStateAndFiresEvent()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(2, 2, SelectionMode.Replace);
        svc.UpdateRectangleSelection(5, 5);
        svc.FinalizeSelection();
        var snap = svc.CreateSnapshot();

        // Mutate state: cancel, then restore.
        svc.Cancel();
        Assert.False(svc.HasActiveSelection);

        var ec = new EventCounter();
        svc.SelectionChanged += (_, _) => ec.Count++;

        svc.RestoreSnapshot(snap);

        Assert.True(svc.HasActiveSelection);
        Assert.Equal(2, svc.MinX);
        Assert.Equal(5, svc.MaxX);
        Assert.Equal(1, ec.Count);
    }

    [Fact]
    public void Snapshot_RoundTrip_PreservesObservableSelection()
    {
        var svc = new SelectionService();
        svc.BeginLassoSelection(0, 0);
        svc.AddLassoPoint(5, 0);
        svc.AddLassoPoint(0, 5);
        svc.FinalizeSelection();

        // Capture the "is in selection" map for the bounding box.
        bool[,] before = new bool[6, 6];
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                before[x, y] = svc.IsPixelInSelection(x, y);

        var snap = svc.CreateSnapshot();
        svc.Cancel();
        svc.RestoreSnapshot(snap);

        // After restore the same predicate should hold.
        for (int y = 0; y < 6; y++)
            for (int x = 0; x < 6; x++)
                Assert.Equal(before[x, y], svc.IsPixelInSelection(x, y));
    }

    // ════════════════════════════════════════════════════════════════════════
    // N. SelectionChanged event audit
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Events_RectangleBuildSequence_FiresOncePerCall()
    {
        var (svc, ec) = NewService();

        svc.BeginRectangleSelection(0, 0);     // +1
        Assert.Equal(1, ec.Count);

        svc.UpdateRectangleSelection(3, 3);    // +1
        Assert.Equal(2, ec.Count);

        svc.FinalizeSelection();               // +1
        Assert.Equal(3, ec.Count);
    }

    [Fact]
    public void Events_LiftCommitDelete_FireOnceEach()
    {
        var svc = new SelectionService();
        var state = MakeState(8, 8, (1, 1));
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(3, 3);
        svc.FinalizeSelection();

        var ec = new EventCounter();
        svc.SelectionChanged += (_, _) => ec.Count++;

        svc.LiftSelection(state);    // +1
        Assert.Equal(1, ec.Count);

        svc.CommitSelection(state);  // +1
        Assert.Equal(2, ec.Count);

        // Set up another selection then delete it.
        svc.BeginRectangleSelection(0, 0, SelectionMode.Replace);
        svc.UpdateRectangleSelection(2, 2);
        svc.FinalizeSelection();
        int beforeDelete = ec.Count;

        svc.DeleteSelection(state);  // +1
        Assert.Equal(beforeDelete + 1, ec.Count);
    }

    [Fact]
    public void Events_DragSequence_FiresExactlyFiveTimes()
    {
        var svc = new SelectionService();
        svc.PasteAsFloating(new PixelClipboardData(FullMask(2, 2), 2, 2), 16, 16);

        var ec = new EventCounter();
        svc.SelectionChanged += (_, _) => ec.Count++;

        svc.BeginDrag();          // +1
        svc.MoveFloatingTo(1, 1); // +1
        svc.MoveFloatingTo(2, 2); // +1
        svc.MoveFloatingTo(3, 3); // +1
        svc.EndDrag();            // +1

        Assert.Equal(5, ec.Count);
    }

    // ════════════════════════════════════════════════════════════════════════
    // T. Elliptical Marquee Selection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BeginEllipseSelection_SetsCorrectEphemState()
    {
        var (svc, ec) = NewService();

        svc.BeginEllipseSelection(5, 5, SelectionMode.Replace);

        Assert.True(svc.IsSelecting);
        Assert.False(svc.HasActiveSelection);
        Assert.Equal(5, svc.MinX);
        Assert.Equal(5, svc.MaxX);
        Assert.Equal(5, svc.MinY);
        Assert.Equal(5, svc.MaxY);
        Assert.NotNull(svc.Mask); // Ellipse uses non-null mask even during dragging
        Assert.Equal(1, ec.Count);
    }

    [Fact]
    public void UpdateEllipseSelection_ComputesSubpixelInclusionCorrectly()
    {
        var svc = new SelectionService();
        svc.BeginEllipseSelection(0, 0, SelectionMode.Replace);
        svc.UpdateEllipseSelection(4, 4);

        // 5x5 bounding box: 0 to 4 in x and y.
        // rx = 2.4, ry = 2.4 (with offset), cx = 2.0, cy = 2.0.
        // The mask must be a 5x5 array.
        Assert.NotNull(svc.Mask);
        var mask = svc.Mask;
        Assert.Equal(5, mask.GetLength(0));
        Assert.Equal(5, mask.GetLength(1));

        // Corners (0,0), (4,0), (0,4), (4,4) should be false:
        Assert.False(mask[0, 0]);
        Assert.False(mask[4, 0]);
        Assert.False(mask[0, 4]);
        Assert.False(mask[4, 4]);

        Assert.True(mask[2, 2]); // center
        Assert.True(mask[0, 2]); // middle left edge
        Assert.True(mask[2, 0]); // middle top edge
        Assert.True(mask[1, 0]); // top-inner-left edge
        Assert.True(mask[1, 1]); // upper-left inner pixel
    }

    [Fact]
    public void FinalizeSelection_CreatesValidPixelatedMask()
    {
        var svc = new SelectionService();
        svc.BeginEllipseSelection(1, 1, SelectionMode.Replace);
        svc.UpdateEllipseSelection(3, 3);
        
        Assert.True(svc.IsSelecting);
        Assert.False(svc.HasActiveSelection);

        svc.FinalizeSelection();

        Assert.False(svc.IsSelecting);
        Assert.True(svc.HasActiveSelection);
        
        Assert.Equal(1, svc.MinX);
        Assert.Equal(3, svc.MaxX);
        Assert.Equal(1, svc.MinY);
        Assert.Equal(3, svc.MaxY);

        Assert.NotNull(svc.Mask);
        Assert.False(svc.Mask[0, 0]); // top-left corner is now excluded for a beautiful 3x3 cross/ellipse
        Assert.True(svc.Mask[0, 1]);  // middle-left edge is included
        Assert.True(svc.Mask[1, 1]);  // center is included
    }

    [Fact]
    public void UpdateRectangleSelection_WithAlt_SymmetricalCentering()
    {
        var svc = new SelectionService();
        svc.BeginRectangleSelection(5, 5, SelectionMode.Replace);
        
        // Drag to (7, 8) with isAltDown = true
        svc.UpdateRectangleSelection(7, 8, isAltDown: true);

        Assert.Equal(3, svc.MinX); // 5 - 2
        Assert.Equal(7, svc.MaxX); // 5 + 2
        Assert.Equal(2, svc.MinY); // 5 - 3
        Assert.Equal(8, svc.MaxY); // 5 + 3
    }

    [Fact]
    public void UpdateEllipseSelection_WithAlt_SymmetricalCentering()
    {
        var svc = new SelectionService();
        svc.BeginEllipseSelection(5, 5, SelectionMode.Replace);
        
        // Drag to (7, 8) with isAltDown = true
        svc.UpdateEllipseSelection(7, 8, isAltDown: true);

        Assert.Equal(3, svc.MinX); // 5 - 2
        Assert.Equal(7, svc.MaxX); // 5 + 2
        Assert.Equal(2, svc.MinY); // 5 - 3
        Assert.Equal(8, svc.MaxY); // 5 + 3
    }
}

