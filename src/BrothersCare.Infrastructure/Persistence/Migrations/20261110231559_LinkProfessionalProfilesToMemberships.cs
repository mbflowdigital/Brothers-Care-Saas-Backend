using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LinkProfessionalProfilesToMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CouncilType",
                table: "professionals",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantMembershipId",
                table: "professionals",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_professionals_TenantId_TenantMembershipId",
                table: "professionals",
                columns: new[] { "TenantId", "TenantMembershipId" },
                unique: true,
                filter: "[TenantMembershipId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_professionals_tenant_memberships_TenantId_TenantMembershipId",
                table: "professionals",
                columns: new[] { "TenantId", "TenantMembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_professionals_tenant_memberships_TenantId_TenantMembershipId",
                table: "professionals");

            migrationBuilder.DropIndex(
                name: "IX_professionals_TenantId_TenantMembershipId",
                table: "professionals");

            migrationBuilder.DropColumn(
                name: "CouncilType",
                table: "professionals");

            migrationBuilder.DropColumn(
                name: "TenantMembershipId",
                table: "professionals");
        }
    }
}
