using System.Collections.Generic;
using Hexprite.Core;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class ExportSequenceTests
    {
        [Fact]
        public void GetExportFrameSequence_Forward_ReturnsSequentialIndices()
        {
            var sequence = ExportService.GetExportFrameSequence(4, PlaybackDirection.Forward);
            Assert.Equal(new List<int> { 0, 1, 2, 3 }, sequence);
        }

        [Fact]
        public void GetExportFrameSequence_Reverse_ReturnsReversedIndices()
        {
            var sequence = ExportService.GetExportFrameSequence(4, PlaybackDirection.Reverse);
            Assert.Equal(new List<int> { 3, 2, 1, 0 }, sequence);
        }

        [Fact]
        public void GetExportFrameSequence_PingPong_ReturnsPingPongIndices()
        {
            var sequence = ExportService.GetExportFrameSequence(4, PlaybackDirection.PingPong);
            // Ping-pong for 4 frames should be: 0, 1, 2, 3, 2, 1
            Assert.Equal(new List<int> { 0, 1, 2, 3, 2, 1 }, sequence);
        }

        [Fact]
        public void GetExportFrameSequence_PingPong_WithTwoFrames_ReturnsOnlyTwoFrames()
        {
            // Ping-pong for 2 frames doesn't make sense to reverse inner, so it just returns 0, 1
            var sequence = ExportService.GetExportFrameSequence(2, PlaybackDirection.PingPong);
            Assert.Equal(new List<int> { 0, 1 }, sequence);
        }

        [Fact]
        public void GetExportFrameSequence_EmptyOrOne_ReturnsCorrectly()
        {
            Assert.Empty(ExportService.GetExportFrameSequence(0, PlaybackDirection.Forward));
            
            var sequence = ExportService.GetExportFrameSequence(1, PlaybackDirection.PingPong);
            Assert.Equal(new List<int> { 0 }, sequence);
        }
    }
}
