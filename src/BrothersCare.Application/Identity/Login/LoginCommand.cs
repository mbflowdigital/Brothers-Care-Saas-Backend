namespace BrothersCare.Application.Identity.Login;

public sealed record LoginCommand(string Email, string Password)
{
    public override string ToString() => "[REDACTED]";
}
