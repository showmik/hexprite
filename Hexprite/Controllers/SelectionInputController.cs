using System;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.ViewModels;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Owns the selection-tool input state machine: marquee/lasso/magic-wand
    /// down/move dispatch, selection mode logic (Add/Subtract/Intersect),
    /// floating layer management, and commit/cancel orchestration.
    /// Extracted from MainWindow.xaml.cs to move domain logic out of the View.
    /// </summary>
    public class SelectionInputController(
        MainViewModel vm,
        ISelectionService selectionService,
        IDrawingService drawingService) : ISelectionInputController
    {
        private readonly MainViewModel _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        private readonly ISelectionService _selection = selectionService ?? throw new ArgumentNullException(nameof(selectionService));
        private readonly IDrawingService _drawingService = drawingService ?? throw new ArgumentNullException(nameof(drawingService));

        private int _selectionAnchorX = -1;
        private int _selectionAnchorY = -1;
        private bool _wasSelectionActiveOnDown;
        private bool _hasDragged;
        private bool _transformUndoPushed;
        private bool _dragUndoPushed;

        /// <summary>
        /// Main entry point for selection tool input from the View.
        /// The View only provides pixel coordinates and modifier key state.
        /// </summary>
        public void ProcessInput(int x, int y, ToolAction action, bool isShiftDown, bool isAltDown, bool isInverse = false)
        {
            if (_vm.IsProcessing) return;

            switch (action)
            {
                case ToolAction.Down:
                    HandleDown(x, y, isShiftDown, isAltDown, isInverse);
                    break;
                case ToolAction.Move:
                    HandleMove(x, y, isShiftDown, isAltDown);
                    break;
                case ToolAction.Up:
                    HandleUp();
                    break;
            }
        }

        /// <summary>
        /// Commits the current selection (stamps floating pixels) and clears overlays.
        /// Returns true if there was an active selection that was committed.
        /// </summary>
        public bool CommitIfActive(bool saveHistory = true)
        {
            if (!_selection.HasActiveSelection) return false;

            if (_selection.IsTransforming)
                CommitTransformIfActive();

            bool wasFloating = _selection.IsFloating;

            if (saveHistory && wasFloating)
                _vm.SaveStateForUndo();

            _selection.CommitSelection(_vm.SpriteState, _vm.FloatingPasteMode);
            _vm.RedrawGridFromMemory();
            
            if (wasFloating)
            {
                _vm.MarkCodeStale();
            }
            
            return true;
        }

        /// <summary>
        /// Begins a floating-layer drag from the specified screen-space anchor.
        /// Called by the View when a click lands inside an active selection.
        /// Returns true if the drag was started.
        /// </summary>
        public bool TryBeginDrag(int pixelX, int pixelY)
        {
            if (_vm.IsProcessing) return false;

            if (!_vm.CanModifyActiveLayer)
            {
                _vm.ShowStatus("Cannot modify selection: layer is locked, hidden, or multiple layers selected.", 3000);
                return false;
            }

            if (!_selection.HasActiveSelection || !_selection.IsPointInSelectionBounds(pixelX, pixelY))
                return false;

            // Reset any stale drag state from previous interrupted drag (e.g., focus lost to UI element)
            if (_selection.IsDragging)
                _selection.EndDrag();

            _dragUndoPushed = false;
            
            // Only lift if not already floating (avoids redundant undo entries on click)
            if (!_selection.IsFloating)
            {
                if (!_selection.HasAnyPixelInSelection(_vm.SpriteState))
                {
                    _vm.ShowStatus("Selected area on active layer is empty.", 3000);
                    return false;
                }

                _vm.SaveStateForUndo();
                _selection.LiftSelection(_vm.SpriteState);
            }
            
            _selection.BeginDrag();
            _vm.RedrawGridFromMemory(updateHardware: false);
            return true;
        }

        /// <summary>
        /// Lifts the active selection in place (without moving it) so transform handles
        /// appear immediately. No-op if there is no active selection or it is already floating.
        /// </summary>
        public void EnterTransformMode()
        {
            if (!_vm.CanModifyActiveLayer)
            {
                _vm.ShowStatus("Cannot modify selection: layer is locked, hidden, or multiple layers selected.", 3000);
                return;
            }

            if (!_selection.HasActiveSelection) return;
            if (_selection.IsFloating) return;

            if (!_selection.HasAnyPixelInSelection(_vm.SpriteState))
            {
                _vm.ShowStatus("Selected area on active layer is empty.", 3000);
                return;
            }

            // Switch to Marquee BEFORE lifting so the user can immediately
            // click and drag transform handles. This must happen before
            // LiftSelection because the tool-change handler (OnToolChanged)
            // commits any floating selection for non-Move tools. If we lifted
            // first, the just-created floating state would be immediately
            // committed, preventing the bounding box / transform handles from
            // appearing (the "first-time CTRL+T" bug).
            var tool = _vm.CurrentTool;
            bool isSelectionTool = tool == ToolMode.Marquee
                                || tool == ToolMode.Lasso
                                || tool == ToolMode.EllipticalMarquee
                                || tool == ToolMode.MagicWand
                                || tool == ToolMode.Move;
            if (!isSelectionTool)
                _vm.CurrentTool = ToolMode.Marquee;

            _vm.SaveStateForUndo();
            _selection.LiftSelection(_vm.SpriteState);
            _vm.RedrawGridFromMemory();
        }

        // ── Private: input dispatch ────────────────────────────────────────

        private void HandleDown(int x, int y, bool isShiftDown, bool isAltDown, bool isInverse = false)
        {
            var mode = _selection.HasActiveSelection ? DetermineSelectionMode(isShiftDown, isAltDown) : SelectionMode.Replace;

            // If clicking inside an active selection with Replace mode, lift & drag
            if (_selection.HasActiveSelection && _selection.IsPointInSelectionBounds(x, y) && mode == SelectionMode.Replace)
            {
                // Drag is handled by TryBeginDrag, called from the View with screen-space coords
                return;
            }

            // Capture if a selection exists BEFORE we potentially commit it in Replace mode
            _wasSelectionActiveOnDown = _selection.HasActiveSelection;

            if (mode == SelectionMode.Replace)
            {
                CommitIfActive();
                // If we committed, the new selection is starting fresh, so we allow constraint
                _wasSelectionActiveOnDown = false;
            }
            else if (_selection.IsFloating)
            {
                CommitIfActive();
            }

            _hasDragged = false;

            if (_vm.CurrentTool == ToolMode.MagicWand)
            {
                var fillMask = _drawingService.GetFloodFillMask(_vm.SpriteState, x, y, _selection, _vm.FloatingPasteMode,
                    out int minX, out int minY, out int maxX, out int maxY);

                if (isInverse)
                {
                    var domain = DrawingService.GetPixelDomain(_vm.SpriteState);
                    var inverseMask = BuildInverseMask(fillMask, minX, minY,
                        domain.minX, domain.minY, domain.width, domain.height);
                    _selection.ApplyMask(inverseMask, domain.minX, domain.minY, domain.maxX, domain.maxY, mode);
                }
                else
                {
                    _selection.ApplyMask(fillMask, minX, minY, maxX, maxY, mode);
                }
                _vm.RedrawGridFromMemory();
            }
            else
            {
                _selectionAnchorX = x;
                _selectionAnchorY = y;
                if (_vm.CurrentTool == ToolMode.Lasso)
                    _selection.BeginLassoSelection(x, y, mode);
                else if (_vm.CurrentTool == ToolMode.EllipticalMarquee)
                    _selection.BeginEllipseSelection(x, y, mode);
                else
                    _selection.BeginRectangleSelection(x, y, mode);
            }
        }

        private void HandleMove(int x, int y, bool isShiftDown, bool isAltDown)
        {
            if (!_selection.IsSelecting) return;

            _hasDragged = true;

            bool applyConstraints = !_wasSelectionActiveOnDown;
            bool applyShiftConstraint = applyConstraints && isShiftDown;
            bool applyAltCenter = applyConstraints && isAltDown;

            if (_vm.CurrentTool == ToolMode.Lasso)
            {
                _selection.AddLassoPoint(x, y);
            }
            else
            {
                // Constrain to square/circle when Shift is held
                if (applyShiftConstraint && _selectionAnchorX != -1 && _selectionAnchorY != -1)
                {
                    int dx = x - _selectionAnchorX;
                    int dy = y - _selectionAnchorY;
                    int maxDist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int signX = dx == 0 ? 1 : Math.Sign(dx);
                    int signY = dy == 0 ? 1 : Math.Sign(dy);
                    x = _selectionAnchorX + signX * maxDist;
                    y = _selectionAnchorY + signY * maxDist;
                }

                if (_vm.CurrentTool == ToolMode.EllipticalMarquee)
                    _selection.UpdateEllipseSelection(x, y, applyAltCenter);
                else
                    _selection.UpdateRectangleSelection(x, y, applyAltCenter);
            }
        }

        private void HandleUp()
        {
            if (_selection.IsSelecting)
            {
                if (_hasDragged)
                    _selection.FinalizeSelection();
                else
                    _selection.CancelSelectionDrag();
            }

            _selectionAnchorX = -1;
            _selectionAnchorY = -1;
            _hasDragged = false;

            if (_vm.IsHardwarePreviewEnabled)
            {
                _vm.RedrawGridFromMemory();
            }
        }

        /// <summary>
        /// Called by the View on any mouse movement during a selection drag,
        /// even when the pixel coordinates haven't changed (sub-pixel movement).
        /// This lets us distinguish a real drag from a stationary click.
        /// </summary>
        public void NotifyMouseMoved()
        {
            _hasDragged = true;
        }

        private static SelectionMode DetermineSelectionMode(bool isShiftDown, bool isAltDown)
        {
            if (isShiftDown && isAltDown) return SelectionMode.Intersect;
            if (isShiftDown) return SelectionMode.Add;
            if (isAltDown) return SelectionMode.Subtract;
            return SelectionMode.Replace;
        }

        internal static bool[,] BuildInverseMask(
            bool[,] fillMask,
            int fillMinX,
            int fillMinY,
            int domainMinX,
            int domainMinY,
            int domainWidth,
            int domainHeight)
        {
            var inverseMask = new bool[domainWidth, domainHeight];
            int fillWidth = fillMask.GetLength(0);
            int fillHeight = fillMask.GetLength(1);

            for (int my = 0; my < domainHeight; my++)
            {
                int canvasY = domainMinY + my;
                for (int mx = 0; mx < domainWidth; mx++)
                {
                    int canvasX = domainMinX + mx;
                    int fx = canvasX - fillMinX;
                    int fy = canvasY - fillMinY;
                    bool isFilled = fx >= 0 && fx < fillWidth && fy >= 0 && fy < fillHeight && fillMask[fx, fy];
                    inverseMask[mx, my] = !isFilled;
                }
            }

            return inverseMask;
        }

        // ── Floating selection resize (transform handles) ─────────────────

        private int _transformAnchorOx;
        private int _transformAnchorOy;
        private int _transformAnchorOw;
        private int _transformAnchorOh;

        // ── Rotation state ─────────────────────────────────────────────────
        private bool _isRotationTransform;
        private double _rotationCenterPxX;
        private double _rotationCenterPxY;
        private double _committedRotationOffset;
        private double _lastRotationMouseAngle;
        private double _accumulatedRotationDeg;

        /// <summary>
        /// Hit-tests transform handles in image-local coordinates (same space as <see cref="MainWindow.GetPixelCoordinates"/> input).
        /// </summary>
        public TransformHandle HitTestHandle(double mouseImgX, double mouseImgY, double actualW, double actualH)
        {
            if (!_selection.IsFloating || !_selection.HasActiveSelection || actualW <= 0 || actualH <= 0)
                return TransformHandle.None;

            if (!double.IsFinite(mouseImgX) || !double.IsFinite(mouseImgY) || !double.IsFinite(actualW) || !double.IsFinite(actualH))
                return TransformHandle.None;

            int cw = _vm.SpriteState.Width;
            int ch = _vm.SpriteState.Height;
            // Bug 6: guard against zero canvas dimensions — cellMin would be 0,
            // making handleSizeCells NaN and silently disabling all handle hit-tests.
            if (cw <= 0 || ch <= 0) return TransformHandle.None;

            int fx = _selection.FloatingX;
            int fy = _selection.FloatingY;
            int fw = _selection.FloatingWidth;
            int fh = _selection.FloatingHeight;
            if (fw <= 0 || fh <= 0) return TransformHandle.None;

            double px = mouseImgX / actualW * cw;
            double py = mouseImgY / actualH * ch;

            double pixelsPerCellX = actualW / cw;
            double pixelsPerCellY = actualH / ch;
            double pixelsPerCell = Math.Min(pixelsPerCellX, pixelsPerCellY);
            if (pixelsPerCell <= 0.0) return TransformHandle.None;

            // Visual handle size is constant 10px regardless of zoom
            // Convert to cell coordinates: 10px / pixelsPerCell, with padding for comfortable hit
            const double handleSizePixels = 10.0;
            double handleSizeCells = handleSizePixels / pixelsPerCell;
            double hitR = Math.Max(handleSizeCells * 0.75, 1.0);

            bool Near(double hx, double hy)
            {
                double dx = px - hx, dy = py - hy;
                return dx * dx + dy * dy <= hitR * hitR;
            }

            double midX = fx + fw * 0.5;
            double midY = fy + fh * 0.5;
            int right = fx + fw;
            int bottom = fy + fh;

            // Transform mouse into the selection's local (pre-rotation) space.
            // This lets all distance comparisons work correctly at any rotation
            // without computing rotated handle positions.
            double rotAngle = _selection.RotationAngle;
            if (Math.Abs(rotAngle) > 0.01)
            {
                double rad = -rotAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                double dx = px - midX;
                double dy = py - midY;
                px = cos * dx - sin * dy + midX;
                py = sin * dx + cos * dy + midY;
            }
            if (Near(fx, fy)) return TransformHandle.NW;
            if (Near(right, fy)) return TransformHandle.NE;
            if (Near(fx, bottom)) return TransformHandle.SW;
            if (Near(right, bottom)) return TransformHandle.SE;
            if (Near(midX, fy)) return TransformHandle.N;
            if (Near(midX, bottom)) return TransformHandle.S;
            if (Near(fx, midY)) return TransformHandle.W;
            if (Near(right, midY)) return TransformHandle.E;

            // Rotation zone: corner handles + area slightly outside them
            double rotR = hitR * 2.5;
            bool NearRotate(double hx, double hy)
            {
                double dx = px - hx, dy = py - hy;
                double dist2 = dx * dx + dy * dy;
                return dist2 > hitR * hitR && dist2 <= rotR * rotR;
            }

            if (NearRotate(fx, fy) || NearRotate(right, fy) ||
                NearRotate(fx, bottom) || NearRotate(right, bottom))
                return TransformHandle.Rotate;

            return TransformHandle.None;
        }

        /// <summary>
        /// Starts a resize drag from a handle. Lifts the selection if it is not already floating.
        /// </summary>
        public bool TryBeginTransform(TransformHandle handle)
        {
            if (_vm.IsProcessing) return false;

            if (!_vm.CanModifyActiveLayer)
            {
                _vm.ShowStatus("Cannot modify selection: layer is locked, hidden, or multiple layers selected.", 3000);
                return false;
            }

            if (handle == TransformHandle.None || !_selection.HasActiveSelection)
                return false;

            _transformUndoPushed = false;
            _isRotationTransform = handle == TransformHandle.Rotate;

            if (!_selection.IsFloating)
            {
                _vm.SaveStateForUndo();
                _transformUndoPushed = true; // We pushed for the lift, no need to push again
                _selection.LiftSelection(_vm.SpriteState);
            }

            _transformAnchorOx = _selection.FloatingX;
            _transformAnchorOy = _selection.FloatingY;
            _transformAnchorOw = _selection.FloatingWidth;
            _transformAnchorOh = _selection.FloatingHeight;

            _selection.BeginTransform(handle);
            _vm.RedrawGridFromMemory(updateHardware: false);
            return true;
        }

        /// <summary>
        /// Applies resize delta in pixel space relative to the mouse-down anchor (same as marquee drag deltas).
        /// </summary>
        public void UpdateTransformFromDelta(int deltaX, int deltaY, bool shiftAspect, bool altFromCenter)
        {
            if (!_selection.IsTransforming)
                return;

            if (deltaX == 0 && deltaY == 0)
                return;

            // Defer the undo push until the user actually drags the handle.
            // This prevents "double undos" if they just click or cancel.
            if (!_transformUndoPushed && (deltaX != 0 || deltaY != 0))
            {
                _vm.SaveStateForUndo();
                _transformUndoPushed = true;
            }

            int oldX = _selection.FloatingX;
            int oldY = _selection.FloatingY;
            int oldW = _selection.FloatingWidth;
            int oldH = _selection.FloatingHeight;

            var handle = _selection.ActiveTransformHandle;

            // When the selection is rotated, the world-space mouse delta doesn't
            // align with the handle's local direction.  Inverse-rotate the delta
            // into the selection's local coordinate space so ComputeResizeRect
            // moves the correct edges.
            double rotAngle = _selection.RotationAngle;
            if (Math.Abs(rotAngle) > 0.01)
            {
                double rad = -rotAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);
                double ldx = cos * deltaX - sin * deltaY;
                double ldy = sin * deltaX + cos * deltaY;
                deltaX = (int)Math.Round(ldx, MidpointRounding.AwayFromZero);
                deltaY = (int)Math.Round(ldy, MidpointRounding.AwayFromZero);
            }

            var (x, y, w, h, flipX, flipY) = ComputeResizeRect(
                handle,
                _transformAnchorOx, _transformAnchorOy, _transformAnchorOw, _transformAnchorOh,
                deltaX, deltaY,
                shiftAspect, altFromCenter);

            // When the selection is rotated and the resize is NOT from-center (Alt),
            // the rotation pivot (rect center) shifts because one edge moved.
            // This makes the opposite edge appear to move in world-space.
            // Compensate by translating so the anchor edge stays visually fixed.
            // Math: offset = R * d - d, where d = new_center - old_center.
            if (Math.Abs(rotAngle) > 0.01 && !altFromCenter)
            {
                double oldCx = _transformAnchorOx + _transformAnchorOw / 2.0;
                double oldCy = _transformAnchorOy + _transformAnchorOh / 2.0;
                double newCx = x + w / 2.0;
                double newCy = y + h / 2.0;
                double dcx = newCx - oldCx;
                double dcy = newCy - oldCy;

                double rad = rotAngle * Math.PI / 180.0;
                double cos = Math.Cos(rad);
                double sin = Math.Sin(rad);

                // R * d - d = ((cos-1)*dcx - sin*dcy, sin*dcx + (cos-1)*dcy)
                double offsetX = (cos - 1) * dcx - sin * dcy;
                double offsetY = sin * dcx + (cos - 1) * dcy;

                x += (int)Math.Round(offsetX, MidpointRounding.AwayFromZero);
                y += (int)Math.Round(offsetY, MidpointRounding.AwayFromZero);
            }

            _selection.UpdateTransform(x, y, w, h, flipX, flipY);

            // Use full redraw when rotated — the rotated content can extend
            // beyond the local AABB, making partial region computation unreliable.
            if (Math.Abs(rotAngle) > 0.01)
            {
                _vm.RedrawGridFromMemory(updateHardware: false);
            }
            else
            {
                int newX = _selection.FloatingX;
                int newY = _selection.FloatingY;
                int newW = _selection.FloatingWidth;
                int newH = _selection.FloatingHeight;

                int minX = Math.Min(oldX, newX);
                int minY = Math.Min(oldY, newY);
                int maxX = Math.Max(oldX + oldW - 1, newX + newW - 1);
                int maxY = Math.Max(oldY + oldH - 1, newY + newH - 1);

                const int padding = 1;
                _vm.RedrawRegion(minX - padding, minY - padding, maxX + padding, maxY + padding, updatePreviewSimulation: false);
            }
        }

        public void CommitTransformIfActive()
        {
            if (!_selection.IsTransforming)
                return;

            _isRotationTransform = false;
            _selection.CommitTransform();
            _vm.MarkCodeStale();
            _vm.RedrawGridFromMemory();
        }

        public void CancelTransformIfActive()
        {
            if (!_selection.IsTransforming)
                return;

            _isRotationTransform = false;
            _selection.CancelTransform();
            
            // Pop duplicate history state if we pushed one before cancelling
            if (_transformUndoPushed && _vm.UndoCommand.CanExecute(null))
            {
                _vm.UndoCommand.Execute(null);
            }
            else
            {
                _vm.RedrawGridFromMemory();
            }

            // Bug 10: reset flag so a future cancel doesn't pop an unrelated undo entry
            _transformUndoPushed = false;
        }

        // ── Rotation via corner handles ────────────────────────────────────

        /// <summary>
        /// Records the initial angle from the floating selection center to the mouse.
        /// Called once on mouse-down when the rotation zone is hit.
        /// </summary>
        public void BeginRotation(double mouseImgX, double mouseImgY, double actualW, double actualH)
        {
            if (!_selection.IsFloating || !_selection.IsTransforming) return;

            int cw = _vm.SpriteState.Width;
            int ch = _vm.SpriteState.Height;
            if (cw <= 0 || ch <= 0 || actualW <= 0 || actualH <= 0) return;
            if (!double.IsFinite(mouseImgX) || !double.IsFinite(mouseImgY) || !double.IsFinite(actualW) || !double.IsFinite(actualH)) return;

            // Convert image coords to pixel-space
            double px = mouseImgX / actualW * cw;
            double py = mouseImgY / actualH * ch;

            // Center of the floating selection in pixel-space
            _rotationCenterPxX = _selection.FloatingX + _selection.FloatingWidth / 2.0;
            _rotationCenterPxY = _selection.FloatingY + _selection.FloatingHeight / 2.0;

            _lastRotationMouseAngle = Math.Atan2(py - _rotationCenterPxY, px - _rotationCenterPxX);
            _accumulatedRotationDeg = 0.0;

            // Capture the committed rotation so the new drag adds to it
            // rather than replacing it.
            _committedRotationOffset = _selection.RotationAngle;
        }

        /// <summary>
        /// Computes the cumulative rotation angle from the mouse position and applies it.
        /// Called on every mouse-move during a rotation drag.
        /// </summary>
        public void UpdateRotationFromMouse(double mouseImgX, double mouseImgY,
            double actualW, double actualH, bool shiftConstrain)
        {
            if (!_selection.IsTransforming || !_isRotationTransform) return;

            int cw = _vm.SpriteState.Width;
            int ch = _vm.SpriteState.Height;
            if (cw <= 0 || ch <= 0 || actualW <= 0 || actualH <= 0) return;
            if (!double.IsFinite(mouseImgX) || !double.IsFinite(mouseImgY) || !double.IsFinite(actualW) || !double.IsFinite(actualH)) return;

            double px = mouseImgX / actualW * cw;
            double py = mouseImgY / actualH * ch;

            double currentAngle = Math.Atan2(py - _rotationCenterPxY, px - _rotationCenterPxX);
            double stepRad = currentAngle - _lastRotationMouseAngle;
            // Normalize incremental step to [-π, π] to handle the atan2 discontinuity seamlessly across 360°
            stepRad = Math.Atan2(Math.Sin(stepRad), Math.Cos(stepRad));
            _accumulatedRotationDeg += stepRad * 180.0 / Math.PI;
            _lastRotationMouseAngle = currentAngle;

            double effectiveDeltaDeg = _accumulatedRotationDeg;
            if (shiftConstrain)
            {
                // Snap to 15° increments
                effectiveDeltaDeg = Math.Round(effectiveDeltaDeg / 15.0, MidpointRounding.AwayFromZero) * 15.0;
            }

            // Defer undo push until the user actually rotates
            if (!_transformUndoPushed && Math.Abs(effectiveDeltaDeg) > 0.5)
            {
                // Temporarily revert to the pristine start state before taking the undo snapshot
                _selection.UpdateRotation(_committedRotationOffset);
                _vm.SaveStateForUndo();
                _transformUndoPushed = true;
            }

            _selection.UpdateRotation(_committedRotationOffset + effectiveDeltaDeg);

            // FloatingX/Y/W/H don't change during rotation (only RotationAngle does),
            // but the rotated content extends beyond the original AABB. Use a full
            // redraw to ensure all affected pixels are updated.
            _vm.RedrawGridFromMemory(updateHardware: false);
        }

        public void UpdateDrag(int newX, int newY)
        {
            if (!_selection.IsDragging || !_selection.IsFloating) return;
            if (newX == _selection.FloatingX && newY == _selection.FloatingY) return;

            if (!_dragUndoPushed)
            {
                _vm.SaveStateForUndo();
                _dragUndoPushed = true;
            }

            int oldX = _selection.FloatingX;
            int oldY = _selection.FloatingY;
            int fw = _selection.FloatingWidth;
            int fh = _selection.FloatingHeight;

            _selection.MoveFloatingTo(newX, newY);

            // When rotated, the rendered content extends beyond FloatingX/Y/W/H.
            // Use full redraw to prevent stale pixel ghosting.
            if (Math.Abs(_selection.RotationAngle) > 0.01)
            {
                _vm.RedrawGridFromMemory(updateHardware: false);
            }
            else
            {
                int minX = Math.Min(oldX, newX);
                int minY = Math.Min(oldY, newY);
                int maxX = Math.Max(oldX + fw - 1, newX + fw - 1);
                int maxY = Math.Max(oldY + fh - 1, newY + fh - 1);

                const int padding = 1;
                _vm.RedrawRegion(minX - padding, minY - padding, maxX + padding, maxY + padding, updatePreviewSimulation: false);
            }
        }

        public void CancelDragIfActive()
        {
            if (!_selection.IsDragging) return;
            _selection.EndDrag();
            
            if (_dragUndoPushed && _vm.UndoCommand.CanExecute(null))
            {
                _vm.UndoCommand.Execute(null);
            }

            _dragUndoPushed = false;
        }

        /// <inheritdoc/>
        public void ResetControllerState()
        {
            _selectionAnchorX = -1;
            _selectionAnchorY = -1;
            _wasSelectionActiveOnDown = false;
            _hasDragged = false;
            _transformUndoPushed = false;
            _dragUndoPushed = false;
            _isRotationTransform = false;
            _accumulatedRotationDeg = 0.0;
            _lastRotationMouseAngle = 0.0;
        }

        /// <summary>Computes the floating bounding rect after a resize drag.</summary>
        internal static (int x, int y, int w, int h, bool flipX, bool flipY) ComputeResizeRect(
            TransformHandle handle,
            int ox, int oy, int ow, int oh,
            int dx, int dy,
            bool shiftAspect, bool altFromCenter)
        {
            int left = ox;
            int top = oy;
            int right = ox + ow - 1;
            int bottom = oy + oh - 1;

            bool moveLeft = handle is TransformHandle.NW or TransformHandle.SW or TransformHandle.W;
            bool moveRight = handle is TransformHandle.NE or TransformHandle.SE or TransformHandle.E;
            bool moveTop = handle is TransformHandle.NW or TransformHandle.NE or TransformHandle.N;
            bool moveBottom = handle is TransformHandle.SW or TransformHandle.SE or TransformHandle.S;

            int newLeft = left;
            int newRight = right;
            int newTop = top;
            int newBottom = bottom;

            if (moveLeft)
            {
                newLeft = left + dx;
                if (altFromCenter) newRight = right - dx;
            }
            else if (moveRight)
            {
                newRight = right + dx;
                if (altFromCenter) newLeft = left - dx;
            }

            if (moveTop)
            {
                newTop = top + dy;
                if (altFromCenter) newBottom = bottom - dy;
            }
            else if (moveBottom)
            {
                newBottom = bottom + dy;
                if (altFromCenter) newTop = top - dy;
            }

            bool flipX = newRight < newLeft;
            bool flipY = newBottom < newTop;

            int normLeft = Math.Min(newLeft, newRight);
            int normRight = Math.Max(newLeft, newRight);
            int normTop = Math.Min(newTop, newBottom);
            int normBottom = Math.Max(newTop, newBottom);

            int x = normLeft;
            int y = normTop;
            int w = Math.Max(1, normRight - normLeft + 1);
            int h = Math.Max(1, normBottom - normTop + 1);

            if (shiftAspect && ow > 0 && oh > 0 && handle != TransformHandle.None)
            {
                double sx = w / (double)ow;
                double sy = h / (double)oh;
                // For side handles (N,S,E,W), only one dimension changes during drag.
                // Use the scale from the dimension actually being dragged.
                // For diagonal handles, use the larger scale (existing behavior).
                bool isSideHandle = handle is TransformHandle.N or TransformHandle.S
                                                    or TransformHandle.E or TransformHandle.W;
                double s;
                if (isSideHandle)
                {
                    // N/S: use height scale, E/W: use width scale
                    s = (handle is TransformHandle.N or TransformHandle.S) ? Math.Abs(sy) : Math.Abs(sx);
                }
                else
                {
                    s = Math.Max(Math.Abs(sx), Math.Abs(sy));
                }

                int nw = Math.Max(1, (int)Math.Round(ow * s, MidpointRounding.AwayFromZero));
                int nh = Math.Max(1, (int)Math.Round(oh * s, MidpointRounding.AwayFromZero));

                if (altFromCenter)
                {
                    double cx = ox + ow / 2.0;
                    double cy = oy + oh / 2.0;
                    x = (int)Math.Round(cx - nw / 2.0, MidpointRounding.AwayFromZero);
                    y = (int)Math.Round(cy - nh / 2.0, MidpointRounding.AwayFromZero);
                    w = nw;
                    h = nh;
                    return (x, y, w, h, flipX, flipY);
                }

                // Re-anchor to the same fixed edges (opposite the dragged edges),
                // taking flips into account.
                if (moveLeft)
                {
                    int fixedRight = right;
                    x = flipX ? fixedRight : fixedRight - nw + 1;
                }
                else if (moveRight)
                {
                    int fixedLeft = left;
                    x = flipX ? fixedLeft - nw + 1 : fixedLeft;
                }
                else
                {
                    // Non-dragged dimension: center it so the anchor stays on the
                    // dragged edge's axis, not at a corner.  This prevents the
                    // selection from visually drifting when Shift-resizing a
                    // rotated selection via a side handle (N/S/E/W).
                    double cx = ox + ow / 2.0;
                    x = (int)Math.Round(cx - nw / 2.0, MidpointRounding.AwayFromZero);
                }

                if (moveTop)
                {
                    int fixedBottom = bottom;
                    y = flipY ? fixedBottom : fixedBottom - nh + 1;
                }
                else if (moveBottom)
                {
                    int fixedTop = top;
                    y = flipY ? fixedTop - nh + 1 : fixedTop;
                }
                else
                {
                    // Non-dragged dimension: center it (see X comment above).
                    double cy = oy + oh / 2.0;
                    y = (int)Math.Round(cy - nh / 2.0, MidpointRounding.AwayFromZero);
                }

                w = nw;
                h = nh;
            }

            return (x, y, w, h, flipX, flipY);
        }
    }
}
