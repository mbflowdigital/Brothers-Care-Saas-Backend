using BrothersCare.Infrastructure.Persistence;
using BrothersCare.Application.Identity;
using BrothersCare.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
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

        var connectionString = configuration.GetConnectionString("BrothersCare");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<BrothersCareDbContext>(options => options.UseSqlServer(connectionString));
            services.AddScoped<IPlatformAdministratorBootstrapper, PlatformAdministratorBootstrapper>();
        }

        return services;
    }
}
