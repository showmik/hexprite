using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Data;
using System.Xml.Linq;
using Hexprite.Converters;
using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests.Converters
{
    [Trait("Category", "Unit")]
    public class EnumToIndexConverterTests
    {
        private sealed class EnumHost : INotifyPropertyChanged
        {
            private AnimationExportLayout _layout;
            public AnimationExportLayout Layout
            {
                get => _layout;
                set { _layout = value; OnPropertyChanged(); }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        [Fact]
        public void Convert_EnumValue_ReturnsUnderlyingIndex()
        {
            var converter = new EnumToIndexConverter();

            Assert.Equal(2, converter.Convert(AnimationExportLayout.HorizontalSpriteSheet, typeof(int), null!, null!));
            Assert.Equal(1, converter.Convert(AnimationTimingMode.NonBlockingMillis, typeof(int), null!, null!));
        }

        [Fact]
        public void ConvertBack_Index_ReturnsEnumOfTargetType()
        {
            var converter = new EnumToIndexConverter();

            Assert.Equal(AnimationExportLayout.HorizontalSpriteSheet,
                converter.ConvertBack(2, typeof(AnimationExportLayout), null!, null!));
            Assert.Equal(AnimationTimingMode.NonBlockingMillis,
                converter.ConvertBack(1, typeof(AnimationTimingMode), null!, null!));
        }

        [Fact]
        public void ConvertBack_NoSelection_DoesNothing()
        {
            var converter = new EnumToIndexConverter();

            Assert.Same(Binding.DoNothing, converter.ConvertBack(-1, typeof(AnimationTimingMode), null!, null!));
        }

        [Fact]
        public void ComboBoxBinding_RoundTripsEnumBothDirections()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var host = new EnumHost { Layout = AnimationExportLayout.HorizontalSpriteSheet };
                var combo = new ComboBox();
                for (int i = 0; i < 4; i++) combo.Items.Add(new ComboBoxItem { Content = i.ToString() });
                combo.DataContext = host;
                combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty,
                    new Binding(nameof(EnumHost.Layout))
                    {
                        Mode = BindingMode.TwoWay,
                        Converter = new EnumToIndexConverter()
                    });

                Assert.Equal(2, combo.SelectedIndex);

                combo.SelectedIndex = 1;
                Assert.Equal(AnimationExportLayout.VerticalSpriteSheet, host.Layout);
            });
        }

        [Theory]
        [InlineData("CboAnimationLayout")]
        [InlineData("CboAnimationTiming")]
        public void SidebarPanel_EnumCombos_UseEnumToIndexConverter(string comboName)
        {
            var xamlPath = FindRepoFile(Path.Combine("Hexprite", "Views", "SidebarPanel.xaml"));
            var doc = XDocument.Load(xamlPath);
            XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

            var combo = doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == comboName);
            var selectedIndex = (string?)combo.Attribute("SelectedIndex");

            Assert.NotNull(selectedIndex);
            Assert.Contains("EnumToIndexConverter", selectedIndex);
        }

        private static string FindRepoFile(string relativePath)
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, relativePath);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            throw new FileNotFoundException(relativePath);
        }
    }
}
