namespace BrothersCare.Application.Identity;

public interface IPlatformAdministratorActivationTokenIssuer
{
    Task<PlatformAdministratorActivationTokenIssueResult> IssueAsync(
        PlatformAdministratorActivationTokenIssueRequest request,
        CancellationToken cancellationToken);
}

public sealed record PlatformAdministratorActivationTokenIssueRequest(
    string Email,
    TimeSpan Lifetime);

public sealed record PlatformAdministratorActivationTokenIssueResult(
    string Token,
    DateTimeOffset ExpiresAt);
