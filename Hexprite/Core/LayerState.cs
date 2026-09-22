using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    public enum LayerBlendMode { Normal, Xor, Mask, Subtract }
    public enum LayerOpacityMode { Solid, Checkerboard, Sparse, Dense }

    /// <summary>
    /// Represents the global state of a single layer, including visibility and metadata.
    /// </summary>
    public class LayerState
    {
        /// <summary>Name of the layer.</summary>
        public string Name { get; set; } = "Layer";
        /// <summary>Whether the layer is currently visible.</summary>
        public bool IsVisible { get; set; } = true;
        /// <summary>Whether the layer is locked for editing.</summary>
        public bool IsLocked { get; set; }

        public bool IsGlobal { get; set; }

        /// <summary>
        /// The independent per-frame buffers captured when this layer became global.
        /// This is intentionally ignored by the normal runtime model and persisted
        /// through the serialized proxy below so localization can be reversible.
        /// </summary>
        [JsonIgnore]
        public List<IPixelBuffer>? PreGlobalFramePixels { get; set; }

        [JsonPropertyName("PreGlobalFramePixelsV1")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<SerializedPixelLayer>? SerializedPreGlobalFramePixels
        {
            get => PreGlobalFramePixels?.ConvertAll(SerializedPixelLayer.From);
            set => PreGlobalFramePixels = value?.ConvertAll(pixel => pixel.ToPixelBuffer());
        }
        public bool ExcludeFromExport { get; set; }
        public bool PreserveOverflow { get; set; }
        public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;
        public LayerOpacityMode OpacityMode { get; set; } = LayerOpacityMode.Solid;

        /// <summary>Legacy pixel data. Used only for migrating old files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool[]? Pixels { get; set; }

        /// <summary>Creates a deep copy of the layer state.</summary>
        public LayerState Clone()
        {
            return new LayerState
            {
                Name = Name,
                IsVisible = IsVisible,
                IsLocked = IsLocked,
                IsGlobal = IsGlobal,
                PreGlobalFramePixels = PreGlobalFramePixels?.ConvertAll(p => p.Clone()),
                ExcludeFromExport = ExcludeFromExport,
                PreserveOverflow = PreserveOverflow,
                BlendMode = BlendMode,
                OpacityMode = OpacityMode,
                Pixels = Pixels != null ? (bool[])Pixels.Clone() : null,
            };
        }
    }
}
