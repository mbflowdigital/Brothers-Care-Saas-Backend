using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantAndClinicUnitProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CommercialEmail",
                table: "tenants",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalName",
                table: "tenants",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "tenants",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "clinic_units",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Complement",
                table: "clinic_units",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "clinic_units",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "clinic_units",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Number",
                table: "clinic_units",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "clinic_units",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "clinic_units",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "clinic_units",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Street",
                table: "clinic_units",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_clinic_units_default_per_tenant",
                table: "clinic_units",
                column: "TenantId",
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_clinic_units_default_per_tenant",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "CommercialEmail",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "LegalName",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "City",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "Complement",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "District",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "State",
                table: "clinic_units");

            migrationBuilder.DropColumn(
                name: "Street",
                table: "clinic_units");
        }
    }
}
