using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfessionalAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "professional_availability_blocks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_availability_blocks", x => x.Id);
                    table.UniqueConstraint("AK_professional_availability_blocks_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_professional_availability_blocks_interval", "[StartsAt] < [EndsAt]");
                    table.ForeignKey(
                        name: "FK_professional_availability_blocks_professionals_TenantId_ProfessionalId",
                        columns: x => new { x.TenantId, x.ProfessionalId },
                        principalTable: "professionals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "professional_availability_windows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProfessionalId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClinicUnitId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DayOfWeek = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndsAt = table.Column<TimeOnly>(type: "time", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_professional_availability_windows", x => x.Id);
                    table.UniqueConstraint("AK_professional_availability_windows_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_professional_availability_windows_day", "[DayOfWeek] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_professional_availability_windows_interval", "[StartsAt] < [EndsAt]");
                    table.ForeignKey(
                        name: "FK_professional_availability_windows_clinic_units_TenantId_ClinicUnitId",
                        columns: x => new { x.TenantId, x.ClinicUnitId },
                        principalTable: "clinic_units",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_professional_availability_windows_professionals_TenantId_ProfessionalId",
                        columns: x => new { x.TenantId, x.ProfessionalId },
                        principalTable: "professionals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_professional_availability_blocks_TenantId",
                table: "professional_availability_blocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_professional_availability_blocks_TenantId_ProfessionalId_StartsAt",
                table: "professional_availability_blocks",
                columns: new[] { "TenantId", "ProfessionalId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_professional_availability_windows_TenantId",
                table: "professional_availability_windows",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_professional_availability_windows_TenantId_ClinicUnitId",
                table: "professional_availability_windows",
                columns: new[] { "TenantId", "ClinicUnitId" });

            migrationBuilder.CreateIndex(
                name: "IX_professional_availability_windows_TenantId_ProfessionalId_ClinicUnitId_DayOfWeek_StartsAt",
                table: "professional_availability_windows",
                columns: new[] { "TenantId", "ProfessionalId", "ClinicUnitId", "DayOfWeek", "StartsAt" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "professional_availability_blocks");

            migrationBuilder.DropTable(
                name: "professional_availability_windows");
        }
    }
}
