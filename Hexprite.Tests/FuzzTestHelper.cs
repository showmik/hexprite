using System;

namespace Hexprite.Tests
{
    /// <summary>
    /// Provides iteration counts and configuration for chaos fuzzers and property tests.
    /// Uses fast, statistically thorough defaults (~300-500 iterations) during local development,
    /// and allows deep scaling (10,000+ iterations) on CI/nightly runs when HEXPRITE_DEEP_FUZZ is set.
    /// </summary>
    public static class FuzzTestHelper
    {
        public static int GetIterationCount(int defaultFastCount = 500, int deepCount = 10000)
        {
            var deep = Environment.GetEnvironmentVariable("HEXPRITE_DEEP_FUZZ");
            return string.Equals(deep, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(deep, "true", StringComparison.OrdinalIgnoreCase)
                ? deepCount
                : defaultFastCount;
        }
    }
}
