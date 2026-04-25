import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { RouterLocation } from '@vaadin/router';
import * as webhookApi from '../../api/webhooks.js';
import { EVENT_TYPES } from '../../api/webhooks.js';
import { ApiException } from '../../api/client.js';
import { authState } from '../../state/auth-state.js';
import type {
  WebhookResponse,
  WebhookCreatedResponse,
  WebhookDeliveryResponse,
} from '../../api/types.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-webhooks-page')
export class SgWebhooksPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; max-width: var(--sg-content-width, 900px); margin: 0 auto; padding: 2rem 1rem; }
    h1 { margin: 0 0 1rem; font-size: 1.5rem; }
    h2 { margin: 2rem 0 0.5rem; font-size: 1.1rem; border-bottom: 1px solid var(--sg-border, #ddd); padding-bottom: 0.4rem; }
    p.help { color: var(--sg-text-secondary, #666); font-size: 0.9rem; margin: 0.25rem 0 1rem; }

    form { display: flex; flex-direction: column; gap: 0.75rem; max-width: 560px; }
    label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.875rem; }
    input[type=text], input[type=url], textarea {
      padding: 0.5rem; border: 1px solid var(--sg-border, #ccc);
      border-radius: 4px; font: inherit; background: var(--sg-bg, #fff); color: inherit;
    }
    .events { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 0.35rem 0.75rem; }
    .events label { flex-direction: row; align-items: center; gap: 0.4rem; font-family: var(--sg-font-mono, monospace); }

    button {
      padding: 0.5rem 1rem; border-radius: 4px; border: 1px solid var(--sg-border, #ccc);
      background: var(--sg-surface, #fff); color: inherit; cursor: pointer; font: inherit;
    }
    button.primary { background: var(--sg-primary, #2563eb); color: #fff; border-color: var(--sg-primary, #2563eb); }
    button.danger { color: var(--sg-danger, #c00); border-color: var(--sg-danger, #c00); background: none; }
    button:disabled { opacity: 0.5; cursor: not-allowed; }

    .error { color: var(--sg-danger, #c00); font-size: 0.875rem; white-space: pre-wrap; }
    .badge { display: inline-block; padding: 0.1rem 0.45rem; border-radius: 999px; font-size: 0.75rem; border: 1px solid var(--sg-border, #ccc); }
    .badge.ok { color: #007a3d; border-color: #85d1a3; background: #e7f7ee; }
    .badge.fail { color: #a40000; border-color: #f0a0a0; background: #fbe7e7; }
    .badge.off { color: #666; }

    .created {
      background: var(--sg-surface-alt, #fff7d6); border: 1px solid #e2c44e;
      padding: 0.75rem; border-radius: 4px; margin: 0.75rem 0;
    }
    .created .token {
      font-family: var(--sg-font-mono, monospace); word-break: break-all;
      padding: 0.5rem; background: var(--sg-bg, #fff); border-radius: 4px;
      border: 1px solid var(--sg-border, #ccc); margin: 0.5rem 0;
    }
    .created .warning { color: #8a6400; font-size: 0.85rem; }

    table { width: 100%; border-collapse: collapse; margin-top: 0.75rem; font-size: 0.9rem; }
    th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--sg-border, #eee); vertical-align: top; }
    th { font-weight: 600; color: var(--sg-text-secondary, #666); }
    td.muted { color: var(--sg-text-secondary, #888); }
    td.actions { text-align: right; white-space: nowrap; }
    td.url { font-family: var(--sg-font-mono, monospace); word-break: break-all; max-width: 320px; }
    .empty { color: var(--sg-text-secondary, #888); font-style: italic; padding: 1rem 0; }

    .delivery-row.fail td { background: color-mix(in srgb, var(--sg-danger, #c00) 6%, transparent); }
    details { margin-top: 0.75rem; }
  `];

  @state() private _repoOwner = '';
  @state() private _repoSlug = '';
  @state() private _error = '';
  @state() private _submitting = false;
  @state() private _url = '';
  @state() private _description = '';
  @state() private _events = new Set<string>();
  @state() private _created: WebhookCreatedResponse | null = null;
  @state() private _copyFeedback = '';
  @state() private _deliveriesFor: string | null = null;
  @state() private _deliveries: WebhookDeliveryResponse[] = [];

  // autoload: false — connectedCallback redirects unauthenticated users
  // to /login, so we only kick off the fetch after that gate passes.
  private _hooksCtl = new LoadController<WebhookResponse[]>(
    this,
    () => webhookApi.list(this._repoOwner, this._repoSlug).then(r => r.items),
    { autoload: false },
  );

  onBeforeEnter(location: RouterLocation) {
    this._repoOwner = (location.params.owner as string) ?? '';
    this._repoSlug = (location.params.slug as string) ?? '';
  }

  async connectedCallback() {
    super.connectedCallback();
    if (!authState.isAuthenticated) {
      window.location.href = '/login';
      return;
    }
    await this._hooksCtl.reload();
  }

  private _messageFor(err: unknown, fallback: string): string {
    if (err instanceof ApiException) {
      const details = err.error.errors?.map(e => `${e.field}: ${e.message}`).join('\n');
      return details ? `${err.error.message}\n${details}` : err.error.message;
    }
    return fallback;
  }

  private _toggleEvent(ev: string, checked: boolean) {
    if (checked) this._events.add(ev);
    else this._events.delete(ev);
    this.requestUpdate();
  }

  private async _onCreate(e: Event) {
    e.preventDefault();
    this._error = '';
    if (!this._url.trim()) { this._error = 'URL is required.'; return; }
    if (this._events.size === 0) { this._error = 'Select at least one event.'; return; }
    this._submitting = true;
    try {
      this._created = await webhookApi.create(this._repoOwner, this._repoSlug, {
        url: this._url.trim(),
        description: this._description.trim() || undefined,
        events: Array.from(this._events),
      });
      this._url = '';
      this._description = '';
      this._events = new Set();
      await this._hooksCtl.reload();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to create webhook.');
    } finally {
      this._submitting = false;
    }
  }

  private async _onToggle(hook: WebhookResponse) {
    try {
      await webhookApi.update(this._repoOwner, this._repoSlug, hook.id, { enabled: !hook.enabled });
      await this._hooksCtl.reload();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to update webhook.');
    }
  }

  private async _onDelete(hook: WebhookResponse) {
    if (!confirm(`Delete webhook for ${hook.url}? This cannot be undone.`)) return;
    try {
      await webhookApi.remove(this._repoOwner, this._repoSlug, hook.id);
      await this._hooksCtl.reload();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to delete webhook.');
    }
  }

  private async _onTest(hook: WebhookResponse) {
    try {
      await webhookApi.test(this._repoOwner, this._repoSlug, hook.id);
      this._copyFeedback = `Ping queued for ${hook.url}`;
      setTimeout(() => { this._copyFeedback = ''; }, 2500);
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to send test.');
    }
  }

  private async _onViewDeliveries(hook: WebhookResponse) {
    if (this._deliveriesFor === hook.id) {
      this._deliveriesFor = null;
      this._deliveries = [];
      return;
    }
    try {
      const res = await webhookApi.deliveries(this._repoOwner, this._repoSlug, hook.id);
      this._deliveries = res.items;
      this._deliveriesFor = hook.id;
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to load deliveries.');
    }
  }

  private async _copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      this._copyFeedback = 'Copied!';
      setTimeout(() => { this._copyFeedback = ''; }, 1500);
    } catch { this._copyFeedback = 'Copy failed'; }
  }

  private _statusBadge(hook: WebhookResponse) {
    if (!hook.enabled) return html`<span class="badge off">disabled</span>`;
    if (hook.consecutiveFailures > 0)
      return html`<span class="badge fail">${hook.consecutiveFailures} failed</span>`;
    if (hook.lastDeliveryAt) return html`<span class="badge ok">healthy</span>`;
    return html`<span class="badge">new</span>`;
  }

  render() {
    return html`
      <h1>Webhooks — ${this._repoOwner}/${this._repoSlug}</h1>
      <p class="help">
        Webhooks let external services react to events in this repository. Each request includes an
        <code>X-Scribegate-Signature-256</code> header (HMAC-SHA256 of the raw body using your secret).
        Verify it on your endpoint before trusting the payload.
        <a href="/${this._repoOwner}/${this._repoSlug}">← back to repository</a>
      </p>

      ${this._created ? html`
        <div class="created">
          <strong>Webhook created</strong>
          <div class="token">${this._created.secret}</div>
          <button @click=${() => this._copy(this._created!.secret)}>
            ${this._copyFeedback || 'Copy secret'}
          </button>
          <button @click=${() => { this._created = null; this._copyFeedback = ''; }}>Dismiss</button>
          <p class="warning">
            Copy the secret now — it is only shown once. Use it to verify the
            HMAC signature on each webhook request.
          </p>
        </div>
      ` : ''}

      <h2>Add webhook</h2>
      <form @submit=${this._onCreate}>
        <label>
          URL
          <input type="url" required placeholder="https://example.com/scribegate-hook"
            .value=${this._url}
            @input=${(e: Event) => { this._url = (e.target as HTMLInputElement).value; }} />
        </label>
        <label>
          Description (optional)
          <input type="text" maxlength="500"
            .value=${this._description}
            @input=${(e: Event) => { this._description = (e.target as HTMLInputElement).value; }} />
        </label>
        <fieldset style="border:1px solid var(--sg-border,#ddd); border-radius:4px; padding:0.5rem 0.75rem;">
          <legend style="font-size:0.875rem;">Events</legend>
          <div class="events">
            ${EVENT_TYPES.map(ev => html`
              <label>
                <input type="checkbox"
                  .checked=${this._events.has(ev)}
                  @change=${(e: Event) => this._toggleEvent(ev, (e.target as HTMLInputElement).checked)} />
                ${ev}
              </label>
            `)}
          </div>
        </fieldset>
        ${this._error ? html`<div class="error">${this._error}</div>` : ''}
        <button class="primary" type="submit" ?disabled=${this._submitting}>
          ${this._submitting ? 'Creating…' : 'Create webhook'}
        </button>
        ${this._copyFeedback && !this._created ? html`<span class="muted">${this._copyFeedback}</span>` : ''}
      </form>

      <h2>Existing webhooks</h2>
      ${this._hooksCtl.status === 'loading' && !this._hooksCtl.data
        ? html`<p>Loading…</p>`
        : this._hooksCtl.status === 'error'
          ? html`<div class="error">${this._hooksCtl.error}</div>`
          : (this._hooksCtl.data ?? []).length === 0
            ? html`<p class="empty">No webhooks yet.</p>`
            : html`
            <table>
              <thead>
                <tr>
                  <th>URL</th>
                  <th>Events</th>
                  <th>Status</th>
                  <th>Last delivery</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                ${(this._hooksCtl.data ?? []).map(h => html`
                  <tr>
                    <td class="url">
                      ${h.url}
                      ${h.description ? html`<div class="muted">${h.description}</div>` : ''}
                    </td>
                    <td class="muted" style="font-size:0.8rem;">
                      ${h.events.join(', ')}
                    </td>
                    <td>${this._statusBadge(h)}</td>
                    <td class="muted">
                      ${h.lastDeliveryAt
                        ? html`${new Date(h.lastDeliveryAt).toLocaleString()} · ${h.lastDeliveryStatus ?? '—'}`
                        : '—'}
                    </td>
                    <td class="actions">
                      <button @click=${() => this._onTest(h)}>Test</button>
                      <button @click=${() => this._onViewDeliveries(h)}>
                        ${this._deliveriesFor === h.id ? 'Hide' : 'Deliveries'}
                      </button>
                      <button @click=${() => this._onToggle(h)}>
                        ${h.enabled ? 'Disable' : 'Enable'}
                      </button>
                      <button class="danger" @click=${() => this._onDelete(h)}>Delete</button>
                    </td>
                  </tr>
                  ${this._deliveriesFor === h.id ? html`
                    <tr>
                      <td colspan="5">
                        ${this._deliveries.length === 0
                          ? html`<div class="empty">No deliveries yet.</div>`
                          : html`
                            <table>
                              <thead>
                                <tr>
                                  <th>When</th>
                                  <th>Event</th>
                                  <th>Status</th>
                                  <th>Attempts</th>
                                  <th>Duration</th>
                                  <th>Error</th>
                                </tr>
                              </thead>
                              <tbody>
                                ${this._deliveries.map(d => html`
                                  <tr class="delivery-row ${d.succeeded ? '' : 'fail'}">
                                    <td class="muted">${new Date(d.createdAt).toLocaleString()}</td>
                                    <td>${d.eventType}</td>
                                    <td>${d.statusCode ?? '—'}</td>
                                    <td>${d.attemptCount}</td>
                                    <td class="muted">${d.durationMs} ms</td>
                                    <td class="muted">${d.error ?? ''}</td>
                                  </tr>
                                `)}
                              </tbody>
                            </table>
                          `}
                      </td>
                    </tr>
                  ` : ''}
                `)}
              </tbody>
            </table>
          `}

      <details>
        <summary>Payload and signature verification</summary>
        <pre style="background:var(--sg-bg,#f6f8fa);padding:0.75rem;border-radius:4px;overflow:auto;font-size:0.8rem;">
POST https://your-endpoint
Content-Type: application/json
X-Scribegate-Event: proposal.approved
X-Scribegate-Delivery: 01860...
X-Scribegate-Signature-256: sha256=&lt;hex&gt;

{
  "repository": { "id": "…", "slug": "docs", "name": "Docs" },
  "proposal":   { "id": "…", "title": "…", "status": "Approved" },
  "actor":      { "id": "…", "username": "…" },
  "timestamp":  "2026-04-16T19:00:00Z"
}

# Node.js verification:
const h = crypto.createHmac('sha256', secret).update(rawBody).digest('hex');
if (!crypto.timingSafeEqual(Buffer.from('sha256=' + h), Buffer.from(req.header('X-Scribegate-Signature-256')))) { /* reject */ }
        </pre>
      </details>
    `;
  }
}
