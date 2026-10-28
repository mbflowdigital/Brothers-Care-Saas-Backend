using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Contracts;

public interface IPlatformAdministratorStore
{
    Task<IPlatformAdministratorTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IPlatformAdministratorTransaction : IAsyncDisposable
{
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<PlatformAdministrator?> FindAdministratorAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    void AddUser(User user);
    void AddCredential(UserCredential credential);
    void AddAdministrator(PlatformAdministrator administrator);
    void AddToken(AccountActionToken token);
    Task RevokeActivationTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
