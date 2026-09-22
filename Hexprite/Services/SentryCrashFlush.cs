using System;
using Microsoft.Extensions.Configuration;
using Sentry;

namespace Hexprite.Services
{
    /// <summary>
    /// Bounded flush so crash events upload before the process exits (terminating unhandled exceptions).
    /// </summary>
    public static class SentryCrashFlush
    {
        private const string AppSettingsFile = "appsettings.json";

        /// <summary>
        /// Flushes queued Sentry events up to <paramref name="timeout"/>; no-ops if the SDK is disabled.
        /// Swallows errors so flush never masks the original crash.
        /// </summary>
        public static void TryFlushPendingEvents(TimeSpan? timeout = null)
        {
            if (!SentrySdk.IsEnabled || !LoggingService.GetPrivacyOptions().TelemetryEnabled)
            {
                return;
            }

            TimeSpan wait = timeout ?? GetConfiguredTimeout();
            if (wait <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                SentrySdk.Flush(wait);
            }
            catch
            {
                // Ignore — crash path must not throw from telemetry.
            }
        }

        /// <summary>
        /// Config: Sentry:CrashFlushTimeoutSeconds (1–30). Override: HEXEL_Sentry__CrashFlushTimeoutSeconds
        /// </summary>
        public static TimeSpan GetConfiguredTimeout()
        {
            int seconds = 3;
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile(AppSettingsFile, optional: true, reloadOnChange: false)
                    .AddUserSecrets(typeof(SentryCrashFlush).Assembly, optional: true)
                    .AddEnvironmentVariables(prefix: "HEXEL_")
                    .Build();

                if (int.TryParse(configuration["Sentry:CrashFlushTimeoutSeconds"], out int parsed))
                {
                    seconds = parsed;
                }
            }
            catch
            {
                // Fallback to default timeout if config reading fails during crash
            }

            seconds = Math.Clamp(seconds, 1, 30);
            return TimeSpan.FromSeconds(seconds);
        }
    }
}
