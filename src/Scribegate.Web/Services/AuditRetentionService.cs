using Scribegate.Core.Stores;

namespace Scribegate.Web.Services;

/// <summary>
/// Periodically prunes personal-data fields from old audit events so the
/// audit trail retains the "who did what" record for security and
/// compliance without keeping personally identifying metadata longer than
/// necessary.
/// </summary>
/// <remarks>
/// Only the <c>IpAddress</c> column is pruned today — it's the one field
/// that directly identifies a natural person under GDPR and is not
/// required for the audit purpose once the initial investigation window
/// has passed. The event itself (actor, target, timestamp, event type)
/// is retained indefinitely.
/// </remarks>
public class AuditRetentionService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AuditRetentionService> logger) : BackgroundService
{
    private readonly TimeSpan _ipRetention = TimeSpan.FromDays(
        configuration.GetValue("Scribegate:Audit:IpRetentionDays", 90));

    private readonly TimeSpan _interval = TimeSpan.FromHours(
        configuration.GetValue("Scribegate:Audit:PruneIntervalHours", 24));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup so a long-idle instance catches up immediately,
        // then on the configured interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Audit IP prune failed; will retry on next interval");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PruneAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IAuditEventStore>();
        var cutoff = DateTime.UtcNow - _ipRetention;
        var affected = await store.PruneIpAddressesOlderThanAsync(cutoff, ct);
        if (affected > 0)
        {
            logger.LogInformation(
                "Audit retention: pruned IP address from {Count} audit event(s) older than {Cutoff:O}",
                affected,
                cutoff);
        }
    }
}
