using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Hexprite.Rendering
{
    public enum TokenType
    {
        Default,
        Keyword,
        Literal,
        Comment,
        Identifier,
    }

    public readonly struct TokenSpan(int start, int length, TokenType type)
    {
        public int Start { get; } = start;
        public int Length { get; } = length;
        public TokenType Type { get; } = type;
    }

    /// <summary>
    /// Represents a UI component that renders formatted text with syntax highlighting.
    /// </summary>
    public class SyntaxHighlightBox : FrameworkElement
    {
        private FormattedText? _formattedText;
        private string? _cachedText;
        private List<TokenSpan>? _cachedSpans;
        private SolidColorBrush? _cachedDefaultBrush;
        private SolidColorBrush? _cachedKeywordBrush;
        private SolidColorBrush? _cachedLiteralBrush;
        private SolidColorBrush? _cachedCommentBrush;
        private SolidColorBrush? _cachedIdentifierBrush;

        /// <summary>Defines the font family for the text.</summary>
        public static readonly DependencyProperty FontFamilyProperty =
            DependencyProperty.Register(nameof(FontFamily), typeof(FontFamily), typeof(SyntaxHighlightBox),
                new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFontPropertyChanged));

        public FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        /// <summary>Defines the font size for the text.</summary>
        public static readonly DependencyProperty FontSizeProperty =
            DependencyProperty.Register(nameof(FontSize), typeof(double), typeof(SyntaxHighlightBox),
                new FrameworkPropertyMetadata(12.0,
                    FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
                    OnFontPropertyChanged));

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        private static void OnFontPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SyntaxHighlightBox box)
            {
                box.RebuildFormattedText();
            }
        }

        private readonly List<int> _searchMatches = [];
        private int _searchMatchLength;
        private int _activeSearchIndex = -1;

        private static readonly SolidColorBrush s_searchMatchBrush = new(Color.FromArgb(120, 255, 215, 0));
        private static readonly SolidColorBrush s_activeSearchMatchBrush = new(Color.FromArgb(220, 255, 140, 0));
        private static readonly Pen s_activeSearchMatchPen = new(new SolidColorBrush(Color.FromArgb(240, 255, 255, 255)), 1.2);

        static SyntaxHighlightBox()
        {
            s_searchMatchBrush.Freeze();
            s_activeSearchMatchBrush.Freeze();
            s_activeSearchMatchPen.Freeze();
        }

        /// <summary>
        /// Sets the active search match highlights.
        /// </summary>
        public void SetSearchMatches(IEnumerable<int>? matchIndices, int length, int activeIndex)
        {
            _searchMatches.Clear();
            if (matchIndices != null && length > 0)
            {
                _searchMatches.AddRange(matchIndices);
            }
            _searchMatchLength = length;
            _activeSearchIndex = activeIndex;
            InvalidateVisual();
        }

        /// <summary>
        /// Clears all search match highlights.
        /// </summary>
        public void ClearSearchMatches()
        {
            _searchMatches.Clear();
            _searchMatchLength = 0;
            _activeSearchIndex = -1;
            InvalidateVisual();
        }

        /// <summary>
        /// Gets the bounding rectangle of a character or substring range.
        /// </summary>
        public Rect GetRectFromCharacterIndex(int index, int length = 1)
        {
            if (_formattedText == null || string.IsNullOrEmpty(_cachedText) || index < 0 || index + length > _cachedText.Length)
                return Rect.Empty;

            try
            {
                var geom = _formattedText.BuildHighlightGeometry(new Point(0, 0), index, Math.Max(1, length));
                return geom?.Bounds ?? Rect.Empty;
            }
            catch
            {
                return Rect.Empty;
            }
        }

        /// <summary>
        /// Updates the code text and applies syntax highlight colors.
        /// </summary>
        public void UpdateCode(string text, List<TokenSpan> spans, 
            SolidColorBrush defaultBrush, SolidColorBrush keywordBrush, 
            SolidColorBrush literalBrush, SolidColorBrush commentBrush, 
            SolidColorBrush identifierBrush)
        {
            _cachedText = text;
            _cachedSpans = spans;
            _cachedDefaultBrush = defaultBrush;
            _cachedKeywordBrush = keywordBrush;
            _cachedLiteralBrush = literalBrush;
            _cachedCommentBrush = commentBrush;
            _cachedIdentifierBrush = identifierBrush;

            RebuildFormattedText();
        }

        /// <summary>
        /// Maximum number of syntax token spans formatted with SetForegroundBrush.
        /// With array literal span merging, 25,000 spans covers over 150 animation frames (~10,000 lines).
        /// </summary>
        public const int MaxFormattedSpans = 25000;

        /// <summary>
        /// Safety limit on rendered character count to prevent out-of-memory on multi-megabyte files.
        /// </summary>
        public const int MaxRenderCharLength = 500000;

        /// <summary>
        /// Rebuilds the FormattedText object with current font family, size, and tokens.
        /// </summary>
        public void RebuildFormattedText()
        {
            string? text = _cachedText;
            if (string.IsNullOrEmpty(text))
            {
                _formattedText = null;
                InvalidateMeasure();
                InvalidateVisual();
                return;
            }

            var spans = _cachedSpans;
            var defaultBrush = _cachedDefaultBrush ?? Brushes.White;
            var keywordBrush = _cachedKeywordBrush ?? defaultBrush;
            var literalBrush = _cachedLiteralBrush ?? defaultBrush;
            var commentBrush = _cachedCommentBrush ?? defaultBrush;
            var identifierBrush = _cachedIdentifierBrush ?? defaultBrush;

            if (text.Length > MaxRenderCharLength)
            {
                text = text[..MaxRenderCharLength] + string.Create(CultureInfo.InvariantCulture, $"\n\n// ... Output truncated for UI rendering ({text.Length:N0} total chars). Use Selectable Text mode or Copy Code to inspect entire file.");
            }

            // Create Typeface
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

            // Create FormattedText with default brush
            _formattedText = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                defaultBrush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Apply color spans (capped to MaxFormattedSpans to guarantee smooth UI thread performance)
            if (spans != null)
            {
                int spanCount = Math.Min(spans.Count, MaxFormattedSpans);
                for (int i = 0; i < spanCount; i++)
                {
                    var span = spans[i];
                    if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > text.Length)
                        continue;

                    SolidColorBrush brush = span.Type switch
                    {
                        TokenType.Keyword => keywordBrush,
                        TokenType.Literal => literalBrush,
                        TokenType.Comment => commentBrush,
                        TokenType.Identifier => identifierBrush,
                        _ => defaultBrush,
                    };

                    _formattedText.SetForegroundBrush(brush, span.Start, span.Length);
                }
            }

            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (_formattedText == null)
                return new Size(0, 0);

            return new Size(_formattedText.WidthIncludingTrailingWhitespace, _formattedText.Height);
        }

        private const int MaxVisualHighlights = 500;

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (_formattedText == null) return;

            // Draw search match highlights under the text
            if (_searchMatches.Count > 0 && _searchMatchLength > 0 && _cachedText != null)
            {
                int textLen = _cachedText.Length;

                // 1. Draw inactive match boxes (capped for smooth real-time performance)
                int renderLimit = Math.Min(_searchMatches.Count, MaxVisualHighlights);
                for (int i = 0; i < renderLimit; i++)
                {
                    if (i == _activeSearchIndex) continue;
                    int start = _searchMatches[i];
                    if (start < 0 || start + _searchMatchLength > textLen) continue;

                    try
                    {
                        var geom = _formattedText.BuildHighlightGeometry(new Point(0, 0), start, _searchMatchLength);
                        if (geom != null)
                        {
                            drawingContext.DrawGeometry(s_searchMatchBrush, pen: null, geom);
                        }
                    }
                    catch { }
                }

                // 2. Draw active match box on top (always rendered regardless of cap)
                if (_activeSearchIndex >= 0 && _activeSearchIndex < _searchMatches.Count)
                {
                    int activeStart = _searchMatches[_activeSearchIndex];
                    if (activeStart >= 0 && activeStart + _searchMatchLength <= textLen)
                    {
                        try
                        {
                            var geom = _formattedText.BuildHighlightGeometry(new Point(0, 0), activeStart, _searchMatchLength);
                            if (geom != null)
                            {
                                drawingContext.DrawGeometry(s_activeSearchMatchBrush, s_activeSearchMatchPen, geom);
                            }
                        }
                        catch { }
                    }
                }
            }

            // Draw the formatted text
            drawingContext.DrawText(_formattedText, new Point(0, 0));
        }
    }
}
