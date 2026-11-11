using BrothersCare.Application.Identity.Sessions;

namespace BrothersCare.Application.Identity.Login;

public sealed class LoginResult(
    LoginStatus status,
    SessionSummary? session = null,
    string? cookieValue = null,
    DateTimeOffset? absoluteExpiresAt = null)
{
    public LoginStatus Status { get; } = status;
    public SessionSummary? Session { get; } = session;
    public string? CookieValue { get; } = cookieValue;
    public DateTimeOffset? AbsoluteExpiresAt { get; } = absoluteExpiresAt;

    public override string ToString() => "[REDACTED]";
}

public enum LoginStatus
{
    Succeeded = 1,
    InvalidInput = 2,
    InvalidCredentials = 3,
}
