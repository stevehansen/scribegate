using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Events;
using Scribegate.Data.Events;

namespace Scribegate.Web.Events;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the in-process domain-event bus, scope, and the EF
    /// <see cref="DomainEventSaveChangesInterceptor"/>. The interceptor is
    /// registered as <see cref="IInterceptor"/> so the data-layer
    /// <c>AddScribegateData</c> picks it up via DI without a hard reference
    /// from Data → Web.
    /// </summary>
    public static IServiceCollection AddScribegateDomainEvents(this IServiceCollection services)
    {
        // Scope holds the per-request deferred queue + explicit-tx depth. The
        // bus needs the concrete type to call its internal EnqueueDeferred;
        // public callers see only IDomainEventScope.
        services.AddScoped<DomainEventScope>();
        services.AddScoped<IDomainEventScope>(sp => sp.GetRequiredService<DomainEventScope>());

        services.AddScoped<IDomainEventBus, DomainEventBus>();

        // Scoped interceptor so it shares the request's IDomainEventScope.
        services.AddScoped<DomainEventSaveChangesInterceptor>();
        services.AddScoped<IInterceptor>(sp => sp.GetRequiredService<DomainEventSaveChangesInterceptor>());

        return services;
    }
}
