namespace BrothersCare.Application.Identity;

public interface IAccountActivationService
{
    Task<AccountActivationResult> ActivateAsync(AccountActivationRequest request, CancellationToken cancellationToken);
}

public sealed record AccountActivationRequest(string Token, string Password, string PasswordConfirmation);

public enum AccountActivationResult
{
    Activated = 1,
    InvalidToken = 2,
    InvalidPassword = 3,
}
