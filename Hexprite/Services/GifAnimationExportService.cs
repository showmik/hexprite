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
            // 1. Header "GIF89a"
            stream.Write([(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a']);

            // 2. Logical Screen Descriptor
            WriteUInt16(stream, (ushort)width);
            WriteUInt16(stream, (ushort)height);
            stream.WriteByte(0x87); // Global color table flag = 1, color res = 8, 256 colors
            stream.WriteByte(0x00); // Background color index = 0
            stream.WriteByte(0x00); // Aspect ratio

            // 3. Global Color Table (256 entries = 768 bytes)
            byte[] colorTable = new byte[768];
            // Index 0: Background
            colorTable[0] = bgR;
            colorTable[1] = bgG;
            colorTable[2] = bgB;
            // Index 1: Foreground
            colorTable[3] = fgR;
            colorTable[4] = fgG;
            colorTable[5] = fgB;

            stream.Write(colorTable, 0, colorTable.Length);

            // 4. Netscape 2.0 Application Extension (Infinite Loop)
            stream.Write([
                0x21, 0xFF, 0x0B,
                (byte)'N', (byte)'E', (byte)'T', (byte)'S', (byte)'C', (byte)'A', (byte)'P', (byte)'E', (byte)'2', (byte)'.', (byte)'0',
                0x03, 0x01, 0x00, 0x00, 0x00
            ]);

            // 5. Write Frames
            foreach (var frameData in frames)
            {
                // Graphics Control Extension
                stream.Write([0x21, 0xF9, 0x04, 0x04]); // Disposal = 1
                WriteUInt16(stream, (ushort)delayCentiseconds);
                stream.WriteByte(0x00); // Transparent color
                stream.WriteByte(0x00); // Block terminator

                // Image Descriptor
                stream.WriteByte(0x2C); // Image separator
                WriteUInt16(stream, 0); // Left
                WriteUInt16(stream, 0); // Top
                WriteUInt16(stream, (ushort)width);
                WriteUInt16(stream, (ushort)height);
                stream.WriteByte(0x00); // No local color table

                // Image Data (LZW)
                WriteLzw(stream, frameData, 8);
            }

            // 6. GIF Trailer
            stream.WriteByte(0x3B);
        }

        private static void WriteLzw(Stream stream, byte[] indexedPixels, int minCodeSize)
        {
            stream.WriteByte((byte)minCodeSize);
            int clearCode = 1 << minCodeSize;
            int eoiCode = clearCode + 1;
            int nextCode = clearCode + 2;
            int currentCodeSize = minCodeSize + 1;
            int maxCode = 1 << currentCodeSize;

            var dict = new Dictionary<int, int>(4096);
            int bitAccumulator = 0;
            int bitCount = 0;
            byte[] subBlock = new byte[255];
            int subBlockPos = 0;

            void EmitBits(int code, int bits)
            {
                bitAccumulator |= (code << bitCount);
                bitCount += bits;
                while (bitCount >= 8)
                {
                    subBlock[subBlockPos++] = (byte)(bitAccumulator & 0xFF);
                    bitAccumulator >>= 8;
                    bitCount -= 8;
                    if (subBlockPos == 255)
                    {
                        stream.WriteByte(255);
                        stream.Write(subBlock, 0, 255);
                        subBlockPos = 0;
                    }
                }
            }

            void FlushBits()
            {
                if (bitCount > 0)
                {
                    subBlock[subBlockPos++] = (byte)(bitAccumulator & 0xFF);
                    bitAccumulator = 0;
                    bitCount = 0;
                }
                if (subBlockPos > 0)
                {
                    stream.WriteByte((byte)subBlockPos);
                    stream.Write(subBlock, 0, subBlockPos);
                    subBlockPos = 0;
                }
                stream.WriteByte(0x00);
            }

            EmitBits(clearCode, currentCodeSize);

            if (indexedPixels.Length == 0)
            {
                EmitBits(eoiCode, currentCodeSize);
                FlushBits();
                return;
            }

            int currentPrefix = indexedPixels[0];

            for (int i = 1; i < indexedPixels.Length; i++)
            {
                byte nextChar = indexedPixels[i];
                int key = (currentPrefix << 8) | nextChar;

                if (dict.TryGetValue(key, out int code))
                {
                    currentPrefix = code;
                }
                else
                {
                    EmitBits(currentPrefix, currentCodeSize);

                    if (nextCode < 4096)
                    {
                        dict[key] = nextCode++;
                        if (nextCode > maxCode && currentCodeSize < 12)
                        {
                            currentCodeSize++;
                            maxCode = 1 << currentCodeSize;
                        }
                    }
                    else
                    {
                        EmitBits(clearCode, currentCodeSize);
                        dict.Clear();
                        currentCodeSize = minCodeSize + 1;
                        maxCode = 1 << currentCodeSize;
                        nextCode = clearCode + 2;
                    }

                    currentPrefix = nextChar;
                }
            }

            EmitBits(currentPrefix, currentCodeSize);
            EmitBits(eoiCode, currentCodeSize);
            FlushBits();
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
        }
    }
}