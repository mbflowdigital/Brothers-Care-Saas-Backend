using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExtendAppointmentLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "appointments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CancelledAt",
                table: "appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CancelledByMembershipId",
                table: "appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                table: "appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CompletedByMembershipId",
                table: "appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedByMembershipId",
                table: "appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NoShowAt",
                table: "appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "NoShowByMembershipId",
                table: "appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoShowReason",
                table: "appointments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "appointments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "appointments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByMembershipId",
                table: "appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql("UPDATE [appointments] SET [UpdatedAt] = [CreatedAt] WHERE [UpdatedAt] IS NULL;");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "appointments",
                type: "datetimeoffset",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "datetimeoffset",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_CancelledByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CancelledByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_CompletedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CompletedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_CreatedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CreatedByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_NoShowByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "NoShowByMembershipId" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_Status_StartsAt",
                table: "appointments",
                columns: new[] { "TenantId", "Status", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_appointments_TenantId_UpdatedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "UpdatedByMembershipId" });

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CancelledByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CancelledByMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CompletedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CompletedByMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CreatedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "CreatedByMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_NoShowByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "NoShowByMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_UpdatedByMembershipId",
                table: "appointments",
                columns: new[] { "TenantId", "UpdatedByMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CancelledByMembershipId",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CompletedByMembershipId",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_CreatedByMembershipId",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_NoShowByMembershipId",
                table: "appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_appointments_tenant_memberships_TenantId_UpdatedByMembershipId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_CancelledByMembershipId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_CompletedByMembershipId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_CreatedByMembershipId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_NoShowByMembershipId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_Status_StartsAt",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "IX_appointments_TenantId_UpdatedByMembershipId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CancelledAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CancelledByMembershipId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CompletedByMembershipId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "CreatedByMembershipId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "NoShowAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "NoShowByMembershipId",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "NoShowReason",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "UpdatedByMembershipId",
                table: "appointments");
        }
    }
}
