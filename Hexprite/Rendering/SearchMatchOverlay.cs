using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hexprite.Rendering
{
    /// <summary>
    /// Renders search match background highlights behind a TextBox control.
    /// </summary>
    public class SearchMatchOverlay : FrameworkElement
    {
        private readonly List<Rect> _inactiveRects = [];
        private Rect _activeRect = Rect.Empty;

        private static readonly SolidColorBrush s_searchMatchBrush = new(Color.FromArgb(120, 255, 215, 0)); // Semi-transparent Gold
        private static readonly SolidColorBrush s_activeSearchMatchBrush = new(Color.FromArgb(220, 255, 140, 0)); // Vibrant Amber
        private static readonly Pen s_activeSearchMatchPen = new(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), 1.2);

        static SearchMatchOverlay()
        {
            s_searchMatchBrush.Freeze();
            s_activeSearchMatchBrush.Freeze();
            s_activeSearchMatchPen.Freeze();
        }

        /// <summary>
        /// Clears all match rectangles.
        /// </summary>
        public void Clear()
        {
            _inactiveRects.Clear();
            _activeRect = Rect.Empty;
            InvalidateVisual();
        }

        private const int MaxVisualMatches = 500;

        /// <summary>
        /// Recalculates and updates match rectangles based on TextBox character indices.
        /// </summary>
        public void UpdateMatches(TextBox? textBox, IReadOnlyList<int>? matchIndices, int length, int activeIndex)
        {
            _inactiveRects.Clear();
            _activeRect = Rect.Empty;

            if (textBox == null || matchIndices == null || matchIndices.Count == 0 || length <= 0)
            {
                InvalidateVisual();
                return;
            }

            string text = textBox.Text;
            if (string.IsNullOrEmpty(text))
            {
                InvalidateVisual();
                return;
            }

            if (textBox.ActualWidth == 0 && textBox.IsVisible)
            {
                textBox.UpdateLayout();
            }

            // 1. Always compute active match first
            if (activeIndex >= 0 && activeIndex < matchIndices.Count)
            {
                int activeStart = matchIndices[activeIndex];
                if (activeStart >= 0 && activeStart + length <= text.Length)
                {
                    _activeRect = ComputeRect(textBox, text, activeStart, length);
                }
            }

            // 2. Compute inactive matches (capped to MaxVisualMatches for high FPS)
            int renderLimit = Math.Min(matchIndices.Count, MaxVisualMatches);
            for (int i = 0; i < renderLimit; i++)
            {
                if (i == activeIndex) continue;
                int start = matchIndices[i];
                if (start < 0 || start + length > text.Length) continue;

                var rect = ComputeRect(textBox, text, start, length);
                if (rect != Rect.Empty)
                {
                    _inactiveRects.Add(rect);
                }
            }

            InvalidateVisual();
        }

        private static Rect ComputeRect(TextBox textBox, string text, int start, int length)
        {
            try
            {
                var r1 = textBox.GetRectFromCharacterIndex(start);
                if (r1 == Rect.Empty) return Rect.Empty;

                var r2 = textBox.GetRectFromCharacterIndex(start + length);

                if (r2 != Rect.Empty && Math.Abs(r1.Top - r2.Top) < 2)
                {
                    double width = Math.Max(4, r2.Left - r1.Left);
                    return new Rect(r1.Left, r1.Top, width, r1.Height);
                }
                else
                {
                    double approxCharWidth = textBox.FontSize * 0.6;
                    double width = Math.Max(4, approxCharWidth * length);
                    return new Rect(r1.Left, r1.Top, width, r1.Height);
                }
            }
            catch
            {
                return Rect.Empty;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            // 1. Draw inactive match boxes
            foreach (var r in _inactiveRects)
            {
                if (r != Rect.Empty)
                {
                    drawingContext.DrawRoundedRectangle(s_searchMatchBrush, pen: null, r, 2, 2);
                }
            }

            // 2. Draw active match box on top
            if (_activeRect != Rect.Empty)
            {
                drawingContext.DrawRoundedRectangle(s_activeSearchMatchBrush, s_activeSearchMatchPen, _activeRect, 2, 2);
            }
        }
    }
}
