using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.AppUser.</summary>
public sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("AppUser");
        b.HasKey(u => u.Id).HasName("PK_AppUser");

        b.Property(u => u.Email).HasColumnType("VARCHAR(320)").IsRequired();
        b.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UQ_AppUser_Email");

        // BIT NOT NULL CONSTRAINT DF_AppUser_Conf DEFAULT 0
        var emailConfirmed = b.Property(u => u.EmailConfirmed);
        emailConfirmed.HasDefaultValue(false);
        // TA-3.2 names the default constraint DF_AppUser_Conf — EF needs an explicit name per column to match verbatim (AC-052-4).
        emailConfirmed.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_AppUser_Conf");

        b.Property(u => u.DisplayName).HasColumnType("NVARCHAR(100)").IsRequired();
        b.Property(u => u.PasswordHash).HasColumnType("VARCHAR(256)"); // PBKDF2 (ASP.NET Identity PasswordHasher)

        // FK_AppUser_Plan -> dbo.Plan(Id); DDL has no ON DELETE clause = SQL Server default NO ACTION (EC-052-1).
        b.HasOne<Plan>()
            .WithMany()
            .HasForeignKey(u => u.PlanId)
            .HasConstraintName("FK_AppUser_Plan")
            .OnDelete(DeleteBehavior.NoAction);

        // TA-3.2 defines no index on PlanId — the automatic FK index is suppressed via
        // ForeignKeyIndexConvention removal in WaDbContext.ConfigureConventions (no per-entity API exists).

        // VARCHAR(16) NOT NULL CONSTRAINT DF_AppUser_Theme DEFAULT 'system'
        var theme = b.Property(u => u.Theme);
        theme.HasColumnType("VARCHAR(16)").HasDefaultValue("system").IsRequired();
        // TA-3.2 names the default constraint DF_AppUser_Theme — EF needs an explicit name per column to match verbatim (AC-052-4).
        theme.Metadata.SetAnnotation("Relational:DefaultConstraintName", "DF_AppUser_Theme");
    }
}
