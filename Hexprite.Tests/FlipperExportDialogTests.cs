using System;
using Hexprite.Core;
using Hexprite.Services;
using Hexprite.Views;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class FlipperExportDialogTests
    {
        [Fact]
        public void FlipperExportDialog_InitializesWithoutNullReferenceException()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                var exportService = new FlipperExportService();

                var dlg = new FlipperExportDialog(exportService, sprite);

                Assert.NotNull(dlg);
                Assert.Equal("MyAnimation", dlg.TxtAnimationName.Text);
                Assert.Equal("5", dlg.TxtFrameRate.Text);
            });
        }

        [Fact]
        public void FlipperExportDialog_WithCycle_InitializesAndToggles()
        {
            WpfTestHelper.RunOnSta(() =>
            {
                var sprite = new SpriteState(128, 64);
                sprite.FlipperCycle = new FlipperAnimationCycle
                {
                    FramesOrder = [0],
                    PassiveFrameCount = 1,
                    ActiveFrameCount = 0
                };
                var exportService = new FlipperExportService();

                var dlg = new FlipperExportDialog(exportService, sprite);

                Assert.NotNull(dlg);
                Assert.True(dlg.ChkPreserveCycle.IsChecked);
                Assert.False(dlg.TxtPassiveFrames.IsEnabled);
            });
        }
    }
}
