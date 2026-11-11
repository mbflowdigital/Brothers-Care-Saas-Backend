using BrothersCare.Application.Identity.Contracts;

namespace BrothersCare.Application.Identity.Sessions;

public sealed record AuthenticatedSession(SessionSummary Summary, CurrentPrincipal Principal)
{
    public override string ToString() => "[REDACTED]";
}
