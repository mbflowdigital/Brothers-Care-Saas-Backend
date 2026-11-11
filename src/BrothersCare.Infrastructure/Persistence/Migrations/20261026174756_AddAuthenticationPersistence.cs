using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BrothersCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Active (1) is preserved; legacy Inactive (2) becomes Revoked (5).
            migrationBuilder.Sql("UPDATE tenant_memberships SET [Status] = 5 WHERE [Status] = 2;");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_tenant_memberships_Id_UserId",
                table: "tenant_memberships",
                columns: new[] { "Id", "UserId" });

            migrationBuilder.CreateTable(
                name: "account_action_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_membership_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    purpose = table.Column<int>(type: "int", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_action_tokens", x => x.id);
                    table.CheckConstraint("ck_account_action_tokens_expiry", "expires_at > created_at");
                    table.CheckConstraint("ck_account_action_tokens_membership", "(purpose = 3 AND tenant_membership_id IS NOT NULL) OR (purpose IN (1, 2) AND tenant_membership_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_account_action_tokens_tenant_memberships_tenant_membership_id_user_id",
                        columns: x => new { x.tenant_membership_id, x.user_id },
                        principalTable: "tenant_memberships",
                        principalColumns: new[] { "Id", "UserId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_action_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_credentials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<int>(type: "int", nullable: false),
                    failed_sign_in_count = table.Column<int>(type: "int", nullable: false),
                    lockout_ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    password_changed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_credentials", x => x.id);
                    table.CheckConstraint("ck_user_credentials_count", "failed_sign_in_count >= 0");
                    table.CheckConstraint("ck_user_credentials_locked", "status <> 3 OR lockout_ends_at IS NOT NULL");
                    table.CheckConstraint("ck_user_credentials_password", "status NOT IN (2, 3) OR (password_hash IS NOT NULL AND password_changed_at IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_user_credentials_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    secret_hash = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    active_tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    idle_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    absolute_expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revocation_reason = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.CheckConstraint("ck_user_sessions_expiry", "idle_expires_at > created_at AND idle_expires_at <= absolute_expires_at");
                    table.ForeignKey(
                        name: "FK_user_sessions_tenants_active_tenant_id",
                        column: x => x.active_tenant_id,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_action_tokens_secret_hash",
                table: "account_action_tokens",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_action_tokens_tenant_membership_id_purpose",
                table: "account_action_tokens",
                columns: new[] { "tenant_membership_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_account_action_tokens_tenant_membership_id_user_id",
                table: "account_action_tokens",
                columns: new[] { "tenant_membership_id", "user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_account_action_tokens_user_id_purpose",
                table: "account_action_tokens",
                columns: new[] { "user_id", "purpose" });

            migrationBuilder.CreateIndex(
                name: "IX_user_credentials_status_lockout_ends_at",
                table: "user_credentials",
                columns: new[] { "status", "lockout_ends_at" });

            migrationBuilder.CreateIndex(
                name: "IX_user_credentials_user_id",
                table: "user_credentials",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_active_tenant_id",
                table: "user_sessions",
                column: "active_tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_secret_hash",
                table: "user_sessions",
                column: "secret_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_user_id_revoked_at_absolute_expires_at",
                table: "user_sessions",
                columns: new[] { "user_id", "revoked_at", "absolute_expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old model cannot represent invitations or suspensions: downgrade without granting access.
            migrationBuilder.Sql("UPDATE tenant_memberships SET [Status] = 2 WHERE [Status] IN (3, 4, 5);");

            migrationBuilder.DropTable(
                name: "account_action_tokens");

            migrationBuilder.DropTable(
                name: "user_credentials");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_tenant_memberships_Id_UserId",
                table: "tenant_memberships");
        }
    }
}
