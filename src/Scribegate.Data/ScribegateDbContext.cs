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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ScribegateDbContext).Assembly);
    }
}
