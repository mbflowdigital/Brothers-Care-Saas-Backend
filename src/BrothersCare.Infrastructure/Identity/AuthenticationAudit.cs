using BrothersCare.Application.Identity.Contracts;
using Microsoft.Extensions.Logging;

namespace BrothersCare.Infrastructure.Identity;

public sealed class AuthenticationAudit(ILogger<AuthenticationAudit> logger) : IAuthenticationAudit
{
    private static readonly Action<ILogger, AuthenticationAuditEvent, DateTimeOffset, Exception?> WriteEvent =
        LoggerMessage.Define<AuthenticationAuditEvent, DateTimeOffset>(LogLevel.Information,
            new EventId(1001, "AuthenticationAudit"), "Authentication event {Action} at {OccurredAt}");

    public void Record(AuthenticationAuditEvent action, DateTimeOffset occurredAt) =>
        WriteEvent(logger, action, occurredAt, null);
}
