using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BrothersCare.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Register persistence and external adapters here when their contracts exist.
        return services;
    }
}
