namespace BrothersCare.Application.Identity;

public interface IPlatformAdministratorBootstrapper
{
    Task<PlatformAdministratorBootstrapResult> EnsureSuperAdministratorAsync(
        PlatformAdministratorBootstrapRequest request,
        CancellationToken cancellationToken);
}

public sealed record PlatformAdministratorBootstrapRequest(string Email, string DisplayName);

public sealed record PlatformAdministratorBootstrapResult(bool Created);
