using Hexprite.Core;

namespace Hexprite.Controllers
{
    /// <summary>
    /// Interface for the tool input controller that handles tool input state machine,
    /// down/move/up dispatch, shape constraint logic, and preview orchestration.
    /// </summary>
    public interface IToolInputController
    {
        /// <summary>
        /// Main entry point for all non-selection tool input from the View.
        /// </summary>
        void ProcessToolInput(int x, int y, ToolAction action, DrawMode mode, bool isShiftDown, bool isAltDown = false);

        /// <summary>
        /// Cancels any in-progress shape drawing and resets tracking state.
        /// Called by the View when switching tools to prevent stale draw flags.
        /// </summary>
        void CancelInProgressDrawing();

        // ── Shape drawing state (read by View for overlay rendering) ──
        bool IsDrawingLine { get; }
        bool IsDrawingRectangle { get; }
        bool IsDrawingEllipse { get; }
        bool IsDrawingFilledRectangle { get; }
        bool IsDrawingFilledEllipse { get; }
        bool IsDrawingGradient { get; }

        // ── Move tool state (read by View for overlay rendering) ──
        bool IsMoving { get; }
    }
}
