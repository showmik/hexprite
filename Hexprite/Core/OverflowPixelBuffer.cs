using System;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Linq;

namespace Hexprite.Core
{
    public class SerializedPixelLayer
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool[]? Pixels { get; set; }

        [JsonPropertyName("Data")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BitPackedData { get; set; }

        [JsonPropertyName("PixelCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int PixelCount { get; set; }

        public bool PreserveOverflow { get; set; }
        public int MarginX { get; set; }
        public int MarginY { get; set; }
        public int ExtendedWidth { get; set; }
        public int ExtendedHeight { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }

        public static string EncodeBitPacked(bool[]? pixels)
        {
            if (pixels == null || pixels.Length == 0) return string.Empty;
            int byteCount = (pixels.Length + 7) / 8;
            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i])
                {
                    bytes[i >> 3] |= (byte)(1 << (i & 7));
                }
            }
            return Convert.ToBase64String(bytes);
        }

        public static bool[] DecodeBitPacked(string? base64, int expectedLength)
        {
            if (string.IsNullOrEmpty(base64) || expectedLength <= 0) return new bool[Math.Max(0, expectedLength)];
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                bool[] pixels = new bool[expectedLength];
                int maxBit = Math.Min(expectedLength, bytes.Length * 8);
                for (int i = 0; i < maxBit; i++)
                {
                    pixels[i] = (bytes[i >> 3] & (1 << (i & 7))) != 0;
                }
                return pixels;
            }
            catch
            {
                return new bool[expectedLength];
            }
        }
        
        public IPixelBuffer ToPixelBuffer()
        {
            bool[] pixels;
            if (Pixels != null && Pixels.Length > 0)
            {
                pixels = Pixels;
            }
            else if (!string.IsNullOrEmpty(BitPackedData))
            {
                int count = PixelCount;
                if (count <= 0)
                {
                    if (PreserveOverflow && ExtendedWidth > 0 && ExtendedHeight > 0)
                    {
                        count = ExtendedWidth * ExtendedHeight;
                    }
                    else if (CanvasWidth > 0 && CanvasHeight > 0)
                    {
                        count = CanvasWidth * CanvasHeight;
                    }
                    else
                    {
                        count = (Convert.FromBase64String(BitPackedData).Length) * 8;
                    }
                }
                pixels = DecodeBitPacked(BitPackedData, count);
            }
            else
            {
                int count = PixelCount > 0 ? PixelCount : (CanvasWidth > 0 && CanvasHeight > 0 ? CanvasWidth * CanvasHeight : 128 * 64);
                pixels = new bool[count];
            }

            if (PreserveOverflow)
            {
                // Fallback for old save files that didn't have CanvasWidth/Height
                int canvasWidth = CanvasWidth > 0 ? CanvasWidth : ExtendedWidth - 2 * MarginX;
                int canvasHeight = CanvasHeight > 0 ? CanvasHeight : ExtendedHeight - 2 * MarginY;
                return new OverflowPixelBuffer(pixels, canvasWidth, canvasHeight, ExtendedWidth, ExtendedHeight, MarginX, MarginY);
            }
            return new MonochromePixelBuffer(pixels);
        }
        
        public static SerializedPixelLayer From(IPixelBuffer buffer)
        {
            if (buffer is OverflowPixelBuffer ovf)
            {
                var extData = ovf.GetExtendedData();
                return new SerializedPixelLayer
                {
                    BitPackedData = EncodeBitPacked(extData),
                    PixelCount = extData.Length,
                    PreserveOverflow = true,
                    ExtendedWidth = ovf.ExtendedWidth,
                    ExtendedHeight = ovf.ExtendedHeight,
                    MarginX = ovf.MarginX,
                    MarginY = ovf.MarginY,
                    CanvasWidth = ovf.CanvasWidth,
                    CanvasHeight = ovf.CanvasHeight,
                };
            }
            
            var monoData = buffer.GetMonochromeData();
            return new SerializedPixelLayer
            {
                BitPackedData = EncodeBitPacked(monoData),
                PixelCount = monoData.Length,
                PreserveOverflow = false,
            };
        }
    }

    public class OverflowPixelBuffer : IPixelBuffer
    {
        private bool[] _extendedPixels;
        public int CanvasWidth { get; private set; }
        public int CanvasHeight { get; private set; }
        private bool[]? _viewCache;
        private bool _isViewCacheDirty = true;
        
        public int ExtendedWidth { get; private set; }
        public int ExtendedHeight { get; private set; }
        
        public int MarginX { get; private set; }
        public int MarginY { get; private set; }

        public bool PreserveOverflow => true;

        public OverflowPixelBuffer(int canvasWidth, int canvasHeight, int initialMargin = 128)
        {
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            MarginX = initialMargin;
            MarginY = initialMargin;
            ExtendedWidth = CanvasWidth + 2 * MarginX;
            ExtendedHeight = CanvasHeight + 2 * MarginY;
            _extendedPixels = new bool[ExtendedWidth * ExtendedHeight];
        }

        public OverflowPixelBuffer(bool[] extendedPixels, int canvasWidth, int canvasHeight, int extendedWidth, int extendedHeight, int marginX, int marginY)
        {
            _extendedPixels = extendedPixels;
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            ExtendedWidth = extendedWidth;
            ExtendedHeight = extendedHeight;
            MarginX = marginX;
            MarginY = marginY;
        }

        public bool[] GetMonochromeData()
        {
            int requiredLen = CanvasWidth * CanvasHeight;
            if (_viewCache == null || _viewCache.Length != requiredLen)
            {
                _viewCache = new bool[requiredLen];
                _isViewCacheDirty = true;
            }

            if (_isViewCacheDirty)
            {
                for (int y = 0; y < CanvasHeight; y++)
                {
                    int srcOffset = (y + MarginY) * ExtendedWidth + MarginX;
                    Array.Copy(_extendedPixels, srcOffset, _viewCache, y * CanvasWidth, CanvasWidth);
                }
                _isViewCacheDirty = false;
            }
            return _viewCache;
        }

        public void WriteMonochromeData(bool[] data)
        {
            int requiredLen = CanvasWidth * CanvasHeight;
            if (data.Length != requiredLen) return;
            if (_viewCache == null || _viewCache.Length != requiredLen)
            {
                _viewCache = new bool[requiredLen];
            }
            if (!ReferenceEquals(_viewCache, data))
            {
                Array.Copy(data, _viewCache, requiredLen);
            }
            _isViewCacheDirty = false;
            for (int y = 0; y < CanvasHeight; y++)
            {
                int dstOffset = (y + MarginY) * ExtendedWidth + MarginX;
                Array.Copy(data, y * CanvasWidth, _extendedPixels, dstOffset, CanvasWidth);
            }
        }
        
        public bool[] GetExtendedData() => (bool[])_extendedPixels.Clone();

        public void EnsureCapacity(int minCanvasX, int minCanvasY, int maxCanvasX, int maxCanvasY)
        {
            int requiredExtMinX = minCanvasX + MarginX;
            int requiredExtMinY = minCanvasY + MarginY;
            int requiredExtMaxX = maxCanvasX + MarginX;
            int requiredExtMaxY = maxCanvasY + MarginY;

            if (requiredExtMinX >= 0 && requiredExtMinY >= 0 && requiredExtMaxX < ExtendedWidth && requiredExtMaxY < ExtendedHeight)
            {
                return; // already fits
            }

            int newMarginX = MarginX;
            int newMarginY = MarginY;
            int newExtendedWidth = ExtendedWidth;
            int newExtendedHeight = ExtendedHeight;

            if (requiredExtMinX < 0)
            {
                int expand = -requiredExtMinX + 128; // Add buffer
                newMarginX += expand;
                newExtendedWidth += expand;
            }
            if (requiredExtMinY < 0)
            {
                int expand = -requiredExtMinY + 128;
                newMarginY += expand;
                newExtendedHeight += expand;
            }
            
            int currentExtMaxX = maxCanvasX + newMarginX;
            if (currentExtMaxX >= newExtendedWidth)
            {
                newExtendedWidth = currentExtMaxX + 128;
            }
            
            int currentExtMaxY = maxCanvasY + newMarginY;
            if (currentExtMaxY >= newExtendedHeight)
            {
                newExtendedHeight = currentExtMaxY + 128;
            }

            var newPixels = new bool[newExtendedWidth * newExtendedHeight];
            int dx = newMarginX - MarginX;
            int dy = newMarginY - MarginY;

            for (int y = 0; y < ExtendedHeight; y++)
            {
                int newY = y + dy;
                if (newY >= 0 && newY < newExtendedHeight)
                {
                    Array.Copy(_extendedPixels, y * ExtendedWidth, newPixels, newY * newExtendedWidth + dx, ExtendedWidth);
                }
            }

            _extendedPixels = newPixels;
            ExtendedWidth = newExtendedWidth;
            ExtendedHeight = newExtendedHeight;
            MarginX = newMarginX;
            MarginY = newMarginY;
            _isViewCacheDirty = true;
        }

        public void SetPixel(int canvasX, int canvasY, bool value)
        {
            SetPixelNoInvalidate(canvasX, canvasY, value);
            _isViewCacheDirty = true;
        }

        public void Clear()
        {
            Array.Clear(_extendedPixels, 0, _extendedPixels.Length);
            _isViewCacheDirty = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPixelFast(int canvasX, int canvasY, bool value)
        {
            int extX = canvasX + MarginX;
            int extY = canvasY + MarginY;
            _extendedPixels[extY * ExtendedWidth + extX] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetPixelFast(int canvasX, int canvasY)
        {
            int extX = canvasX + MarginX;
            int extY = canvasY + MarginY;
            return _extendedPixels[extY * ExtendedWidth + extX];
        }

        public void SetPixelNoInvalidate(int canvasX, int canvasY, bool value)
        {
            EnsureCapacity(canvasX, canvasY, canvasX, canvasY);
            int extX = canvasX + MarginX;
            int extY = canvasY + MarginY;
            _extendedPixels[extY * ExtendedWidth + extX] = value;
        }

        public void InvalidateViewCache() => _isViewCacheDirty = true;

        public void ShiftContent(int dx, int dy)
        {
            MarginX -= dx;
            MarginY -= dy;
            _isViewCacheDirty = true;
            EnsureCapacity(0, 0, CanvasWidth - 1, CanvasHeight - 1);
        }
        
        public OverflowPixelBuffer ResizeCanvas(int newW, int newH, int dx, int dy)
        {
            int newMarginX = MarginX - dx;
            int newMarginY = MarginY - dy;
            var newBuffer = new OverflowPixelBuffer((bool[])_extendedPixels.Clone(), newW, newH, ExtendedWidth, ExtendedHeight, newMarginX, newMarginY);
            newBuffer.EnsureCapacity(0, 0, newW - 1, newH - 1);
            return newBuffer;
        }

        public IPixelBuffer Clone()
        {
            return new OverflowPixelBuffer((bool[])_extendedPixels.Clone(), CanvasWidth, CanvasHeight, ExtendedWidth, ExtendedHeight, MarginX, MarginY);
        }

        public void Invert()
        {
            for (int i = 0; i < _extendedPixels.Length; i++)
                _extendedPixels[i] = !_extendedPixels[i];
            _isViewCacheDirty = true;
        }
    }
}
