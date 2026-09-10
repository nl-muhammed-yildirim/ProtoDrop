using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace wa.infrastructure.Persistence.ModelConfigurations;

public class AuthTokenConfiguration : IEntityTypeConfiguration<AuthToken>
{
    public void Configure(EntityTypeBuilder<AuthToken> e)
    {
        e.ToTable("AuthToken", "dbo");
        e.HasKey(k => k.Id);

        e.Property(t => t.Id).HasColumnName("Id").HasColumnType("uniqueidentifier");
        e.Property(t => t.Email).HasColumnName("Email").HasColumnType("varchar(320)").HasMaxLength(320).IsRequired();
        e.Property(t => t.TokenHash).HasColumnName("TokenHash").HasColumnType("char(64)").HasMaxLength(64).IsRequired();
        e.HasIndex(i => i.TokenHash).IsUnique().HasDatabaseName("UQ_AuthToken_Hash");
        e.Property(t => t.Purpose).HasColumnName("Purpose").HasColumnType("tinyint").IsRequired();
        e.Property(t => t.ExpiresAtUtc).HasColumnName("ExpiresAtUtc").HasColumnType("datetime2").IsRequired();
        e.Property(t => t.RedeemedAtUtc).HasColumnName("RedeemedAtUtc").HasColumnType("datetime2");
        e.Property(t => t.CreatedAtUtc).HasColumnName("CreatedAtUtc").HasColumnType("datetime2").IsRequired();
    }
}