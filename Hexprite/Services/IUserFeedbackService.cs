namespace Hexprite.Services
{
    public sealed class FeedbackSubmitResult
    {
        public bool Success { get; init; }
        public string? EventId { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public interface IUserFeedbackService
    {
        FeedbackSubmitResult SubmitFeedback(UserFeedbackInput input);
    }
}
