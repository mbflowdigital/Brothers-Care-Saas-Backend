using BrothersCare.Domain.Care;
using BrothersCare.Domain.Identity;
using BrothersCare.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace BrothersCare.Infrastructure.Persistence;

public sealed class BrothersCareDbContext(DbContextOptions<BrothersCareDbContext> options) : DbContext(options)
{
    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<ClinicalRecord> ClinicalRecords => Set<ClinicalRecord>();

    public DbSet<ClinicUnit> ClinicUnits => Set<ClinicUnit>();

    public DbSet<ClinicUnitMembership> ClinicUnitMemberships => Set<ClinicUnitMembership>();

    public DbSet<Patient> Patients => Set<Patient>();

    public DbSet<PatientCareAssignment> PatientCareAssignments => Set<PatientCareAssignment>();

    public DbSet<Professional> Professionals => Set<Professional>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder);
        ConfigureTenant(modelBuilder);
        ConfigureClinicUnit(modelBuilder);
        ConfigureTenantMembership(modelBuilder);
        ConfigureClinicUnitMembership(modelBuilder);
        ConfigurePatient(modelBuilder);
        ConfigureProfessional(modelBuilder);
        ConfigurePatientCareAssignment(modelBuilder);
        ConfigureAppointment(modelBuilder);
        ConfigureClinicalRecord(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<User>();
        entity.ToTable("users");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.Property(item => item.Email).HasMaxLength(320).IsRequired();
        entity.Property(item => item.NormalizedEmail).HasMaxLength(320).IsRequired();
        entity.Property(item => item.DisplayName).HasMaxLength(200).IsRequired();
        entity.HasIndex(item => item.NormalizedEmail).IsUnique();
    }

    private static void ConfigureTenant(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Tenant>();
        entity.ToTable("tenants");
        entity.HasKey(item => item.Id);
        entity.Property(item => item.Id).ValueGeneratedNever();
        entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => item.Status);
    }

    private static void ConfigureClinicUnit(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicUnit>();
        entity.ToTable("clinic_units");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.Name).HasMaxLength(200).IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.Name }).IsUnique();
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTenantMembership(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TenantMembership>();
        entity.ToTable("tenant_memberships");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.Role).HasConversion<int>().IsRequired();
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.UserId }).IsUnique();
        entity.HasIndex(item => new { item.TenantId, item.Status, item.Role });
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureClinicUnitMembership(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicUnitMembership>();
        entity.ToTable("clinic_unit_memberships");
        ConfigureTenantScopedKey(entity);
        entity.HasIndex(item => new { item.TenantId, item.ClinicUnitId, item.TenantMembershipId }).IsUnique();
        entity.HasIndex(item => new { item.TenantId, item.TenantMembershipId });
        entity.HasOne<ClinicUnit>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ClinicUnitId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.TenantMembershipId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePatient(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Patient>();
        entity.ToTable("patients");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.FullName).HasMaxLength(200).IsRequired();
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.Status, item.FullName });
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureProfessional(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Professional>();
        entity.ToTable("professionals");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.FullName).HasMaxLength(200).IsRequired();
        entity.Property(item => item.Specialty).HasMaxLength(150).IsRequired();
        entity.Property(item => item.RegistrationNumber).HasMaxLength(100).IsRequired();
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.RegistrationNumber }).IsUnique();
        entity.HasIndex(item => new { item.TenantId, item.Status, item.FullName });
        entity.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(item => item.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigurePatientCareAssignment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<PatientCareAssignment>();
        entity.ToTable("patient_care_assignments");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.PatientId, item.ProfessionalId, item.ClinicUnitId })
            .IsUnique();
        entity.HasIndex(item => new { item.TenantId, item.ProfessionalId, item.Status });
        entity.HasIndex(item => new { item.TenantId, item.PatientId, item.Status });
        entity.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.PatientId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Professional>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ProfessionalId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ClinicUnit>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ClinicUnitId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureAppointment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<Appointment>();
        entity.ToTable("appointments");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.ProfessionalId, item.StartsAt });
        entity.HasIndex(item => new { item.TenantId, item.PatientId, item.StartsAt });
        entity.HasIndex(item => new { item.TenantId, item.ClinicUnitId, item.StartsAt });
        entity.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.PatientId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Professional>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ProfessionalId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<ClinicUnit>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.ClinicUnitId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureClinicalRecord(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<ClinicalRecord>();
        entity.ToTable("clinical_records");
        ConfigureTenantScopedKey(entity);
        entity.Property(item => item.Content).HasColumnType("nvarchar(max)").IsRequired();
        entity.Property(item => item.Status).HasConversion<int>().IsRequired();
        entity.HasIndex(item => new { item.TenantId, item.AppointmentId }).IsUnique();
        entity.HasIndex(item => new { item.TenantId, item.PatientId, item.CreatedAt });
        entity.HasIndex(item => new { item.TenantId, item.AuthorMembershipId, item.Status });
        entity.HasOne<Appointment>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.AppointmentId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<Patient>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.PatientId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
        entity.HasOne<TenantMembership>()
            .WithMany()
            .HasForeignKey(item => new { item.TenantId, item.AuthorMembershipId })
            .HasPrincipalKey(item => new { item.TenantId, item.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTenantScopedKey<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity)
        where TEntity : class
    {
        entity.HasKey("Id");
        entity.Property<Guid>("Id").ValueGeneratedNever();
        entity.Property<Guid>("TenantId").IsRequired();
        entity.HasAlternateKey("TenantId", "Id");
        entity.HasIndex("TenantId");
    }
}
