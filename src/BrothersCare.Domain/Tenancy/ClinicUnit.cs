namespace BrothersCare.Domain.Tenancy;

public sealed class ClinicUnit
{
    private ClinicUnit()
    {
    }

    public ClinicUnit(Guid id, Guid tenantId, string name, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Unit and tenant ids are required.");
        }

        Id = id;
        TenantId = tenantId;
        Rename(name);
        CreatedAt = createdAt;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    public bool IsDefault { get; private set; }

    public string? PostalCode { get; private set; }

    public string? Street { get; private set; }

    public string? Number { get; private set; }

    public string? Complement { get; private set; }

    public string? District { get; private set; }

    public string? City { get; private set; }

    public string? State { get; private set; }

    public string? Phone { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
    }

    public void ConfigureContactAndAddress(
        string postalCode,
        string street,
        string number,
        string? complement,
        string district,
        string city,
        string state,
        string phone)
    {
        var normalizedPostalCode = NormalizeDigits(postalCode, 8, nameof(postalCode));
        var normalizedStreet = NormalizeRequiredText(street, 200, nameof(street));
        var normalizedNumber = NormalizeRequiredText(number, 20, nameof(number));
        var normalizedComplement = NormalizeOptionalText(complement, 100, nameof(complement));
        var normalizedDistrict = NormalizeRequiredText(district, 100, nameof(district));
        var normalizedCity = NormalizeRequiredText(city, 100, nameof(city));
        var normalizedState = NormalizeState(state);
        var normalizedPhone = NormalizePhone(phone);

        PostalCode = normalizedPostalCode;
        Street = normalizedStreet;
        Number = normalizedNumber;
        Complement = normalizedComplement;
        District = normalizedDistrict;
        City = normalizedCity;
        State = normalizedState;
        Phone = normalizedPhone;
    }

    public void MarkAsDefault()
    {
        if (!IsActive)
        {
            throw new InvalidOperationException("Only an active clinic unit can be the default unit.");
        }

        IsDefault = true;
    }

    public void RemoveDefault() => IsDefault = false;

    public void Activate() => IsActive = true;

    public void Deactivate()
    {
        if (IsDefault)
        {
            throw new InvalidOperationException("The default clinic unit cannot be deactivated.");
        }

        IsActive = false;
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

    private static string? NormalizeOptionalText(string? value, int maximumLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"Value cannot exceed {maximumLength} characters.", parameterName);
        }

        return normalized;
    }

    private static string NormalizeDigits(string value, int length, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(character => !char.IsDigit(character) && character is not ' ' and not '-'))
        {
            throw new ArgumentException("Value contains invalid characters.", parameterName);
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length != length)
        {
            throw new ArgumentException($"Value must contain {length} digits.", parameterName);
        }

        return digits;
    }

    private static string NormalizeState(string value)
    {
        var normalized = NormalizeRequiredText(value, 2, nameof(value)).ToUpperInvariant();
        if (normalized.Length != 2 || normalized.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("State must be a two-letter code.", nameof(value));
        }

        return normalized;
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
