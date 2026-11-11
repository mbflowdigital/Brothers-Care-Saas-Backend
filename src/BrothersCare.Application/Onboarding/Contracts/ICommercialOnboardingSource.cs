namespace BrothersCare.Application.Onboarding.Contracts;

public interface ICommercialOnboardingSource
{
    Task<CommercialOnboardingLookup> FindAsync(byte[] tokenHash, CancellationToken cancellationToken);
}

public sealed record CommercialOnboardingLookup(
    CommercialOnboardingLookupStatus Status,
    CommercialOnboardingRecord? Record = null);

public sealed record CommercialOnboardingRecord(
    Guid Id,
    string AdministratorEmail,
    string Cnpj,
    string? CompanyName,
    DateTimeOffset ExpiresAt);

public enum CommercialOnboardingLookupStatus
{
    Ready = 1,
    Invalid = 2,
    Expired = 3,
    Revoked = 4,
    Used = 5,
    Unavailable = 6,
}
