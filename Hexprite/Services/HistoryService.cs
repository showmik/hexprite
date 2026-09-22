using System.Collections.Generic;
using Hexprite.Core;

namespace Hexprite.Services
{
    /// <summary>
    /// Manages application state history using stack-based undo/redo operations.
    /// Limits history depth to <see cref="MaxHistory"/> entries.
    /// </summary>
    public class HistoryService : IHistoryService
    {
        private const int MaxHistory = 100;

        private readonly LinkedList<SpriteState> _undoStack = new();
        private readonly LinkedList<SpriteState> _redoStack = new();

        /// <summary>Indicates whether there are states available to undo.</summary>
        public bool CanUndo => _undoStack.Count > 0;
        /// <summary>Indicates whether there are states available to redo.</summary>
        public bool CanRedo => _redoStack.Count > 0;

        /// <summary>
        /// Saves a snapshot of the current state to the undo stack and clears redo history.
        /// </summary>
        /// <param name="state">The sprite state to save. Selection snapshots are not persisted.</param>
        public void SaveState(SpriteState state)
        {
            if (state == null) return;

            _undoStack.AddLast(CloneForHistory(state));
            _redoStack.Clear();
            
            while (_undoStack.Count > MaxHistory)
            {
                _undoStack.RemoveFirst();
            }
        }

        /// <summary>
        /// Restores the previous state from the undo stack, moving the current state to redo.
        /// </summary>
        /// <param name="currentState">The state currently on the canvas.</param>
        /// <returns>The restored sprite state.</returns>
        public SpriteState Undo(SpriteState currentState)
        {
            if (_undoStack.Count == 0) return currentState;

            _redoStack.AddLast(CloneForHistory(currentState));
            var restored = _undoStack.Last!.Value;
            _undoStack.RemoveLast();
            restored.EnsureLayers();
            return restored;
        }

        /// <summary>
        /// Restores a previously undone state from the redo stack, moving the current state to undo.
        /// </summary>
        /// <param name="currentState">The state currently on the canvas.</param>
        /// <returns>The restored sprite state.</returns>
        public SpriteState Redo(SpriteState currentState)
        {
            if (_redoStack.Count == 0) return currentState;

            _undoStack.AddLast(CloneForHistory(currentState));
            var restored = _redoStack.Last!.Value;
            _redoStack.RemoveLast();
            restored.EnsureLayers();
            return restored;
        }

        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }

        /// <summary>
        /// Creates a deep copy of the sprite state for history tracking.
        /// Selection snapshots are excluded as they are managed externally by the selection service.
        /// </summary>
        private static SpriteState CloneForHistory(SpriteState state)
            // SelectionSnapshot is already cloned by the selection service before SaveStateForUndo.
            // Avoid cloning it a second time when persisting history entries.
            => state.Clone(cloneSelectionSnapshot: false);
    }
}
