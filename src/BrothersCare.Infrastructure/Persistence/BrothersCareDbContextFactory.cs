using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace BrothersCare.Infrastructure.Persistence;

public sealed class BrothersCareDbContextFactory : IDesignTimeDbContextFactory<BrothersCareDbContext>
{
    public BrothersCareDbContext CreateDbContext(string[] args)
    {
        var currentDirectory = Directory.GetCurrentDirectory();
        var apiDirectory = File.Exists(Path.Combine(currentDirectory, "appsettings.json"))
            ? currentDirectory
            : Path.GetFullPath(Path.Combine(currentDirectory, "src", "BrothersCare.Api"));
        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        var connectionString = configuration.GetConnectionString("BrothersCare")
            ?? throw new InvalidOperationException("Connection string 'BrothersCare' is required for design-time operations.");

        var optionsBuilder = new DbContextOptionsBuilder<BrothersCareDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new BrothersCareDbContext(optionsBuilder.Options);
    }
}
