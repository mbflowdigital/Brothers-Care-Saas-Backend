namespace BrothersCare.Application.Identity.Contracts;

public sealed class AuthenticationSettings(
    int maximumFailedAttempts = 5,
    int lockoutMinutes = 15,
    int idleTimeoutMinutes = 30,
    int absoluteLifetimeHours = 12,
    int passwordRecoveryTokenMinutes = 30)
{
    public int MaximumFailedAttempts { get; } = maximumFailedAttempts > 0
        ? maximumFailedAttempts : throw new ArgumentOutOfRangeException(nameof(maximumFailedAttempts));

    public TimeSpan LockoutDuration { get; } = lockoutMinutes > 0
        ? TimeSpan.FromMinutes(lockoutMinutes) : throw new ArgumentOutOfRangeException(nameof(lockoutMinutes));

    public TimeSpan IdleTimeout { get; } = idleTimeoutMinutes > 0
        ? TimeSpan.FromMinutes(idleTimeoutMinutes) : throw new ArgumentOutOfRangeException(nameof(idleTimeoutMinutes));

    public TimeSpan AbsoluteLifetime { get; } = absoluteLifetimeHours > 0
        ? TimeSpan.FromHours(absoluteLifetimeHours) : throw new ArgumentOutOfRangeException(nameof(absoluteLifetimeHours));

    public TimeSpan PasswordRecoveryTokenLifetime { get; } = passwordRecoveryTokenMinutes > 0
        ? TimeSpan.FromMinutes(passwordRecoveryTokenMinutes) : throw new ArgumentOutOfRangeException(nameof(passwordRecoveryTokenMinutes));
}
