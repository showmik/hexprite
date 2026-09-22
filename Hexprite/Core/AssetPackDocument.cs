using System;
using System.Collections.Generic;
using System.Linq;

namespace Hexprite.Core
{
    /// <summary>
    /// Persisted simulator test state for Flipper Zero asset packs.
    /// </summary>
    public class FlipperSimulatorSettings
    {
        public int Level { get; set; } = 1;
        public int Mood { get; set; }
        public int SelectedPaletteIndex { get; set; }
        public bool ShowLcdGrid { get; set; } = true;
        public bool ShowDesktopHud { get; set; } = true;
        public bool ShowSpeechBubble { get; set; } = true;
        public int BubbleX { get; set; } = 2;
        public int BubbleY { get; set; } = 2;
        public string CustomBubbleText { get; set; } = "Feed me!";
        public SpeechBubbleTailPosition BubbleTail { get; set; } = SpeechBubbleTailPosition.BottomLeft;
        public double SpeedMultiplier { get; set; } = 1.0;
        public int CandidateModeIndex { get; set; }
    }

    /// <summary>
    /// Serializable document model for Flipper Zero asset pack manifests.
    /// Persisted as .hexpack JSON files.
    /// </summary>
    public class AssetPackDocument
    {
        public int SchemaVersion { get; set; } = 2;
        public string PackName { get; set; } = "Flipper Asset Pack";
        public bool IsStockMode { get; set; }
        public List<FlipperManifestEntry> Entries { get; set; } = [];
        public FlipperSimulatorSettings? SimulatorSettings { get; set; }

        private Dictionary<string, SpriteState> _animations = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, SpriteState> Animations
        {
            get => _animations;
            set
            {
                if (value == null)
                {
                    _animations = new(StringComparer.OrdinalIgnoreCase);
                }
                else if (value.Comparer != StringComparer.OrdinalIgnoreCase)
                {
                    _animations = new(value, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _animations = value;
                }
            }
        }

        private Dictionary<string, string> _animationFilePaths = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> AnimationFilePaths
        {
            get => _animationFilePaths;
            set
            {
                if (value == null)
                {
                    _animationFilePaths = new(StringComparer.OrdinalIgnoreCase);
                }
                else if (value.Comparer != StringComparer.OrdinalIgnoreCase)
                {
                    _animationFilePaths = new(value, StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    _animationFilePaths = value;
                }
            }
        }

        public void EnsureCaseInsensitiveAnimations()
        {
            if (_animations != null && _animations.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                _animations = new Dictionary<string, SpriteState>(_animations, StringComparer.OrdinalIgnoreCase);
            }
            if (_animationFilePaths != null && _animationFilePaths.Comparer != StringComparer.OrdinalIgnoreCase)
            {
                _animationFilePaths = new Dictionary<string, string>(_animationFilePaths, StringComparer.OrdinalIgnoreCase);
            }
        }

        public static AssetPackDocument CreateNew(string name = "Flipper Asset Pack")
        {
            var doc = new AssetPackDocument
            {
                PackName = name,
                IsStockMode = false,
                Entries =
                [
                    new() { Name = "anim_baby",  MinLevel = 1,  MaxLevel = 10, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                    new() { Name = "anim_teen",  MinLevel = 11, MaxLevel = 20, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                    new() { Name = "anim_adult", MinLevel = 21, MaxLevel = 30, MinButthurt = 0, MaxButthurt = 14, Weight = 1 },
                ]
            };

            foreach (var entry in doc.Entries)
            {
                doc.Animations[entry.Name] = new SpriteState(128, 64)
                {
                    ColorMode = ColorMode.Monochrome,
                    FrameRateFps = 10
                };
            }

            return doc;
        }

        public static AssetPackDocument FromManifest(
            FlipperManifest manifest,
            string? name = null,
            IReadOnlyDictionary<string, SpriteState>? animations = null)
        {
            ArgumentNullException.ThrowIfNull(manifest);

            var doc = new AssetPackDocument
            {
                PackName = name ?? "Imported Pack",
                IsStockMode = manifest.Entries.Count > 0 && manifest.Entries.All(e => e.MaxLevel <= 3),
                Entries = manifest.Entries.Select(e => e.Clone()).ToList(),
            };

            if (animations != null)
            {
                foreach (var (animName, sprite) in animations)
                {
                    if (sprite != null && !string.IsNullOrWhiteSpace(animName))
                    {
                        doc.Animations[animName] = sprite;
                    }
                }
            }

            return doc;
        }
    }
}
