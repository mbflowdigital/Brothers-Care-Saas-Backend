using System.Diagnostics.CodeAnalysis;

namespace BrothersCare.Application.Identity.Sessions;

public sealed class SessionToken
{
    private SessionToken(Guid sessionId, string secret)
    {
        SessionId = sessionId;
        Secret = secret;
    }

    public Guid SessionId { get; }
    public string Secret { get; }

    public static bool TryParse(string? value, [NotNullWhen(true)] out SessionToken? token)
    {
        token = null;
        if (value is null || value.Length != 80 || value[36] != '.' ||
            !Guid.TryParseExact(value.AsSpan(0, 36), "D", out var sessionId) || sessionId == Guid.Empty)
        {
            return false;
        }

        var secret = value[37..];
        if (!secret.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
        {
            return false;
        }

        token = new SessionToken(sessionId, secret);
        return true;
    }

    public override string ToString() => "[REDACTED]";
}
