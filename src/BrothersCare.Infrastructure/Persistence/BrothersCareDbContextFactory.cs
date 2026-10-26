using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BrothersCare.Infrastructure.Persistence;

public sealed class BrothersCareDbContextFactory : IDesignTimeDbContextFactory<BrothersCareDbContext>
{
    public BrothersCareDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__BrothersCare")
            ?? "Server=localhost;Database=BrothersCare;Trusted_Connection=True;TrustServerCertificate=True";

        var optionsBuilder = new DbContextOptionsBuilder<BrothersCareDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new BrothersCareDbContext(optionsBuilder.Options);
    }
}
