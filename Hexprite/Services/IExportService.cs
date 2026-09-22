using System.Collections.Generic;
using System.Windows.Media.Imaging;
using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IExportService
    {
        void Export(string targetPath, SpriteState spriteState, ImageExportSettings settings);
        void ExportBitmaps(string targetPath, IReadOnlyList<BitmapSource> frames, ImageExportSettings settings);
    }
}
