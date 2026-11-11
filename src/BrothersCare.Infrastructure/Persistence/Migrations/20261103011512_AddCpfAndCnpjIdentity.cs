using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCpfAndCnpjIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Cpf",
                table: "users",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cnpj",
                table: "tenants",
                type: "nvarchar(14)",
                maxLength: 14,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_Cpf",
                table: "users",
                column: "Cpf",
                unique: true,
                filter: "[Cpf] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_tenants_Cnpj",
                table: "tenants",
                column: "Cnpj",
                unique: true,
                filter: "[Cnpj] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_Cpf",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_tenants_Cnpj",
                table: "tenants");

            migrationBuilder.DropColumn(
                name: "Cpf",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Cnpj",
                table: "tenants");
        }
    }
}
