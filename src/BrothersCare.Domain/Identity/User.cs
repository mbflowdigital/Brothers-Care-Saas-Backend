namespace BrothersCare.Domain.Identity;

public sealed class User
{
    private User()
    {
    }

    public User(Guid id, string email, string displayName, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("User id is required.", nameof(id));
        }

        Id = id;
        SetEmail(email);
        SetDisplayName(displayName);
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string? Cpf { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public void SetEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        Email = email.Trim();
        NormalizedEmail = Email.ToUpperInvariant();
    }

    public void SetDisplayName(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        DisplayName = displayName.Trim();
    }

    public void SetCpf(string cpf) => Cpf = NormalizeCpf(cpf);

    public static string NormalizeCpf(string cpf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cpf);
        var normalized = NormalizeDigits(cpf, 11, nameof(cpf));
        if (normalized.Distinct().Count() == 1)
        {
            throw new ArgumentException("CPF is invalid.", nameof(cpf));
        }

        var firstDigit = CalculateDigit(normalized[..9], 10);
        var secondDigit = CalculateDigit(normalized[..9] + firstDigit, 11);
        if (normalized[9] != firstDigit || normalized[10] != secondDigit)
        {
            throw new ArgumentException("CPF is invalid.", nameof(cpf));
        }

        return normalized;
    }

    private static string NormalizeDigits(string value, int length, string parameterName)
    {
        if (value.Any(item => !char.IsDigit(item) && item is not '.' and not '-' and not ' '))
        {
            throw new ArgumentException("Document contains invalid characters.", parameterName);
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        if (digits.Length != length)
        {
            throw new ArgumentException("Document has an invalid length.", parameterName);
        }

        return digits;
    }

    private static char CalculateDigit(string value, int initialWeight)
    {
        var sum = value.Select((digit, index) => (digit - '0') * (initialWeight - index)).Sum();
        var remainder = sum % 11;
        return remainder < 2 ? '0' : (char)('0' + 11 - remainder);
    }
}
