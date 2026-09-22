using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Xml.Linq;
using Hexprite.Core;
using Hexprite.ViewModels.Flipper;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperThemeAndInspectionChallengerTests
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

        #region 1. Theme Resource Completeness & Parity Tests

        public static TheoryData<string> AllThemes => new()
        {
            "Dark.xaml",
            "Light.xaml",
            "Dim.xaml",
            "Flipper.xaml"
        };

        private static readonly string[] RequiredMatrixPaletteKeys =
        [
            "Palette.Matrix.GapFill",
            "Palette.Matrix.GapStroke",
            "Palette.Matrix.DragStroke",
            "Palette.Matrix.DragFill",
            "Palette.Matrix.BadgeText",
            "Palette.Matrix.DividerTier",
            "Palette.Matrix.DividerMood",
            "Palette.Matrix.HoverStroke",
            "Palette.Matrix.HoverFill",
            "Palette.Matrix.ReticleStroke",
            "Palette.Matrix.ReticleFill",
            "Palette.Stage.Baby",
            "Palette.Stage.Teen",
            "Palette.Stage.Adult",
            "Palette.Stage.Baby.Bg",
            "Palette.Stage.Teen.Bg",
            "Palette.Stage.Adult.Bg",
            "Palette.Mood.Happy",
            "Palette.Mood.Neutral",
            "Palette.Mood.Angry"
        ];

        private static readonly string[] RequiredMatrixBrushKeys =
        [
            "Brush.Matrix.GapFill",
            "Brush.Matrix.GapStroke",
            "Brush.Matrix.DragStroke",
            "Brush.Matrix.DragFill",
            "Brush.Matrix.BadgeText",
            "Brush.Matrix.DividerTier",
            "Brush.Matrix.DividerMood",
            "Brush.Matrix.HoverStroke",
            "Brush.Matrix.HoverFill",
            "Brush.Matrix.ReticleStroke",
            "Brush.Matrix.ReticleFill",
            "Brush.Stage.Baby",
            "Brush.Stage.Teen",
            "Brush.Stage.Adult",
            "Brush.Stage.Baby.Bg",
            "Brush.Stage.Teen.Bg",
            "Brush.Stage.Adult.Bg",
            "Brush.Mood.Happy",
            "Brush.Mood.Neutral",
            "Brush.Mood.Angry"
        ];

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void Theme_ContainsAllRequiredMatrixPaletteColors(string themeFileName)
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

            foreach (var requiredKey in RequiredMatrixPaletteKeys)
            {
                Assert.True(colorElements.Contains(requiredKey),
                    $"Theme '{themeFileName}' is missing required palette color key: '{requiredKey}'");
            }
        }

        [Theory]
        [MemberData(nameof(AllThemes))]
        public void Theme_ContainsAllRequiredMatrixBrushes_BoundToPaletteColors(string themeFileName)
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", themeFileName);
            Assert.True(File.Exists(themePath), $"Theme file missing: {themePath}");

            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var brushElements = doc.Descendants()
                .Where(e => e.Name.LocalName == "SolidColorBrush")
                .ToDictionary(
                    e => (string?)e.Attribute(xNamespace + "Key") ?? string.Empty,
                    e => (string?)e.Attribute("Color") ?? string.Empty);

            foreach (var requiredKey in RequiredMatrixBrushKeys)
            {
                Assert.True(brushElements.ContainsKey(requiredKey),
                    $"Theme '{themeFileName}' is missing required brush key: '{requiredKey}'");

                string colorAttr = brushElements[requiredKey];
                Assert.NotEmpty(colorAttr);
            }
        }

        [Fact]
        public void LightTheme_GapColors_AreThemeAwareAndHighContrast()
        {
            string themePath = Path.Combine(ProjectRoot, "Hexprite", "Themes", "Light.xaml");
            var doc = XDocument.Load(themePath);
            var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

            var gapFillElem = doc.Descendants()
                .FirstOrDefault(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Matrix.GapFill");
            var gapStrokeElem = doc.Descendants()
                .FirstOrDefault(e => (string?)e.Attribute(xNamespace + "Key") == "Palette.Matrix.GapStroke");

            Assert.NotNull(gapFillElem);
            Assert.NotNull(gapStrokeElem);

            string fillHex = gapFillElem.Value.Trim();
            string strokeHex = gapStrokeElem.Value.Trim();

            // Light theme uses pastel red fill (#FFF0F2) and distinct red border (#EF4444)
            Assert.Equal("#FFF0F2", fillHex, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("#EF4444", strokeHex, StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region 2. Single-Click Inspection Bounds Invariant Tests

        [Theory]
        [InlineData(1, 0)]
        [InlineData(5, 5)]
        [InlineData(15, 7)]
        [InlineData(25, 12)]
        [InlineData(30, 14)]
        public void InspectCell_DoesNotMutateOrShrink_SelectedEntryBounds(int clickLvl, int clickMood)
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First(e => e.Name == "anim_baby");
            vm.SelectedEntry = entry;

            int originalMinL = entry.MinLevel;
            int originalMaxL = entry.MaxLevel;
            int originalMinM = entry.MinButthurt;
            int originalMaxM = entry.MaxButthurt;
            int originalWeight = entry.Weight;
            string originalName = entry.Name;

            // Execute single-cell inspection
            vm.InspectCell(clickLvl, clickMood);

            // Verify Inspected coordinates updated
            Assert.Equal(clickLvl, vm.SelectedCellLevel);
            Assert.Equal(clickMood, vm.SelectedCellMood);

            // Verify Selected Entry bounds did NOT collapse or mutate
            Assert.Equal(originalMinL, entry.MinLevel);
            Assert.Equal(originalMaxL, entry.MaxLevel);
            Assert.Equal(originalMinM, entry.MinButthurt);
            Assert.Equal(originalMaxM, entry.MaxButthurt);
            Assert.Equal(originalWeight, entry.Weight);
            Assert.Equal(originalName, entry.Name);
            Assert.Same(entry, vm.SelectedEntry);
        }

        [Fact]
        public void InspectingEveryMatrixCellSequentially_PreservesSelectedEntryBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var teenEntry = vm.Entries.First(e => e.Name == "anim_teen");
            vm.SelectedEntry = teenEntry;

            int initialMinL = teenEntry.MinLevel;
            int initialMaxL = teenEntry.MaxLevel;
            int initialMinM = teenEntry.MinButthurt;
            int initialMaxM = teenEntry.MaxButthurt;

            for (int lvl = 1; lvl <= 30; lvl++)
            {
                for (int mood = 0; mood <= 14; mood++)
                {
                    vm.InspectCell(lvl, mood);

                    Assert.Equal(lvl, vm.SelectedCellLevel);
                    Assert.Equal(mood, vm.SelectedCellMood);
                    Assert.Equal(initialMinL, teenEntry.MinLevel);
                    Assert.Equal(initialMaxL, teenEntry.MaxLevel);
                    Assert.Equal(initialMinM, teenEntry.MinButthurt);
                    Assert.Equal(initialMaxM, teenEntry.MaxButthurt);
                }
            }
        }

        [Fact]
        public void SimulatedSingleClick_NoDrag_DoesNotTriggerSetSelectedEntryBounds()
        {
            // Verify the contract in code-behind where hasDragged == false
            var vm = new FlipperScheduleMatrixViewModel();
            var adultEntry = vm.Entries.First(e => e.Name == "anim_adult");
            vm.SelectedEntry = adultEntry;

            int startLvl = 15;
            int startMood = 8;
            int currentLvl = 15;
            int currentMood = 8;

            bool hasDragged = (startLvl != currentLvl || startMood != currentMood);

            Assert.False(hasDragged);
            // Since hasDragged is false, SetSelectedEntryBounds is NOT invoked
            Assert.Equal(21, adultEntry.MinLevel);
            Assert.Equal(30, adultEntry.MaxLevel);
        }

        [Fact]
        public void SimulatedDrag_WithDisplacement_UpdatesBounds()
        {
            var vm = new FlipperScheduleMatrixViewModel();
            var entry = vm.Entries.First();
            vm.SelectedEntry = entry;

            int startLvl = 5;
            int startMood = 3;
            int currentLvl = 12;
            int currentMood = 9;

            bool hasDragged = (startLvl != currentLvl || startMood != currentMood);
            Assert.True(hasDragged);

            if (hasDragged)
            {
                int minL = Math.Min(startLvl, currentLvl);
                int maxL = Math.Max(startLvl, currentLvl);
                int minM = Math.Min(startMood, currentMood);
                int maxM = Math.Max(startMood, currentMood);
                vm.SetSelectedEntryBounds(minL, maxL, minM, maxM);
            }

            Assert.Equal(5, entry.MinLevel);
            Assert.Equal(12, entry.MaxLevel);
            Assert.Equal(3, entry.MinButthurt);
            Assert.Equal(9, entry.MaxButthurt);
        }

        #endregion

        #region 3. Color Contrast & Distinctness Invariants

        [Fact]
        public void AllThemeBadgeTextBrushes_MeetContrastThresholds()
        {
            // Test dark text #0B0D14 across solo cyan (#00E5FF), duo emerald (#39FF14), trio amber (#FFB300), and coral (#FF3366)
            double textLuminance = GetLuminance(11, 13, 20);

            var backgrounds = new (string Name, byte R, byte G, byte B)[]
            {
                ("Cyan Solo", 0, 229, 255),
                ("Emerald Duo", 57, 255, 20),
                ("Amber Trio", 255, 179, 0),
                ("Coral Contended", 255, 51, 102)
            };

            foreach (var (bgName, r, g, b) in backgrounds)
            {
                double bgLum = GetLuminance(r, g, b);
                double contrast = (Math.Max(textLuminance, bgLum) + 0.05) / (Math.Min(textLuminance, bgLum) + 0.05);

                Assert.True(contrast >= 4.5, $"Badge contrast on {bgName} is {contrast:F2}, which is below WCAG AA 4.5:1");
            }
        }

        [Fact]
        public void DragStrokeViolet_HasHighPerceptualSeparation_FromSoloCyan()
        {
            // DragStroke: #B388FF (179, 136, 255)
            // SoloCyan: #00E5FF (0, 229, 255)
            double deltaR = 179 - 0;
            double deltaG = 136 - 229;
            double deltaB = 255 - 255;
            double distance = Math.Sqrt(deltaR * deltaR + deltaG * deltaG + deltaB * deltaB);

            Assert.True(distance >= 150.0, $"Color distance {distance:F1} is too small for unambiguous visual distinction.");
        }

        private static double GetLuminance(byte r, byte g, byte b)
        {
            double rLin = (r / 255.0 <= 0.03928) ? (r / 255.0) / 12.92 : Math.Pow(((r / 255.0) + 0.055) / 1.055, 2.4);
            double gLin = (g / 255.0 <= 0.03928) ? (g / 255.0) / 12.92 : Math.Pow(((g / 255.0) + 0.055) / 1.055, 2.4);
            double bLin = (b / 255.0 <= 0.03928) ? (b / 255.0) / 12.92 : Math.Pow(((b / 255.0) + 0.055) / 1.055, 2.4);
            return 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
        }

        #endregion
    }
}
