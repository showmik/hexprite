using System;
using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    /// <summary>
    /// Additional document metadata captured alongside autosaved state.
    /// </summary>
    public class AutosaveMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public int TabIndex { get; set; }
        public bool IsActiveTab { get; set; }
        public string? ParentPackName { get; set; }
        public string? ParentPackPath { get; set; }
        public string? PackEntryName { get; set; }
    }

    /// <summary>
    /// Unified container format for autosaved documents, storing document mode,
    /// metadata, parent pack context, and the document payload.
    /// </summary>
    public class AutosaveEnvelope
    {
        public int SchemaVersion { get; set; } = 2;
        public string DocumentId { get; set; } = string.Empty;
        public DocumentMode Mode { get; set; } = DocumentMode.Sprite;
        public string Title { get; set; } = string.Empty;
        public string? FilePath { get; set; }
        public bool IsDirty { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public int TabIndex { get; set; }
        public bool IsActiveTab { get; set; }

        public string? ParentPackName { get; set; }
        public string? ParentPackPath { get; set; }
        public string? PackEntryName { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SpriteState? SpriteState { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AssetPackDocument? AssetPackDocument { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public FontDocument? FontDocument { get; set; }
    }
}
