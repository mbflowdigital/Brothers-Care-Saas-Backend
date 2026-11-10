namespace BrothersCare.Application.Identity;

public interface IPlatformAdministratorBootstrapper
{
    Task<PlatformAdministratorBootstrapResult> EnsureSuperAdministratorAsync(
        PlatformAdministratorBootstrapRequest request,
        CancellationToken cancellationToken);
}

public sealed record PlatformAdministratorBootstrapRequest(
    string Email,
    string DisplayName,
    string? Cpf = null,
    string? InitialPassword = null);

public sealed record PlatformAdministratorBootstrapResult(bool Created);
