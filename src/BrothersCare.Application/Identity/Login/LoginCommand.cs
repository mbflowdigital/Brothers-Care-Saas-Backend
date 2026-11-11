namespace BrothersCare.Application.Identity.Login;

public sealed record LoginCommand(string Cpf, string Password)
{
    public override string ToString() => "[REDACTED]";
}
