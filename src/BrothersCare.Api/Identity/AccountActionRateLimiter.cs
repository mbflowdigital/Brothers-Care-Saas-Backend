using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;

namespace BrothersCare.Api.Identity;

public sealed class AccountActionRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> activation;
    private readonly PartitionedRateLimiter<string> confirmation;
    private readonly PartitionedRateLimiter<string> recoveryIp;
    private readonly PartitionedRateLimiter<string> recoveryEmail;

    public AccountActionRateLimiter(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        activation = LoginRateLimiter.Create(configuration.GetValue("Authentication:RateLimits:ActivationLimit", 10),
            configuration.GetValue("Authentication:RateLimits:ActivationWindowMinutes", 15));
        confirmation = LoginRateLimiter.Create(configuration.GetValue("Authentication:RateLimits:RecoveryConfirmationLimit", 10),
            configuration.GetValue("Authentication:RateLimits:RecoveryConfirmationWindowMinutes", 15));
        recoveryIp = LoginRateLimiter.Create(configuration.GetValue("Authentication:RateLimits:RecoveryIpLimit", 10),
            configuration.GetValue("Authentication:RateLimits:RecoveryIpWindowMinutes", 30));
        recoveryEmail = LoginRateLimiter.Create(configuration.GetValue("Authentication:RateLimits:RecoveryEmailLimit", 3),
            configuration.GetValue("Authentication:RateLimits:RecoveryEmailWindowMinutes", 30));
    }

    public int? Activation(string ipAddress) => Acquire(activation, ipAddress);
    public int? RecoveryConfirmation(string ipAddress) => Acquire(confirmation, ipAddress);

    public int? RecoveryRequest(string ipAddress, string? email)
    {
        var denied = Acquire(recoveryIp, ipAddress);
        if (denied is not null)
        {
            return denied;
        }

        var normalized = email is { Length: <= 320 } ? email.Trim().ToUpperInvariant() : "invalid";
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return Acquire(recoveryEmail, key);
    }

    public void Dispose()
    {
        activation.Dispose();
        confirmation.Dispose();
        recoveryIp.Dispose();
        recoveryEmail.Dispose();
        GC.SuppressFinalize(this);
    }

    private static int? Acquire(PartitionedRateLimiter<string> limiter, string key)
    {
        using var lease = limiter.AttemptAcquire(key);
        return lease.IsAcquired ? null : LoginRateLimiter.GetRetryAfter(lease);
    }
}
