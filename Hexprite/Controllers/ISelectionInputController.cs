using Hexprite.Core;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Interface for the selection input controller that handles selection-tool input
    /// state machine, marquee/lasso/magic-wand dispatch, and floating layer management.
    /// </summary>
    public interface ISelectionInputController
    {
        /// <summary>
        /// Main entry point for selection tool input from the View.
        /// The View only provides pixel coordinates and modifier key state.
        /// </summary>
        void ProcessInput(int x, int y, ToolAction action, bool isShiftDown, bool isAltDown, bool isInverse = false);

        /// <summary>
        /// Commits the current selection (stamps floating pixels) and clears overlays.
        /// Returns true if there was an active selection that was committed.
        /// </summary>
        bool CommitIfActive(bool saveHistory = true);

        /// <summary>
        /// Begins a floating-layer drag from the specified screen-space anchor.
        /// Called by the View when a click lands inside an active selection.
        /// Returns true if the drag was started.
        /// </summary>
        bool TryBeginDrag(int pixelX, int pixelY);

        /// <summary>
        /// Lifts the active selection in place (without moving it) so transform handles
        /// appear immediately. No-op if there is no active selection or it is already floating.
        /// </summary>
        void EnterTransformMode();

        /// <summary>
        /// Called by the View on any mouse movement during a selection drag,
        /// even when the pixel coordinates haven't changed (sub-pixel movement).
        /// This lets us distinguish a real drag from a stationary click.
        /// </summary>
        void NotifyMouseMoved();

        /// <summary>
        /// Hit-tests transform handles in image-local coordinates.
        /// </summary>
        TransformHandle HitTestHandle(double mouseImgX, double mouseImgY, double actualW, double actualH);

        /// <summary>
        /// Starts a resize drag from a handle. Lifts the selection if it is not already floating.
        /// </summary>
        bool TryBeginTransform(TransformHandle handle);

        /// <summary>
        /// Applies resize delta in pixel space relative to the mouse-down anchor.
        /// </summary>
        void UpdateTransformFromDelta(int deltaX, int deltaY, bool shiftAspect, bool altFromCenter);

        /// <summary>
        /// Commits any active transform operation.
        /// </summary>
        void CommitTransformIfActive();

        /// <summary>
        /// Cancels any active transform operation.
        /// </summary>
        void CancelTransformIfActive();

        /// <summary>
        /// Records the initial angle from the floating selection center to the mouse.
        /// Called once on mouse-down when a rotation zone is hit.
        /// </summary>
        void BeginRotation(double mouseImgX, double mouseImgY, double actualW, double actualH);

        /// <summary>
        /// Computes the cumulative rotation angle from the mouse position and applies it.
        /// Called on every mouse-move during a rotation drag.
        /// </summary>
        void UpdateRotationFromMouse(double mouseImgX, double mouseImgY, double actualW, double actualH, bool shiftConstrain);

        void UpdateDrag(int newX, int newY);
        void CancelDragIfActive();

        /// <summary>
        /// Resets ephemeral controller state (anchors, drag flags, undo tracking).
        /// </summary>
        void ResetControllerState();
    }
}
