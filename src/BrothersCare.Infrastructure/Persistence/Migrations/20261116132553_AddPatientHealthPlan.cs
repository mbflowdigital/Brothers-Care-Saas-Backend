using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientHealthPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HealthPlan",
                table: "patients",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HealthPlan",
                table: "patients");
        }
    }
}
