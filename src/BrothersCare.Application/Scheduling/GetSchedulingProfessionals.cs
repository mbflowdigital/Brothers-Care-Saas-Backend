using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;

namespace BrothersCare.Application.Scheduling;

public sealed class GetSchedulingProfessionals(
    ICurrentPrincipalAccessor principalAccessor,
    IAppointmentStore store,
    TimeProvider timeProvider)
{
    public async Task<SchedulingProfessionalsResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var transaction = await store.BeginAsync(cancellationToken);
        var access = await SchedulingAccess.ResolveAgendaViewerAsync(
            principalAccessor.Principal, transaction, timeProvider, cancellationToken);
        if (access.Status != SchedulingAccessStatus.Success)
        {
            return new(access.Status switch
            {
                SchedulingAccessStatus.Unauthenticated => SchedulingProfessionalsStatus.Unauthenticated,
                SchedulingAccessStatus.Forbidden => SchedulingProfessionalsStatus.Forbidden,
                SchedulingAccessStatus.ProfessionalNotFound => SchedulingProfessionalsStatus.ProfessionalNotFound,
                _ => throw new InvalidOperationException($"Unsupported scheduling access status: {access.Status}."),
            });
        }

        var scope = access.Scope
            ?? throw new InvalidOperationException("Successful agenda access must include its scope.");
        var professionals = await transaction.ListProfessionalOptionsAsync(
            scope.TenantId, scope.MembershipId, cancellationToken);
        if (scope.ProfessionalId is not null)
        {
            professionals = professionals
                .Where(item => item.Id == scope.ProfessionalId)
                .ToArray();
        }

        return new(SchedulingProfessionalsStatus.Success, professionals);
    }
}

public enum SchedulingProfessionalsStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    ProfessionalNotFound = 4,
}

public sealed record SchedulingProfessionalsResult(
    SchedulingProfessionalsStatus Status,
    IReadOnlyList<SchedulingProfessionalOption>? Professionals = null);
