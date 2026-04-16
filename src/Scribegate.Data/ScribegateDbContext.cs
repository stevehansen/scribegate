using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;

namespace Scribegate.Data;

public class ScribegateDbContext(DbContextOptions<ScribegateDbContext> options) : DbContext(options)
{
    public DbSet<Repository> Repositories => Set<Repository>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Revision> Revisions => Set<Revision>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RepositoryMembership> RepositoryMemberships => Set<RepositoryMembership>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RevisionSignature> RevisionSignatures => Set<RevisionSignature>();
    public DbSet<Proposal> Proposals => Set<Proposal>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Comment> Comments => Set<Comment>();
    public DbSet<ContentReport> ContentReports => Set<ContentReport>();
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
    public DbSet<ShareLink> ShareLinks => Set<ShareLink>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScribegateDbContext).Assembly);
    }
}
