using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scribegate.Core.Entities;
using Scribegate.Core.Stores;
using Scribegate.Data;

namespace Scribegate.Web.Services;

public class WebhookDeliveryWorker(
    WebhookDispatcher dispatcher,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<WebhookDeliveryWorker> logger) : BackgroundService
{
    private static readonly int[] RetryDelaysSeconds = [2, 10, 60];
    private const int FailureThresholdToDisable = 10;
    private const int MaxResponseBodyLength = 2000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var envelope in dispatcher.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await ProcessAsync(envelope, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error delivering webhook event {EventType}", envelope.EventType);
            }
        }
    }

    private async Task ProcessAsync(WebhookEnvelope envelope, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWebhookStore>();
        var db = scope.ServiceProvider.GetRequiredService<ScribegateDbContext>();

        IReadOnlyList<Webhook> targets;
        if (envelope.TargetWebhookId.HasValue)
        {
            // Direct delivery (e.g., /test) bypasses Enabled and subscription filters —
            // the caller explicitly asked to deliver to this specific hook.
            var hook = await store.GetByIdAsync(envelope.TargetWebhookId.Value, ct);
            targets = hook is null ? [] : [hook];
        }
        else
        {
            targets = await store.ListSubscribersAsync(envelope.RepositoryId, envelope.EventType, ct);
        }
        if (targets.Count == 0) return;

        var client = httpClientFactory.CreateClient("webhooks");

        foreach (var webhook in targets)
        {
            if (ct.IsCancellationRequested) break;
            await DeliverAsync(webhook, envelope, client, store, db, ct);
        }
    }

    private async Task DeliverAsync(Webhook webhook, WebhookEnvelope envelope, HttpClient client, IWebhookStore store, ScribegateDbContext db, CancellationToken ct)
    {
        var deliveryId = Guid.CreateVersion7();
        var attempts = 0;
        int? finalStatus = null;
        string? finalError = null;
        string? responseSnippet = null;
        var sw = Stopwatch.StartNew();

        for (var i = 0; i <= RetryDelaysSeconds.Length; i++)
        {
            attempts++;
            try
            {
                using var req = BuildRequest(webhook, envelope, deliveryId);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

                finalStatus = (int)res.StatusCode;
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                responseSnippet = TruncateUtf16Safe(body, MaxResponseBodyLength);

                if (res.IsSuccessStatusCode) break;
                if (i == RetryDelaysSeconds.Length) break;

                var status = (int)res.StatusCode;
                if (status is >= 400 and < 500 && status != 408 && status != 429) break;
            }
            catch (Exception ex)
            {
                finalError = TruncateUtf16Safe(ex.Message, 1000);
                if (i == RetryDelaysSeconds.Length) break;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(RetryDelaysSeconds[i]), ct); }
            catch (OperationCanceledException) { break; }
        }

        sw.Stop();
        var succeeded = finalStatus is >= 200 and < 300;

        await RecordDeliveryAsync(store, db, webhook, envelope, deliveryId, attempts, finalStatus, finalError, responseSnippet, succeeded, (int)sw.ElapsedMilliseconds, ct);
    }

    private HttpRequestMessage BuildRequest(Webhook webhook, WebhookEnvelope envelope, Guid deliveryId)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, webhook.Url);
        var payload = envelope.PayloadJson;

        var signature = ComputeSignature(webhook.Secret, payload);
        req.Headers.Add("X-Scribegate-Event", envelope.EventType);
        req.Headers.Add("X-Scribegate-Delivery", deliveryId.ToString());
        req.Headers.Add("X-Scribegate-Signature-256", $"sha256={signature}");
        req.Headers.Add("User-Agent", "Scribegate-Webhook/1.0");

        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        return req;
    }

    private async Task RecordDeliveryAsync(
        IWebhookStore store, ScribegateDbContext db, Webhook webhook, WebhookEnvelope envelope,
        Guid deliveryId, int attempts, int? statusCode, string? error, string? responseBody,
        bool succeeded, int durationMs, CancellationToken ct)
    {
        try
        {
            var delivery = new WebhookDelivery
            {
                Id = deliveryId,
                WebhookId = webhook.Id,
                EventType = envelope.EventType,
                Payload = envelope.PayloadJson,
                AttemptCount = attempts,
                StatusCode = statusCode,
                Error = error,
                ResponseBody = responseBody,
                Succeeded = succeeded,
                DurationMs = durationMs,
                DeliveredAt = DateTime.UtcNow,
            };
            await store.CreateDeliveryAsync(delivery, ct);

            // Atomic status updates — avoids clobbering concurrent admin PUTs
            // (which rewrite URL, events, description, etc.).
            var now = DateTime.UtcNow;
            if (succeeded)
            {
                await db.Webhooks
                    .Where(w => w.Id == webhook.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.ConsecutiveFailures, 0)
                        .SetProperty(w => w.LastDeliveryAt, now)
                        .SetProperty(w => w.LastDeliveryStatus, statusCode),
                        ct);
            }
            else
            {
                await db.Webhooks
                    .Where(w => w.Id == webhook.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.ConsecutiveFailures, w => w.ConsecutiveFailures + 1)
                        .SetProperty(w => w.LastDeliveryAt, now)
                        .SetProperty(w => w.LastDeliveryStatus, statusCode),
                        ct);

                var disabled = await db.Webhooks
                    .Where(w => w.Id == webhook.Id && w.Enabled && w.ConsecutiveFailures >= FailureThresholdToDisable)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(w => w.Enabled, false)
                        .SetProperty(w => w.DisabledAt, now),
                        ct);

                if (disabled > 0)
                {
                    logger.LogWarning("Auto-disabled webhook {WebhookId} after {Threshold}+ consecutive failures",
                        webhook.Id, FailureThresholdToDisable);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to record webhook delivery for {WebhookId}", webhook.Id);
        }
    }

    public static string ComputeSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    // Truncate a string on a UTF-16 code-unit boundary without splitting a
    // surrogate pair. Cheap defense against cosmetic breakage in saved logs.
    private static string TruncateUtf16Safe(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
        var end = maxLength;
        if (end > 0 && char.IsHighSurrogate(value[end - 1])) end--;
        return value[..end];
    }
}
