import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import * as sharesApi from '../../api/shares.js';
import { ApiException } from '../../api/client.js';
import type { ShareLinkResponse, ShareLinkCreatedResponse } from '../../api/types.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-share-dialog')
export class SgShareDialog extends LitElement {
  static styles = [boxReset, css`
    :host { display: none; }
    :host([open]) { display: block; }

    .backdrop {
      position: fixed; inset: 0; background: rgba(0, 0, 0, 0.45);
      display: flex; align-items: center; justify-content: center;
      z-index: 100;
    }
    .dialog {
      background: var(--sg-surface, #fff);
      color: var(--sg-text, #111);
      border-radius: 8px;
      width: min(560px, 92vw);
      max-height: 90vh; overflow-y: auto;
      box-shadow: 0 10px 40px rgba(0,0,0,0.25);
      padding: 1.5rem;
    }
    h2 { margin: 0 0 1rem; font-size: 1.25rem; }

    form { display: flex; flex-direction: column; gap: 0.75rem; }
    label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.875rem; }
    input, select, textarea {
      padding: 0.5rem; border: 1px solid var(--sg-border, #ccc);
      border-radius: 4px; font: inherit; background: var(--sg-bg, #fff); color: inherit;
    }
    textarea { min-height: 60px; resize: vertical; }

    .row { display: flex; gap: 0.75rem; align-items: end; }
    .row > * { flex: 1; }
    .checkbox-row { flex-direction: row; align-items: center; gap: 0.5rem; }

    .actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem; }
    button {
      padding: 0.5rem 1rem; border-radius: 4px; border: 1px solid var(--sg-border, #ccc);
      background: var(--sg-surface, #fff); color: inherit; cursor: pointer; font: inherit;
    }
    button.primary {
      background: var(--sg-primary, #2563eb); color: #fff; border-color: var(--sg-primary, #2563eb);
    }
    button:disabled { opacity: 0.5; cursor: not-allowed; }

    .error { color: var(--sg-danger, #c00); font-size: 0.875rem; }

    .created {
      background: var(--sg-surface-alt, #f5f5f5); border: 1px solid var(--sg-border, #ccc);
      padding: 0.75rem; border-radius: 4px; margin-top: 0.75rem;
    }
    .created .url {
      font-family: var(--sg-font-mono, monospace); word-break: break-all;
      padding: 0.5rem; background: var(--sg-bg, #fff); border-radius: 4px; margin: 0.5rem 0;
      border: 1px solid var(--sg-border, #ccc);
    }

    .existing { margin-top: 1.5rem; border-top: 1px solid var(--sg-border, #ccc); padding-top: 1rem; }
    .existing h3 { margin: 0 0 0.5rem; font-size: 0.95rem; }
    .link-row {
      display: flex; justify-content: space-between; gap: 0.5rem; align-items: center;
      padding: 0.4rem 0; border-bottom: 1px solid var(--sg-border, #eee); font-size: 0.875rem;
    }
    .link-row:last-child { border-bottom: none; }
    .link-meta { color: var(--sg-text-secondary, #666); font-size: 0.8rem; }
    .revoked { opacity: 0.55; text-decoration: line-through; }
  `];

  @property({ type: Boolean, reflect: true }) open = false;
  @property() repoSlug = '';
  @property() docPath = '';

  @state() private _description = '';
  @state() private _expiresInDays = 7;
  @state() private _permanent = false;
  @state() private _submitting = false;
  @state() private _error = '';
  @state() private _created: ShareLinkCreatedResponse | null = null;
  @state() private _existing: ShareLinkResponse[] = [];
  @state() private _copyFeedback = '';

  updated(changed: Map<string, unknown>) {
    if (changed.has('open') && this.open) {
      this._reset();
      this._loadExisting();
    }
  }

  private _reset() {
    this._description = '';
    this._expiresInDays = 7;
    this._permanent = false;
    this._error = '';
    this._created = null;
    this._copyFeedback = '';
  }

  private async _loadExisting() {
    try {
      const result = await sharesApi.list(this.repoSlug, this.docPath);
      this._existing = result.items;
    } catch {
      this._existing = [];
    }
  }

