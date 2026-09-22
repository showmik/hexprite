namespace Hexprite.Services
{
    public sealed class BugReportResult
    {
        public bool Success { get; init; }
        public string? EventId { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public interface IBugReportService
    {
        BugReportResult SubmitReport(BugReportInput input);
    }
}
