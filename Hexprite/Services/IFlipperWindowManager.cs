using Hexprite.Core;

namespace Hexprite.Services
{
    public interface IFlipperWindowManager
    {
        void ShowSimulator(SpriteState? initialSprite = null);
        void ShowSimulator(
            System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName = "AssetPack");
        void ShowSimulator(
            System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations,
            string packName,
            FlipperSimulatorSettings? settings,
            System.Action<FlipperSimulatorSettings>? onSettingsChanged = null);
        void ShowMediaSlicer(SpriteState? initialSprite = null);
        void ShowScheduleMatrix(System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)>? pack = null, string packName = "Flipper Asset Pack");
        void ShowScreenMirror();
        void ShowDeploy(SpriteState? sprite = null);
        void ShowDeploy(System.Collections.Generic.IReadOnlyList<(string RelativePath, byte[] Data)> files, string packName = "AssetPack");
        void ShowDeploy(System.Collections.Generic.IReadOnlyList<(string Name, SpriteState Sprite, FlipperManifestEntry ManifestEntry)> animations, string packName = "AssetPack");
        bool? ShowExportDialog(SpriteState sprite, FlipperExportSettings settings);
    }
}
