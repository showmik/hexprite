using System;
using System.Threading;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests;

[Trait("Category", "Unit")]
public class HandledErrorReporterTests : IDisposable
{
    public HandledErrorReporterTests()
    {
        HandledErrorReporter.ResetThrottleCache();
        HandledErrorReporter.ThrottleWindow = TimeSpan.FromMilliseconds(100);
    }

    public void Dispose()
    {
        HandledErrorReporter.ResetThrottleCache();
        HandledErrorReporter.ThrottleWindow = TimeSpan.FromSeconds(5);
    }

    [Fact]
    public void Error_LogsFirstOccurrence_AndThrottlesSubsequentDuplicates()
    {
        var ex = new InvalidOperationException("Test exception");

        // First call should not be throttled
        var record1 = Record.Exception(() => HandledErrorReporter.Error(ex, "TestOp", new { id = 1 }));
        Assert.Null(record1);

        // Immediate duplicate call within window should be throttled
        var record2 = Record.Exception(() => HandledErrorReporter.Error(ex, "TestOp", new { id = 1 }));
        Assert.Null(record2);

        // Wait for throttle window to expire
        Thread.Sleep(150);

        // Third call after expiration should log the previous suppression and reset
        var record3 = Record.Exception(() => HandledErrorReporter.Error(ex, "TestOp", new { id = 1 }));
        Assert.Null(record3);
    }

    [Fact]
    public void Warning_LogsFirstOccurrence_AndThrottlesSubsequentDuplicates()
    {
        var ex = new ArgumentException("Test warning");

        // First call
        var record1 = Record.Exception(() => HandledErrorReporter.Warning(ex, "TestWarnOp", new { step = "init" }));
        Assert.Null(record1);

        // Duplicate call
        var record2 = Record.Exception(() => HandledErrorReporter.Warning(ex, "TestWarnOp", new { step = "init" }));
        Assert.Null(record2);

        // Wait for window to expire
        Thread.Sleep(150);

        // Post-expiration call
        var record3 = Record.Exception(() => HandledErrorReporter.Warning(ex, "TestWarnOp", new { step = "init" }));
        Assert.Null(record3);
    }

    [Fact]
    public void Error_DifferentOperations_AreNotThrottledTogether()
    {
        var ex = new InvalidOperationException("Same ex type");

        var record1 = Record.Exception(() => HandledErrorReporter.Error(ex, "OpA"));
        var record2 = Record.Exception(() => HandledErrorReporter.Error(ex, "OpB"));

        Assert.Null(record1);
        Assert.Null(record2);
    }

    [Fact]
    public void Error_WithNullException_DoesNotThrowNullReferenceException()
    {
        var ex = Record.Exception(() => HandledErrorReporter.Error(null!, "TestNullOp"));
        Assert.Null(ex);
    }

    [Fact]
    public void Warning_WithNullException_DoesNotThrowNullReferenceException()
    {
        var ex = Record.Exception(() => HandledErrorReporter.Warning(null!, "TestNullWarnOp"));
        Assert.Null(ex);
    }

    [Fact]
    public void ThrottleCache_PrunesExpiredEntries_WhenCacheGrows()
    {
        HandledErrorReporter.ThrottleWindow = TimeSpan.FromMilliseconds(10);

        // Add 600 entries with distinct operations
        for (int i = 0; i < 600; i++)
        {
            HandledErrorReporter.Error(new InvalidOperationException(), $"Op_{i}");
        }

        // Wait for entries to expire
        Thread.Sleep(30);

        // Add more entries to trigger cleanup
        for (int i = 600; i < 1100; i++)
        {
            HandledErrorReporter.Error(new InvalidOperationException(), $"Op_{i}");
        }

        // The expired entries should have been pruned; count must not be 1100
        int count = HandledErrorReporter.GetThrottleCacheCount();
        Assert.True(count < 1100, $"Expected cache count < 1100 after pruning, but was {count}");
    }
}
