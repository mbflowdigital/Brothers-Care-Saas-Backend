using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlPasswordRecoveryStore(BrothersCareDbContext dbContext) : IPasswordRecoveryStore
{
    public async Task<IPasswordRecoveryTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new RecoveryTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class RecoveryTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IPasswordRecoveryTransaction
    {
        public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);

        public Task<User?> FindUserByIdAsync(Guid userId, CancellationToken cancellationToken) =>
            context.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);

        public async Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken)
        {
            var credential = await context.UserCredentials.SingleOrDefaultAsync(
                item => item.UserId == userId,
                cancellationToken);
            if (credential is not null)
            {
                await context.Entry(credential).ReloadAsync(cancellationToken);
            }

            return credential;
        }

        public async Task<AccountActionToken?> FindTokenAsync(byte[] secretHash, CancellationToken cancellationToken)
        {
            var userId = await context.AccountActionTokens.AsNoTracking().Where(item => item.SecretHash == secretHash)
                .Select(item => (Guid?)item.UserId).SingleOrDefaultAsync(cancellationToken);
            if (userId is null)
            {
                return null;
            }

            await FindCredentialAsync(userId.Value, cancellationToken);
            return await context.AccountActionTokens.SingleOrDefaultAsync(
                item => item.SecretHash == secretHash,
                cancellationToken);
        }

        public void AddToken(AccountActionToken token) => context.AccountActionTokens.Add(token);

        public async Task RevokeRecoveryTokensAsync(
            Guid userId, DateTimeOffset now, Guid? exceptTokenId, CancellationToken cancellationToken)
        {
            var tokens = await context.AccountActionTokens.Where(item => item.UserId == userId &&
                item.Purpose == AccountActionTokenPurpose.PasswordRecovery && item.RevokedAt == null &&
                item.ConsumedAt == null && (exceptTokenId == null || item.Id != exceptTokenId)).ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        public async Task RevokeSessionsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var sessions = await context.UserSessions.Where(item => item.UserId == userId && item.RevokedAt == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions)
            {
                session.Revoke(now, SessionRevocationReason.PasswordChanged);
            }
        }

        public async Task SaveAsync(CancellationToken cancellationToken) => await context.SaveChangesAsync(cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
