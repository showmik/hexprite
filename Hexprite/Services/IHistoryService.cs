using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IHistoryService
    {
        bool CanUndo { get; }
        bool CanRedo { get; }
        void SaveState(SpriteState state);
        SpriteState Undo(SpriteState currentState);
        SpriteState Redo(SpriteState currentState);
        void Clear();
    }
}
