using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingOnboardingTenantStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_status",
                table: "tenants");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_status",
                table: "tenants",
                sql: "[Status] IN (1, 2, 3, 4)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_tenants_status",
                table: "tenants");

            migrationBuilder.AddCheckConstraint(
                name: "CK_tenants_status",
                table: "tenants",
                sql: "[Status] IN (1, 2, 3)");
        }
    }
}
