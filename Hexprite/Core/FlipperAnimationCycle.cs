using System;
using System.Collections.Generic;
using System.Linq;

namespace Hexprite.Core
{
    public class FlipperAnimationCycle
    {
        public int[] FramesOrder { get; set; } = [];
        public int PassiveFrameCount { get; set; }
        public int ActiveFrameCount { get; set; }
        public int ActiveCycles { get; set; } = 1;
        public int Duration { get; set; } = 3600;
        public int ActiveCooldown { get; set; }
        public int BubbleSlots { get; set; }
        public FlipperSpeechBubble? SpeechBubble { get; set; }
        public List<FlipperSpeechBubble> SpeechBubbles { get; set; } = [];

        public FlipperAnimationCycle Clone() => new()
        {
            FramesOrder = (int[])FramesOrder.Clone(),
            PassiveFrameCount = PassiveFrameCount,
            ActiveFrameCount = ActiveFrameCount,
            ActiveCycles = ActiveCycles,
            Duration = Duration,
            ActiveCooldown = ActiveCooldown,
            BubbleSlots = BubbleSlots,
            SpeechBubble = SpeechBubble?.Clone(),
            SpeechBubbles = SpeechBubbles.Select(b => b.Clone()).ToList(),
        };
    }
}
