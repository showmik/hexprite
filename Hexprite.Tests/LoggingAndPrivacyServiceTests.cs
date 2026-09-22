using System;
using System.IO;
using System.Text.Json;
using Hexprite.Services;
using Xunit;

namespace Hexprite.Tests
{
    [Trait("Category", "Unit")]
    public class LoggingAndPrivacyServiceTests
    {
        [Fact]
        public void PrivacyOptions_DefaultValues_AreCorrect()
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            Assert.True(options.TelemetryEnabled);
            Assert.False(options.AttachLogsByDefault);
            Assert.True(options.AllowLogAttachments);
            Assert.True(options.RedactPersonalData);
            Assert.False(options.ShareContactEmailByDefault);
            Assert.True(options.AllowContactEmailInTelemetry);
        }

        [Fact]
        public void PrivacyOptions_MergeWith_OverridesPropertiesCorrectly()
        {
            var defaults = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            var overrides = new LoggingService.PrivacyOptions(
                telemetryEnabled: false,
                attachLogsByDefault: true,
                allowLogAttachments: false,
                redactPersonalData: false,
                shareContactEmailByDefault: true,
                allowContactEmailInTelemetry: false);

            var merged = defaults.MergeWith(overrides);

            Assert.False(merged.TelemetryEnabled);
            Assert.True(merged.AttachLogsByDefault);
            Assert.False(merged.AllowLogAttachments);
            Assert.False(merged.RedactPersonalData);
            Assert.True(merged.ShareContactEmailByDefault);
            Assert.False(merged.AllowContactEmailInTelemetry);
        }

