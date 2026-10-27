namespace BrothersCare.Application.Identity.Contracts;

public interface ITokenGenerator
{
    GeneratedActionToken Generate();
    bool TryHash(string secret, out byte[] secretHash);
    bool Matches(string secret, byte[] expectedHash);
}

public sealed class GeneratedActionToken(string secret, byte[] secretHash)
{
    public string Secret { get; } = secret;
    public byte[] SecretHash { get; } = secretHash;

    public override string ToString() => "[REDACTED]";
}
