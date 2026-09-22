using System.Collections.Generic;
using System.Windows;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface ISpriteSheetSlicerService
    {
        /// <summary>
        /// Slices the input bitmap into an animated <see cref="SpriteState"/> containing all sliced frames.
        /// </summary>
        SpriteState SliceToAnimationSprite(BitmapSource source, SpriteSheetSliceSettings settings);

        /// <summary>
        /// Slices the input bitmap into a list of individual, single-frame <see cref="SpriteState"/> objects (e.g. for tilesets or icon packs).
        /// </summary>
        IReadOnlyList<SpriteState> SliceToIndividualSprites(BitmapSource source, SpriteSheetSliceSettings settings);

        /// <summary>
        /// Computes the pixel coordinate bounding boxes for every slice in the source image.
        /// Useful for visual slice overlay rendering.
        /// </summary>
        IReadOnlyList<Int32Rect> CalculateSliceRects(BitmapSource source, SpriteSheetSliceSettings settings);

        /// <summary>
        /// Analyzes the source bitmap dimensions and returns suggested frame and grid dimensions.
        /// </summary>
        (int SuggestedFrameWidth, int SuggestedFrameHeight, int Columns, int Rows) DetectGrid(BitmapSource source);

        /// <summary>
        /// Scans the source image for non-transparent pixel islands using 4-connected flood fill.
        /// Returns tight bounding boxes for each contiguous sprite region.
        /// </summary>
        IReadOnlyList<Int32Rect> DetectAlphaIslands(BitmapSource source, int alphaThreshold = 10, int minIslandWidth = 2, int minIslandHeight = 2);

        /// <summary>
        /// Calculates individual frame width and height given image size, row/column counts, offset, and spacing.
        /// </summary>
        (int FrameWidth, int FrameHeight) ComputeFrameSizeFromCount(int imageWidth, int imageHeight, int columns, int rows, int offsetX, int offsetY, int spacingX, int spacingY);

        /// <summary>
        /// Filters out bounding rects where all pixels have alpha at or below the specified threshold.
        /// </summary>
        IReadOnlyList<Int32Rect> PruneEmptyRects(BitmapSource source, IReadOnlyList<Int32Rect> rects, int alphaThreshold = 0);

        /// <summary>
        /// If the file is an animated multi-frame GIF, composites and stitches all frames into a horizontal sprite strip.
        /// Returns null if the file is not a multi-frame GIF.
        /// </summary>
        BitmapSource? CreateStripFromGif(string filePath);
    }
}
