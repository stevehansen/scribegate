using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Stores;
using Scribegate.Data.Stores;

namespace Scribegate.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddScribegateData(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ScribegateDbContext>(options =>
            options.UseSqlite(connectionString));

        services.AddScoped<IRepositoryStore, SqliteRepositoryStore>();
        services.AddScoped<IDocumentStore, SqliteDocumentStore>();
        services.AddScoped<IRevisionStore, SqliteRevisionStore>();
        services.AddScoped<ISystemSettingStore, SqliteSystemSettingStore>();
        services.AddScoped<IAuditEventStore, SqliteAuditEventStore>();
        services.AddScoped<IProposalStore, SqliteProposalStore>();
        services.AddScoped<IReviewStore, SqliteReviewStore>();
        services.AddScoped<ICommentStore, SqliteCommentStore>();
        services.AddScoped<IMembershipStore, SqliteMembershipStore>();
        services.AddScoped<IContentReportStore, SqliteContentReportStore>();

        return services;
    }
}
