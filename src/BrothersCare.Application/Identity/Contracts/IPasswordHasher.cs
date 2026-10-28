using BrothersCare.Domain.Identity;

namespace BrothersCare.Application.Identity.Contracts;

public interface IPasswordHasher
{
    string Hash(User user, string password);
    bool Verify(User user, string passwordHash, string password);
    void VerifyDummy(string password);
}
