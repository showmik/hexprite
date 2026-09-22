using System;
using System.Buffers;
using System.Reflection;
using Sentry;
using Serilog;

namespace Hexprite.Services
{
    public sealed class UserFeedbackService : IUserFeedbackService
    {
        private static readonly SearchValues<char> NewLineChars = SearchValues.Create("\r\n");

        public FeedbackSubmitResult SubmitFeedback(UserFeedbackInput input)
        {
            ArgumentNullException.ThrowIfNull(input);

            LoggingService.PrivacyOptions privacy = LoggingService.GetPrivacyOptions();
            using var operation = LoggingService.BeginOperation(
                "UserFeedbackService.SubmitFeedback",
                new
                {
                    category = string.IsNullOrWhiteSpace(input.Category) ? "General" : input.Category.Trim(),
                    includeRecentLogs = input.IncludeRecentLogs,
                    includeContactEmail = input.IncludeContactEmail,
                    telemetryEnabled = privacy.TelemetryEnabled,
                });

            if (!privacy.TelemetryEnabled)
            {
                Log.Warning("Feedback submission blocked because telemetry is disabled in privacy settings.");
                return new FeedbackSubmitResult
                {
                    Success = false,
                    Message = "Telemetry is disabled in privacy settings.",
                };
            }

            try
            {
                string messageBody = LoggingService.SanitizeForTelemetry(input.Message, privacy, allowEmail: false);
                string category = LoggingService.SanitizeForTelemetry(input.Category, privacy, allowEmail: false);
                category = string.IsNullOrWhiteSpace(category) ? "General" : category;
                string title = BuildEventTitle(messageBody);
                string? contactEmail = null;
                if (input.IncludeContactEmail && privacy.AllowContactEmailInTelemetry)
                {
                    string sanitizedEmail = LoggingService.SanitizeForTelemetry(input.ContactEmail, privacy, allowEmail: true);
                    contactEmail = string.IsNullOrWhiteSpace(sanitizedEmail) ? null : sanitizedEmail;
                }

                SentryId eventId = SentrySdk.CaptureMessage(
                    title,
                    scope =>
                    {
                        scope.SetTag("report.type", "feedback");
                        scope.SetTag("report.channel", "in-app");
                        scope.SetTag("feedback.category", category);
                        scope.SetTag("app.version", GetAppVersion());
                        scope.SetExtra("message", messageBody);
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
                    SentryLevel.Info);

                if (eventId == SentryId.Empty)
                {
                    Log.Warning("SentrySdk.CaptureMessage returned SentryId.Empty. Feedback submission may have failed.");
                    return new FeedbackSubmitResult
                    {
                        Success = false,
                        Message = "Unable to submit feedback. The telemetry service may be disabled or offline.",
                    };
                }

                // Also send a corresponding Feedback entry so it shows
                // up on the event's feedback tab in Sentry.
                _ = SentrySdk.CaptureFeedback(
                    string.IsNullOrWhiteSpace(messageBody) ? "User feedback" : messageBody,
                    contactEmail,
                    name: null,
                    associatedEventId: eventId);

                Log.Information("User feedback submitted. EventId={EventId}", eventId.ToString());

                return new FeedbackSubmitResult
                {
                    Success = true,
                    EventId = eventId.ToString(),
                    Message = "Thanks! Your feedback was submitted.",
                };
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "UserFeedbackService.SubmitFeedback");
                return new FeedbackSubmitResult
                {
                    Success = false,
                    Message = $"Unable to submit feedback: {ex.Message}",
                };
            }
        }

        private static string BuildEventTitle(string messageBody)
        {
            if (string.IsNullOrEmpty(messageBody))
            {
                return "User feedback";
            }

            int end = messageBody.AsSpan().IndexOfAny(NewLineChars);
            string firstLine = end >= 0 ? messageBody[..end] : messageBody;
            firstLine = firstLine.Trim();
            if (firstLine.Length > 120)
            {
                return string.Concat(firstLine.AsSpan(0, 117), "...");
            }

            return string.IsNullOrEmpty(firstLine) ? "User feedback" : firstLine;
        }

        private static string GetAppVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }
    }
}
