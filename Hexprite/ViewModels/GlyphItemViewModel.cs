using CommunityToolkit.Mvvm.ComponentModel;
using Hexprite.Core;
using System.Windows.Media.Imaging;

namespace Hexprite.ViewModels
{
    /// <summary>
    /// ViewModel for a single item in the font's glyph map.
    /// Used to populate the grid of character cells in Font Mode.
    /// </summary>
    public partial class GlyphItemViewModel(GlyphState state, Action<GlyphItemViewModel>? requestPreview = null) : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Character))]
        public partial int CodePoint { get; set; } = state.CodePoint;

        /// <summary>
        /// Display character corresponding to the CodePoint.
        /// </summary>
        public char Character => (char)CodePoint;

        /// <summary>
        /// Formatted hex code string (e.g. "0x41").
        /// </summary>
        public string HexCode => $"0x{CodePoint:X2}";

        /// <summary>
        /// Printable character display string or empty if whitespace/control.
        /// </summary>
        public string CharDisplay => (char.IsControl(Character) || char.IsWhiteSpace(Character)) ? string.Empty : Character.ToString();

        /// <summary>
        /// Whether the character is a visible printable character.
        /// </summary>
        public bool IsPrintable => !char.IsControl(Character) && !char.IsWhiteSpace(Character);

        public string DisplayLabel
        {
            get
            {
                if (char.IsControl(Character) || char.IsWhiteSpace(Character))
                {
                    return $"0x{CodePoint:X2}";
                }
                return $"'{Character}' (0x{CodePoint:X2})";
            }
        }

        [ObservableProperty]
        public partial bool IsActive { get; set; }

        [ObservableProperty]
        public partial bool IsCustomized { get; set; } = state.IsCustomized;

        private WriteableBitmap? _miniPreviewBitmap;
        public WriteableBitmap? MiniPreviewBitmap
        {
            get
            {
                if (_miniPreviewBitmap == null && _requestPreview != null)
                {
                    _requestPreview(this);
                }
                return _miniPreviewBitmap;
            }
            set => SetProperty(ref _miniPreviewBitmap, value);
        }

        public void SetMiniPreviewBitmapSilent(WriteableBitmap? bitmap)
        {
            _miniPreviewBitmap = bitmap;
            // Do NOT call OnPropertyChanged here because this is called from the getter
        }

        public WriteableBitmap? PeekMiniPreviewBitmap() => _miniPreviewBitmap;

        private readonly Action<GlyphItemViewModel>? _requestPreview = requestPreview;
    }
}
