import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import * as sharesApi from '../../api/shares.js';
import { ApiException } from '../../api/client.js';
import type { PublicShareLinkResponse } from '../../api/types.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-markdown-view.js';
import '../shared/sg-time-ago.js';

@customElement('sg-share-page')
export class SgSharePage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; max-width: var(--sg-content-width, 760px); margin: 0 auto; padding: 2rem 1rem; }

    .banner {
      background: var(--sg-surface-alt, #f5f5f5);
      border: 1px solid var(--sg-border, #ddd);
      padding: 0.6rem 0.9rem; border-radius: 6px;
      font-size: 0.85rem; color: var(--sg-text-secondary, #666);
      margin-bottom: 1.5rem;
      display: flex; justify-content: space-between; gap: 1rem; flex-wrap: wrap;
    }
    .banner strong { color: var(--sg-text, #111); }

    header { border-bottom: 1px solid var(--sg-border, #ddd); padding-bottom: 1rem; margin-bottom: 1.5rem; }
    h1 { margin: 0 0 0.25rem; font-size: 1.5rem; }
    .meta { color: var(--sg-text-secondary, #666); font-size: 0.85rem; }

    .error {
      text-align: center; padding: 3rem 1rem; color: var(--sg-danger, #c00);
    }
    .error h1 { color: inherit; }
  `];

  @property() location: any;

  @state() private _data: PublicShareLinkResponse | null = null;
  @state() private _loading = true;
  @state() private _error: { code: string; message: string; details?: string } | null = null;

  private get _token(): string {
    return this.location?.params?.token ?? '';
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    try {
      this._data = await sharesApi.resolve(this._token);
    } catch (err) {
      if (err instanceof ApiException) {
        this._error = { code: err.error.code, message: err.error.message, details: err.error.details };
      } else {
        this._error = { code: 'UNKNOWN', message: 'Could not load this share link.' };
      }
    } finally {
      this._loading = false;
    }
  }

  render() {
    if (this._loading) return html`<p>Loading…</p>`;

    if (this._error) {
      return html`
        <div class="error">
          <h1>${this._error.message}</h1>
          ${this._error.details ? html`<p>${this._error.details}</p>` : ''}
        </div>
      `;
    }

    if (!this._data) return html``;

    return html`
      <div class="banner">
        <span>
          Shared read-only view from
          <strong>${this._data.repositoryName}</strong>
        </span>
        ${this._data.expiresAt
          ? html`<span>Expires <sg-time-ago datetime=${this._data.expiresAt}></sg-time-ago></span>`
          : html`<span>No expiry</span>`}
      </div>

      <header>
        <h1>${this._data.documentPath}</h1>
        <div class="meta">
          Revision ${this._data.revisionId.slice(0, 8)}
          · ${this._data.revisionMessage}
          · <sg-time-ago datetime=${this._data.revisionCreatedAt}></sg-time-ago>
        </div>
      </header>

      <sg-markdown-view
        .content=${this._data.content}
        owner=${this._data.repositoryOwner}
        slug=${this._data.repositorySlug}
        documentPath=${this._data.documentPath}
        shareToken=${this._token}
      ></sg-markdown-view>
    `;
  }
}
