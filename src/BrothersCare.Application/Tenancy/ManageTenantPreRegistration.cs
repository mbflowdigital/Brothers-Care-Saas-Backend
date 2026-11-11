using System.Net.Mail;

namespace BrothersCare.Application.Tenancy;

public interface ITenantPreRegistrationStore
{
    Task<TenantPreRegistrationDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> CnpjBelongsToAnotherTenantAsync(Guid tenantId, string cnpj, CancellationToken cancellationToken);
    Task<TenantPreRegistrationStatus> UpdateAsync(Guid tenantId, TenantPreRegistrationInput input, CancellationToken cancellationToken);
    Task<TenantPreRegistrationStatus> CancelAsync(Guid tenantId, DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed record TenantPreRegistrationDetails(
    Guid Id, string CompanyName, string Cnpj, string ResponsibleName, string ResponsibleEmail, string Status);

public sealed record TenantPreRegistrationInput(
    string CompanyName, string Cnpj, string ResponsibleName, string ResponsibleEmail);

public enum TenantPreRegistrationStatus
{
    Completed,
    NotFound,
    InvalidState,
    InvalidInput,
    CnpjAlreadyRegistered,
}

public sealed class ManageTenantPreRegistration(ITenantPreRegistrationStore store, TimeProvider timeProvider)
{
    public Task<TenantPreRegistrationDetails?> GetAsync(Guid tenantId, CancellationToken cancellationToken) =>
        tenantId == Guid.Empty ? Task.FromResult<TenantPreRegistrationDetails?>(null) : store.GetAsync(tenantId, cancellationToken);

    public async Task<TenantPreRegistrationStatus> UpdateAsync(
        Guid tenantId, TenantPreRegistrationInput input, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || !TryValidate(input, out var cnpj))
        {
            return TenantPreRegistrationStatus.InvalidInput;
        }

        if (await store.CnpjBelongsToAnotherTenantAsync(tenantId, cnpj!, cancellationToken))
        {
            return TenantPreRegistrationStatus.CnpjAlreadyRegistered;
        }

        return await store.UpdateAsync(tenantId, input with { Cnpj = cnpj! }, cancellationToken);
    }

    public Task<TenantPreRegistrationStatus> CancelAsync(Guid tenantId, CancellationToken cancellationToken) =>
        tenantId == Guid.Empty
            ? Task.FromResult(TenantPreRegistrationStatus.NotFound)
            : store.CancelAsync(tenantId, timeProvider.GetUtcNow(), cancellationToken);

    private static bool TryValidate(TenantPreRegistrationInput input, out string? cnpj)
    {
        cnpj = null;
        if (string.IsNullOrWhiteSpace(input.CompanyName) || input.CompanyName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(input.ResponsibleName) || input.ResponsibleName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(input.ResponsibleEmail) || input.ResponsibleEmail.Trim().Length > 320 ||
            !MailAddress.TryCreate(input.ResponsibleEmail.Trim(), out var email) ||
            !string.Equals(email.Address, input.ResponsibleEmail.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        try { cnpj = Domain.Tenancy.Tenant.NormalizeCnpj(input.Cnpj); return true; }
        catch (ArgumentException) { return false; }
    }
}
