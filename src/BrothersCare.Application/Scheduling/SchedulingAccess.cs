using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Scheduling.Contracts;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Scheduling;

internal static class SchedulingAccess
{
    public static async Task<SchedulingAccessResult> ResolveProfessionalAsync(
        CurrentPrincipal? principal,
        IAppointmentTransaction transaction,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var membership = await ResolveActiveMembershipAsync(
            principal, transaction, timeProvider, cancellationToken);
        if (membership.Status != SchedulingAccessStatus.Success)
        {
            return new(membership.Status);
        }

        if (membership.Role != TenantMembershipRole.Professional)
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        var professional = await transaction.FindActiveProfessionalAsync(
            membership.TenantId, principal!.UserId, membership.MembershipId, cancellationToken);
        return professional is null
            ? new(SchedulingAccessStatus.ProfessionalNotFound)
            : new(SchedulingAccessStatus.Success,
                new(membership.TenantId, membership.MembershipId, professional));
    }

    public static async Task<SchedulingAccessResult> ResolveAppointmentManagerAsync(
        CurrentPrincipal? principal,
        Guid? targetProfessionalId,
        IAppointmentTransaction transaction,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var membership = await ResolveActiveMembershipAsync(
            principal, transaction, timeProvider, cancellationToken);
        if (membership.Status != SchedulingAccessStatus.Success)
        {
            return new(membership.Status);
        }

        SchedulingProfessional? professional;
        if (membership.Role == TenantMembershipRole.Professional)
        {
            professional = await transaction.FindActiveProfessionalAsync(
                membership.TenantId, principal!.UserId, membership.MembershipId, cancellationToken);
            if (professional is not null && targetProfessionalId is { } requestedId && requestedId != professional.Id)
            {
                return new(SchedulingAccessStatus.Forbidden);
            }
        }
        else if (membership.Role is TenantMembershipRole.ClinicAdministrator or TenantMembershipRole.Receptionist)
        {
            if (targetProfessionalId is not { } requestedId || requestedId == Guid.Empty)
            {
                return new(SchedulingAccessStatus.ProfessionalNotFound);
            }

            professional = await transaction.FindActiveProfessionalByIdAsync(
                membership.TenantId, requestedId, cancellationToken);
        }
        else
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        return professional is null
            ? new(SchedulingAccessStatus.ProfessionalNotFound)
            : new(SchedulingAccessStatus.Success,
                new(membership.TenantId, membership.MembershipId, professional));
    }

    public static async Task<SchedulingAgendaAccessResult> ResolveAgendaViewerAsync(
        CurrentPrincipal? principal,
        IAppointmentTransaction transaction,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var membership = await ResolveActiveMembershipAsync(
            principal, transaction, timeProvider, cancellationToken);
        if (membership.Status != SchedulingAccessStatus.Success)
        {
            return new(membership.Status);
        }

        if (membership.Role is TenantMembershipRole.ClinicAdministrator or TenantMembershipRole.Receptionist)
        {
            return new(SchedulingAccessStatus.Success,
                new SchedulingAgendaScope(membership.TenantId, membership.MembershipId, null, IsClinicAdministrator: true));
        }

        if (membership.Role != TenantMembershipRole.Professional)
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        var professional = await transaction.FindActiveProfessionalAsync(
            membership.TenantId, principal!.UserId, membership.MembershipId, cancellationToken);
        return professional is null
            ? new(SchedulingAccessStatus.ProfessionalNotFound)
            : new(SchedulingAccessStatus.Success,
                new SchedulingAgendaScope(membership.TenantId, membership.MembershipId, professional.Id, IsClinicAdministrator: false));
    }

    public static async Task<SchedulingAvailabilityAccessResult> ResolveAvailabilityManagerAsync(
        CurrentPrincipal? principal,
        Guid targetProfessionalId,
        IAppointmentTransaction transaction,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var membership = await ResolveActiveMembershipAsync(
            principal, transaction, timeProvider, cancellationToken);
        if (membership.Status != SchedulingAccessStatus.Success)
        {
            return new(membership.Status);
        }

        SchedulingProfessional? professional;
        if (membership.Role == TenantMembershipRole.ClinicAdministrator)
        {
            professional = await transaction.FindActiveProfessionalByIdAsync(
                membership.TenantId, targetProfessionalId, cancellationToken);
        }
        else if (membership.Role == TenantMembershipRole.Professional)
        {
            professional = await transaction.FindActiveProfessionalAsync(
                membership.TenantId, principal!.UserId, membership.MembershipId, cancellationToken);
            if (professional is not null && professional.Id != targetProfessionalId)
            {
                return new(SchedulingAccessStatus.Forbidden);
            }
        }
        else
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        return professional is null
            ? new(SchedulingAccessStatus.ProfessionalNotFound)
            : new(SchedulingAccessStatus.Success, new(
                membership.TenantId,
                membership.MembershipId,
                professional,
                membership.Role == TenantMembershipRole.ClinicAdministrator));
    }

    private static async Task<SchedulingMembershipResult> ResolveActiveMembershipAsync(
        CurrentPrincipal? principal,
        IAppointmentTransaction transaction,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (principal?.ActiveTenantId is not { } tenantId ||
            principal.TenantMembershipId is not { } membershipId)
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(now) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return new(SchedulingAccessStatus.Unauthenticated);
        }

        if (session.ActiveTenantId != tenantId)
        {
            return new(SchedulingAccessStatus.Forbidden);
        }

        var role = await transaction.FindActiveMembershipRoleAsync(
            tenantId, principal.UserId, membershipId, cancellationToken);
        return role is null
            ? new(SchedulingAccessStatus.Forbidden)
            : new(SchedulingAccessStatus.Success, tenantId, membershipId, role);
    }
}

internal enum SchedulingAccessStatus
{
    Success = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    ProfessionalNotFound = 4,
}

internal sealed record SchedulingAccessResult(
    SchedulingAccessStatus Status,
    SchedulingScope? Scope = null);

internal sealed record SchedulingScope(
    Guid TenantId,
    Guid MembershipId,
    SchedulingProfessional Professional);

internal sealed record SchedulingAgendaScope(
    Guid TenantId,
    Guid MembershipId,
    Guid? ProfessionalId,
    bool IsClinicAdministrator);

internal sealed record SchedulingAgendaAccessResult(
    SchedulingAccessStatus Status,
    SchedulingAgendaScope? Scope = null);

internal sealed record SchedulingAvailabilityScope(
    Guid TenantId,
    Guid ActorMembershipId,
    SchedulingProfessional Professional,
    bool IsClinicAdministrator);

internal sealed record SchedulingAvailabilityAccessResult(
    SchedulingAccessStatus Status,
    SchedulingAvailabilityScope? Scope = null);

internal sealed record SchedulingMembershipResult(
    SchedulingAccessStatus Status,
    Guid TenantId = default,
    Guid MembershipId = default,
    TenantMembershipRole? Role = null);
