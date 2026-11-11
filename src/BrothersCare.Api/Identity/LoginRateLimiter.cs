using System.Threading.RateLimiting;

namespace BrothersCare.Api.Identity;

public sealed class LoginRateLimiter : IDisposable
{
    private readonly PartitionedRateLimiter<string> ipLimiter;
    private readonly PartitionedRateLimiter<string> cpfLimiter;

    public LoginRateLimiter(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ipLimiter = Create(configuration.GetValue("Authentication:LoginRateLimit:IpLimit", 10),
            configuration.GetValue("Authentication:LoginRateLimit:IpWindowMinutes", 10));
        cpfLimiter = Create(configuration.GetValue("Authentication:LoginRateLimit:CpfLimit", 5),
            configuration.GetValue("Authentication:LoginRateLimit:CpfWindowMinutes", 15));
    }

    public int? GetRetryAfterSeconds(string ipAddress, string cpf)
    {
        using var ipLease = ipLimiter.AttemptAcquire(ipAddress);
        if (!ipLease.IsAcquired)
        {
            return GetRetryAfter(ipLease);
        }

        using var cpfLease = cpfLimiter.AttemptAcquire(cpf);
        return cpfLease.IsAcquired ? null : GetRetryAfter(cpfLease);
    }

    public void Dispose()
    {
        ipLimiter.Dispose();
        cpfLimiter.Dispose();
        GC.SuppressFinalize(this);
    }

    internal static int GetRetryAfter(RateLimitLease lease) =>
        lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)) : 60;

    internal static PartitionedRateLimiter<string> Create(int permitLimit, int windowMinutes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(permitLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowMinutes);
        return PartitionedRateLimiter.Create<string, string>(key => RateLimitPartition.GetFixedWindowLimiter(key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(windowMinutes),
                QueueLimit = 0,
                AutoReplenishment = true,
            }));
    }
}
