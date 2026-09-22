using Hexprite.Core;
using Xunit;

namespace Hexprite.Tests;

/// <summary>
/// Tests for the ToolSettings model class.
/// </summary>
[Trait("Category", "Unit")]
    public class ToolSettingsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new ToolSettings();

        Assert.Equal(ToolMode.Pencil, settings.CurrentTool);
        Assert.Equal(1, settings.BrushSize);
        Assert.Equal(BrushShape.Circle, settings.BrushShape);
        Assert.Equal(0, settings.BrushAngle);
        Assert.False(settings.IsPixelPerfectEnabled);
    }

    [Theory]
    [InlineData(ToolMode.Pencil, 1, true)]   // Pixel-perfect available
    [InlineData(ToolMode.Pencil, 2, true)]   // Brush size > 1
    [InlineData(ToolMode.Eraser, 5, true)]   // Eraser tool
    [InlineData(ToolMode.Line, 1, false)]    // Wrong tool
    [InlineData(ToolMode.Fill, 1, false)]    // Wrong tool
    public void IsPixelPerfectAvailable_ReturnsExpectedValue(ToolMode tool, int brushSize, bool expected)
    {
        var settings = new ToolSettings
        {
            CurrentTool = tool,
            BrushSize = brushSize
        };

        Assert.Equal(expected, settings.IsPixelPerfectAvailable);
    }

    [Fact]
    public void BrushSize_ClampsToMinimum()
    {
        var settings = new ToolSettings { BrushSize = 0 };
        Assert.Equal(1, settings.BrushSize);
    }

    [Fact]
    public void BrushSize_ClampsToMaximum()
    {
        var settings = new ToolSettings { BrushSize = 100 };
        Assert.Equal(64, settings.BrushSize);
    }

    [Theory]
    [InlineData(-90, 270)]   // Negative angle
    [InlineData(360, 0)]     // Full rotation
    [InlineData(450, 90)]    // Over 360
    [InlineData(-450, 270)]  // Negative over 360
    public void BrushAngle_NormalizesTo0To359(int input, int expected)
    {
        var settings = new ToolSettings { BrushAngle = input };
        Assert.Equal(expected, settings.BrushAngle);
    }

    [Fact]
    public void PropertyChanges_RaisePropertyChanged()
    {
        var settings = new ToolSettings();
        string? changedProperty = null;
        settings.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        settings.CurrentTool = ToolMode.Line;
        Assert.Equal(nameof(ToolSettings.CurrentTool), changedProperty);
    }

    [Fact]
    public void BrushSizeChange_RaisesIsPixelPerfectAvailableChanged()
    {
        var settings = new ToolSettings { CurrentTool = ToolMode.Pencil, BrushSize = 1 };
        var propertiesChanged = new List<string>();
        settings.PropertyChanged += (_, e) => propertiesChanged.Add(e.PropertyName!);

        settings.BrushSize = 2;

        Assert.Contains(nameof(ToolSettings.BrushSize), propertiesChanged);
        Assert.Contains(nameof(ToolSettings.IsPixelPerfectAvailable), propertiesChanged);
    }

    [Fact]
    public void CurrentToolChange_RaisesIsPixelPerfectAvailableChanged()
    {
        var settings = new ToolSettings { CurrentTool = ToolMode.Pencil, BrushSize = 1 };
        var propertiesChanged = new List<string>();
        settings.PropertyChanged += (_, e) => propertiesChanged.Add(e.PropertyName!);

        settings.CurrentTool = ToolMode.Line;

        Assert.Contains(nameof(ToolSettings.CurrentTool), propertiesChanged);
        Assert.Contains(nameof(ToolSettings.IsPixelPerfectAvailable), propertiesChanged);
    }
}
