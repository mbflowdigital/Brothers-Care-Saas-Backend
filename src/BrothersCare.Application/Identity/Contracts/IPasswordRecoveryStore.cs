using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Contracts;

public interface IPasswordRecoveryStore
{
    Task<IPasswordRecoveryTransaction> BeginAsync(CancellationToken cancellationToken);
}

public interface IPasswordRecoveryTransaction : IAsyncDisposable
{
    Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken);
    Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken);
    void AddToken(AccountActionToken token);
    Task RevokeRecoveryTokensAsync(Guid userId, DateTimeOffset now, Guid? exceptTokenId, CancellationToken cancellationToken);
    Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken);
    Task SaveAsync(CancellationToken cancellationToken);
    Task CommitAsync(CancellationToken cancellationToken);
}
