using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImplementClinicalRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records");

            migrationBuilder.RenameColumn(
                name: "Content",
                table: "clinical_records",
                newName: "ProceduresAndObservations");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "clinical_records",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<string>(
                name: "AssessmentAndPlan",
                table: "clinical_records",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "ClinicUnitId",
                table: "clinical_records",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientReport",
                table: "clinical_records",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "clinical_records",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql("""
                UPDATE records
                SET records.ClinicUnitId = appointments.ClinicUnitId,
                    records.UpdatedAt = records.CreatedAt
                FROM clinical_records AS records
                INNER JOIN appointments
                    ON appointments.TenantId = records.TenantId
                    AND appointments.Id = records.AppointmentId;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "ClinicUnitId",
                table: "clinical_records",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "Version",
                table: "clinical_records",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records",
                columns: new[] { "TenantId", "AppointmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_TenantId_ClinicUnitId",
                table: "clinical_records",
                columns: new[] { "TenantId", "ClinicUnitId" });

            migrationBuilder.AddForeignKey(
                name: "FK_clinical_records_clinic_units_TenantId_ClinicUnitId",
                table: "clinical_records",
                columns: new[] { "TenantId", "ClinicUnitId" },
                principalTable: "clinic_units",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clinical_records_clinic_units_TenantId_ClinicUnitId",
                table: "clinical_records");

            migrationBuilder.DropIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records");

            migrationBuilder.DropIndex(
                name: "IX_clinical_records_TenantId_ClinicUnitId",
                table: "clinical_records");

            migrationBuilder.DropColumn(
                name: "AssessmentAndPlan",
                table: "clinical_records");

            migrationBuilder.DropColumn(
                name: "ClinicUnitId",
                table: "clinical_records");

            migrationBuilder.DropColumn(
                name: "PatientReport",
                table: "clinical_records");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "clinical_records");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "clinical_records");

            migrationBuilder.RenameColumn(
                name: "ProceduresAndObservations",
                table: "clinical_records",
                newName: "Content");

            migrationBuilder.AlterColumn<Guid>(
                name: "AppointmentId",
                table: "clinical_records",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_clinical_records_TenantId_AppointmentId",
                table: "clinical_records",
                columns: new[] { "TenantId", "AppointmentId" },
                unique: true);
        }
    }
}
