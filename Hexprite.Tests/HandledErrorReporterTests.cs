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
}
