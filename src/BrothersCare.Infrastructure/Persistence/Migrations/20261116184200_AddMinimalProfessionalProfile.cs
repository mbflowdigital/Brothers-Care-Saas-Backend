using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMinimalProfessionalProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouncilState",
                table: "professionals",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DefaultAppointmentDurationMinutes",
                table: "professionals",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CouncilState",
                table: "professionals");

            migrationBuilder.DropColumn(
                name: "DefaultAppointmentDurationMinutes",
                table: "professionals");
        }
    }
}
