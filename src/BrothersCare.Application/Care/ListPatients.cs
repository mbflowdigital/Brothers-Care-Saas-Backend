using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;

namespace BrothersCare.Application.Care;

public sealed class ListPatients(
    ICurrentPrincipalAccessor principalAccessor,
    IPatientRegistrationStore store,
    TimeProvider timeProvider)
{
    public async Task<PatientListResult> ExecuteAsync(
        string? search,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (page < 1 || pageSize is < 1 or > 100)
        {
            return new(PatientRegistrationStatus.InvalidInput);
        }

        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId)
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(timeProvider.GetUtcNow()) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Unauthenticated);
        }

        if (session.ActiveTenantId != tenantId ||
            !await transaction.HasActiveClinicAdministratorAsync(tenantId, principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        return new(PatientRegistrationStatus.Success,
            await transaction.ListAsync(tenantId, normalizedSearch, page, pageSize, cancellationToken));
    }
}

public sealed record PatientListResult(
    PatientRegistrationStatus Status,
    PatientPage? Page = null);
