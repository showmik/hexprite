using Hexprite.Core;
using Hexprite.Rendering;
using Xunit;

namespace Hexprite.Tests;

/// <summary>
/// Tests the pure overlay visibility logic extracted from
/// <see cref="SelectionOverlayRenderer.ComputeOverlayVisibility"/>.
/// Guards against regressions where the elliptical marquee shows
/// both the smooth ellipse AND the jagged pixel boundary simultaneously,
/// or where boolean operations (Shift/Alt+drag) lose the base selection preview.
/// </summary>
[Trait("Category", "Unit")]
    public class SelectionOverlayVisibilityTests
{
    // ════════════════════════════════════════════════════════════════════════
    // A. Elliptical Marquee — simple drag (Replace mode, no base mask)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EllipticalMarquee_Dragging_NoBaseMask_SuppressesLasso()
    {
        // During a simple elliptical marquee drag, the mask is non-null
        // (pixel rasterization), but the lasso overlay should NOT render
        // because the smooth ellipse overlay is the correct preview.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: true,
            isSelecting: true,
            hasActiveSelection: false,
            hasBaseMask: false,
            currentTool: ToolMode.EllipticalMarquee);

        Assert.False(needsLasso);
    }

    [Fact]
    public void EllipticalMarquee_Finalized_ShowsLasso()
    {
        // After mouse-up, IsSelecting becomes false.
        // The mask-based lasso renderer should activate to show the
        // jagged pixel-boundary outline of the selected pixels.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: true,
            isSelecting: false,
            hasActiveSelection: true,
            hasBaseMask: false,
            currentTool: ToolMode.EllipticalMarquee);

        Assert.True(needsLasso);
    }

    // ════════════════════════════════════════════════════════════════════════
    // B. Elliptical Marquee — boolean operations (Shift+drag = Add, etc.)
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void EllipticalMarquee_Dragging_WithBaseMask_ShowsLasso()
    {
        // When Shift+dragging (Add mode) with an existing selection,
        // BaseMask is non-null. The lasso renderer must still run to
        // show the previous selection's boundary while dragging.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: true,
            isSelecting: true,
            hasActiveSelection: true,
            hasBaseMask: true,
            currentTool: ToolMode.EllipticalMarquee);

        Assert.True(needsLasso);
    }

    [Fact]
    public void EllipticalMarquee_Dragging_WithBaseMask_NoMask_ShowsLasso()
    {
        // Edge case: at the very start of a Shift+drag before the ellipse
        // mask has been computed. BaseMask is set but Mask may still be null.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: true,
            hasActiveSelection: true,
            hasBaseMask: true,
            currentTool: ToolMode.EllipticalMarquee);

        Assert.True(needsLasso);
    }

    // ════════════════════════════════════════════════════════════════════════
    // C. Rectangle Marquee — should never be affected by the ellipse fix
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RectMarquee_Dragging_NoMask_NoLasso()
    {
        // Simple rectangle drag: Mask is null (fast path), no lasso needed.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: true,
            hasActiveSelection: false,
            hasBaseMask: false,
            currentTool: ToolMode.Marquee);

        Assert.False(needsLasso);
    }

    [Fact]
    public void RectMarquee_Finalized_NoMask_NoLasso()
    {
        // Finalized rectangle with no mask → uses marquee overlay, not lasso.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: false,
            hasActiveSelection: true,
            hasBaseMask: false,
            currentTool: ToolMode.Marquee);

        Assert.False(needsLasso);
    }

    [Fact]
    public void RectMarquee_Finalized_WithMask_ShowsLasso()
    {
        // A finalized rectangle that has a mask (e.g., from boolean combine)
        // should use the lasso renderer to show the mask boundary.
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: true,
            isSelecting: false,
            hasActiveSelection: true,
            hasBaseMask: false,
            currentTool: ToolMode.Marquee);

        Assert.True(needsLasso);
    }

    // ════════════════════════════════════════════════════════════════════════
    // D. Lasso tool — should always use the lasso renderer during drag
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Lasso_Dragging_ShowsLasso()
    {
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: true,
            hasActiveSelection: false,
            hasBaseMask: false,
            currentTool: ToolMode.Lasso);

        Assert.True(needsLasso);
    }

    [Fact]
    public void Lasso_Finalized_WithMask_ShowsLasso()
    {
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: true,
            isSelecting: false,
            hasActiveSelection: true,
            hasBaseMask: false,
            currentTool: ToolMode.Lasso);

        Assert.True(needsLasso);
    }

    // ════════════════════════════════════════════════════════════════════════
    // E. Magic Wand — should always use the lasso renderer during drag
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MagicWand_Dragging_ShowsLasso()
    {
        var (needsLasso, _, _) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: true,
            hasActiveSelection: false,
            hasBaseMask: false,
            currentTool: ToolMode.MagicWand);

        Assert.True(needsLasso);
    }

    // ════════════════════════════════════════════════════════════════════════
    // F. No selection at all — nothing should render
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoSelection_NoMask_AllHidden()
    {
        var (needsLasso, showMarquee, showEllipse) = SelectionOverlayRenderer.ComputeOverlayVisibility(
            hasMask: false,
            isSelecting: false,
            hasActiveSelection: false,
            hasBaseMask: false,
            currentTool: ToolMode.Marquee);

        Assert.False(needsLasso);
        Assert.False(showMarquee);
        Assert.False(showEllipse);
    }
}
