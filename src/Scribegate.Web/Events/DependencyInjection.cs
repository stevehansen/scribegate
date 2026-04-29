using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Scribegate.Core.Events;
using Scribegate.Data.Events;
using Scribegate.Web.Events.Handlers;

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

        // Per-event handlers. Order in this method = dispatch order for the
        // event family. Audit goes first (immediate, joins the transaction);
        // notify before webhook keeps end-user-visible signals ahead of
        // outbound integrations.
        services.AddScoped<IImmediateDomainEventHandler<ProposalMergedEvent>, AuditProposalMergedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalMergedEvent>, NotifyProposalMergedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalMergedEvent>, WebhookProposalMergedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<DocumentCreatedEvent>, AuditDocumentCreatedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<DocumentCreatedEvent>, WebhookDocumentCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<DocumentUpdatedEvent>, AuditDocumentUpdatedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<DocumentUpdatedEvent>, WebhookDocumentUpdatedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<DocumentArchivedEvent>, AuditDocumentArchivedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<DocumentArchivedEvent>, WebhookDocumentArchivedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<DocumentUnarchivedEvent>, AuditDocumentUnarchivedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<DocumentMovedEvent>, AuditDocumentMovedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<DocumentMovedEvent>, WebhookDocumentMovedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<RepositoryCreatedEvent>, AuditRepositoryCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<RepositoryUpdatedEvent>, AuditRepositoryUpdatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<RepositoryDeletedEvent>, AuditRepositoryDeletedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<MemberAddedEvent>, AuditMemberAddedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<MemberUpdatedEvent>, AuditMemberUpdatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<MemberRemovedEvent>, AuditMemberRemovedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ShareLinkCreatedEvent>, AuditShareLinkCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ShareLinkRevokedEvent>, AuditShareLinkRevokedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ShareLinkAccessedEvent>, AuditShareLinkAccessedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<WebhookCreatedEvent>, AuditWebhookCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<WebhookUpdatedEvent>, AuditWebhookUpdatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<WebhookDeletedEvent>, AuditWebhookDeletedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<WebhookTestedEvent>, AuditWebhookTestedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<DocumentTemplateCreatedEvent>, AuditDocumentTemplateCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<DocumentTemplateUpdatedEvent>, AuditDocumentTemplateUpdatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<DocumentTemplateDeletedEvent>, AuditDocumentTemplateDeletedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<MediaUploadedEvent>, AuditMediaUploadedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<MediaDeletedEvent>, AuditMediaDeletedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<UserRegisteredEvent>, AuditUserRegisteredHandler>();
        services.AddScoped<IImmediateDomainEventHandler<UserLoggedInEvent>, AuditUserLoggedInHandler>();
        services.AddScoped<IImmediateDomainEventHandler<UserLoginFailedEvent>, AuditUserLoginFailedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ApiTokenCreatedEvent>, AuditApiTokenCreatedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ApiTokenRevokedEvent>, AuditApiTokenRevokedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<SmtpTestRunEvent>, AuditSmtpTestRunHandler>();
        services.AddScoped<IImmediateDomainEventHandler<SystemSettingChangedEvent>, AuditSystemSettingChangedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<UserTierChangedEvent>, AuditUserTierChangedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ContentReportedEvent>, AuditContentReportedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<ContentReportReviewedEvent>, AuditContentReportReviewedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<RepositoryExportedEvent>, AuditRepositoryExportedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<SiteGeneratedEvent>, AuditSiteGeneratedHandler>();
        services.AddScoped<IImmediateDomainEventHandler<RepositoryClonedEvent>, AuditRepositoryClonedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ProposalCreatedEvent>, AuditProposalCreatedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalCreatedEvent>, NotifyProposalCreatedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalCreatedEvent>, WebhookProposalCreatedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ProposalSubmittedEvent>, AuditProposalSubmittedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalSubmittedEvent>, WebhookProposalSubmittedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ProposalWithdrawnEvent>, AuditProposalWithdrawnHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalWithdrawnEvent>, WebhookProposalWithdrawnHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ProposalRejectedEvent>, AuditProposalRejectedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalRejectedEvent>, NotifyProposalRejectedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ProposalRejectedEvent>, WebhookProposalRejectedHandler>();

        services.AddScoped<IDeferredDomainEventHandler<CommentCreatedEvent>, NotifyCommentCreatedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<CommentCreatedEvent>, WebhookCommentCreatedHandler>();

        services.AddScoped<IImmediateDomainEventHandler<ReviewSubmittedEvent>, AuditReviewSubmittedHandler>();
        services.AddScoped<IDeferredDomainEventHandler<ReviewSubmittedEvent>, WebhookReviewSubmittedHandler>();

        return services;
    }
}
