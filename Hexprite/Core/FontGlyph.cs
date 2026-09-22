namespace Hexprite.Core
{
    /// <summary>
    /// Represents a font glyph in Hexprite.
    /// Extends <see cref="GlyphState"/> with constructors and factory methods.
    /// </summary>
    public class FontGlyph : GlyphState
    {
        public FontGlyph()
        {
        }

        public FontGlyph(int codePoint, int width, int height)
        {
            CodePoint = codePoint;
            Width = width;
            Height = height;
            Pixels = new bool[width * height];
            XAdvance = width + 1;
        }

        public static FontGlyph FromGlyphState(GlyphState state)
        {
            if (state == null) return new FontGlyph();
            return new FontGlyph
            {
                CodePoint = state.CodePoint,
                Width = state.Width,
                Height = state.Height,
                Pixels = state.Pixels != null ? (bool[])state.Pixels.Clone() : [],
                XOffset = state.XOffset,
                YOffset = state.YOffset,
                XAdvance = state.XAdvance,
                IsCustomized = state.IsCustomized,
            };
        }
    }
}
