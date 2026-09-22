using System;
using System.Collections.Generic;
using System.IO;
using Hexprite.Core;
using Hexprite.Resources.Fonts;

namespace Hexprite.Services
{
    public interface IGifAnimationExportService
    {
        void ExportGif(
            SpriteState sprite,
            string outputPath,
            int scale = 3,
            uint fgColor = 0xFF110B00,
            uint bgColor = 0xFFFF8200,
            int fps = 5,
            bool includeSpeechBubble = false,
            FlipperSpeechBubble? bubble = null,
            bool includeHud = false,
            int hudLevel = 1,
            int hudMood = 0,
            bool fullCycle = false);
    }

    public class GifAnimationExportService : IGifAnimationExportService
    {
        public void ExportGif(
            SpriteState sprite,
            string outputPath,
            int scale = 3,
            uint fgColor = 0xFF110B00,
            uint bgColor = 0xFFFF8200,
            int fps = 5,
            bool includeSpeechBubble = false,
            FlipperSpeechBubble? bubble = null,
            bool includeHud = false,
            int hudLevel = 1,
            int hudMood = 0,
            bool fullCycle = false)
        {
            ArgumentNullException.ThrowIfNull(sprite);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            scale = Math.Clamp(scale, 1, 8);
            fps = Math.Clamp(fps, 1, 60);

            int width = 128 * scale;
            int height = 64 * scale;

            // Determine sequence of physical frame indices to render
            List<int> frameIndices = GetSequenceToRender(sprite, fullCycle);
            if (frameIndices.Count == 0)
            {
                frameIndices = [0];
            }

            var framesData = new List<byte[]>();

            foreach (int fIdx in frameIndices)
            {
                int safeIdx = Math.Clamp(fIdx, 0, Math.Max(0, sprite.Frames.Count - 1));
                bool[] screenPixels = new bool[128 * 64];

                if (sprite.Frames.Count > safeIdx)
                {
                    bool[] monoPixels = sprite.CompositeFramePixels(safeIdx, isExport: true);

                    int sw = Math.Min(128, sprite.Width);
                    int sh = Math.Min(64, sprite.Height);
                    int offsetX = (128 - sw) / 2;
                    int offsetY = (64 - sh) / 2;

                    for (int y = 0; y < sh; y++)
                    {
                        for (int x = 0; x < sw; x++)
                        {
                            int srcIdx = y * sprite.Width + x;
                            if (srcIdx < monoPixels.Length && monoPixels[srcIdx])
                            {
                                int destX = offsetX + x;
                                int destY = offsetY + y;
                                if (destX >= 0 && destX < 128 && destY >= 0 && destY < 64)
                                {
                                    screenPixels[destY * 128 + destX] = true;
                                }
                            }
                        }
                    }
                }

                // Apply Speech Bubble
                if (includeSpeechBubble)
                {
                    var targetBubble = bubble ?? sprite.FlipperCycle?.SpeechBubble;
                    targetBubble?.Draw(screenPixels, 128, 64);
                }

                // Apply HUD Overlay
                if (includeHud)
                {
                    FlipperHudRenderer.Draw(screenPixels, 128, 64, hudLevel, hudMood);
                }

                // Scale up pixels (Nearest-Neighbor)
                byte[] scaledIndexedPixels = new byte[width * height];
                for (int y = 0; y < 64; y++)
                {
                    for (int x = 0; x < 128; x++)
                    {
                        byte colorIndex = screenPixels[y * 128 + x] ? (byte)1 : (byte)0;
                        for (int dy = 0; dy < scale; dy++)
                        {
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int targetX = x * scale + dx;
                                int targetY = y * scale + dy;
                                scaledIndexedPixels[targetY * width + targetX] = colorIndex;
                            }
                        }
                    }
                }

                framesData.Add(scaledIndexedPixels);
            }

            // Extract RGB colors for palette
            byte bgR = (byte)((bgColor >> 16) & 0xFF);
            byte bgG = (byte)((bgColor >> 8) & 0xFF);
            byte bgB = (byte)(bgColor & 0xFF);

            byte fgR = (byte)((fgColor >> 16) & 0xFF);
            byte fgG = (byte)((fgColor >> 8) & 0xFF);
            byte fgB = (byte)(fgColor & 0xFF);

            int delayCentiseconds = (int)Math.Max(1, Math.Round(100.0 / fps));

            SafeFileIo.WriteStreamAtomic(outputPath, fs =>
            {
                EncodeGif89a(fs, width, height, framesData, bgR, bgG, bgB, fgR, fgG, fgB, delayCentiseconds);
            });
        }

        private static List<int> GetSequenceToRender(SpriteState sprite, bool fullCycle)
        {
            var cycle = sprite.FlipperCycle;
            if (cycle != null && cycle.FramesOrder.Length > 0)
            {
                if (fullCycle)
                {
                    // Render passive prefix + active suffix for ActiveCycles count
                    var sequence = new List<int>();
                    int passiveCount = Math.Clamp(cycle.PassiveFrameCount, 0, cycle.FramesOrder.Length);
                    for (int i = 0; i < passiveCount; i++)
                    {
                        sequence.Add(cycle.FramesOrder[i]);
                    }

                    int activeCount = Math.Clamp(cycle.ActiveFrameCount, 0, cycle.FramesOrder.Length - passiveCount);
                    int cycles = Math.Max(1, cycle.ActiveCycles);
                    for (int c = 0; c < cycles; c++)
                    {
                        for (int i = 0; i < activeCount; i++)
                        {
                            sequence.Add(cycle.FramesOrder[passiveCount + i]);
                        }
                    }
                    return sequence;
                }
                else
                {
                    // Render passive prefix only
                    int passiveCount = Math.Clamp(cycle.PassiveFrameCount, 0, cycle.FramesOrder.Length);
                    if (passiveCount > 0)
                    {
                        var sequence = new List<int>();
                        for (int i = 0; i < passiveCount; i++)
                        {
                            sequence.Add(cycle.FramesOrder[i]);
                        }
                        return sequence;
                    }
                }
            }

            var defaultSeq = new List<int>();
            for (int i = 0; i < sprite.Frames.Count; i++)
            {
                defaultSeq.Add(i);
            }
            return defaultSeq;
        }

        private static void EncodeGif89a(
            Stream stream,
            int width,
            int height,
            IReadOnlyList<byte[]> frames,
            byte bgR, byte bgG, byte bgB,
            byte fgR, byte fgG, byte fgB,
            int delayCentiseconds)
        {
            using var encoder = new GifEncoder(stream, (ushort)width, (ushort)height);
            encoder.SetPalette([
                System.Windows.Media.Color.FromRgb(bgR, bgG, bgB),
                System.Windows.Media.Color.FromRgb(fgR, fgG, fgB)
            ]);
            encoder.SetLoop(0);

            ushort delay = (ushort)Math.Clamp(delayCentiseconds, 1, 65535);
            foreach (var frameData in frames)
            {
                encoder.AddFrame(frameData, delay, disposalMethod: 1);
            }
            encoder.Finish();
        }
    }
}