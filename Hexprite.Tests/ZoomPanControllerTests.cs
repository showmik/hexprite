using Hexprite.Controllers;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ZoomPanControllerTests
    {
        [Fact]
        public void SnapToTick_ZeroTickFrequency_ClampsWithoutDivisionByZero()
        {
            double result = ZoomPanController.SnapToTick(
                value: 5.5, direction: 1, tick: 0, current: 1.0, min: 1.0, max: 10.0);

            Assert.Equal(5.5, result);
        }

        [Fact]
        public void SnapToTick_SnapsToNearestStep()
        {
            double result = ZoomPanController.SnapToTick(
                value: 1.18, direction: 1, tick: 0.25, current: 1.0, min: 0.25, max: 10.0);

            Assert.Equal(1.25, result);
        }

        [Fact]
        public void SnapToTick_WhenUnchanged_AdvancesInDirection()
        {
            // value snaps to 1.0, which equals current (1.0). Direction > 0 forces next tick (1.25)
            double result = ZoomPanController.SnapToTick(
                value: 1.02, direction: 1, tick: 0.25, current: 1.0, min: 0.25, max: 10.0);

            Assert.Equal(1.25, result);
        }

        [Fact]
        public void SnapToTick_RespectsMinAndMaxBounds()
        {
            double resultMin = ZoomPanController.SnapToTick(
                value: -5.0, direction: -1, tick: 0.5, current: 1.0, min: 0.5, max: 10.0);
            Assert.Equal(0.5, resultMin);

            double resultMax = ZoomPanController.SnapToTick(
                value: 100.0, direction: 1, tick: 0.5, current: 1.0, min: 0.5, max: 10.0);
            Assert.Equal(10.0, resultMax);
        }
    }
}