  private async _onSubmit(e: Event) {
    e.preventDefault();
    this._error = '';
    this._submitting = true;
    try {
      const result = await sharesApi.create(this.repoSlug, {
        path: this.docPath,
        description: this._description.trim() || undefined,
        expiresInDays: this._permanent ? undefined : this._expiresInDays,
        permanent: this._permanent,
      });
      this._created = result;
      await this._loadExisting();
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Failed to create share link.';
    } finally {
      this._submitting = false;
    }
  }

  private async _onRevoke(id: string) {
    if (!confirm('Revoke this share link? Anyone using it will lose access immediately.')) return;
    try {
      await sharesApi.revoke(this.repoSlug, id);
      await this._loadExisting();
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Failed to revoke.';
    }
  }

  private async _copy(text: string) {
    try {
      await navigator.clipboard.writeText(text);
      this._copyFeedback = 'Copied!';
      setTimeout(() => { this._copyFeedback = ''; }, 1500);
    } catch {
      this._copyFeedback = 'Copy failed';
    }
  }

  private _close() {
    this.open = false;
    this.dispatchEvent(new CustomEvent('close'));
  }

  render() {
    if (!this.open) return html``;

    const fullUrl = this._created ? `${window.location.origin}${this._created.url}` : '';

    return html`
      <div class="backdrop" @click=${(e: Event) => { if (e.target === e.currentTarget) this._close(); }}>
        <div class="dialog" role="dialog" aria-modal="true" aria-labelledby="share-title">
          <h2 id="share-title">Share "${this.docPath}"</h2>

          ${this._created ? html`
            <div class="created">
              <strong>Share link created.</strong>
              <div class="url">${fullUrl}</div>
              <button @click=${() => this._copy(fullUrl)}>
                ${this._copyFeedback || 'Copy link'}
              </button>
              <p style="font-size: 0.8rem; margin: 0.5rem 0 0; color: var(--sg-text-secondary);">
                ${this._created.expiresAt
                  ? html`Expires ${new Date(this._created.expiresAt).toLocaleString()}`
                  : html`This link never expires.`}
              </p>
            </div>
          ` : html`
            <form @submit=${this._onSubmit}>
              <label>
                Description (optional)
                <input
                  type="text"
                  .value=${this._description}
                  @input=${(e: Event) => { this._description = (e.target as HTMLInputElement).value; }}
                  placeholder="e.g. Review copy for Q2 comms" />
              </label>

              <label class="checkbox-row">
                <input
                  type="checkbox"
                  .checked=${this._permanent}
                  @change=${(e: Event) => { this._permanent = (e.target as HTMLInputElement).checked; }} />
                Never expires
              </label>

              ${!this._permanent ? html`
                <label>
                  Expires in (days)
                  <input
                    type="number"
                    min="1"
                    max="365"
                    .value=${String(this._expiresInDays)}
                    @input=${(e: Event) => { this._expiresInDays = Number((e.target as HTMLInputElement).value); }} />
                </label>
              ` : ''}

              ${this._error ? html`<div class="error">${this._error}</div>` : ''}

              <div class="actions">
                <button type="button" @click=${this._close}>Cancel</button>
                <button class="primary" type="submit" ?disabled=${this._submitting}>
                  ${this._submitting ? 'Creating...' : 'Create link'}
                </button>
              </div>
            </form>
          `}

          ${this._existing.length > 0 ? html`
            <div class="existing">
              <h3>Existing links (${this._existing.length})</h3>
              ${this._existing.map(link => html`
                <div class="link-row ${!link.isActive ? 'revoked' : ''}">
                  <div>
                    <div>${link.tokenPrefix}… ${link.description ? html`— ${link.description}` : ''}</div>
                    <div class="link-meta">
                      ${link.isActive
                        ? (link.expiresAt
                            ? `expires ${new Date(link.expiresAt).toLocaleDateString()}`
                            : 'never expires')
                        : (link.revokedAt ? 'revoked' : 'expired')}
                      · ${link.accessCount} ${link.accessCount === 1 ? 'view' : 'views'}
                      · by ${link.createdBy}
                    </div>
                  </div>
                  ${link.isActive
                    ? html`<button @click=${() => this._onRevoke(link.id)}>Revoke</button>`
                    : ''}
                </div>
              `)}
            </div>
          ` : ''}

          ${this._created ? html`
            <div class="actions">
              <button @click=${this._close}>Done</button>
            </div>
          ` : ''}
        </div>
      </div>
    `;
  }
}
