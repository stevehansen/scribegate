import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import * as authApi from '../../api/auth.js';
import { ApiException } from '../../api/client.js';
import { authState } from '../../state/auth-state.js';
import type { ApiTokenResponse, ApiTokenCreatedResponse } from '../../api/types.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-settings-page')
export class SgSettingsPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; max-width: var(--sg-content-width, 760px); margin: 0 auto; padding: 2rem 1rem; }
    h1 { margin: 0 0 1rem; font-size: 1.5rem; }
    h2 { margin: 2rem 0 0.5rem; font-size: 1.1rem; border-bottom: 1px solid var(--sg-border, #ddd); padding-bottom: 0.4rem; }
    p.help { color: var(--sg-text-secondary, #666); font-size: 0.9rem; margin: 0.25rem 0 1rem; }

    form { display: flex; flex-direction: column; gap: 0.75rem; max-width: 420px; }
    label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.875rem; }
    input {
      padding: 0.5rem; border: 1px solid var(--sg-border, #ccc);
      border-radius: 4px; font: inherit; background: var(--sg-bg, #fff); color: inherit;
    }
    button {
      padding: 0.5rem 1rem; border-radius: 4px; border: 1px solid var(--sg-border, #ccc);
      background: var(--sg-surface, #fff); color: inherit; cursor: pointer; font: inherit;
      align-self: flex-start;
    }
    button.primary { background: var(--sg-primary, #2563eb); color: #fff; border-color: var(--sg-primary, #2563eb); }
    button.danger { background: none; color: var(--sg-danger, #c00); border-color: var(--sg-danger, #c00); }
    button:disabled { opacity: 0.5; cursor: not-allowed; }

    .error { color: var(--sg-danger, #c00); font-size: 0.875rem; }

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

    table { width: 100%; border-collapse: collapse; margin-top: 0.75rem; }
    th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--sg-border, #eee); font-size: 0.9rem; }
    th { font-weight: 600; color: var(--sg-text-secondary, #666); }
    td.muted { color: var(--sg-text-secondary, #888); }
    td.actions { text-align: right; }
    .empty { color: var(--sg-text-secondary, #888); font-style: italic; padding: 1rem 0; }
  `];

  @state() private _error = '';
  @state() private _submitting = false;
  @state() private _name = '';
  @state() private _expiresInDays: number | '' = '';
  @state() private _created: ApiTokenCreatedResponse | null = null;
  @state() private _copyFeedback = '';

  // autoload: false — connectedCallback redirects unauthenticated users
  // to /login, so we only kick off the fetch after that gate passes.
  private _tokensCtl = new LoadController<ApiTokenResponse[]>(
    this,
    () => authApi.listApiTokens(),
    { autoload: false },
  );

  async connectedCallback() {
    super.connectedCallback();
    if (!authState.isAuthenticated) {
      window.location.href = '/login';
      return;
    }
    await this._tokensCtl.reload();
  }

  private async _onCreate(e: Event) {
    e.preventDefault();
    this._error = '';
    if (!this._name.trim()) {
      this._error = 'Name is required.';
      return;
    }
    this._submitting = true;
    try {
      const expires = typeof this._expiresInDays === 'number' && this._expiresInDays > 0
        ? this._expiresInDays
        : undefined;
      this._created = await authApi.createApiToken(this._name.trim(), expires);
      this._name = '';
      this._expiresInDays = '';
      await this._tokensCtl.reload();
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Failed to create token.';
    } finally {
      this._submitting = false;
    }
  }

  private async _onRevoke(id: string, name: string) {
    if (!confirm(`Revoke API token "${name}"? Anything using it will stop working immediately.`)) return;
    try {
      await authApi.deleteApiToken(id);
      await this._tokensCtl.reload();
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

  private _dismissCreated() {
    this._created = null;
    this._copyFeedback = '';
  }

  render() {
    return html`
      <h1>Settings</h1>

      <h2>API tokens</h2>
      <p class="help">
        API tokens let the <code>sg</code> CLI and external services act on your behalf.
        Use <code>sg auth token &lt;token&gt;</code> to save one in the CLI, or pass it as
        <code>Authorization: Bearer &lt;token&gt;</code> in API requests.
      </p>

      ${this._created ? html`
        <div class="created">
          <strong>New API token "${this._created.name}"</strong>
          <div class="token">${this._created.token}</div>
          <button @click=${() => this._copy(this._created!.token)}>
            ${this._copyFeedback || 'Copy token'}
          </button>
          <button @click=${this._dismissCreated}>Dismiss</button>
          <p class="warning">
            Copy it now — this is the only time the raw token is shown.
            It's SHA-256 hashed in the database.
          </p>
        </div>
      ` : ''}

      <form @submit=${this._onCreate}>
        <label>
          Name
          <input
            type="text"
            maxlength="80"
            placeholder="e.g. CI pipeline, my laptop sg CLI"
            .value=${this._name}
            @input=${(e: Event) => { this._name = (e.target as HTMLInputElement).value; }}
            required />
        </label>
        <label>
          Expires in (days, blank = never)
          <input
            type="number"
            min="1"
            max="3650"
            .value=${String(this._expiresInDays)}
            @input=${(e: Event) => {
              const v = (e.target as HTMLInputElement).value;
              this._expiresInDays = v === '' ? '' : Number(v);
            }} />
        </label>
        ${this._error ? html`<div class="error">${this._error}</div>` : ''}
        <button class="primary" type="submit" ?disabled=${this._submitting}>
          ${this._submitting ? 'Creating…' : 'Create token'}
        </button>
      </form>

      ${this._tokensCtl.status === 'loading' && !this._tokensCtl.data
        ? html`<p>Loading…</p>`
        : this._tokensCtl.status === 'error'
          ? html`<div class="error">${this._tokensCtl.error}</div>`
          : (this._tokensCtl.data ?? []).length === 0
            ? html`<p class="empty">No API tokens yet.</p>`
            : html`
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Created</th>
                  <th>Last used</th>
                  <th>Expires</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                ${(this._tokensCtl.data ?? []).map(t => html`
                  <tr>
                    <td>${t.name}</td>
                    <td class="muted">${new Date(t.createdAt).toLocaleDateString()}</td>
                    <td class="muted">${t.lastUsedAt ? new Date(t.lastUsedAt).toLocaleDateString() : 'never'}</td>
                    <td class="muted">${t.expiresAt ? new Date(t.expiresAt).toLocaleDateString() : 'never'}</td>
                    <td class="actions">
                      <button class="danger" @click=${() => this._onRevoke(t.id, t.name)}>Revoke</button>
                    </td>
                  </tr>
                `)}
              </tbody>
            </table>
          `}
    `;
  }
}
