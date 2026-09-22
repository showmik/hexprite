using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using Serilog;

namespace Hexprite.Services
{
    /// <summary>
    /// Structured logging for caught exceptions. Events at Error and above are forwarded to Sentry
    /// when the Serilog Sentry sink is configured (see <see cref="LoggingService"/>).
    /// Includes thread-safe error throttling to suppress flood loops during rapid events.
    /// </summary>
    public static class HandledErrorReporter
    {
        private static readonly Lock Sync = new();
        private static readonly Dictionary<string, ThrottleEntry> ThrottleCache = [];

        /// <summary>
        /// Sliding window duration for suppressing duplicate handled errors. Default is 5 seconds.
        /// </summary>
        public static TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromSeconds(5);

        private sealed class ThrottleEntry
        {
            public DateTime LastLoggedUtc { get; set; }
            public int SuppressedCount { get; set; }
        }

        /// <summary>
        /// Resets the throttle cache. Intended for test isolation.
        /// </summary>
        public static void ResetThrottleCache()
        {
            lock (Sync)
            {
                ThrottleCache.Clear();
            }
        }

        internal static int GetThrottleCacheCount()
        {
            lock (Sync)
            {
                return ThrottleCache.Count;
            }
        }

        public static void Error(
            Exception ex,
            string operation,
            object? context = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            if (ShouldThrottle(operation, callerMemberName, ex, out int suppressedCount))
            {
                return;
            }

            if (suppressedCount > 0)
            {
                Log.Warning(
                    "Suppressed {SuppressedCount} duplicate handled error(s) for operation '{Operation}' in the last {WindowSeconds}s",
                    suppressedCount, operation, ThrottleWindow.TotalSeconds);
            }

            if (context is null)
            {
                Log.Error(ex, "Handled error: {HandledOperation} Caller={CallerMember}", operation, callerMemberName);
            }
            else
            {
                Log.Error(ex, "Handled error: {HandledOperation} Caller={CallerMember} {@HandledContext}", operation, callerMemberName, context);
            }
        }

        public static void Warning(
            Exception ex,
            string operation,
            object? context = null,
            [CallerMemberName] string? callerMemberName = null)
        {
            if (ShouldThrottle(operation, callerMemberName, ex, out int suppressedCount))
            {
                return;
            }

            if (suppressedCount > 0)
            {
                Log.Warning(
                    "Suppressed {SuppressedCount} duplicate handled warning(s) for operation '{Operation}' in the last {WindowSeconds}s",
                    suppressedCount, operation, ThrottleWindow.TotalSeconds);
            }

            if (context is null)
            {
                Log.Warning(ex, "Handled warning: {HandledOperation} Caller={CallerMember}", operation, callerMemberName);
            }
            else
            {
                Log.Warning(ex, "Handled warning: {HandledOperation} Caller={CallerMember} {@HandledContext}", operation, callerMemberName, context);
            }
        }

        private const int MaxCacheEntries = 1000;

        private static bool ShouldThrottle(string operation, string? caller, Exception? ex, out int suppressedCount)
        {
            suppressedCount = 0;
            string exTypeName = ex?.GetType().FullName ?? "UnknownException";
            string key = $"{operation}|{caller}|{exTypeName}";
            DateTime now = DateTime.UtcNow;

            lock (Sync)
            {
                if (ThrottleCache.TryGetValue(key, out var entry))
                {
                    if (now - entry.LastLoggedUtc < ThrottleWindow)
                    {
                        entry.SuppressedCount++;
                        return true;
                    }

                    suppressedCount = entry.SuppressedCount;
                    entry.LastLoggedUtc = now;
                    entry.SuppressedCount = 0;
                    return false;
                }

                if (ThrottleCache.Count >= MaxCacheEntries)
                {
                    PruneExpiredEntries(now);
                }

                ThrottleCache[key] = new ThrottleEntry
                {
                    LastLoggedUtc = now,
                    SuppressedCount = 0
                };
                return false;
            }
        }

        private static void PruneExpiredEntries(DateTime now)
        {
            List<string>? expiredKeys = null;
            foreach (var (k, v) in ThrottleCache)
            {
                if (now - v.LastLoggedUtc >= ThrottleWindow)
                {
                    expiredKeys ??= [];
                    expiredKeys.Add(k);
                }
            }

            if (expiredKeys != null)
            {
                foreach (string k in expiredKeys)
                {
                    ThrottleCache.Remove(k);
                }
            }
        }
    }
}
