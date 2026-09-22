using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Serilog.Context;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using Sentry;
using Hexprite.Core;

namespace Hexprite.Services
{
    public static class LoggingService
    {
        private const string DefaultAppName = "Hexprite";
        private const string AppSettingsFile = "appsettings.json";
        private static readonly string UserSettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hexprite");
        private static readonly string UserPrivacySettingsFile =
            Path.Combine(UserSettingsDirectory, "privacy-settings.json");
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
        private static string? _currentSessionId;

        public static void Initialize()
        {
            string appName = GetAppName();
            IConfiguration configuration = BuildConfiguration();
            string appNameFromConfig = configuration["Logging:AppName"] ?? appName;
            string logDirectory = BuildLogDirectory(appNameFromConfig);

            try
            {
                Directory.CreateDirectory(logDirectory);
                ConfigureSerilogSelfDiagnostics(logDirectory);

                string? sentryDsn = configuration["Sentry:Dsn"];
                string sentryEnvironment = configuration["Sentry:Environment"] ?? "beta";
                bool sentryAutoSessionTracking = ParseBool(configuration["Sentry:AutoSessionTracking"], fallback: true);

                LogEventLevel minimumLevel = ParseLogLevel(configuration["Logging:MinimumLevel"], LogEventLevel.Information);
                LogEventLevel sentryBreadcrumbLevel = ParseLogLevel(configuration["Sentry:MinimumBreadcrumbLevel"], LogEventLevel.Information);
                LogEventLevel sentryEventLevel = ParseLogLevel(configuration["Sentry:MinimumEventLevel"], LogEventLevel.Error);

                int retainedFileCount = ParseInt(configuration["Logging:RetainedFileCountLimit"], 14);
                int fileSizeLimitMb = ParseInt(configuration["Logging:FileSizeLimitMB"], 10);

                string sessionId = Guid.NewGuid().ToString("N");
                _currentSessionId = sessionId;

                var loggerConfiguration = new LoggerConfiguration()
                    .MinimumLevel.Is(minimumLevel)
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .MinimumLevel.Override("System", LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithThreadId()
                    .Enrich.WithProperty("AppName", appNameFromConfig)
                    .Enrich.WithProperty("AppVersion", GetAppVersion())
                    .Enrich.WithProperty("Environment", sentryEnvironment)
                    .Enrich.WithProperty("SessionId", sessionId)
                    .Enrich.WithProperty("ProcessId", Environment.ProcessId)
                    .WriteTo.File(
                        path: Path.Combine(logDirectory, "log-.txt"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: retainedFileCount,
                        fileSizeLimitBytes: fileSizeLimitMb * 1024 * 1024,
                        rollOnFileSizeLimit: true,
                        shared: true,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] (Thread:{ThreadId} Process:{ProcessId} Session:{SessionId}) {Message:lj}{NewLine}{Exception}",
                        formatProvider: CultureInfo.InvariantCulture);

                // Serilog's Sentry sink initializes its own SDK; it does not pick up a separate SentrySdk.Init.
                // DSN and core options must be set on the sink's options or GetDsn() throws.
                if (!string.IsNullOrWhiteSpace(sentryDsn))
                {
                    loggerConfiguration = loggerConfiguration.WriteTo.Sentry(options =>
                    {
                        options.Dsn = sentryDsn;
                        options.Environment = sentryEnvironment;
                        options.Release = $"{appNameFromConfig}@{GetAppVersion()}";
                        options.AutoSessionTracking = sentryAutoSessionTracking;
                        options.AttachStacktrace = true;
                        options.SendDefaultPii = false;
                        options.MinimumBreadcrumbLevel = sentryBreadcrumbLevel;
                        options.MinimumEventLevel = sentryEventLevel;
                        options.SetBeforeSend((sentryEvent, _) =>
                        {
                            PrivacyOptions privacy = GetPrivacyOptions();
                            if (!privacy.TelemetryEnabled)
                            {
                                return null;
                            }

                            if (privacy.RedactPersonalData)
                            {
                                SanitizeSentryEvent(sentryEvent, privacy);
                            }

                            return sentryEvent;
                        });
                        options.SetBeforeBreadcrumb((breadcrumb, _) =>
                        {
                            PrivacyOptions privacy = GetPrivacyOptions();
                            if (!privacy.TelemetryEnabled)
                            {
                                return null;
                            }

                            if (privacy.RedactPersonalData && !string.IsNullOrEmpty(breadcrumb.Message))
                            {
                                return new Breadcrumb(
                                    message: SanitizeForTelemetry(breadcrumb.Message, privacy, allowEmail: false),
                                    type: breadcrumb.Type ?? "default",
                                    data: breadcrumb.Data,
                                    category: breadcrumb.Category,
                                    level: breadcrumb.Level);
                            }

                            return breadcrumb;
                        });
                    });
                }

                Log.Logger = loggerConfiguration.CreateLogger();
                LogStartupSummary(logDirectory, minimumLevel, retainedFileCount, fileSizeLimitMb, !string.IsNullOrWhiteSpace(sentryDsn));

                if (string.IsNullOrWhiteSpace(sentryDsn))
                {
                    Log.Warning(
                        "Sentry DSN is not configured. Set Sentry:Dsn via dotnet user-secrets (dev), environment variable HEXEL_Sentry__Dsn, or appsettings.json. Optional: Sentry:CrashFlushTimeoutSeconds (1-30) or HEXEL_Sentry__CrashFlushTimeoutSeconds for terminating-crash upload wait.");
                }
            }
            catch (Exception ex)
            {
                SetupFallbackConsoleLogger();
                Log.Error(ex, "Failed to initialize file/Sentry logging. Falling back to console logger.");
            }
        }

        public static void Shutdown()
        {
            try
            {
                Log.Information("Shutting down logging.");
                Log.CloseAndFlush();
            }
            finally
            {
                SentrySdk.Close();
            }
        }

        public static string GetLogDirectory()
        {
            IConfiguration configuration = BuildConfiguration();
            string appName = configuration["Logging:AppName"] ?? GetAppName();
            return BuildLogDirectory(appName);
        }

        private static readonly string FirstRunMarkerFile =
            Path.Combine(UserSettingsDirectory, ".first-run-complete");

        public static bool IsFirstRun()
        {
            return !File.Exists(FirstRunMarkerFile);
        }

        public static void MarkFirstRunComplete()
        {
            try
            {
                Directory.CreateDirectory(UserSettingsDirectory);
                File.WriteAllText(FirstRunMarkerFile, DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "LoggingService.MarkFirstRunComplete");
            }
        }

        /// <summary>
        /// Max number of newest log files to attach to manual bug reports (clamped 1–10).
        /// Config: BugReporting:MaxAttachedLogs; override: HEXEL_BugReporting__MaxAttachedLogs
        /// </summary>
        public static int GetBugReportingMaxAttachedLogs()
        {
            IConfiguration configuration = BuildConfiguration();
            int n = ParseInt(configuration["BugReporting:MaxAttachedLogs"], 2);
            return Math.Clamp(n, 1, 10);
        }

        public static PrivacyOptions GetPrivacyOptions()
        {
            IConfiguration configuration = BuildConfiguration();
            var defaults = new PrivacyOptions(
                telemetryEnabled: ParseBool(configuration["Privacy:TelemetryEnabled"], fallback: true),
                attachLogsByDefault: ParseBool(configuration["Privacy:AttachLogsByDefault"], fallback: false),
                allowLogAttachments: ParseBool(configuration["Privacy:AllowLogAttachments"], fallback: true),
                redactPersonalData: ParseBool(configuration["Privacy:RedactPersonalData"], fallback: true),
                shareContactEmailByDefault: ParseBool(configuration["Privacy:ShareContactEmailByDefault"], fallback: false),
                allowContactEmailInTelemetry: ParseBool(configuration["Privacy:AllowContactEmailInTelemetry"], fallback: true));

            PrivacyOptions? saved = LoadPrivacyOptionsFromDisk();
            return saved is null ? defaults : defaults.MergeWith(saved);
        }

        public static bool SavePrivacyOptions(PrivacyOptions options)
        {
            try
            {
                string json = JsonSerializer.Serialize(options, JsonOptions);
                SafeFileIo.WriteAllTextAtomic(UserPrivacySettingsFile, json, maxRetries: 3, createBackup: false);
                Log.Information("Saved privacy settings to {SettingsFile}", UserPrivacySettingsFile);
                return true;
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Error(ex, "LoggingService.SavePrivacyOptions", new { UserPrivacySettingsFile });
                return false;
            }
        }

        public static string SanitizeForTelemetry(string? value)
        {
            PrivacyOptions options = GetPrivacyOptions();
            return SanitizeForTelemetry(value, options, allowEmail: false);
        }

        public static string SanitizeForTelemetry(string? value, PrivacyOptions options, bool allowEmail)
        {
            string text = value?.Trim() ?? string.Empty;
            if (!options.RedactPersonalData || text.Length == 0)
            {
                return text;
            }

            text = RedactSecretsAndTokens(text);
            text = RedactPaths(text);
            text = RedactUserFoldersAndName(text);
            text = RedactIpAddresses(text);
            if (!allowEmail)
            {
                text = RedactEmails(text);
            }

            return text;
        }

        public const int MaxAttachedLogBytesPerFile = 512 * 1024; // 512 KB
        public const int MaxAttachedLogLines = 2000;

        /// <summary>
        /// Attaches the newest rolling log files to a Sentry scope (manual bug or feedback reports).
        /// If personal data redaction is enabled, sanitizes log contents prior to attaching.
        /// Bounded to maximum bytes/lines to avoid large allocations and payload drops.
        /// </summary>
        public static void AttachRecentLogFilesToScope(Scope scope)
        {
            try
            {
                PrivacyOptions privacy = GetPrivacyOptions();
                if (!privacy.AllowLogAttachments)
                {
                    Log.Information("Log attachment disabled by privacy settings.");
                    return;
                }

                string logDirectory = GetLogDirectory();
                if (!Directory.Exists(logDirectory))
                {
                    return;
                }

                int maxFiles = GetBugReportingMaxAttachedLogs();
                string[] latestFiles = [.. Directory.EnumerateFiles(logDirectory, "*.txt")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .Take(maxFiles)];

                foreach (string file in latestFiles)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (!fileInfo.Exists) continue;

                        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

                        // If file is larger than the byte limit, seek near the end
                        bool wasTruncated = false;
                        if (fileInfo.Length > MaxAttachedLogBytesPerFile)
                        {
                            stream.Seek(-MaxAttachedLogBytesPerFile, SeekOrigin.End);
                            wasTruncated = true;
                        }

                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        // If we sought into the middle of a file, discard the partial line
                        if (wasTruncated)
                        {
                            reader.ReadLine();
                        }

                        var lines = new List<string>();
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (privacy.RedactPersonalData)
                            {
                                line = SanitizeForTelemetry(line, privacy, allowEmail: false);
                            }
                            lines.Add(line);
                            if (lines.Count > MaxAttachedLogLines)
                            {
                                lines.RemoveAt(0);
                            }
                        }

                        var sb = new StringBuilder();
                        if (wasTruncated)
                        {
                            sb.AppendLine("[... Earlier log entries omitted for size ...]");
                        }
                        foreach (var l in lines)
                        {
                            sb.AppendLine(l);
                        }

                        byte[] sanitizedBytes = Encoding.UTF8.GetBytes(sb.ToString());
                        scope.AddAttachment(sanitizedBytes, Path.GetFileName(file));
                    }
                    catch (Exception readEx)
                    {
                        Log.Warning(readEx, "Failed to read or sanitize log file {File} for attachment; skipping.", file);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Unable to attach recent log files to report scope.");
            }
        }

