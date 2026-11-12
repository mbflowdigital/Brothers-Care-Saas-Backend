using BrothersCare.Application.Identity.Contracts;
using BrothersCare.Application.Onboarding.Contracts;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;

namespace BrothersCare.Application.Onboarding;

public sealed class CompleteOnboarding(
    IOnboardingCompletionStore store,
    IPasswordHasher passwordHasher,
    ITokenGenerator tokenGenerator,
    TimeProvider timeProvider,
    IAuthenticationAudit audit)
{
    public async Task<OnboardingCompletionResult> ExecuteAsync(
        CompleteOnboardingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!TryValidateRequest(command, out var cpf, out var cnpj, out var tokenHash))
        {
            return new(OnboardingCompletionStatus.InvalidInput);
        }

        await using var transaction = await store.BeginAsync(cancellationToken);
        var token = await transaction.FindTokenAsync(tokenHash!, cancellationToken);
        var tokenStatus = ValidateToken(token, command.Token);
        if (tokenStatus is not OnboardingCompletionStatus.Completed)
        {
            return new(tokenStatus);
        }

        var now = timeProvider.GetUtcNow();
        if (token!.ExpiresAt <= now)
        {
            return new(OnboardingCompletionStatus.ExpiredToken);
        }

        var membershipId = token.TenantMembershipId!.Value;
        var user = await transaction.FindUserAsync(token.UserId, cancellationToken);
        var membership = await transaction.FindMembershipAsync(membershipId, token.UserId, cancellationToken);
        if (user is null || membership is null || membership.Status != MembershipStatus.Invited ||
            membership.Role != TenantMembershipRole.ClinicAdministrator)
        {
            return new(OnboardingCompletionStatus.InvalidToken);
        }

        var tenant = await transaction.FindTenantAsync(membership.TenantId, cancellationToken);
        if (tenant is null || tenant.Status != TenantStatus.PendingOnboarding || tenant.Cnpj is null ||
            await transaction.HasClinicUnitAsync(tenant.Id, cancellationToken))
        {
            return new(OnboardingCompletionStatus.InvalidState);
        }

        if (!string.Equals(tenant.Cnpj, cnpj, StringComparison.Ordinal))
        {
            return new(OnboardingCompletionStatus.CnpjMismatch);
        }

        var existingCpf = await transaction.FindUserByCpfAsync(cpf!, cancellationToken);
        if (existingCpf is not null && existingCpf.Id != user.Id)
        {
            return new(OnboardingCompletionStatus.CpfUnavailable);
        }

        if (user.Cpf is not null && user.Cpf != cpf)
        {
            return new(OnboardingCompletionStatus.CpfUnavailable);
        }

        var credential = await transaction.FindCredentialAsync(user.Id, cancellationToken);
        if (credential is null || credential.Status is UserCredentialStatus.Disabled or UserCredentialStatus.Locked)
        {
            return new(OnboardingCompletionStatus.InvalidCredentials);
        }

        var needsActivation = credential.Status == UserCredentialStatus.PendingActivation;
        if (needsActivation && !IsValidNewPassword(
                command.Administrator.Password, command.Administrator.PasswordConfirmation))
        {
            return new(OnboardingCompletionStatus.InvalidInput);
        }

        if (!needsActivation && (credential.Status != UserCredentialStatus.Active || credential.PasswordHash is null ||
            !IsValidExistingPassword(command.Administrator.Password) ||
            !passwordHasher.Verify(user, credential.PasswordHash, command.Administrator.Password)))
        {
            return new(OnboardingCompletionStatus.InvalidCredentials);
        }

        try
        {
            user.SetDisplayName(command.Administrator.DisplayName);
            user.SetCpf(cpf!);
            tenant.CompleteProfile(
                command.Company.LegalName,
                command.Company.TradeName,
                command.Company.CommercialEmail,
                command.Company.Phone);

            var unit = new ClinicUnit(Guid.NewGuid(), tenant.Id, command.ClinicUnit.Name, now);
            unit.ConfigureContactAndAddress(
                command.ClinicUnit.PostalCode,
                command.ClinicUnit.Street,
                command.ClinicUnit.Number,
                command.ClinicUnit.Complement,
                command.ClinicUnit.District,
                command.ClinicUnit.City,
                command.ClinicUnit.State,
                command.ClinicUnit.Phone);
            unit.MarkAsDefault();

            if (needsActivation)
            {
                credential.Activate(passwordHasher.Hash(user, command.Administrator.Password), now);
                await transaction.RevokeSessionsAsync(user.Id, now, cancellationToken);
            }

            membership.AcceptInvitation();
            tenant.CompleteOnboarding();
            transaction.AddClinicUnit(unit);
            transaction.AddClinicUnitMembership(new ClinicUnitMembership(
                Guid.NewGuid(), tenant.Id, unit.Id, membership.Id, now));
            token.Consume(now);
        }
        catch (ArgumentException)
        {
            return new(OnboardingCompletionStatus.InvalidInput);
        }
        catch (InvalidOperationException)
        {
            return new(OnboardingCompletionStatus.InvalidState);
        }

        await transaction.CommitAsync(cancellationToken);
        audit.Record(AuthenticationAuditEvent.OnboardingCompleted, now);
        return new(OnboardingCompletionStatus.Completed, tenant.Id);
    }

    private OnboardingCompletionStatus ValidateToken(AccountActionToken? token, string presentedToken)
    {
        if (token is null || token.Purpose != AccountActionTokenPurpose.CommercialOnboarding ||
            token.TenantMembershipId is null || !tokenGenerator.Matches(presentedToken, token.SecretHash))
        {
            return OnboardingCompletionStatus.InvalidToken;
        }

        if (token.ConsumedAt is not null) return OnboardingCompletionStatus.UsedToken;
        if (token.RevokedAt is not null) return OnboardingCompletionStatus.RevokedToken;
        return OnboardingCompletionStatus.Completed;
    }

    private bool TryValidateRequest(
        CompleteOnboardingCommand command, out string? cpf, out string? cnpj, out byte[]? tokenHash)
    {
        cpf = null;
        cnpj = null;
        tokenHash = null;
        if (string.IsNullOrWhiteSpace(command.Token) || command.Token.Length > 512 ||
            !tokenGenerator.TryHash(command.Token, out tokenHash) ||
            !IsRequired(command.Administrator.DisplayName, 200) ||
            string.IsNullOrEmpty(command.Administrator.Password) || command.Administrator.Password.Length > 128 ||
            !IsRequired(command.Company.LegalName, 200) || !IsRequired(command.Company.TradeName, 200) ||
            !IsRequired(command.Company.CommercialEmail, 320) || !IsRequired(command.Company.Phone, 32) ||
            !IsRequired(command.ClinicUnit.Name, 200) || !IsRequired(command.ClinicUnit.PostalCode, 16) ||
            !IsRequired(command.ClinicUnit.Street, 200) || !IsRequired(command.ClinicUnit.Number, 20) ||
            command.ClinicUnit.Complement?.Length > 100 || !IsRequired(command.ClinicUnit.District, 100) ||
            !IsRequired(command.ClinicUnit.City, 100) || !IsRequired(command.ClinicUnit.State, 2) ||
            !IsRequired(command.ClinicUnit.Phone, 32))
        {
            return false;
        }

        try
        {
            cpf = User.NormalizeCpf(command.Administrator.Cpf);
            cnpj = Tenant.NormalizeCnpj(command.Cnpj);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool IsValidNewPassword(string password, string confirmation) =>
        password.Length is >= 12 and <= 128 && confirmation.Length <= 128 &&
        password.Any(char.IsUpper) && password.Any(char.IsLower) && password.Any(char.IsDigit) &&
        string.Equals(password, confirmation, StringComparison.Ordinal);

    private static bool IsValidExistingPassword(string password) =>
        !string.IsNullOrEmpty(password) && password.Length <= 128;

    private static bool IsRequired(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;
}

public sealed record CompleteOnboardingCommand(
    string Token,
    string Cnpj,
    OnboardingAdministratorData Administrator,
    OnboardingCompanyData Company,
    OnboardingClinicUnitData ClinicUnit)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record OnboardingAdministratorData(
    string DisplayName,
    string Cpf,
    string Password,
    string PasswordConfirmation)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record OnboardingCompanyData(
    string LegalName,
    string TradeName,
    string CommercialEmail,
    string Phone)
{
    public override string ToString() => "[REDACTED]";
}

public sealed record OnboardingClinicUnitData(
    string Name,
    string PostalCode,
    string Street,
    string Number,
    string? Complement,
    string District,
    string City,
    string State,
    string Phone)
{
    public override string ToString() => "[REDACTED]";
}

public enum OnboardingCompletionStatus
{
    Completed = 1,
    InvalidInput = 2,
    InvalidToken = 3,
    ExpiredToken = 4,
    RevokedToken = 5,
    UsedToken = 6,
    CpfUnavailable = 7,
    InvalidCredentials = 8,
    InvalidState = 9,
    CnpjMismatch = 10,
}

public sealed record OnboardingCompletionResult(OnboardingCompletionStatus Status, Guid? TenantId = null);
