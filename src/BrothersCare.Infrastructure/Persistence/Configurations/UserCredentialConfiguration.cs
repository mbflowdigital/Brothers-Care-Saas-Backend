using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BrothersCare.Infrastructure.Persistence.Configurations;

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        builder.ToTable("user_credentials", table =>
        {
            table.HasCheckConstraint("ck_user_credentials_count", "failed_sign_in_count >= 0");
            table.HasCheckConstraint("ck_user_credentials_locked", "status <> 3 OR lockout_ends_at IS NOT NULL");
            table.HasCheckConstraint("ck_user_credentials_password", "status NOT IN (2, 3) OR (password_hash IS NOT NULL AND password_changed_at IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.Id).HasColumnName("id");
        builder.Property(item => item.UserId).HasColumnName("user_id");
        builder.Property(item => item.PasswordHash).HasColumnName("password_hash");
        builder.Property(item => item.Status).HasColumnName("status");
        builder.Property(item => item.FailedSignInCount).HasColumnName("failed_sign_in_count");
        builder.Property(item => item.LockoutEndsAt).HasColumnName("lockout_ends_at");
        builder.Property(item => item.PasswordChangedAt).HasColumnName("password_changed_at");
        builder.Property(item => item.CreatedAt).HasColumnName("created_at");
        builder.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => item.UserId).IsUnique();
        builder.HasIndex(item => new { item.Status, item.LockoutEndsAt });
    }
}