        private static void SanitizeSentryEvent(SentryEvent sentryEvent, PrivacyOptions privacy)
        {
            if (sentryEvent.Message?.Message is not null)
            {
                sentryEvent.Message = SanitizeForTelemetry(sentryEvent.Message.Message, privacy, allowEmail: false);
            }

            if (!string.IsNullOrWhiteSpace(sentryEvent.ServerName))
            {
                sentryEvent.ServerName = null;
            }

            if (sentryEvent.Extra.Count > 0)
            {
                var keys = sentryEvent.Extra.Keys.ToArray();
                foreach (string key in keys)
                {
                    if (sentryEvent.Extra.TryGetValue(key, out object? val) && val is string strVal)
                    {
                        sentryEvent.SetExtra(key, SanitizeForTelemetry(strVal, privacy, allowEmail: false));
                    }
                }
            }
        }

        /// <summary>
        /// Represents a timed diagnostic scope with failure tracking and optional slow threshold warning.
        /// </summary>
        public interface ITimedOperation : IDisposable
        {
            void MarkFailed(Exception? ex = null);
            void MarkFailed(string reason);
        }

        /// <summary>
        /// Creates a timing scope that logs operation start, completion, and duration.
        /// </summary>
        public static ITimedOperation BeginOperation(string operationName, object? context = null, int? slowThresholdMs = null)
        {
            return new TimedOperationScope(operationName, context, slowThresholdMs);
        }

