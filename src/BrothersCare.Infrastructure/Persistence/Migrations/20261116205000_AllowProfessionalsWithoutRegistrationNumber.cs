using BrothersCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BrothersCareDbContext))]
[Migration("20261116205000_AllowProfessionalsWithoutRegistrationNumber")]
public sealed class AllowProfessionalsWithoutRegistrationNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_professionals_TenantId_RegistrationNumber",
            table: "professionals");

        migrationBuilder.CreateIndex(
            name: "IX_professionals_TenantId_RegistrationNumber",
            table: "professionals",
            columns: new[] { "TenantId", "RegistrationNumber" },
            unique: true,
            filter: "[RegistrationNumber] <> N''");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_professionals_TenantId_RegistrationNumber",
            table: "professionals");

        migrationBuilder.CreateIndex(
            name: "IX_professionals_TenantId_RegistrationNumber",
            table: "professionals",
            columns: new[] { "TenantId", "RegistrationNumber" },
            unique: true);
    }
}