        [Fact]
        public void PrivacyOptions_SerializationRoundTrip_PreservesAllFields()
        {
            var original = new LoggingService.PrivacyOptions(
                telemetryEnabled: false,
                attachLogsByDefault: true,
                allowLogAttachments: false,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string json = JsonSerializer.Serialize(original);
            var deserialized = JsonSerializer.Deserialize<LoggingService.PrivacyOptions>(json);

            Assert.NotNull(deserialized);
            if (deserialized is not null)
            {
                Assert.Equal(original.TelemetryEnabled, deserialized.TelemetryEnabled);
                Assert.Equal(original.AttachLogsByDefault, deserialized.AttachLogsByDefault);
                Assert.Equal(original.AllowLogAttachments, deserialized.AllowLogAttachments);
                Assert.Equal(original.RedactPersonalData, deserialized.RedactPersonalData);
                Assert.Equal(original.ShareContactEmailByDefault, deserialized.ShareContactEmailByDefault);
                Assert.Equal(original.AllowContactEmailInTelemetry, deserialized.AllowContactEmailInTelemetry);
            }
        }

        [Theory]
        [InlineData(@"C:\Users\Alice\Documents\Secret\sprite.hexp", "[redacted-path]")]
        [InlineData(@"D:\Projects\Artwork\icon.png", "[redacted-path]")]
        [InlineData(@"C:/Users/Bob/Desktop/game.hexp", "[redacted-path]")]
        [InlineData(@"file:///C:/Users/Charlie/AppData/Local/Hexprite/Logs/log.txt", "[redacted-path]")]
        [InlineData(@"\\fileserver\shares\alice\secrets.json", "[redacted-path]")]
        [InlineData(@"/home/alice/project/sprite.hexp", "[redacted-path]")]
        [InlineData(@"/Users/bob/Desktop/font.hexfont", "[redacted-path]")]
        [InlineData(@"/tmp/crash-dump.log", "[redacted-path]")]
        public void SanitizeForTelemetry_RedactsSensitivePaths(string input, string expected)
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string result = LoggingService.SanitizeForTelemetry(input, options, allowEmail: false);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Crash connected to 192.168.1.50 on port 80", "Crash connected to [redacted-ip] on port 80")]
        [InlineData("Host 10.0.0.1 failed to respond", "Host [redacted-ip] failed to respond")]
        [InlineData("IPv6 fe80::1 failed handshake", "IPv6 [redacted-ip] failed handshake")]
        public void SanitizeForTelemetry_RedactsIpAddresses(string input, string expected)
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string result = LoggingService.SanitizeForTelemetry(input, options, allowEmail: false);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", "Authorization: Bearer [redacted-token]")]
        [InlineData("GitHub PAT ghp_1234567890abcdefghijklmnopqrstuvwxyz", "GitHub PAT [redacted-token]")]
        [InlineData("GitHub token: ghp_1234567890abcdefghijklmnopqrstuvwxyz", "GitHub token: [redacted-secret]")]
        [InlineData("https://api.example.com?token=secret123&apiKey=xyz987", "https://api.example.com?token=[redacted-secret]&apiKey=[redacted-secret]")]
        [InlineData("user config: password=SuperSecretPassword123!", "user config: password=[redacted-secret]")]
        public void SanitizeForTelemetry_RedactsSecretsAndTokens(string input, string expected)
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string result = LoggingService.SanitizeForTelemetry(input, options, allowEmail: false);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void SanitizeForTelemetry_RedactsCurrentUserAndUserFolder()
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string currentUser = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(currentUser) && currentUser.Length > 1)
            {
                string textWithUser = $"Crash in thread for user {currentUser} during export.";
                string sanitized = LoggingService.SanitizeForTelemetry(textWithUser, options, allowEmail: false);
                Assert.DoesNotContain(currentUser, sanitized);
                Assert.Contains("[redacted-user]", sanitized);
            }
        }

        [Fact]
        public void SanitizeForTelemetry_RedactsEmails_WhenNotAllowed()
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string input = "User test.developer@example.com reported an issue with C:\\Test\\app.log";
            string sanitized = LoggingService.SanitizeForTelemetry(input, options, allowEmail: false);

            Assert.DoesNotContain("test.developer@example.com", sanitized);
            Assert.Contains("[redacted-email]", sanitized);
            Assert.Contains("[redacted-path]", sanitized);
        }

        [Fact]
        public void SanitizeForTelemetry_PreservesEmail_WhenAllowed()
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: true,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string input = "test.developer@example.com";
            string sanitized = LoggingService.SanitizeForTelemetry(input, options, allowEmail: true);

            Assert.Equal("test.developer@example.com", sanitized);
        }

        [Fact]
        public void SanitizeForTelemetry_DoesNotRedact_WhenRedactPersonalDataIsFalse()
        {
            var options = new LoggingService.PrivacyOptions(
                telemetryEnabled: true,
                attachLogsByDefault: false,
                allowLogAttachments: true,
                redactPersonalData: false,
                shareContactEmailByDefault: false,
                allowContactEmailInTelemetry: true);

            string input = "User test@example.com with file C:\\Path\\file.hexp";
            string sanitized = LoggingService.SanitizeForTelemetry(input, options, allowEmail: false);

            Assert.Equal(input, sanitized);
        }

        [Fact]
        public void BugReportService_SubmitReport_NullInputThrows()
        {
            var service = new BugReportService();
            Assert.Throws<ArgumentNullException>(() => service.SubmitReport(null!));
        }

        [Fact]
        public void UserFeedbackService_SubmitFeedback_NullInputThrows()
        {
            var service = new UserFeedbackService();
            Assert.Throws<ArgumentNullException>(() => service.SubmitFeedback(null!));
        }

        [Fact]
        public void SentryCrashFlush_GetConfiguredTimeout_ClampsToBounds()
        {
            TimeSpan timeout = SentryCrashFlush.GetConfiguredTimeout();
            Assert.InRange(timeout.TotalSeconds, 1.0, 30.0);
        }

        [Fact]
        public void SentryCrashFlush_TryFlushPendingEvents_DoesNotThrow()
        {
            var ex = Record.Exception(() => SentryCrashFlush.TryFlushPendingEvents(TimeSpan.FromMilliseconds(10)));
            Assert.Null(ex);
        }

        [Fact]
        public void LoggingService_BeginOperation_CompletesWithoutException()
        {
            using var op = LoggingService.BeginOperation("TestOperation", new { key = "val" });
            Assert.NotNull(op);
        }

        [Fact]
        public void LoggingService_BeginOperation_MarkFailed_CompletesCleanly()
        {
            using var op = LoggingService.BeginOperation("TestOperationFailed", new { key = "val" });
            op.MarkFailed(new InvalidOperationException("Simulated error"));
            Assert.NotNull(op);
        }

        [Fact]
        public void LoggingService_BeginOperation_WithSlowThreshold_CompletesCleanly()
        {
            using var op = LoggingService.BeginOperation("TestSlowOperation", new { key = "val" }, slowThresholdMs: 10);
            System.Threading.Thread.Sleep(15);
            Assert.NotNull(op);
        }

        [Fact]
        public void LoggingService_PushContext_DisposesWithoutException()
        {
            using var ctx = LoggingService.PushContext("TestContext", new { file = "test.png" });
            Assert.NotNull(ctx);
        }

        [Fact]
        public void LoggingService_GetBugReportingMaxAttachedLogs_ReturnsClampedValue()
        {
            int maxLogs = LoggingService.GetBugReportingMaxAttachedLogs();
            Assert.InRange(maxLogs, 1, 10);
        }
    }
}
