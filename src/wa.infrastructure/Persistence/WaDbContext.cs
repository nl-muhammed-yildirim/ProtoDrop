using Microsoft.EntityFrameworkCore;

namespace wa.infrastructure.Persistence;

public class WaDbContext : DbContext
{
    public WaDbContext(DbContextOptions<WaDbContext> options)
        : base(options)
    {
    }

    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<AuthToken> AuthTokens => Set<AuthToken>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<BlobRef> BlobRefs => Set<BlobRef>();
    public DbSet<FileItem> FileItems => Set<FileItem>();
    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();
    public DbSet<EmailSuppression> EmailSuppressions => Set<EmailSuppression>();
    public DbSet<DownloadEvent> DownloadEvents => Set<DownloadEvent>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<StripeEvent> StripeEvents => Set<StripeEvent>();
    public DbSet<FeatureFlag> FeatureFlags => Set<FeatureFlag>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<EventOutbox> EventOutboxes => Set<EventOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dbo");

        modelBuilder.ApplyConfiguration(new PlanConfiguration());
        modelBuilder.ApplyConfiguration(new AppUserConfiguration());
        modelBuilder.ApplyConfiguration(new AuthTokenConfiguration());
        modelBuilder.ApplyConfiguration(new TransferConfiguration());
        modelBuilder.ApplyConfiguration(new BlobRefConfiguration());
        modelBuilder.ApplyConfiguration(new FileItemConfiguration());
        modelBuilder.ApplyConfiguration(new EmailRecipientConfiguration());
        modelBuilder.ApplyConfiguration(new EmailSuppressionConfiguration());
        modelBuilder.ApplyConfiguration(new DownloadEventConfiguration());
        modelBuilder.ApplyConfiguration(new SubscriptionConfiguration());
        modelBuilder.ApplyConfiguration(new StripeEventConfiguration());
        modelBuilder.ApplyConfiguration(new FeatureFlagConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new JobRunConfiguration());
        modelBuilder.ApplyConfiguration(new IdempotencyKeyConfiguration());
        modelBuilder.ApplyConfiguration(new EventOutboxConfiguration());
    }
}
