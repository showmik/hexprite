using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hexprite.Core
{
    /// <summary>
    /// Represents the state of a single animation frame, containing its layer pixels and properties.
    /// </summary>
    public class FrameState
    {
        /// <summary>The name of the frame.</summary>
        public string Name { get; set; } = "Frame 1";

        /// <summary>The actual pixel data for each layer in this frame. Index matches SpriteState.Layers.</summary>
        [JsonIgnore]
        public List<IPixelBuffer> LayerPixels { get; set; } = [];

        [JsonPropertyName("LayerPixels")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<bool[]>? LegacySerializedLayerPixels
        {
            get => null;
            set
            {
                if (value != null && LayerPixels.Count == 0)
                {
                    LayerPixels = value.ConvertAll(p => (IPixelBuffer)new MonochromePixelBuffer(p));
                }
            }
        }

        [JsonPropertyName("LayerPixelsV2")]
        [JsonInclude]
        public List<SerializedPixelLayer>? SerializedLayerPixelsV2
        {
            get => LayerPixels.ConvertAll(p => SerializedPixelLayer.From(p));
            set
            {
                if (value != null)
                {
                    LayerPixels = value.ConvertAll(s => s.ToPixelBuffer());
                }
            }
        }

        public int DelayMultiplier { get; set; } = 1;

        // ── Legacy properties for backward compatibility ───────────

        /// <summary>Legacy layers list. Used only for migrating old files.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<LayerState>? Layers { get; set; }

        /// <summary>Legacy active layer index. Moved to SpriteState.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int ActiveLayerIndex { get; set; }

        /// <summary>Creates a deep copy of the frame state.</summary>
        public FrameState Clone()
        {
            return new FrameState
            {
                Name = Name,
                DelayMultiplier = DelayMultiplier,
                LayerPixels = LayerPixels?.ConvertAll(p => p.Clone()) ?? [],
            };
        }
    }
}
