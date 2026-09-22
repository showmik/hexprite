using CommunityToolkit.Mvvm.ComponentModel;

namespace Hexprite.ViewModels
{
    public partial class FrameItemViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string Name { get; set; } = "Frame";

        [ObservableProperty]
        public partial bool IsActive { get; set; }

        [ObservableProperty]
        public partial bool IsSelected { get; set; }

        [ObservableProperty]
        public partial bool HasContent { get; set; }

        [ObservableProperty]
        public partial int DelayMultiplier { get; set; } = 1;

        [ObservableProperty]
        public partial System.Windows.Media.ImageSource? Thumbnail { get; set; }

        [ObservableProperty]
        public partial bool IsPlayingBack { get; set; }
    }
}
