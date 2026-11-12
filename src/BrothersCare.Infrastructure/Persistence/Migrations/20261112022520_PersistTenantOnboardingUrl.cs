using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistTenantOnboardingUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OnboardingUrl",
                table: "tenants",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OnboardingUrl",
                table: "tenants");
        }
    }
}
