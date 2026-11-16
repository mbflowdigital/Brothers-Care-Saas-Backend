using BrothersCare.Application.Care.Contracts;
using BrothersCare.Application.Identity.Contracts;

namespace BrothersCare.Application.Care;

public sealed class GetPatientRegistrationOptions(
    ICurrentPrincipalAccessor principalAccessor,
    IPatientRegistrationStore store,
    TimeProvider timeProvider)
{
    public async Task<PatientRegistrationOptionsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var principal = principalAccessor.Principal;
        if (principal?.ActiveTenantId is not { } tenantId)
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(now) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Unauthenticated);
        }

        if (session.ActiveTenantId != tenantId ||
            !await transaction.HasActiveClinicAdministratorAsync(tenantId, principal.UserId, cancellationToken))
        {
            return new(PatientRegistrationStatus.Forbidden);
        }

        var professionals = await transaction.ListProfessionalOptionsAsync(tenantId, cancellationToken);
        var units = await transaction.ListClinicUnitOptionsAsync(tenantId, cancellationToken);
        return new(PatientRegistrationStatus.Success, new(professionals, units));
    }
}

public sealed record PatientRegistrationOptions(
    IReadOnlyList<PatientProfessionalOption> Professionals,
    IReadOnlyList<PatientClinicUnitOption> ClinicUnits);

public sealed record PatientRegistrationOptionsResult(
    PatientRegistrationStatus Status,
    PatientRegistrationOptions? Options = null);
