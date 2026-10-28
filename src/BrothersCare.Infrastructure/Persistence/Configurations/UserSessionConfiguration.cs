using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BrothersCare.Infrastructure.Persistence.Configurations;

public sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_sessions", table =>
        {
            table.HasCheckConstraint("ck_user_sessions_expiry", "idle_expires_at > created_at AND idle_expires_at <= absolute_expires_at");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.SecretHash).HasColumnName("secret_hash");
        builder.Property(item => item.ActiveTenantId).HasColumnName("active_tenant_id");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(item => item.IdleExpiresAt).HasColumnName("idle_expires_at");
        builder.Property(item => item.AbsoluteExpiresAt).HasColumnName("absolute_expires_at");
        builder.Property(item => item.RevokedAt).HasColumnName("revoked_at");
        builder.Property(item => item.RevocationReason).HasColumnName("revocation_reason");
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(item => item.SecretHash).HasColumnType("binary(32)").IsRequired();
        builder.HasIndex(item => item.SecretHash).IsUnique();
        builder.HasIndex(item => new { item.UserId, item.RevokedAt, item.AbsoluteExpiresAt });
        builder.HasIndex(item => item.ActiveTenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(item => item.ActiveTenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