        /// <summary>
        /// Pushes standard operation properties into Serilog's log context.
        /// Usage: using var _ = LoggingService.PushContext("OpenFile", new { path });
        /// </summary>
        public static IDisposable PushContext(string operationName, object? context = null)
        {
            IDisposable op = LogContext.PushProperty("Operation", operationName);
            IDisposable? payload = context is null ? null : LogContext.PushProperty("OperationContext", context, destructureObjects: true);
            return new CompositeDisposable(op, payload);
        }

        private static string GetAppName()
        {
            return Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyProductAttribute>()?
                .Product
                ?? DefaultAppName;
        }

        private static string GetAppVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
        }

        private static IConfiguration BuildConfiguration()
        {
            string basePath = AppContext.BaseDirectory;

            return new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile(AppSettingsFile, optional: true, reloadOnChange: false)
                .AddUserSecrets(typeof(LoggingService).Assembly, optional: true)
                .AddEnvironmentVariables(prefix: "HEXEL_")
                .Build();
        }

        private static string BuildLogDirectory(string appName)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appName,
                "Logs");
        }

        private static void ConfigureSerilogSelfDiagnostics(string logDirectory)
        {
            string selfLogPath = Path.Combine(logDirectory, "serilog-selflog.txt");
            SelfLog.Enable(message =>
            {
                try
                {
                    if (File.Exists(selfLogPath))
                    {
                        var fi = new FileInfo(selfLogPath);
                        if (fi.Length > 1024 * 1024) // 1 MB limit
                        {
                            string oldPath = Path.Combine(logDirectory, "serilog-selflog.old.txt");
                            try { File.Delete(oldPath); } catch { }
                            File.Move(selfLogPath, oldPath, overwrite: true);
                        }
                    }
                    File.AppendAllText(selfLogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
                }
                catch
                {
                    // Never throw from self-diagnostic channel.
                }
            });
        }

        private static void SetupFallbackConsoleLogger()
        {
            string fallbackPath = Path.Combine(Path.GetTempPath(), "hexel-fallback-log.txt");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    fallbackPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    shared: true,
                    formatProvider: CultureInfo.InvariantCulture)
                .CreateLogger();
        }

        private static void LogStartupSummary(
            string logDirectory,
            LogEventLevel minimumLevel,
            int retainedFileCount,
            int fileSizeLimitMb,
            bool sentryEnabled)
        {
            Log.Information(
                "Logging initialized. Directory={LogDirectory} MinLevel={MinLevel} RetainedFileCount={RetainedFileCount} FileSizeLimitMB={FileSizeLimitMB} SentryEnabled={SentryEnabled} SessionId={SessionId}",
                logDirectory,
                minimumLevel,
                retainedFileCount,
                fileSizeLimitMb,
                sentryEnabled,
                _currentSessionId ?? "unknown");
        }

        private static bool ParseBool(string? value, bool fallback)
        {
            return bool.TryParse(value, out bool parsed) ? parsed : fallback;
        }

        private static int ParseInt(string? value, int fallback)
        {
            return int.TryParse(value, out int parsed) ? parsed : fallback;
        }

        private static LogEventLevel ParseLogLevel(string? value, LogEventLevel fallback)
        {
            return Enum.TryParse(value, ignoreCase: true, out LogEventLevel parsed) ? parsed : fallback;
        }

        private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

        private static string RedactEmails(string input)
        {
            return Regex.Replace(
                input,
                @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
                "[redacted-email]",
                RegexOptions.None,
                RegexTimeout);
        }

        private static string RedactSecretsAndTokens(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Redact Bearer tokens
            string text = Regex.Replace(
                input,
                @"\bBearer\s+[A-Za-z0-9\-_.~+/]+=*",
                "Bearer [redacted-token]",
                RegexOptions.IgnoreCase,
                RegexTimeout);

            // Redact GitHub personal access tokens
            text = Regex.Replace(
                text,
                @"\bgh[pousr]_[A-Za-z0-9_]{20,}\b",
                "[redacted-token]",
                RegexOptions.None,
                RegexTimeout);

            // Redact secrets in query params or key-value pairs (e.g. password=..., secret=..., token=..., apiKey=..., sentry_key=...)
            text = Regex.Replace(
                text,
                @"(?i)\b(password|token|secret|apiKey|api_key|access_token|sentry_key)(\s*[:=]\s*)[""']?([^""'\s&,;}]+)",
                "$1$2[redacted-secret]",
                RegexOptions.None,
                RegexTimeout);

            return text;
        }

        private static string RedactPaths(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Redact file URIs: file:///C:/... or file:///home/...
            string text = Regex.Replace(
                input,
                @"file:///(?:[A-Za-z]:[/\\]|[/\\])[^""'\s<>]+",
                "[redacted-path]",
                RegexOptions.IgnoreCase,
                RegexTimeout);

            // Redact Windows backslash paths: C:\folder\file.ext
            text = Regex.Replace(
                text,
                @"\b[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n\s]+\\)*[^\\/:*?""<>|\r\n\s]*",
                "[redacted-path]",
                RegexOptions.None,
                RegexTimeout);

            // Redact Windows forward slash paths: C:/folder/file.ext
            text = Regex.Replace(
                text,
                @"\b[A-Za-z]:/(?:[^/:*?""<>|\r\n\s]+/)*[^/:*?""<>|\r\n\s]*",
                "[redacted-path]",
                RegexOptions.None,
                RegexTimeout);

            // Redact UNC network paths: \\server\share\path
            text = Regex.Replace(
                text,
                @"\\\\[^\r\n\\/:*?""<>|\s]+\\[^\r\n\\/:*?""<>|\s]+(?:\\(?:[^\r\n\\/:*?""<>|\s]+)?)*",
                "[redacted-path]",
                RegexOptions.None,
                RegexTimeout);

            // Redact POSIX/Unix/macOS absolute paths: /home/..., /Users/..., /tmp/..., /var/..., /opt/..., /etc/..., /usr/..., /private/...
            text = Regex.Replace(
                text,
                @"(?:^|[\s""'\(])\/(?:home|Users|tmp|var|opt|etc|usr|private)\/[^""'\s<>]+",
                m => m.Value.StartsWith('/') ? "[redacted-path]" : m.Value[0] + "[redacted-path]",
                RegexOptions.None,
                RegexTimeout);

            return text;
        }

        private static string RedactIpAddresses(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Redact IPv4 addresses
            string text = Regex.Replace(
                input,
                @"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b",
                "[redacted-ip]",
                RegexOptions.None,
                RegexTimeout);

            // Redact standard IPv6 addresses
            text = Regex.Replace(
                text,
                @"\b(?:[0-9a-fA-F]{1,4}:){7}[0-9a-fA-F]{1,4}\b|\b(?:[0-9a-fA-F]{1,4}:){1,7}:(?:[0-9a-fA-F]{1,4})?\b",
                "[redacted-ip]",
                RegexOptions.None,
                RegexTimeout);

            return text;
        }

        private static string RedactUserFoldersAndName(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            string text = input;

            // Redact user folder segments (e.g. Users\JohnDoe or /Users/JohnDoe or /home/johndoe)
            text = Regex.Replace(
                text,
                @"(?<=[\\/](?:Users|home)[\\/])[^\\/:*?""<>|\r\n\s]+",
                "[redacted-user]",
                RegexOptions.IgnoreCase,
                RegexTimeout);

            // Redact current Windows username if present
            try
            {
                string currentUser = Environment.UserName;
                if (!string.IsNullOrWhiteSpace(currentUser) && currentUser.Length > 1)
                {
                    text = Regex.Replace(text, $@"\b{Regex.Escape(currentUser)}\b", "[redacted-user]", RegexOptions.IgnoreCase, RegexTimeout);
                }
            }
            catch
            {
                // Fallback safely if environment reading fails
            }

            return text;
        }

        private static PrivacyOptions? LoadPrivacyOptionsFromDisk()
        {
            try
            {
                if (!File.Exists(UserPrivacySettingsFile))
                {
                    return null;
                }

                string json = File.ReadAllText(UserPrivacySettingsFile);
                return JsonSerializer.Deserialize<PrivacyOptions>(json);
            }
            catch (Exception ex)
            {
                HandledErrorReporter.Warning(ex, "LoggingService.LoadPrivacyOptionsFromDisk", new { UserPrivacySettingsFile });
                return null;
            }
        }

        public sealed class PrivacyOptions
        {
            public bool TelemetryEnabled { get; init; }
            public bool AttachLogsByDefault { get; init; }
            public bool AllowLogAttachments { get; init; }
            public bool RedactPersonalData { get; init; }
            public bool ShareContactEmailByDefault { get; init; }
            public bool AllowContactEmailInTelemetry { get; init; }

            public PrivacyOptions()
            {
            }

            public PrivacyOptions(
                bool telemetryEnabled,
                bool attachLogsByDefault,
                bool allowLogAttachments,
                bool redactPersonalData,
                bool shareContactEmailByDefault,
                bool allowContactEmailInTelemetry)
            {
                TelemetryEnabled = telemetryEnabled;
                AttachLogsByDefault = attachLogsByDefault;
                AllowLogAttachments = allowLogAttachments;
                RedactPersonalData = redactPersonalData;
                ShareContactEmailByDefault = shareContactEmailByDefault;
                AllowContactEmailInTelemetry = allowContactEmailInTelemetry;
            }

            public PrivacyOptions MergeWith(PrivacyOptions? overrideOptions)
            {
                if (overrideOptions is null)
                {
                    return this;
                }

                return new PrivacyOptions(
                    telemetryEnabled: overrideOptions.TelemetryEnabled,
                    attachLogsByDefault: overrideOptions.AttachLogsByDefault,
                    allowLogAttachments: overrideOptions.AllowLogAttachments,
                    redactPersonalData: overrideOptions.RedactPersonalData,
                    shareContactEmailByDefault: overrideOptions.ShareContactEmailByDefault,
                    allowContactEmailInTelemetry: overrideOptions.AllowContactEmailInTelemetry);
            }
        }

        private sealed class TimedOperationScope : ITimedOperation
        {
            private readonly string _operationName;
            private readonly object? _context;
            private readonly int? _slowThresholdMs;
            private readonly Stopwatch _stopwatch;
            private bool _disposed;
            private bool _failed;
            private string? _failureReason;

            public TimedOperationScope(string operationName, object? context, int? slowThresholdMs = null)
            {
                _operationName = operationName;
                _context = context;
                _slowThresholdMs = slowThresholdMs;
                _stopwatch = Stopwatch.StartNew();

                if (_context is null)
                {
                    Log.Information("Starting operation {Operation}", _operationName);
                }
                else
                {
                    Log.Information("Starting operation {Operation} {@OperationContext}", _operationName, _context);
                }
            }

            public void MarkFailed(Exception? ex = null)
            {
                _failed = true;
                _failureReason = ex?.Message ?? "Exception occurred";
            }

            public void MarkFailed(string reason)
            {
                _failed = true;
                _failureReason = reason;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _stopwatch.Stop();
                long elapsed = _stopwatch.ElapsedMilliseconds;

                if (_failed)
                {
                    if (_context is null)
                    {
                        Log.Error("Failed operation {Operation} in {ElapsedMs}ms Reason={Reason}", _operationName, elapsed, _failureReason ?? "Unknown");
                    }
                    else
                    {
                        Log.Error(
                            "Failed operation {Operation} in {ElapsedMs}ms Reason={Reason} {@OperationContext}",
                            _operationName,
                            elapsed,
                            _failureReason ?? "Unknown",
                            _context);
                    }
                    return;
                }

                if (_slowThresholdMs.HasValue && elapsed > _slowThresholdMs.Value)
                {
                    if (_context is null)
                    {
                        Log.Warning(
                            "Operation {Operation} completed slowly in {ElapsedMs}ms (threshold: {ThresholdMs}ms)",
                            _operationName,
                            elapsed,
                            _slowThresholdMs.Value);
                    }
                    else
                    {
                        Log.Warning(
                            "Operation {Operation} completed slowly in {ElapsedMs}ms (threshold: {ThresholdMs}ms) {@OperationContext}",
                            _operationName,
                            elapsed,
                            _slowThresholdMs.Value,
                            _context);
                    }
                    return;
                }

                if (_context is null)
                {
                    Log.Information("Completed operation {Operation} in {ElapsedMs}ms", _operationName, elapsed);
                    return;
                }

                Log.Information(
                    "Completed operation {Operation} in {ElapsedMs}ms {@OperationContext}",
                    _operationName,
                    elapsed,
                    _context);
            }
        }

        private sealed class CompositeDisposable(IDisposable first, IDisposable? second) : IDisposable
        {
            private readonly IDisposable _first = first;
            private readonly IDisposable? _second = second;

            public void Dispose()
            {
                _second?.Dispose();
                _first.Dispose();
            }
        }
    }
}
