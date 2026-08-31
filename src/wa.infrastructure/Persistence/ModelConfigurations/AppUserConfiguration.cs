using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> e)
    {
        e.ToTable("AppUser", "dbo");
        e.HasKey(k => k.Id);

        e.Property(u => u.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(u => u.Email).HasColumnName("Email").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired();
        e.HasIndex(i => i.Email).IsUnique().HasDatabaseName("UQ_AppUser_Email");
        e.Property(u => u.EmailConfirmed).HasColumnName("EmailConfirmed").HasColumnType("bit").HasDefaultValue(false);
        e.Property(u => u.DisplayName).HasColumnName("DisplayName").HasColumnType("nvarchar(100)").HasMaxLength(100).IsRequired();
        e.Property(u => u.PasswordHash).HasColumnName("PasswordHash").HasColumnType("varchar(256)").HasMaxLength(256);
        e.Property(u => u.PlanId).HasColumnName("PlanId").HasColumnType("uniqueidentifier");
        e.Property(u => u.Theme).HasColumnName("Theme").HasColumnType("varchar(16)").HasMaxLength(16).HasDefaultValue("system").IsRequired();
        e.Property(u => u.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2");
        e.Property(u => u.DeletedAtUtc).HasColumnName("DeletedAtUtc").HasColumnType("datetime2");

        // DDL: CONSTRAINT FK_AppUser_Plan REFERENCES dbo.Plan(Id). No ON DELETE clause = NO ACTION;
        // EF defaults required FKs to CASCADE, so pin it explicitly.
        e.HasOne<Plan>().WithMany().HasForeignKey(u => u.PlanId)
            .HasConstraintName("FK_AppUser_Plan")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
