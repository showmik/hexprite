using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
    public class ExportSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new ExportSettings();

        Assert.Equal(ExportFormat.AdafruitGfx, settings.Format);
        Assert.Equal(ExportLayerMode.CompositeVisible, settings.LayerMode);
        Assert.Equal("mySprite", settings.SpriteName);
        Assert.True(settings.IncludeUsageComment);
        Assert.True(settings.IncludeDimensionConstants);
        Assert.True(settings.UseCommaSeparator);
        Assert.Equal(0, settings.BytesPerLine);
        Assert.True(settings.UppercaseHex);
        Assert.False(settings.IncludeRowComments);
        Assert.False(settings.IncludeArraySize);
    }

    [Theory]
    [InlineData(ExportFormat.AdafruitGfx)]
    [InlineData(ExportFormat.U8g2DrawBitmap)]
    [InlineData(ExportFormat.U8g2DrawXBM)]
    [InlineData(ExportFormat.PlainCArray)]
    [InlineData(ExportFormat.MicroPython)]
    [InlineData(ExportFormat.RawHex)]
    [InlineData(ExportFormat.RawBinary)]
    public void Format_CanBeSet_ToAnyValue(ExportFormat format)
    {
        var settings = new ExportSettings { Format = format };
        Assert.Equal(format, settings.Format);
    }

    [Theory]
    [InlineData(ExportLayerMode.CompositeVisible)]
    [InlineData(ExportLayerMode.ActiveLayerOnly)]
    [InlineData(ExportLayerMode.PerLayer)]
    public void LayerMode_CanBeSet_ToAnyValue(ExportLayerMode mode)
    {
        var settings = new ExportSettings { LayerMode = mode };
        Assert.Equal(mode, settings.LayerMode);
    }

    [Fact]
    public void SpriteName_CanBeModified()
    {
        var settings = new ExportSettings { SpriteName = "CustomName" };
        Assert.Equal("CustomName", settings.SpriteName);
    }

    [Fact]
    public void BytesPerLine_CanBePositive()
    {
        var settings = new ExportSettings { BytesPerLine = 8 };
        Assert.Equal(8, settings.BytesPerLine);
    }

    [Fact]
    public void JsonSerialization_RoundTrip_PreservesValues()
    {
        var original = new ExportSettings
        {
            Format = ExportFormat.U8g2DrawXBM,
            LayerMode = ExportLayerMode.ActiveLayerOnly,
            SpriteName = "TestSprite",
            IncludeUsageComment = false,
            IncludeDimensionConstants = false,
            UseCommaSeparator = false,
            BytesPerLine = 4,
            UppercaseHex = false,
            IncludeRowComments = true,
            IncludeArraySize = true
        };

        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ExportSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.Format, restored.Format);
        Assert.Equal(original.LayerMode, restored.LayerMode);
        Assert.Equal(original.SpriteName, restored.SpriteName);
        Assert.Equal(original.IncludeUsageComment, restored.IncludeUsageComment);
        Assert.Equal(original.IncludeDimensionConstants, restored.IncludeDimensionConstants);
        Assert.Equal(original.UseCommaSeparator, restored.UseCommaSeparator);
        Assert.Equal(original.BytesPerLine, restored.BytesPerLine);
        Assert.Equal(original.UppercaseHex, restored.UppercaseHex);
        Assert.Equal(original.IncludeRowComments, restored.IncludeRowComments);
        Assert.Equal(original.IncludeArraySize, restored.IncludeArraySize);
    }
}
