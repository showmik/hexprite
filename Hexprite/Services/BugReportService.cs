using System;
using System.Reflection;
using Sentry;
using Serilog;

namespace Hexprite.Services
{
    /// <summary>
    /// Service to handle manual bug report submissions through Sentry.
    /// </summary>
    public sealed class BugReportService : IBugReportService
    {
        /// <summary>
        /// Submits a user bug report to Sentry, including optional logs and context.
        /// </summary>
        /// <param name="input">The user-provided bug report data.</param>
        /// <returns>A result indicating success or failure.</returns>
        public BugReportResult SubmitReport(BugReportInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            LoggingService.PrivacyOptions privacy = LoggingService.GetPrivacyOptions();
            using var operation = LoggingService.BeginOperation(
                "BugReportService.SubmitReport",
                new
                {
                    includeRecentLogs = input.IncludeRecentLogs,
                    includeContactEmail = input.IncludeContactEmail,
                    telemetryEnabled = privacy.TelemetryEnabled,
                });

            if (!privacy.TelemetryEnabled)
            {
                Log.Warning("Bug report submission blocked because telemetry is disabled in privacy settings.");
                return new BugReportResult
                {
                    Success = false,
                    Message = "Telemetry is disabled in privacy settings.",
                };
            }

            try
            {
                string summary = LoggingService.SanitizeForTelemetry(input.Summary, privacy, allowEmail: false);
                string steps = LoggingService.SanitizeForTelemetry(input.StepsToReproduce, privacy, allowEmail: false);
                string expected = LoggingService.SanitizeForTelemetry(input.ExpectedBehavior, privacy, allowEmail: false);
                string actual = LoggingService.SanitizeForTelemetry(input.ActualBehavior, privacy, allowEmail: false);
                string? contactEmail = null;
                if (input.IncludeContactEmail && privacy.AllowContactEmailInTelemetry)
                {
                    string sanitizedEmail = LoggingService.SanitizeForTelemetry(input.ContactEmail, privacy, allowEmail: true);
                    contactEmail = string.IsNullOrWhiteSpace(sanitizedEmail) ? null : sanitizedEmail;
                }

                SentryId eventId = SentrySdk.CaptureMessage(
                    string.IsNullOrWhiteSpace(summary) ? "Manual bug report submitted" : summary,
                    scope =>
                    {
                        scope.SetTag("report.type", "manual");
                        scope.SetTag("report.channel", "in-app");
                        scope.SetTag("app.version", GetAppVersion());
                        scope.SetExtra("stepsToReproduce", steps);
                        scope.SetExtra("expectedBehavior", expected);
                        scope.SetExtra("actualBehavior", actual);
                        scope.SetTag("report.contact_email_included", contactEmail is null ? "false" : "true");
                        if (contactEmail is not null)
                        {
                            scope.SetExtra("contactEmail", contactEmail);
                            scope.User = new SentryUser { Email = contactEmail };
                        }

                        if (input.IncludeRecentLogs && privacy.AllowLogAttachments)
                        {
                            LoggingService.AttachRecentLogFilesToScope(scope);
                        }
                    },
                    SentryLevel.Error);

                if (eventId == SentryId.Empty)
                {
                    Log.Warning("SentrySdk.CaptureMessage returned SentryId.Empty. Bug report submission may have failed.");
                    return new BugReportResult
                    {
                        Success = false,
                        Message = "Unable to submit bug report. The telemetry service may be disabled or offline.",
                    };
                }

                // Also send to Sentry's Feedback feature so the report
                // appears on the associated event's feedback tab.
                _ = SentrySdk.CaptureFeedback(
                    BuildBugReportComments(summary, steps, expected, actual),
                    contactEmail,
                    name: null,
                    associatedEventId: eventId);

                Log.Information("Manual bug report submitted. EventId={EventId}", eventId.ToString());

                return new BugReportResult
                {
                    Success = true,
                    EventId = eventId.ToString(),
                    Message = "Thanks! Your bug report was submitted.",
                };
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "BugReportService.SubmitReport");
                return new BugReportResult
                {
                    Success = false,
                    Message = $"Unable to submit bug report: {ex.Message}",
                };
            }
        }

        private static string BuildBugReportComments(string summary, string steps, string expected, string actual)
        {
            // This ends up in Sentry's "User Feedback" comments field.
            // Keep it readable but compact.
            summary = string.IsNullOrWhiteSpace(summary) ? "(no summary)" : summary.Trim();
            steps = string.IsNullOrWhiteSpace(steps) ? "(not provided)" : steps.Trim();
            expected = string.IsNullOrWhiteSpace(expected) ? "(not provided)" : expected.Trim();
            actual = string.IsNullOrWhiteSpace(actual) ? "(not provided)" : actual.Trim();

            return
                $"Summary:\n{summary}\n\n" +
                $"Steps to reproduce:\n{steps}\n\n" +
                $"Expected behavior:\n{expected}\n\n" +
                $"Actual behavior:\n{actual}";
        }

        private static string GetAppVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }
}
