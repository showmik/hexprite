using FsCheck;
using FsCheck.Xunit;
using Hexprite.Services;
using Xunit;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Collections.Generic;

namespace Hexprite.Tests
{
    [Trait("Category", "Fuzz")]
    public class GifEncoderFuzzTests
    {
        [Property(MaxTest = 100)]
        public void GifEncoder_RoundTrip_ShouldCreateValidAnimatedGif(
            ushort w, ushort h, byte frameCount, ushort delay, byte paletteSize, ushort loopCount)
        {
            // Constrain sizes to avoid OutOfMemory or hanging the test
            w = (ushort)(Math.Abs(w % 64) + 1);
            h = (ushort)(Math.Abs(h % 64) + 1);
            frameCount = (byte)(Math.Abs(frameCount % 10) + 1); // 1 to 10 frames
            paletteSize = (byte)(Math.Abs(paletteSize % 256) + 1); // 1 to 256 colors
            
            string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".gif");

            try
            {
                var random = new Random(w * h * frameCount);
                
                // Generate random palette
                var palette = new Color[paletteSize];
                for (int i = 0; i < paletteSize; i++)
                {
                    palette[i] = Color.FromArgb(
                        (byte)(i == 0 ? 0 : 255), // Index 0 usually transparent
                        (byte)random.Next(256), 
                        (byte)random.Next(256), 
                        (byte)random.Next(256));
                }

                using (var fs = File.Create(tempFile))
                {
                    var encoder = new GifEncoder(fs, w, h);
                    encoder.SetPalette(palette);
                    encoder.SetLoop(loopCount);

                    for (int f = 0; f < frameCount; f++)
                    {
                        byte[] pixels = new byte[w * h];
                        // Fill with random indices constrained to the palette size
                        for(int p = 0; p < pixels.Length; p++)
                        {
                            pixels[p] = (byte)random.Next(paletteSize);
                        }

                        encoder.AddFrame(pixels, delay);
                    }
                    encoder.Finish();
                }

                // Now verify the GIF was encoded properly by having WPF decode it
                using (var readStream = File.OpenRead(tempFile))
                {
                    var decoder = BitmapDecoder.Create(readStream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                    
                    if (decoder.Frames.Count != frameCount)
                    {
                        throw new Exception($"Encoded {frameCount} frames, but decoder found {decoder.Frames.Count}!");
                    }

                    if (decoder.Frames[0].PixelWidth != w || decoder.Frames[0].PixelHeight != h)
                    {
                        throw new Exception($"Dimensions corrupted! Expected {w}x{h}, got {decoder.Frames[0].PixelWidth}x{decoder.Frames[0].PixelHeight}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"GifEncoder crashed on {w}x{h} with {frameCount} frames!", ex);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}
