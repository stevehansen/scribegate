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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScribegateDbContext).Assembly);
    }
}
