using System.Security.Cryptography;
using BrothersCare.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using IPasswordHasher = BrothersCare.Application.Identity.Contracts.IPasswordHasher;

namespace BrothersCare.Infrastructure.Identity;

public sealed class PasswordHasher : IPasswordHasher
{
    private static readonly PasswordHasher<User> Hasher = new();
    private static readonly User DummyUser = new(Guid.NewGuid(), "unavailable@example.invalid", "Identity verification", DateTimeOffset.UnixEpoch);
    private static readonly string DummyHash = Hasher.HashPassword(DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    public string Hash(User user, string password) => Hasher.HashPassword(user, password);

    public bool Verify(User user, string passwordHash, string password) =>
        Hasher.VerifyHashedPassword(user, passwordHash, password) != PasswordVerificationResult.Failed;

    public void VerifyDummy(string password) => Hasher.VerifyHashedPassword(DummyUser, DummyHash, password);
}
