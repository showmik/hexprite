using Hexprite.Core;
using System.Collections.Generic;

namespace Hexprite.Services
{
    public interface IAutosaveService
    {
        /// <summary>
        /// Starts the background autosave loop that periodically serializes the SpriteState if it has unsaved changes.
        /// </summary>
        void StartAutosaveLoop(string documentId, System.Func<SpriteState?> stateProvider, System.Func<bool> isDirtyProvider, System.Func<AutosaveMetadata?>? metadataProvider = null);

        /// <summary>
        /// Starts the background autosave loop for an AssetPackDocument.
        /// </summary>
        void StartAssetPackAutosaveLoop(string documentId, System.Func<AssetPackDocument?> stateProvider, System.Func<bool> isDirtyProvider, System.Func<AutosaveMetadata?>? metadataProvider = null);

        /// <summary>
        /// Starts the background autosave loop for a FontDocument.
        /// </summary>
        void StartFontAutosaveLoop(string documentId, System.Func<FontDocument?> stateProvider, System.Func<bool> isDirtyProvider, System.Func<AutosaveMetadata?>? metadataProvider = null);

        /// <summary>
        /// Stops the background autosave loop.
        /// </summary>
        void StopAutosaveLoop();

        /// <summary>
        /// Returns a list of paths to available autosaved files.
        /// </summary>
        IEnumerable<string> GetAvailableAutosaves();

        /// <summary>
        /// Attempts to load the specified autosave file as a unified envelope (handles both v2 envelopes and legacy raw SpriteState).
        /// </summary>
        AutosaveEnvelope? LoadAutosaveEnvelope(string path);

        /// <summary>
        /// Attempts to load the specified autosave file as a SpriteState.
        /// </summary>
        SpriteState? LoadAutosave(string path);

        /// <summary>
        /// Gets or sets the autosave interval in seconds. Defaults to 30.
        /// </summary>
        int IntervalSeconds { get; set; }

        /// <summary>
        /// Immediately triggers a synchronous autosave snapshot of the current document if it has unsaved changes.
        /// Useful during emergency crash handlers or before dangerous operations.
        /// </summary>
        void TriggerImmediateAutosave();

        /// <summary>
        /// Immediately performs an asynchronous autosave snapshot of the current document if it has unsaved changes.
        /// </summary>
        System.Threading.Tasks.Task SaveImmediatelyAsync();

        /// <summary>
        /// Clears the autosave for the current document. Should be called after a successful clean save or deliberate app exit.
        /// </summary>
        void ClearCurrentAutosave();

        /// <summary>
        /// Clears all autosave files.
        /// </summary>
        void ClearAllAutosaves();

        /// <summary>
        /// Moves all current autosave files to a timestamped archive directory (e.g. when user declines recovery).
        /// </summary>
        void ArchiveAllAutosaves(string reason = "Archive");
    }
}
