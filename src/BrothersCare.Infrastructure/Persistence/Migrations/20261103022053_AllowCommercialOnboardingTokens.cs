using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowCommercialOnboardingTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_action_tokens_membership",
                table: "account_action_tokens");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_action_tokens_membership",
                table: "account_action_tokens",
                sql: "(purpose IN (3, 4) AND tenant_membership_id IS NOT NULL) OR (purpose IN (1, 2) AND tenant_membership_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_account_action_tokens_membership",
                table: "account_action_tokens");

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_action_tokens_membership",
                table: "account_action_tokens",
                sql: "(purpose = 3 AND tenant_membership_id IS NOT NULL) OR (purpose IN (1, 2) AND tenant_membership_id IS NULL)");
        }
    }
}
