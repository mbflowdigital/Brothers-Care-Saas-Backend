using System.Net.Mail;
using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Tenancy.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Tenancy;

public sealed class CreateCommercialOnboarding(
    ICommercialOnboardingStore store,
    ITokenGenerator tokenGenerator,
    ICommercialOnboardingSender? invitationSender,
    TimeProvider timeProvider,
    DevelopmentCommercialOnboardingBypass? developmentBypass = null)
{
    public async Task<CommercialOnboardingResult> ExecuteAsync(
        CreateCommercialOnboardingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TryValidate(command, out var cnpj))
        {
            return new(CommercialOnboardingStatus.InvalidInput);
        }

        if (invitationSender is null && developmentBypass is null)
        {
            return new(CommercialOnboardingStatus.DeliveryUnavailable);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        if (await transaction.FindTenantByCnpjAsync(cnpj!, cancellationToken) is not null)
        {
            return new(CommercialOnboardingStatus.CnpjAlreadyRegistered);
        }

        var now = timeProvider.GetUtcNow();
        var tenant = Tenant.CreatePendingOnboarding(Guid.NewGuid(), command.CompanyName, cnpj!, now);
        transaction.AddTenant(tenant);

        var email = command.ResponsibleEmail.Trim();
        var user = await transaction.FindUserByEmailAsync(email.ToUpperInvariant(), cancellationToken);
        if (user is null)
        {
            user = new User(Guid.NewGuid(), email, command.ResponsibleName, now);
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
        var generatedToken = tokenGenerator.Generate();
        var expiresAt = now.AddMinutes(30);
        transaction.AddToken(new AccountActionToken(
            Guid.NewGuid(), user.Id, AccountActionTokenPurpose.CommercialOnboarding,
            generatedToken.SecretHash, now, expiresAt, membership.Id));

        await transaction.SaveAsync(cancellationToken);
        if (invitationSender is not null)
        {
            try
            {
                await invitationSender.SendAsync(new InvitationDelivery(
                    email, command.ResponsibleName.Trim(), tenant.Name, generatedToken.Secret, expiresAt), cancellationToken);
            }
            catch when (developmentBypass is not null)
            {
                // Development keeps the generated link available in the Super Admin screen.
            }
        }
        await transaction.CommitAsync(cancellationToken);
        return new(CommercialOnboardingStatus.Created, tenant.Id,
            developmentBypass is null ? null : generatedToken.Secret);
    }

    private static bool TryValidate(CreateCommercialOnboardingCommand command, out string? cnpj)
    {
        cnpj = null;
        if (!IsRequiredText(command.CompanyName, 200) || !IsRequiredText(command.ResponsibleName, 200) ||
            !IsRequiredText(command.ResponsibleEmail, 320) ||
            !MailAddress.TryCreate(command.ResponsibleEmail.Trim(), out var email) ||
            !string.Equals(email.Address, command.ResponsibleEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            cnpj = Tenant.NormalizeCnpj(command.Cnpj);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsRequiredText(string value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;
}

public sealed record CreateCommercialOnboardingCommand(
    string CompanyName,
    string Cnpj,
    string ResponsibleName,
    string ResponsibleEmail)
{
    public override string ToString() => "[REDACTED]";
}

public enum CommercialOnboardingStatus
{
    Created = 1,
    InvalidInput = 2,
    CnpjAlreadyRegistered = 3,
    DeliveryUnavailable = 4,
}

public sealed record CommercialOnboardingResult(
    CommercialOnboardingStatus Status,
    Guid? TenantId = null,
    string? DevelopmentToken = null)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record DevelopmentCommercialOnboardingBypass;
