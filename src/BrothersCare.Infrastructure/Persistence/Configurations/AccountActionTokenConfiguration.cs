using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BrothersCare.Infrastructure.Persistence.Configurations;

public sealed class AccountActionTokenConfiguration : IEntityTypeConfiguration<AccountActionToken>
{
    public void Configure(EntityTypeBuilder<AccountActionToken> builder)
    {
        builder.ToTable("account_action_tokens", table =>
        {
            table.HasCheckConstraint("ck_account_action_tokens_expiry", "expires_at > created_at");
            table.HasCheckConstraint("ck_account_action_tokens_membership", "(purpose = 3 AND tenant_membership_id IS NOT NULL) OR (purpose IN (1, 2) AND tenant_membership_id IS NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.TenantMembershipId).HasColumnName("tenant_membership_id");
        builder.Property(item => item.Purpose).HasColumnName("purpose");
        builder.Property(item => item.SecretHash).HasColumnName("secret_hash");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.ExpiresAt).HasColumnName("expires_at");
        builder.Property(item => item.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(item => item.RevokedAt).HasColumnName("revoked_at");
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.SecretHash).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(item => item.SecretHash).IsUnique();
        builder.HasIndex(item => new { item.UserId, item.Purpose });
        builder.HasIndex(item => new { item.TenantMembershipId, item.Purpose });
        builder.HasOne<TenantMembership>().WithMany().HasForeignKey(item => new { item.TenantMembershipId, item.UserId }).HasPrincipalKey(item => new { item.Id, item.UserId }).OnDelete(DeleteBehavior.Restrict);
    }
}
