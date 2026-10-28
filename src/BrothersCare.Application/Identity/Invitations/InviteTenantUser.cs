using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Identity.Invitations;

public sealed class InviteTenantUser(
    ICurrentPrincipalAccessor principalAccessor,
    ITenantInvitationStore store,
    ITenantInvitationPlanPolicy planPolicy,
    ITokenGenerator tokenGenerator,
    IInvitationSender? sender,
    TimeProvider timeProvider,
    IAuthenticationAudit audit)
{
    public async Task<InvitationStatus> ExecuteAsync(InviteTenantUserCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var principal = principalAccessor.Principal;
        if (principal is null)
        {
            return InvitationStatus.Unauthenticated;
        }

        if (principal.ActiveTenantId is not { } tenantId)
        {
            return InvitationStatus.Forbidden;
        }

        if (!IsValid(command, out var role))
        {
            return InvitationStatus.InvalidInput;
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var session = await transaction.FindSessionAsync(principal.SessionId, cancellationToken);
        if (session is null || session.UserId != principal.UserId || !session.IsValidAt(timeProvider.GetUtcNow()) ||
            !await transaction.HasActiveCredentialAsync(principal.UserId, cancellationToken))
        {
            return InvitationStatus.Unauthenticated;
        }

        if (session.ActiveTenantId != tenantId)
        {
            return InvitationStatus.ContextChanged;
        }

        var tenant = await transaction.FindTenantAsync(tenantId, cancellationToken);
        var actor = await transaction.FindMembershipAsync(tenantId, principal.UserId, cancellationToken);
        if (tenant?.Status != TenantStatus.Active || actor?.Status != MembershipStatus.Active ||
            actor.Role != TenantMembershipRole.ClinicAdministrator)
        {
            return InvitationStatus.Forbidden;
        }

        if (!await transaction.AreUnitsActiveAsync(tenantId, command.ClinicUnitIds, cancellationToken))
        {
            return InvitationStatus.UnitNotFound;
        }

        var allowedUnits = (await transaction.GetActiveUnitIdsAsync(tenantId, actor.Id, cancellationToken)).ToHashSet();
        if (command.ClinicUnitIds.Any(id => !allowedUnits.Contains(id)))
        {
            return InvitationStatus.Forbidden;
        }

        var user = await transaction.FindUserAsync(command.Email.Trim().ToUpperInvariant(), cancellationToken);
        var membership = user is null ? null : await transaction.FindMembershipAsync(tenantId, user.Id, cancellationToken);
        if (membership?.Status is MembershipStatus.Active or MembershipStatus.Suspended)
        {
            return InvitationStatus.MembershipConflict;
        }

        if (membership is not null)
        {
            var currentUnits = await transaction.GetMembershipUnitIdsAsync(tenantId, membership.Id, cancellationToken);
            if (currentUnits.Any(id => !allowedUnits.Contains(id)))
            {
                return InvitationStatus.Forbidden;
            }
        }

        var plan = await planPolicy.EvaluateAsync(tenantId, membership?.Id, role, command.ClinicUnitIds, cancellationToken);
        if (plan != InvitationPlanDecision.Allowed)
        {
            return plan == InvitationPlanDecision.LimitExceeded ? InvitationStatus.PlanLimitExceeded : InvitationStatus.PlanUnavailable;
        }

        if (sender is null)
        {
            return InvitationStatus.DeliveryUnavailable;
        }

        var now = timeProvider.GetUtcNow();
        if (!session.IsValidAt(now))
        {
            return InvitationStatus.Unauthenticated;
        }

        if (user is null)
        {
            user = new User(Guid.NewGuid(), command.Email, command.DisplayName, now);
            transaction.AddUser(user);
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }
        else if (!await transaction.HasCredentialAsync(user.Id, cancellationToken))
        {
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }

        var renewed = membership is not null;
        if (membership is null)
        {
            membership = TenantMembership.CreateInvitation(Guid.NewGuid(), tenantId, user.Id, role, now);
            transaction.AddMembership(membership);
        }
        else
        {
            membership.RenewInvitation(role);
        }

        await transaction.SetUnitMembershipsAsync(tenantId, membership.Id, command.ClinicUnitIds, now, cancellationToken);
        await transaction.RevokePreviousInvitationsAsync(membership.Id, now, cancellationToken);
        await transaction.RevokeTenantSessionsAsync(tenantId, user.Id, now, cancellationToken);
        var generated = tokenGenerator.Generate();
        var expiresAt = now.AddMinutes(30);
        transaction.AddToken(new AccountActionToken(Guid.NewGuid(), user.Id,
            AccountActionTokenPurpose.InvitationAcceptance, generated.SecretHash, now, expiresAt, membership.Id));
        await transaction.SaveAsync(cancellationToken);
        await sender.SendAsync(new InvitationDelivery(user.Email, command.DisplayName.Trim(), tenant.Name, generated.Secret, expiresAt), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        audit.Record(renewed ? AuthenticationAuditEvent.InvitationReissued : AuthenticationAuditEvent.InvitationIssued, now);
        return InvitationStatus.Accepted;
    }

    private static bool IsValid(InviteTenantUserCommand command, out TenantMembershipRole role)
    {
        role = default;
        return !string.IsNullOrWhiteSpace(command.Email) && command.Email.Length <= 320 &&
            MailAddress.TryCreate(command.Email.Trim(), out var email) &&
            string.Equals(email.Address, command.Email.Trim(), StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(command.DisplayName) && command.DisplayName.Length <= 200 &&
            Enum.TryParse(command.Role, out role) && Enum.IsDefined(role) && role.ToString() == command.Role &&
            command.ClinicUnitIds is not null && command.ClinicUnitIds.All(id => id != Guid.Empty) &&
            command.ClinicUnitIds.Distinct().Count() == command.ClinicUnitIds.Count;
    }
}

public sealed record InviteTenantUserCommand(string Email, string DisplayName, string Role, IReadOnlyList<Guid> ClinicUnitIds)
{
    public override string ToString() => "[REDACTED]";
}

public enum InvitationStatus
{
    Accepted = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    UnitNotFound = 5,
    MembershipConflict = 6,
    ContextChanged = 7,
    PlanLimitExceeded = 8,
    PlanUnavailable = 9,
    DeliveryUnavailable = 10,
}
