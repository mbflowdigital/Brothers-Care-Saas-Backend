using System.Net.Mail;

namespace BrothersCare.Domain.Tenancy;

public sealed class Tenant
{
    private Tenant()
    {
    }

    public Tenant(Guid id, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Tenant id is required.", nameof(id));
        }

        Id = id;
        Rename(name);
        CreatedAt = createdAt;
        Status = TenantStatus.Active;
    }

    public static Tenant CreatePendingOnboarding(Guid id, string name, string cnpj, DateTimeOffset createdAt)
    {
        var tenant = new Tenant(id, name, createdAt);
        tenant.SetCnpj(cnpj);
        tenant.Status = TenantStatus.PendingOnboarding;
        return tenant;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Cnpj { get; private set; }

    public string? LegalName { get; private set; }

    public string? CommercialEmail { get; private set; }

    public string? Phone { get; private set; }

    public TenantStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void SetCnpj(string cnpj) => Cnpj = NormalizeCnpj(cnpj);

    public void CompleteProfile(string legalName, string tradeName, string commercialEmail, string phone)
    {
        var normalizedLegalName = NormalizeRequiredText(legalName, 200, nameof(legalName));
        var normalizedTradeName = NormalizeRequiredText(tradeName, 200, nameof(tradeName));
        var normalizedEmail = NormalizeEmail(commercialEmail);
        var normalizedPhone = NormalizePhone(phone);

        LegalName = normalizedLegalName;
        Rename(normalizedTradeName);
        CommercialEmail = normalizedEmail;
        Phone = normalizedPhone;
    }

    public static string NormalizeCnpj(string cnpj)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cnpj);
        if (cnpj.Any(item => !char.IsDigit(item) && item is not '.' and not '-' and not '/' and not ' '))
        {
            throw new ArgumentException("CNPJ contains invalid characters.", nameof(cnpj));
        }

        var digits = new string(cnpj.Where(char.IsDigit).ToArray());
        if (digits.Length != 14 || digits.Distinct().Count() == 1)
        {
            throw new ArgumentException("CNPJ is invalid.", nameof(cnpj));
        }

        var firstDigit = CalculateDigit(digits[..12], [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        var secondDigit = CalculateDigit(digits[..12] + firstDigit, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
        if (digits[12] != firstDigit || digits[13] != secondDigit)
        {
            throw new ArgumentException("CNPJ is invalid.", nameof(cnpj));
        }

        return digits;
    }

    public void Suspend()
    {
        if (Status is not TenantStatus.Active)
        {
            throw new InvalidOperationException("Only an active tenant can be suspended.");
        }

        Status = TenantStatus.Suspended;
    }

    public void Reactivate()
    {
        if (Status is not TenantStatus.Suspended)
        {
            throw new InvalidOperationException("Only a suspended tenant can be reactivated.");
        }

        Status = TenantStatus.Active;
    }

    public void CompleteOnboarding()
    {
        if (Status is not TenantStatus.PendingOnboarding)
        {
            throw new InvalidOperationException("Only a tenant pending onboarding can complete onboarding.");
        }

        if (Cnpj is null || LegalName is null || CommercialEmail is null || Phone is null)
        {
            throw new InvalidOperationException("The tenant profile must be complete before activation.");
        }

        Status = TenantStatus.Active;
    }

    public void Deactivate()
    {
        if (Status is TenantStatus.Deactivated)
        {
            throw new InvalidOperationException("The tenant is already deactivated.");
        }

        Status = TenantStatus.Deactivated;
    }

    private static char CalculateDigit(string value, IReadOnlyList<int> weights)
    {
        var sum = value.Select((digit, index) => (digit - '0') * weights[index]).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? '0' : (char)('0' + 11 - remainder);
    }

    private static string NormalizeRequiredText(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeEmail(string value)
    {
        var normalized = NormalizeRequiredText(value, 320, nameof(value));
        if (!MailAddress.TryCreate(normalized, out var address) ||
            !string.Equals(address.Address, normalized, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Commercial email is invalid.", nameof(value));
        }

        return address.Address;
    }

    private static string NormalizePhone(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Any(character => !char.IsDigit(character) && character is not ' ' and not '(' and not ')' and not '-' and not '+'))
        {
            throw new ArgumentException("Phone contains invalid characters.", nameof(value));
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length is < 10 or > 11)
        {
            throw new ArgumentException("Phone must contain 10 or 11 digits.", nameof(value));
        }

        return digits;
    }
}

public enum TenantStatus
{
    Active = 1,
    Suspended = 2,
    Deactivated = 3,
    PendingOnboarding = 4,
}
