using System.Security.Cryptography;
using BrothersCare.Application.Identity.Contracts;
using Microsoft.AspNetCore.WebUtilities;

namespace BrothersCare.Infrastructure.Identity;

public sealed class InvitationTokenService : ITokenGenerator
{
    public GeneratedActionToken Generate()
    {
        var secret = RandomNumberGenerator.GetBytes(32);
        return new GeneratedActionToken(WebEncoders.Base64UrlEncode(secret), SHA256.HashData(secret));
    }

    public bool TryHash(string secret, out byte[] secretHash)
    {
        secretHash = [];
        if (string.IsNullOrEmpty(secret) || secret.Length != 43)
        {
            return false;
        }

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(secret);
            if (bytes.Length != 32 || !string.Equals(WebEncoders.Base64UrlEncode(bytes), secret, StringComparison.Ordinal))
            {
                return false;
            }

            secretHash = SHA256.HashData(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool Matches(string secret, byte[] expectedHash) =>
        TryHash(secret, out var hash) && CryptographicOperations.FixedTimeEquals(hash, expectedHash);
}
