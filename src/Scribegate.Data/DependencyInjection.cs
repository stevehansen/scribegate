using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Stores;
using Scribegate.Data.Stores;

namespace Scribegate.Data;

public static class DependencyInjection
{
    public static IServiceCollection AddScribegateData(this IServiceCollection services, string connectionString)
    {
        // (sp, options) overload so any DI-registered IInterceptor (scoped or
        // singleton) is attached at DbContext construction. Lets Web layer
        // register interceptors (e.g. DomainEventSaveChangesInterceptor)
        // without Data → Web coupling.
        services.AddDbContext<ScribegateDbContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            var interceptors = sp.GetServices<IInterceptor>().ToArray();
            if (interceptors.Length > 0)
                options.AddInterceptors(interceptors);
        });

        services.AddScoped<IUserStore, SqliteUserStore>();
        services.AddScoped<IApiTokenStore, SqliteApiTokenStore>();
        services.AddScoped<IMediaAssetStore, SqliteMediaAssetStore>();
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
        services.AddScoped<IShareLinkStore, SqliteShareLinkStore>();
        services.AddScoped<IWebhookStore, SqliteWebhookStore>();
        services.AddScoped<IDocumentTemplateStore, SqliteDocumentTemplateStore>();
        services.AddScoped<INotificationStore, SqliteNotificationStore>();
        services.AddScoped<IRevisionSignatureStore, SqliteRevisionSignatureStore>();
        services.AddScoped<IWebhookDeliveryStore, SqliteWebhookDeliveryStore>();
        services.AddScoped<IDocumentSearchStore, SqliteDocumentSearchStore>();

        return services;
    }
}
