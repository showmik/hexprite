using System.ComponentModel;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// Common interface for document tabs managed by the ShellViewModel.
    /// Allows the shell to host both Sprite documents and Font documents.
    /// </summary>
    public interface IDocumentTab : INotifyPropertyChanged
    {
        /// <summary>Display title for the tab.</summary>
        string Title { get; }

        /// <summary>Absolute path to the saved file, or null if unsaved.</summary>
        string? FilePath { get; }

        /// <summary>True if the document has unsaved modifications.</summary>
        bool HasUnsavedChanges { get; }

        /// <summary>Whether this tab is currently the active one.</summary>
        bool IsActive { get; set; }

        /// <summary>True if the document is linked to a source code snippet.</summary>
        bool IsLinked { get; }

        /// <summary>The mode of this document (Sprite or Font).</summary>
        Core.DocumentMode Mode { get; }

        /// <summary>Saves the document to its current FilePath.</summary>
        void Save();

        /// <summary>Saves the document to a new FilePath.</summary>
        void SaveAs(string path);
    }
}
