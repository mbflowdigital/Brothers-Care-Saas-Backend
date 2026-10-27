using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class CreateTenantWithAdministrator(
    ITenantProvisioningStore store,
    ITokenGenerator tokenGenerator,
    IInvitationSender? invitationSender,
    TimeProvider timeProvider,
    ICurrentPrincipalAccessor principalAccessor,
    IAuthenticationAudit audit)
{
    public async Task<CreateTenantWithAdministratorResult> ExecuteAsync(
        CreateTenantWithAdministratorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var principal = principalAccessor.Principal;
        if (principal is null)
        {
            return new(TenantProvisioningStatus.Unauthenticated);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        if (!await transaction.IsSessionActiveAsync(principal.SessionId, principal.UserId, timeProvider.GetUtcNow(), cancellationToken))
        {
            return new(TenantProvisioningStatus.Unauthenticated);
        }

        if (!await transaction.IsActiveSuperAdministratorAsync(principal.UserId, cancellationToken))
        {
            return new(TenantProvisioningStatus.Forbidden);
        }

        if (!IsValid(command))
        {
            return new(TenantProvisioningStatus.InvalidInput);
        }

        if (invitationSender is null)
        {
            return new(TenantProvisioningStatus.DeliveryUnavailable);
        }

        var now = timeProvider.GetUtcNow();
        var tenant = new Tenant(Guid.NewGuid(), command.TenantName, now);
        transaction.AddTenant(tenant);

        ClinicUnit? unit = null;
        if (command.ClinicUnitName is not null)
        {
            unit = new ClinicUnit(Guid.NewGuid(), tenant.Id, command.ClinicUnitName, now);
            transaction.AddClinicUnit(unit);
        }

        var email = command.AdministratorEmail.Trim();
        var user = await transaction.FindUserAsync(email.ToUpperInvariant(), cancellationToken);
        if (user is null)
        {
            user = new User(Guid.NewGuid(), email, command.AdministratorDisplayName, now);
            transaction.AddUser(user);
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }
        else if (!await transaction.HasCredentialAsync(user.Id, cancellationToken))
        {
            transaction.AddCredential(new UserCredential(Guid.NewGuid(), user.Id, now));
        }

        var membership = TenantMembership.CreateInvitation(
            Guid.NewGuid(), tenant.Id, user.Id, TenantMembershipRole.ClinicAdministrator, now);
        transaction.AddMembership(membership);
        if (unit is not null)
        {
            transaction.AddUnitMembership(new ClinicUnitMembership(
                Guid.NewGuid(), tenant.Id, unit.Id, membership.Id, now));
        }

        var generatedToken = tokenGenerator.Generate();
        var expiresAt = now.AddMinutes(30);
        transaction.AddInvitationToken(new AccountActionToken(
            Guid.NewGuid(), user.Id, AccountActionTokenPurpose.InvitationAcceptance,
            generatedToken.SecretHash, now, expiresAt, membership.Id));

        await transaction.SaveAsync(cancellationToken);
        // Failure to deliver rolls back provisioning. A delivered token is usable only after commit.
        await invitationSender.SendAsync(new InvitationDelivery(
            email, command.AdministratorDisplayName.Trim(), tenant.Name, generatedToken.Secret, expiresAt),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.TenantCreated, now);
        audit.Record(AuthenticationAuditEvent.InvitationIssued, now);

        return new(TenantProvisioningStatus.Created, tenant.Id, unit?.Id);
    }

    private static bool IsValid(CreateTenantWithAdministratorCommand command) =>
        IsRequiredText(command.TenantName, 200) &&
        IsRequiredText(command.AdministratorDisplayName, 200) &&
        (command.ClinicUnitName is null || IsRequiredText(command.ClinicUnitName, 200)) &&
        IsRequiredText(command.AdministratorEmail, 320) &&
        MailAddress.TryCreate(command.AdministratorEmail.Trim(), out var address) &&
        string.Equals(address.Address, command.AdministratorEmail.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsRequiredText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;
}

public sealed record CreateTenantWithAdministratorCommand(
    string TenantName,
    string AdministratorEmail,
    string AdministratorDisplayName,
    string? ClinicUnitName = null);

public sealed record CreateTenantWithAdministratorResult(
    TenantProvisioningStatus Status,
    Guid? TenantId = null,
    Guid? ClinicUnitId = null);

public enum TenantProvisioningStatus
{
    Created = 1,
    Unauthenticated = 2,
    Forbidden = 3,
    InvalidInput = 4,
    DeliveryUnavailable = 5,
}
