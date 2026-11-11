using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Onboarding;

public sealed class GetOnboardingContext(
    ICommercialOnboardingSource source,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider)
{
    public async Task<OnboardingContextResult> ExecuteAsync(
        string? token, string? cnpj, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512 ||
            !tokenGenerator.TryHash(token, out var tokenHash))
        {
            return new(OnboardingContextStatus.Invalid);
        }

        if (!TryNormalizeCnpj(cnpj, out var normalizedCnpj))
        {
            return new(OnboardingContextStatus.CnpjMismatch);
        }

        var lookup = await source.FindAsync(tokenHash, cancellationToken);
        if (lookup.Status != CommercialOnboardingLookupStatus.Ready || lookup.Record is null)
        {
            return new(Map(lookup.Status));
        }

        if (lookup.Record.ExpiresAt <= timeProvider.GetUtcNow())
        {
            return new(OnboardingContextStatus.Expired);
        }

        if (!TryNormalizeCnpj(lookup.Record.Cnpj, out var registeredCnpj) ||
            !string.Equals(normalizedCnpj, registeredCnpj, StringComparison.Ordinal))
        {
            return new(OnboardingContextStatus.CnpjMismatch);
        }

        return new(OnboardingContextStatus.Ready, new(
            lookup.Record.AdministratorEmail,
            lookup.Record.Cnpj,
            lookup.Record.CompanyName,
            lookup.Record.ExpiresAt));
    }

    private static OnboardingContextStatus Map(CommercialOnboardingLookupStatus status) => status switch
    {
        CommercialOnboardingLookupStatus.Expired => OnboardingContextStatus.Expired,
        CommercialOnboardingLookupStatus.Revoked => OnboardingContextStatus.Revoked,
        CommercialOnboardingLookupStatus.Used => OnboardingContextStatus.Used,
        CommercialOnboardingLookupStatus.Unavailable => OnboardingContextStatus.Unavailable,
        _ => OnboardingContextStatus.Invalid,
    };

    private static bool TryNormalizeCnpj(string? value, out string? normalized)
    {
        normalized = null;
        try
        {
            normalized = Tenant.NormalizeCnpj(value ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed record OnboardingContext(
    string Email,
    string Cnpj,
    string? CompanyName,
    DateTimeOffset ExpiresAt);

public sealed record OnboardingContextResult(
    OnboardingContextStatus Status,
    OnboardingContext? Context = null);

public enum OnboardingContextStatus
{
    Ready = 1,
    Invalid = 2,
    Expired = 3,
    Revoked = 4,
    Used = 5,
    Unavailable = 6,
    CnpjMismatch = 7,
}
