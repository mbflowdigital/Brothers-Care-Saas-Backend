using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicUnitTimeZone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TimeZoneId",
                table: "clinic_units",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "America/Sao_Paulo");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TimeZoneId",
                table: "clinic_units");
        }
    }
}
