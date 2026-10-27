using System.Security.Cryptography;
using BrothersCare.Application.Identity;
using BrothersCare.Domain.Identity;
using BrothersCare.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Identity;

public sealed class PlatformAdministratorActivationTokenIssuer(
    BrothersCareDbContext dbContext,
    TimeProvider timeProvider) : IPlatformAdministratorActivationTokenIssuer
{
    public async Task<PlatformAdministratorActivationTokenIssueResult> IssueAsync(
        PlatformAdministratorActivationTokenIssueRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Email);
        if (request.Lifetime <= TimeSpan.Zero || request.Lifetime > TimeSpan.FromHours(24))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The activation token lifetime must be between zero and 24 hours.");
        }

        var normalizedEmail = request.Email.Trim().ToUpperInvariant();
        var user = await dbContext.Users.SingleOrDefaultAsync(
            item => item.NormalizedEmail == normalizedEmail,
            cancellationToken)
            ?? throw new InvalidOperationException("The requested platform administrator was not found.");

        var administrator = await dbContext.PlatformAdministrators.SingleOrDefaultAsync(
            item => item.UserId == user.Id &&
                    item.Role == PlatformAdministratorRole.SuperAdministrator &&
                    item.Status == PlatformAdministratorStatus.Active,
            cancellationToken);
        if (administrator is null)
        {
            throw new InvalidOperationException("The requested user is not an active super administrator.");
        }

        var credential = await dbContext.UserCredentials.SingleOrDefaultAsync(
            item => item.UserId == user.Id,
            cancellationToken);
        if (credential?.Status is UserCredentialStatus.Active)
        {
            throw new InvalidOperationException("An active credential must use the password recovery flow.");
        }

        var now = timeProvider.GetUtcNow();
        var existingTokens = await dbContext.AccountActionTokens
            .Where(item => item.UserId == user.Id &&
                           item.Purpose == AccountActionTokenPurpose.AccountActivation &&
                           item.ConsumedAt == null &&
                           item.RevokedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var existingToken in existingTokens)
        {
            existingToken.Revoke(now);
        }

        var secret = RandomNumberGenerator.GetBytes(32);
        var token = WebEncoders.Base64UrlEncode(secret);
        dbContext.AccountActionTokens.Add(new AccountActionToken(
            Guid.NewGuid(),
            user.Id,
            AccountActionTokenPurpose.AccountActivation,
            SHA256.HashData(secret),
            now,
            now.Add(request.Lifetime)));

        await dbContext.SaveChangesAsync(cancellationToken);
        return new PlatformAdministratorActivationTokenIssueResult(token, now.Add(request.Lifetime));
    }
}
