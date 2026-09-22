using System.Collections.Generic;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperManifestTests
    {
        [Fact]
        public void ParseManifest_ValidContent_ReturnsPopulatedManifest()
        {
            string content = @"
Filetype: Flipper Animation Manifest
Version: 1

Name: DolphinIdle
Min butthurt: 0
Max butthurt: 4
Min level: 1
Max level: 30
Weight: 3

Name: DolphinHappy
Min butthurt: 0
Max butthurt: 2
Min level: 5
Max level: 15
Weight: 10
";
            var manifest = FlipperManifest.Parse(content);

            Assert.Equal("Flipper Animation Manifest", manifest.Filetype);
            Assert.Equal(1, manifest.Version);
            Assert.Equal(2, manifest.Entries.Count);

            Assert.Equal("DolphinIdle", manifest.Entries[0].Name);
            Assert.Equal(0, manifest.Entries[0].MinButthurt);
            Assert.Equal(4, manifest.Entries[0].MaxButthurt);
            Assert.Equal(1, manifest.Entries[0].MinLevel);
            Assert.Equal(30, manifest.Entries[0].MaxLevel);
            Assert.Equal(3, manifest.Entries[0].Weight);

            Assert.Equal("DolphinHappy", manifest.Entries[1].Name);
            Assert.Equal(0, manifest.Entries[1].MinButthurt);
            Assert.Equal(2, manifest.Entries[1].MaxButthurt);
            Assert.Equal(5, manifest.Entries[1].MinLevel);
            Assert.Equal(15, manifest.Entries[1].MaxLevel);
            Assert.Equal(10, manifest.Entries[1].Weight);
        }

        [Fact]
        public void SerializeManifest_GeneratesCompliantText()
        {
            var manifest = new FlipperManifest
            {
                Entries = new List<FlipperManifestEntry>
                {
                    new()
                    {
                        Name = "WalkLeft",
                        MinButthurt = 0,
                        MaxButthurt = 14,
                        MinLevel = 1,
                        MaxLevel = 30,
                        Weight = 2
                    }
                }
            };

            string text = manifest.Serialize();

            Assert.Contains("Filetype: Flipper Animation Manifest", text);
            Assert.Contains("Version: 1", text);
            Assert.Contains("Name: WalkLeft", text);
            Assert.Contains("Min butthurt: 0", text);
            Assert.Contains("Max butthurt: 14", text);
            Assert.Contains("Min level: 1", text);
            Assert.Contains("Max level: 30", text);
            Assert.Contains("Weight: 2", text);
        }

        [Fact]
        public void ValidateEntry_DetectsOutOfRangeErrors()
        {
            var entry = new FlipperManifestEntry
            {
                Name = "",
                MinLevel = 0,
                MaxLevel = 35,
                MinButthurt = -1,
                MaxButthurt = 20,
                Weight = 0
            };

            var diagnostics = entry.Validate(isMomentum: true);

            Assert.Contains(diagnostics, d => d.Message.Contains("Name cannot be empty"));
            Assert.Contains(diagnostics, d => d.Message.Contains("Min level"));
            Assert.Contains(diagnostics, d => d.Message.Contains("Max level"));
            Assert.Contains(diagnostics, d => d.Message.Contains("Min butthurt"));
            Assert.Contains(diagnostics, d => d.Message.Contains("Max butthurt"));
            Assert.Contains(diagnostics, d => d.Message.Contains("Weight"));
        }

        [Fact]
        public void ValidateManifest_DetectsDuplicateNames()
        {
            var manifest = new FlipperManifest
            {
                Entries = new List<FlipperManifestEntry>
                {
                    new() { Name = "Jump", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                    new() { Name = "jump", MinLevel = 1, MaxLevel = 3, MinButthurt = 0, MaxButthurt = 5, Weight = 1 },
                }
            };

            var diagnostics = manifest.Validate(isMomentum: false);

            Assert.Contains(diagnostics, d => d.Message.Contains("Duplicate animation name 'jump'"));
        }

        [Fact]
        public void ParseMetaFile_ValidText_ReturnsPopulatedMeta()
        {
            string metaContent = @"
Filetype: Flipper Animation
Version: 1
Width: 128
Height: 64
Passive frames: 8
Active frames: 4
Frames order: 0 1 2 3 4 5 6 7 8 9 10 11
Frame rate: 10
Duration: 12
Bubble slots: 2
";
            var meta = FlipperAnimationMeta.Parse(metaContent);

            Assert.Equal("Flipper Animation", meta.Filetype);
            Assert.Equal(1, meta.Version);
            Assert.Equal(128, meta.Width);
            Assert.Equal(64, meta.Height);
            Assert.Equal(8, meta.PassiveFrames);
            Assert.Equal(4, meta.ActiveFrames);
            Assert.Equal(10, meta.FrameRate);
            Assert.Equal(12, meta.Duration);
            Assert.Equal(12, meta.FramesOrder.Length);
            Assert.Equal(2, meta.BubbleSlots);
        }

        [Fact]
        public void SerializeMetaFile_ProducesAccurateOutput()
        {
            var meta = new FlipperAnimationMeta
            {
                Width = 128,
                Height = 64,
                PassiveFrames = 2,
                ActiveFrames = 1,
                FramesOrder = [0, 1, 2],
                FrameRate = 6,
                Duration = 3,
                BubbleSlots = 1
            };

            string serialized = meta.Serialize();

            Assert.Contains("Width: 128", serialized);
            Assert.Contains("Height: 64", serialized);
            Assert.Contains("Passive frames: 2", serialized);
            Assert.Contains("Active frames: 1", serialized);
            Assert.Contains("Frames order: 0 1 2", serialized);
            Assert.Contains("Frame rate: 6", serialized);
            Assert.Contains("Duration: 3", serialized);
            Assert.Contains("Active cooldown: 0", serialized);
            Assert.Contains("Bubble slots: 1", serialized);
        }

        [Fact]
        public void ParseMetaFile_WithBubbleSlots_ParsesAllBubbleProperties()
        {
            string metaContent = @"
Filetype: Flipper Animation
Version: 1
Width: 128
Height: 64
Passive frames: 4
Active frames: 2
Frames order: 0 1 2 3 4 5
Active cycles: 1
Frame rate: 5
Duration: 3600
Active cooldown: 2
Bubble slots: 2

Slot: 0
StartFrame: 1
EndFrame: 3
Text: Feed me!
X: 14
Y: 4
AlignH: Center
AlignV: Bottom

Slot: 1
StartFrame: 4
EndFrame: 5
Text: Goodbye
X: 40
Y: 10
AlignH: Left
AlignV: Top
";
            var meta = FlipperAnimationMeta.Parse(metaContent);

            Assert.Equal(2, meta.BubbleSlots);
            Assert.Equal(2, meta.SpeechBubbles.Count);

            var b0 = meta.SpeechBubbles[0];
            Assert.Equal(0, b0.SlotIndex);
            Assert.Equal(1, b0.StartFrame);
            Assert.Equal(3, b0.EndFrame);
            Assert.Equal("Feed me!", b0.Text);
            Assert.Equal(14, b0.X);
            Assert.Equal(4, b0.Y);
            Assert.Equal("Center", b0.AlignH);
            Assert.Equal("Bottom", b0.AlignV);

            var b1 = meta.SpeechBubbles[1];
            Assert.Equal(1, b1.SlotIndex);
            Assert.Equal(4, b1.StartFrame);
            Assert.Equal(5, b1.EndFrame);
            Assert.Equal("Goodbye", b1.Text);
            Assert.Equal(40, b1.X);
            Assert.Equal(10, b1.Y);
            Assert.Equal("Left", b1.AlignH);
            Assert.Equal("Top", b1.AlignV);
        }

        [Fact]
        public void RoundTrip_MetaFile_PreservesAllFieldsAndBubbles()
        {
            var original = new FlipperAnimationMeta
            {
                Width = 128,
                Height = 64,
                PassiveFrames = 2,
                ActiveFrames = 2,
                FramesOrder = [0, 1, 2, 3],
                ActiveCycles = 2,
                FrameRate = 6,
                Duration = 1800,
                ActiveCooldown = 1,
                BubbleSlots = 1,
                SpeechBubbles =
                [
                    new FlipperSpeechBubble
                    {
                        SlotIndex = 0,
                        StartFrame = 2,
                        EndFrame = 3,
                        Text = "Testing round-trip!",
                        X = 20,
                        Y = 8,
                        AlignH = "Right",
                        AlignV = "Top"
                    }
                ]
            };

            string serialized = original.Serialize();
            var parsed = FlipperAnimationMeta.Parse(serialized);

            Assert.Equal(original.Width, parsed.Width);
            Assert.Equal(original.Height, parsed.Height);
            Assert.Equal(original.PassiveFrames, parsed.PassiveFrames);
            Assert.Equal(original.ActiveFrames, parsed.ActiveFrames);
            Assert.Equal(original.FramesOrder, parsed.FramesOrder);
            Assert.Equal(original.ActiveCycles, parsed.ActiveCycles);
            Assert.Equal(original.FrameRate, parsed.FrameRate);
            Assert.Equal(original.Duration, parsed.Duration);
            Assert.Equal(original.ActiveCooldown, parsed.ActiveCooldown);
            Assert.Equal(original.BubbleSlots, parsed.BubbleSlots);
            Assert.Single(parsed.SpeechBubbles);
            Assert.Equal("Testing round-trip!", parsed.SpeechBubbles[0].Text);
            Assert.Equal(20, parsed.SpeechBubbles[0].X);
            Assert.Equal(8, parsed.SpeechBubbles[0].Y);
            Assert.Equal("Right", parsed.SpeechBubbles[0].AlignH);
            Assert.Equal("Top", parsed.SpeechBubbles[0].AlignV);
        }
    }
}
