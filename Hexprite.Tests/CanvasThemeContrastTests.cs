using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class CanvasThemeContrastTests
    {
        private static readonly string[] ThemeFileNames = ["Dark.xaml", "Light.xaml", "Dim.xaml", "Flipper.xaml"];
        private static readonly string ProjectRoot = FindProjectRoot();

        private static string FindProjectRoot()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Hexprite.sln")) || Directory.Exists(Path.Combine(dir, "Hexprite")))
                {
                    return dir;
                }
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return @"H:\dev\hexprite";
        }

        public static TheoryData<string> AllThemes => new()
        {
            "Dark.xaml",
            "Light.xaml",
            "Dim.xaml",
            "Flipper.xaml"
        };

        private static readonly string[] RequiredCanvasPaletteKeys =
        [
            "Palette.Canvas.Workspace",
            "Palette.Surface.Elevated"
        ];

        private static readonly string[] RequiredCanvasBrushKeys =
        [
            "Brush.Canvas.Workspace",
            "Brush.Surface.Elevated"
        ];

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void AllThemes_ContainRequiredCanvasAndElevationPaletteKeys(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            Assert.True(File.Exists(themePath), $"Theme file missing: {themePath}");

            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var colorElements = doc.Descendants()
                .Where(e => e.Name.LocalName == "Color")
                .Select(e => (string?)e.Attribute(xNamespace + "Key"))
                .Where(k => k != null)
                .ToHashSet();

            foreach (var key in RequiredCanvasPaletteKeys)
            {
                Assert.True(colorElements.Contains(key),
                    $"Theme '{themeFileName}' is missing palette color key: '{key}'");
            }
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void AllThemes_ContainRequiredCanvasAndElevationBrushes(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            Assert.True(File.Exists(themePath), $"Theme file missing: {themePath}");

            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var brushElements = doc.Descendants()
                .Where(e => e.Name.LocalName == "SolidColorBrush")
                .Select(e => (string?)e.Attribute(xNamespace + "Key"))
                .Where(k => k != null)
                .ToHashSet();

            foreach (var key in RequiredCanvasBrushKeys)
            {
                Assert.True(brushElements.Contains(key),
                    $"Theme '{themeFileName}' is missing brush key: '{key}'");
            }
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void AllThemes_DoNotContainObsoleteCanvasBorderKeys(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var allKeys = doc.Descendants()
                .Select(e => (string?)e.Attribute(xNamespace + "Key"))
                .Where(k => k != null)
                .ToHashSet();

            Assert.DoesNotContain("Palette.Canvas.Border", allKeys);
            Assert.DoesNotContain("Brush.Canvas.Border", allKeys);
        }

        [Fact]
        public void DarkTheme_CanvasWorkspace_IsDistinguishableFromBlackCanvas()
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Dark.xaml");
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            string workspaceHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();
            string pixelHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Canvas.Pixel").Value.Trim();

            var workspaceColor = (Color)ColorConverter.ConvertFromString(workspaceHex);
            var pixelColor = (Color)ColorConverter.ConvertFromString(pixelHex);
            var trueBlack = Color.FromRgb(0, 0, 0);

            // In Dark theme, workspace must be visibly elevated compared to off-pixel (#080A0F) and true black
            int deltaR = Math.Abs(workspaceColor.R - pixelColor.R);
            int deltaG = Math.Abs(workspaceColor.G - pixelColor.G);
            int deltaB = Math.Abs(workspaceColor.B - pixelColor.B);
            int totalDeltaVsPixel = deltaR + deltaG + deltaB;
            Assert.True(totalDeltaVsPixel >= 50, $"Dark workspace ({workspaceHex}) must have total RGB delta >= 50 against pixel ({pixelHex}), got {totalDeltaVsPixel}");

            int totalDeltaVsBlack = Math.Abs(workspaceColor.R - trueBlack.R) + Math.Abs(workspaceColor.G - trueBlack.G) + Math.Abs(workspaceColor.B - trueBlack.B);
            Assert.True(totalDeltaVsBlack >= 50, $"Dark workspace ({workspaceHex}) must have total RGB delta >= 50 against true black, got {totalDeltaVsBlack}");
        }

        [Fact]
        public void FlipperTheme_CanvasWorkspace_IsDistinguishableFromBlackInk()
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Flipper.xaml");
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            string workspaceHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();
            string drawingHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Canvas.Drawing").Value.Trim();

            var workspaceColor = (Color)ColorConverter.ConvertFromString(workspaceHex);
            var drawingColor = (Color)ColorConverter.ConvertFromString(drawingHex);
            var trueBlack = Color.FromRgb(0, 0, 0);

            // In Flipper theme, workspace must be visibly distinguishable from black drawing ink (#080808) and true black
            int deltaR = Math.Abs(workspaceColor.R - drawingColor.R);
            int deltaG = Math.Abs(workspaceColor.G - drawingColor.G);
            int deltaB = Math.Abs(workspaceColor.B - drawingColor.B);
            int totalDeltaVsInk = deltaR + deltaG + deltaB;
            Assert.True(totalDeltaVsInk >= 50, $"Flipper workspace ({workspaceHex}) must have total RGB delta >= 50 against black ink ({drawingHex}), got {totalDeltaVsInk}");

            int totalDeltaVsBlack = Math.Abs(workspaceColor.R - trueBlack.R) + Math.Abs(workspaceColor.G - trueBlack.G) + Math.Abs(workspaceColor.B - trueBlack.B);
            Assert.True(totalDeltaVsBlack >= 50, $"Flipper workspace ({workspaceHex}) must have total RGB delta >= 50 against true black, got {totalDeltaVsBlack}");
        }

        [Fact]
        public void CanvasPanel_Structure_AuthenticFloatingPanels_MatchesOriginalLayout()
        {
            string canvasPanelPath = Path.Combine(ProjectRoot, "Hexprite", "Views", "CanvasPanel.xaml");
            Assert.True(File.Exists(canvasPanelPath), $"CanvasPanel.xaml missing: {canvasPanelPath}");

            var doc = XDocument.Load(canvasPanelPath);

            // 1. Root is Grid (no outer Border wrapper)
            var rootGrid = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid");
            Assert.NotNull(rootGrid);

            // 2. Toolbar in Row 0 is styled as floating panel card
            var toolbarBorder = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Border" && e.Attribute("Grid.Row")?.Value == "0");
            Assert.NotNull(toolbarBorder);
            Assert.Equal("{StaticResource PanelStyle}", toolbarBorder.Attribute("Style")?.Value);

            // 3. Viewport in Row 1 has PanelStyle and ClipToBounds=True
            var viewportBorder = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Border" && e.Attribute("Grid.Row")?.Value == "1");
            Assert.NotNull(viewportBorder);
            Assert.Equal("{StaticResource PanelStyle}", viewportBorder.Attribute("Style")?.Value);
            Assert.Equal("True", viewportBorder.Attribute("ClipToBounds")?.Value);

            // 4. MainScrollViewer uses unified Brush.Surface.Base background so canvas spans behind panels
            var scrollViewer = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "ScrollViewer" && e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "MainScrollViewer"));
            Assert.NotNull(scrollViewer);
            Assert.Equal("{DynamicResource Brush.Surface.Base}", scrollViewer.Attribute("Background")?.Value);

            // 5. Action bar in Row 2 is styled as floating panel card
            var actionBarBorder = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Border" && e.Attribute("Grid.Row")?.Value == "2");
            Assert.NotNull(actionBarBorder);
            Assert.Equal("{StaticResource PanelStyle}", actionBarBorder.Attribute("Style")?.Value);
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void AllThemes_WorkspacePaletteMatchesSurfaceBase(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            string workspaceHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Canvas.Workspace").Value.Trim();
            string surfaceBaseHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();

            Assert.Equal(surfaceBaseHex, workspaceHex);
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void AllThemes_SurfacePanelIsDistinctFromSurfaceBase(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            string panelHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Panel").Value.Trim();
            string surfaceBaseHex = doc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();

            var panelColor = (Color)ColorConverter.ConvertFromString(panelHex);
            var surfaceBaseColor = (Color)ColorConverter.ConvertFromString(surfaceBaseHex);

            int totalDelta = Math.Abs(panelColor.R - surfaceBaseColor.R) +
                             Math.Abs(panelColor.G - surfaceBaseColor.G) +
                             Math.Abs(panelColor.B - surfaceBaseColor.B);

            // Floating panel cards must visually pop from the canvas desk surface
            Assert.True(totalDelta >= 15,
                $"Theme '{themeFileName}' panel ({panelHex}) must have total RGB delta >= 15 against workspace base ({surfaceBaseHex}) to maintain floating illusion, got {totalDelta}");
        }

        [Fact]
        public void DimTheme_And_LightTheme_MaintainRequiredContrastVsCanvas()
        {
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            // Dim theme check
            string dimPath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Dim.xaml");
            var dimDoc = XDocument.Load(dimPath);
            string dimBaseHex = dimDoc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();
            string dimPixelHex = dimDoc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Canvas.Pixel").Value.Trim();
            var dimBaseColor = (Color)ColorConverter.ConvertFromString(dimBaseHex);
            var dimPixelColor = (Color)ColorConverter.ConvertFromString(dimPixelHex);
            int dimDeltaVsPixel = Math.Abs(dimBaseColor.R - dimPixelColor.R) +
                                  Math.Abs(dimBaseColor.G - dimPixelColor.G) +
                                  Math.Abs(dimBaseColor.B - dimPixelColor.B);
            Assert.True(dimDeltaVsPixel >= 50, $"Dim theme workspace ({dimBaseHex}) must have delta >= 50 vs pixel ({dimPixelHex}), got {dimDeltaVsPixel}");

            // Light theme check
            string lightPath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Light.xaml");
            var lightDoc = XDocument.Load(lightPath);
            string lightBaseHex = lightDoc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Surface.Base").Value.Trim();
            string lightPixelHex = lightDoc.Descendants()
                .First(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Canvas.Pixel").Value.Trim();
            var lightBaseColor = (Color)ColorConverter.ConvertFromString(lightBaseHex);
            var lightPixelColor = (Color)ColorConverter.ConvertFromString(lightPixelHex);
            int lightDeltaVsPixel = Math.Abs(lightBaseColor.R - lightPixelColor.R) +
                                    Math.Abs(lightBaseColor.G - lightPixelColor.G) +
                                    Math.Abs(lightBaseColor.B - lightPixelColor.B);
            Assert.True(lightDeltaVsPixel >= 50, $"Light theme workspace ({lightBaseHex}) must have delta >= 50 vs pixel ({lightPixelHex}), got {lightDeltaVsPixel}");
        }

        [Fact]
        public void MainWindow_UsesSurfaceBaseBackground_ToUnifyWithCanvasWorkspace()
        {
            string mainWindowPath = Path.Combine(ProjectRoot, "Hexprite", "MainWindow.xaml");
            Assert.True(File.Exists(mainWindowPath), $"MainWindow.xaml missing: {mainWindowPath}");

            var doc = XDocument.Load(mainWindowPath);
            Assert.Equal("{DynamicResource Brush.Surface.Base}", doc.Root?.Attribute("Background")?.Value);
        }

        [Fact]
        public void PanelStyle_DefinesFloatingEffectWithDropShadowAndElevation()
        {
            string stylesPath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Styles.xaml");
            Assert.True(File.Exists(stylesPath), $"Styles.xaml missing: {stylesPath}");

            var doc = XDocument.Load(stylesPath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var panelStyle = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Style" && (string?)e.Attribute(xNamespace + "Key") == "PanelStyle");
            Assert.NotNull(panelStyle);

            // 1. Background uses Brush.Surface.Panel
            var bgSetter = panelStyle.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "Background");
            Assert.Equal("{DynamicResource Brush.Surface.Panel}", bgSetter?.Attribute("Value")?.Value);

            // 2. CornerRadius > 0
            var cornerSetter = panelStyle.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "CornerRadius");
            Assert.NotNull(cornerSetter);
            Assert.True(int.Parse(cornerSetter.Attribute("Value")!.Value) > 0);

            // 3. BorderThickness is 0 (no harsh box stroke)
            var borderThicknessSetter = panelStyle.Elements().FirstOrDefault(e =>
                e.Name.LocalName == "Setter" && (string?)e.Attribute("Property") == "BorderThickness");
            Assert.Equal("0", borderThicknessSetter?.Attribute("Value")?.Value);

            // 4. DropShadowEffect exists for floating feeling
            var dropShadow = panelStyle.Descendants().FirstOrDefault(e => e.Name.LocalName == "DropShadowEffect");
            Assert.NotNull(dropShadow);
            Assert.Equal("Black", dropShadow.Attribute("Color")?.Value);
            Assert.True(double.Parse(dropShadow.Attribute("Opacity")!.Value) > 0);
            Assert.True(double.Parse(dropShadow.Attribute("BlurRadius")!.Value) > 0);
        }

        [Fact]
        public void CanvasPanel_CanvasContainer_HasNoArtificialOutlineOrShadow()
        {
            string canvasPanelPath = Path.Combine(ProjectRoot, "Hexprite", "Views", "CanvasPanel.xaml");
            Assert.True(File.Exists(canvasPanelPath), $"CanvasPanel.xaml missing: {canvasPanelPath}");

            var doc = XDocument.Load(canvasPanelPath);

            // Locate the Border wrapping PixelGridContainer
            var pixelGrid = doc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "Grid" && e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "PixelGridContainer"));
            Assert.NotNull(pixelGrid);

            var canvasBorder = pixelGrid.Parent;
            Assert.NotNull(canvasBorder);
            Assert.Equal("Border", canvasBorder.Name.LocalName);

            // Assert no outline border stroke
            Assert.Null(canvasBorder.Attribute("BorderBrush"));
            var borderThickness = canvasBorder.Attribute("BorderThickness")?.Value;
            Assert.True(borderThickness == null || borderThickness == "0");

            // Assert no drop shadow directly under canvas
            var dropShadow = canvasBorder.Descendants().FirstOrDefault(e => e.Name.LocalName == "DropShadowEffect");
            Assert.Null(dropShadow);

            // Assert background is Brush.Canvas.Base
            Assert.Equal("{DynamicResource Brush.Canvas.Base}", canvasBorder.Attribute("Background")?.Value);
        }

        [Fact]
        public void AuxiliaryViewports_AdhereToConsistentWorkspaceBackgrounds()
        {
            // 1. DisplaySimulationWindow
            string displaySimPath = Path.Combine(ProjectRoot, "Hexprite", "Views", "DisplaySimulationWindow.xaml");
            Assert.True(File.Exists(displaySimPath), $"DisplaySimulationWindow.xaml missing: {displaySimPath}");
            var displayDoc = XDocument.Load(displaySimPath);
            var simScrollViewer = displayDoc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ScrollViewer");
            Assert.NotNull(simScrollViewer);
            Assert.Equal("{DynamicResource Brush.Surface.Base}", simScrollViewer.Attribute("Background")?.Value);

            // 2. FontEditorPanel
            string fontEditorPath = Path.Combine(ProjectRoot, "Hexprite", "Views", "FontEditorPanel.xaml");
            Assert.True(File.Exists(fontEditorPath), $"FontEditorPanel.xaml missing: {fontEditorPath}");
            var fontDoc = XDocument.Load(fontEditorPath);
            var fontScrollViewer = fontDoc.Descendants().FirstOrDefault(e =>
                e.Name.LocalName == "ScrollViewer" && e.Attributes().Any(a => a.Name.LocalName == "Name" && a.Value == "MainScrollViewer"));
            Assert.NotNull(fontScrollViewer);
            Assert.Null(fontScrollViewer.Attribute("Background")); // ScrollViewer is transparent

            var glyphBorder = fontScrollViewer.Parent;
            Assert.NotNull(glyphBorder);
            Assert.Equal("Border", glyphBorder.Name.LocalName);
            Assert.Equal("{DynamicResource Brush.Surface.Elevated}", glyphBorder.Attribute("Background")?.Value);
        }
    }
}
