using System.Data;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace BrothersCare.Infrastructure.Persistence.Repositories;

public sealed class SqlPlatformAdministratorStore(BrothersCareDbContext dbContext) : IPlatformAdministratorStore
{
    public async Task<IPlatformAdministratorTransaction> BeginAsync(CancellationToken cancellationToken) =>
        new AdministratorTransaction(dbContext,
            await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken));

    private sealed class AdministratorTransaction(
        BrothersCareDbContext context,
        IDbContextTransaction transaction) : IPlatformAdministratorTransaction
    {
        public Task<User?> FindUserAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            context.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == normalizedEmail, cancellationToken);

        public Task<PlatformAdministrator?> FindAdministratorAsync(Guid userId, CancellationToken cancellationToken) =>
            context.PlatformAdministrators.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        public Task<UserCredential?> FindCredentialAsync(Guid userId, CancellationToken cancellationToken) =>
            context.UserCredentials.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);

        public void AddUser(User user) => context.Users.Add(user);
        public void AddCredential(UserCredential credential) => context.UserCredentials.Add(credential);
        public void AddAdministrator(PlatformAdministrator administrator) => context.PlatformAdministrators.Add(administrator);
        public void AddToken(AccountActionToken token) => context.AccountActionTokens.Add(token);

        public async Task RevokeActivationTokensAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            var tokens = await context.AccountActionTokens.Where(item => item.UserId == userId &&
                item.Purpose == AccountActionTokenPurpose.AccountActivation &&
                item.ConsumedAt == null && item.RevokedAt == null).ToListAsync(cancellationToken);
            foreach (var token in tokens)
            {
                token.Revoke(now);
            }
        }

        public async Task CommitAsync(CancellationToken cancellationToken)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
