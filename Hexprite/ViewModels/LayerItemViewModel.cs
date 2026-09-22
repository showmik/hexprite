using CommunityToolkit.Mvvm.ComponentModel;

namespace Hexprite.ViewModels
{
    public partial class LayerItemViewModel : ObservableObject
    {
        [ObservableProperty]
        public partial string Name { get; set; } = "Layer";

        [ObservableProperty]
        public partial bool IsVisible { get; set; } = true;

        [ObservableProperty]
        public partial bool IsLocked { get; set; }

        [ObservableProperty]
        public partial bool IsActive { get; set; }

        [ObservableProperty]
        public partial bool IsRenaming { get; set; }

        [ObservableProperty]
        public partial bool IsSelected { get; set; }

        [ObservableProperty]
        public partial bool HasContent { get; set; }

        [ObservableProperty]
        public partial bool IsGlobal { get; set; }

        [ObservableProperty]
        public partial bool ExcludeFromExport { get; set; }

        [ObservableProperty]
        public partial bool PreserveOverflow { get; set; }

        [ObservableProperty]
        public partial Hexprite.Core.LayerBlendMode BlendMode { get; set; } = Hexprite.Core.LayerBlendMode.Normal;

        [ObservableProperty]
        public partial Hexprite.Core.LayerOpacityMode OpacityMode { get; set; } = Hexprite.Core.LayerOpacityMode.Solid;
    }
}
