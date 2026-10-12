using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using wa.domain;

namespace wa.infrastructure.Persistence.EntityConfigurations;

/// <summary>TA-3.2: dbo.AuthToken (magic links, single-use).</summary>
public sealed class AuthTokenConfiguration : IEntityTypeConfiguration<AuthToken>
{
    public void Configure(EntityTypeBuilder<AuthToken> b)
    {
        b.ToTable("AuthToken");
        b.HasKey(t => t.Id).HasName("PK_AuthToken");

        b.Property(t => t.Email).HasColumnType("VARCHAR(320)").IsRequired();
        // CHAR(64) NOT NULL CONSTRAINT UQ_AuthToken_Hash UNIQUE — sha256(token), hex (64 chars).
        b.Property(t => t.TokenHash).HasColumnType("CHAR(64)").IsRequired();
        b.HasIndex(t => t.TokenHash).IsUnique().HasDatabaseName("UQ_AuthToken_Hash");

        // TINYINT NOT NULL — 0=signup, 1=login, 2=forgot (byte maps to tinyint by convention; explicit per DDL).
        b.Property(t => t.Purpose).HasColumnType("TINYINT");
    }
}
