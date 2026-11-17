using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkClinicalRecordToAppointment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records",
                columns: new[] { "TenantId", "AppointmentId" });
        }
    }
}
