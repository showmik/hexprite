using System;
using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Owns all selection state. Previously this state was duplicated between
    /// MainViewModel (SyncFloatingState / SetSelectionBounds) and MainWindow
    /// (a dozen private fields). The View now subscribes to SelectionChanged
    /// and reads from this service to update its overlays.
    /// </summary>
    public interface ISelectionService
    {
        // ── Status flags ──────────────────────────────────────────────────
        bool HasActiveSelection { get; }
        bool IsSelecting { get; }
        bool IsFloating { get; }
        bool IsDragging { get; }

        // ── Selection bounds (pixel coordinates) ─────────────────────────
        int MinX { get; }
        int MaxX { get; }
        int MinY { get; }
        int MaxY { get; }

        // ── Drag bounds (pixel coordinates) ──────────────────────────────
        int DragMinX { get; }
        int DragMaxX { get; }
        int DragMinY { get; }
        int DragMaxY { get; }

        /// <summary>
        /// Per-pixel inclusion mask relative to (MinX, MinY).
        /// Null for a marquee selection (every pixel in bounds is selected).
        /// Populated by FinalizeSelection() after a lasso is drawn.
        /// </summary>
        bool[,]? Mask { get; }

        // ── Base Selection (during boolean operations) ───────────────────
        bool[,]? BaseMask { get; }
        int BaseMinX { get; }
        int BaseMinY { get; }
        int BaseMaxX { get; }
        int BaseMaxY { get; }

        // ── Floating layer ────────────────────────────────────────────────
        bool[,]? FloatingPixels { get; set; }
        int FloatingX { get; }
        int FloatingY { get; }
        int FloatingWidth { get; }
        int FloatingHeight { get; }

        /// <summary>Lasso path in pixel coordinates, built up during mouse drag.</summary>
        IReadOnlyList<PixelPoint> LassoPoints { get; }

        // ── Events ────────────────────────────────────────────────────────
        /// <summary>
        /// Raised after any state change. The View subscribes to this to know
        /// when to redraw selection overlays.
        /// </summary>
        event EventHandler SelectionChanged;

        // ── Building a selection ──────────────────────────────────────────
        void BeginRectangleSelection(int x, int y, SelectionMode mode = SelectionMode.Replace);
        void UpdateRectangleSelection(int currentX, int currentY, bool isAltDown = false);

        void BeginEllipseSelection(int x, int y, SelectionMode mode = SelectionMode.Replace);
        void UpdateEllipseSelection(int currentX, int currentY, bool isAltDown = false);

        void BeginLassoSelection(int x, int y, SelectionMode mode = SelectionMode.Replace);
        void AddLassoPoint(int x, int y);

        /// <summary>
        /// Called on mouse-up after drawing a lasso. Computes the Mask from
        /// LassoPoints and marks the selection as active.
        /// </summary>
        void FinalizeSelection();
        
        void ApplyMask(bool[,] mask, int minX, int minY, int maxX, int maxY, SelectionMode mode);

        // ── Querying ──────────────────────────────────────────────────────
        /// <summary>Returns true if the pixel at (x, y) falls inside the active selection.</summary>
        bool IsPixelInSelection(int x, int y);

        /// <summary>
        /// Returns true if (x, y) falls within the selection's bounding box.
        /// Unlike <see cref="IsPixelInSelection"/>, this does NOT check the actual pixel value
        /// when floating — it only tests whether the coordinate is inside the region.
        /// Used for drag hit-testing so that clicking on blank pixels within a floating
        /// selection still allows dragging.
        /// </summary>
        bool IsPointInSelectionBounds(int x, int y);

        /// <summary>Returns true if the pixel at (x, y) falls inside the current lasso polygon.</summary>
        bool IsPointInLasso(int x, int y);

        /// <summary>
        /// Returns true if the active selection contains at least one active (true) pixel on the given sprite state.
        /// </summary>
        bool HasAnyPixelInSelection(SpriteState state);

        // ── Committing / cancelling ───────────────────────────────────────
        /// <summary>
        /// Picks up the selected pixels from the sprite into the floating layer,
        /// clearing them from the canvas. After this call IsFloating is true.
        /// </summary>
        void LiftSelection(SpriteState state);

        /// <summary>
        /// Stamps the floating layer back onto the sprite and clears all selection state.
        /// </summary>
        /// <param name="state">The sprite state to commit to.</param>
        /// <param name="pasteMode">How to handle false pixels in the floating selection.</param>
        void CommitSelection(SpriteState state, FloatingPasteMode pasteMode = FloatingPasteMode.Transparent);
        void ReplaceFloatingData(bool[,] pixels, int x, int y, int w, int h);

        /// <summary>Erases the selected pixels from the sprite and clears selection state.</summary>
        void DeleteSelection(SpriteState state);

        /// <summary>Clears all selection state without modifying the sprite.</summary>
        void Cancel();

        /// <summary>Whether a previous selection mask exists that can be restored with Reselect.</summary>
        bool CanReselect { get; }

        /// <summary>Restores the last active selection mask that was cancelled or deselected.</summary>
        void Reselect();

        /// <summary>
        /// Nudges the active selection mask and bounding box by (dx, dy) without lifting or modifying pixels.
        /// </summary>
        void NudgeSelection(int dx, int dy, int canvasWidth, int canvasHeight);

        /// <summary>
        /// Returns the exact count of active (true) pixels inside the current selection on the active layer.
        /// </summary>
        int CountPixelsInSelection(SpriteState state);

        /// <summary>
        /// Cancels an in-progress selection drag. If in Add/Subtract/Intersect mode with an existing
        /// base selection, restores that base selection rather than clearing all state.
        /// </summary>
        void CancelSelectionDrag();

        // ── Clipboard integration ────────────────────────────────────────
        /// <summary>
        /// Returns a copy of the selected pixels from the sprite (or from the
        /// floating layer if one is active). Does not modify the sprite state.
        /// Returns null if there is no active selection.
        /// </summary>
        PixelClipboardData? CopySelection(SpriteState state);

        /// <summary>
        /// Creates a new floating selection from the given pixel data,
        /// centered on the canvas. Any existing selection is committed first
        /// by the caller.
        /// </summary>
        void PasteAsFloating(PixelClipboardData data, int canvasWidth, int canvasHeight);
        void PasteAsFloatingAt(PixelClipboardData data, int x, int y);

        // ── Drag ─────────────────────────────────────────────────────────
        void BeginDrag();
        void MoveFloatingTo(int newX, int newY);
        void EndDrag();

        // ── Snapshots ────────────────────────────────────────────────────
        SelectionSnapshot CreateSnapshot();
        void RestoreSnapshot(SelectionSnapshot snapshot);

        // ── Transform (resize floating selection) ───────────────────────
        bool IsTransforming { get; }
        TransformHandle ActiveTransformHandle { get; }
        void BeginTransform(TransformHandle handle);
        void UpdateTransform(int newX, int newY, int newW, int newH, bool flipX = false, bool flipY = false);
        void UpdateRotation(double angleDeg);
        void CommitTransform();
        void CancelTransform();

        /// <summary>
        /// Current rotation angle (degrees). Persists after a rotation transform
        /// is committed so overlay handles/boundary remain visually rotated.
        /// Reset to zero when the selection is committed to the canvas or cancelled.
        /// </summary>
        double RotationAngle { get; }

        /// <summary>Pre-rotation floating X (for overlay rendering during rotation).</summary>
        int OriginalFloatingX { get; }
        /// <summary>Pre-rotation floating Y (for overlay rendering during rotation).</summary>
        int OriginalFloatingY { get; }
        /// <summary>Pre-rotation floating width (for overlay rendering during rotation).</summary>
        int OriginalFloatingW { get; }
        /// <summary>Pre-rotation floating height (for overlay rendering during rotation).</summary>
        int OriginalFloatingH { get; }

        // ── Flip (floating selection only) ─────────────────────────────
        void FlipFloatingHorizontally();
        void FlipFloatingVertically();

        /// <summary>
        /// Returns the floating pixel data with any committed rotation baked in.
        /// Use this for rendering/compositing instead of reading FloatingPixels directly,
        /// since FloatingPixels stays in the pre-rotation orientation after a rotation commit.
        /// </summary>
        (bool[,] pixels, bool[,]? mask, int x, int y, int w, int h) GetEffectiveFloating();
    }
}
