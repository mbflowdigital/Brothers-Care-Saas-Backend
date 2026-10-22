using Microsoft.Extensions.DependencyInjection;

namespace BrothersCare.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Register application use cases here when approved features are introduced.
        return services;
    }
}
