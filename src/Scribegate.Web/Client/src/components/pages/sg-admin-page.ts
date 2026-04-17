import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { SettingResponse, AuditEventResponse } from '../../api/types.js';
import * as adminApi from '../../api/admin.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-time-ago.js';

@customElement('sg-admin-page')
export class SgAdminPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-2xl); margin-bottom: 1rem; color: var(--sg-text); }
    h2 { font-size: var(--sg-font-size-lg); margin-top: 1.5rem; margin-bottom: 0.75rem; color: var(--sg-text); }
    .settings { display: flex; flex-direction: column; gap: 0.5rem; }
    .setting {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius-lg);
      background: var(--sg-bg-elevated);
    }
    .setting-key { font-weight: 500; font-size: var(--sg-font-size-sm); color: var(--sg-text); }
    .setting-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); }
    .toggle {
      cursor: pointer; padding: 0.25rem 0.75rem; border-radius: var(--sg-radius);
      font-size: 0.8125rem; border: 1px solid var(--sg-border); background: var(--sg-bg-elevated);
      transition: background var(--sg-transition-fast), color var(--sg-transition-fast);
    }
    .toggle.on { background: var(--sg-success-light); color: var(--sg-success); border-color: var(--sg-success-border); }
    .toggle.off { background: var(--sg-danger-light); color: var(--sg-danger); border-color: var(--sg-danger-border); }
    .value-edit { display: flex; align-items: center; gap: 0.5rem; }
    .value-edit input, .value-edit select {
      background: var(--sg-bg); color: var(--sg-text);
      border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      padding: 0.25rem 0.5rem; font-size: 0.8125rem; min-width: 10rem;
    }
    .value-edit input:focus, .value-edit select:focus { outline: none; border-color: var(--sg-primary); }
    .value-edit button {
      cursor: pointer; padding: 0.25rem 0.75rem; border-radius: var(--sg-radius);
      font-size: 0.8125rem; border: 1px solid var(--sg-border); background: var(--sg-bg-elevated);
      color: var(--sg-text-secondary);
    }
    .value-edit button.primary { background: var(--sg-primary); color: var(--sg-on-primary, #fff); border-color: var(--sg-primary); }
    .value-edit button:hover { background: var(--sg-bg-secondary); }
    .value-edit button.primary:hover { opacity: 0.9; }
    .setting-saved { color: var(--sg-success); font-size: 0.75rem; }
    .audit-table { width: 100%; border-collapse: collapse; font-size: 0.8125rem; }
    .audit-table th {
      text-align: left; padding: 0.5rem; border-bottom: 2px solid var(--sg-border);
      font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary);
    }
    .audit-table td { padding: 0.5rem; border-bottom: 1px solid var(--sg-border); color: var(--sg-text); }
    .badge {
      font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px;
      background: var(--sg-bg-tertiary); color: var(--sg-text-secondary);
    }
    .error { color: var(--sg-danger); }
    .tabs { display: flex; gap: 0; border-bottom: 1px solid var(--sg-border); margin-bottom: 1rem; }
    .tab {
      padding: 0.5rem 1rem; cursor: pointer; font-size: var(--sg-font-size-sm);
      border-bottom: 2px solid transparent; color: var(--sg-text-secondary);
      transition: color var(--sg-transition-fast);
    }
    .tab.active { color: var(--sg-primary); border-bottom-color: var(--sg-primary); font-weight: 500; }
  `];

  @state() private _settings: SettingResponse[] = [];
  @state() private _auditEvents: AuditEventResponse[] = [];
  @state() private _loading = true;
  @state() private _error = '';
  @state() private _tab = 'settings';
  @state() private _drafts: Record<string, string> = {};
  @state() private _savedKey: string | null = null;

  private static readonly ENUM_CHOICES: Record<string, string[]> = {
    'instance.tier_mode': ['none', 'enforced'],
    'instance.default_tier': ['free', 'paid'],
  };

  private static readonly SECRET_KEYS = new Set([
    'oidc.client_secret',
    'smtp.password',
  ]);

  private static readonly NUMERIC_KEYS = new Set([
    'account.age_gate_hours',
    'smtp.port',
    'tier.free.max_repositories',
    'tier.free.max_documents_per_repo',
    'tier.free.max_storage_mb',
    'tier.free.max_api_tokens',
    'tier.free.max_members_per_repo',
    'tier.paid.max_repositories',
    'tier.paid.max_documents_per_repo',
    'tier.paid.max_storage_mb',
    'tier.paid.max_api_tokens',
    'tier.paid.max_members_per_repo',
  ]);

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    try {
      const [settings, audit] = await Promise.all([
        adminApi.listSettings(),
        adminApi.listAuditEvents({ take: 50 }),
      ]);
      this._settings = settings;
      this._auditEvents = audit.items;
    } catch (e) {
      this._error = e instanceof ApiException ? e.error.message : 'Failed to load admin data.';
    } finally { this._loading = false; }
  }

  private async _toggleSetting(key: string, current: string) {
    const newValue = current === 'true' ? 'false' : 'true';
    await this._saveSetting(key, newValue);
  }

  private async _saveSetting(key: string, newValue: string) {
    try {
      this._error = '';
      await adminApi.updateSetting(key, newValue);
      const settings = await adminApi.listSettings();
      this._settings = settings;
      const { [key]: _, ...rest } = this._drafts;
      this._drafts = rest;
      this._savedKey = key;
      setTimeout(() => { if (this._savedKey === key) { this._savedKey = null; this.requestUpdate(); } }, 1500);
    } catch (e) {
      this._error = e instanceof ApiException ? e.error.message : 'Failed to save setting.';
    }
  }

  private _onDraftInput(key: string, value: string) {
    this._drafts = { ...this._drafts, [key]: value };
  }

  private _draftValue(s: SettingResponse): string {
    return this._drafts[s.key] ?? s.value;
  }

  private _isBoolean(v: string) { return v === 'true' || v === 'false'; }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (this._error && !this._settings.length) return html`<p class="error">${this._error}</p>`;

    return html`
      <h1>Administration</h1>

      <div class="tabs">
        <div class="tab ${this._tab === 'settings' ? 'active' : ''}" @click=${() => this._tab = 'settings'}>Settings</div>
        <div class="tab ${this._tab === 'audit' ? 'active' : ''}" @click=${() => this._tab = 'audit'}>Audit Log</div>
      </div>

      ${this._tab === 'settings' ? this._renderSettings() : ''}
      ${this._tab === 'audit' ? this._renderAudit() : ''}
    `;
  }

  private _renderSettings() {
    return html`
      ${this._error ? html`<p class="error">${this._error}</p>` : ''}
      <div class="settings">
        ${this._settings.map(s => html`
          <div class="setting">
            <div>
              <div class="setting-key">${s.key}</div>
              <div class="setting-meta">
                ${SgAdminPage.SECRET_KEYS.has(s.key)
                  ? html`Value: ${s.value ? '••••••••' : '(empty)'}`
                  : html`Value: ${s.value || '(empty)'}`}
              </div>
            </div>
            ${this._isBoolean(s.value) ? this._renderToggle(s) : this._renderEditor(s)}
          </div>
        `)}
      </div>
    `;
  }

  private _renderToggle(s: SettingResponse) {
    return html`
      <div class="value-edit">
        ${this._savedKey === s.key ? html`<span class="setting-saved">Saved</span>` : ''}
        <button class="toggle ${s.value === 'true' ? 'on' : 'off'}" @click=${() => this._toggleSetting(s.key, s.value)}>
          ${s.value === 'true' ? 'Enabled' : 'Disabled'}
        </button>
      </div>
    `;
  }

  private _renderEditor(s: SettingResponse) {
    const draft = this._draftValue(s);
    const dirty = draft !== s.value;
    const choices = SgAdminPage.ENUM_CHOICES[s.key];
    const isSecret = SgAdminPage.SECRET_KEYS.has(s.key);
    const isNumeric = SgAdminPage.NUMERIC_KEYS.has(s.key);

    const input = choices
      ? html`<select @change=${(e: Event) => this._onDraftInput(s.key, (e.target as HTMLSelectElement).value)}>
          ${choices.map(c => html`<option value=${c} ?selected=${draft === c}>${c}</option>`)}
        </select>`
      : html`<input
          type=${isSecret ? 'password' : isNumeric ? 'number' : 'text'}
          .value=${draft}
          @input=${(e: Event) => this._onDraftInput(s.key, (e.target as HTMLInputElement).value)}
          @keydown=${(e: KeyboardEvent) => { if (e.key === 'Enter' && dirty) this._saveSetting(s.key, draft); }}
        />`;

    return html`
      <div class="value-edit">
        ${this._savedKey === s.key ? html`<span class="setting-saved">Saved</span>` : ''}
        ${input}
        <button class="primary" ?disabled=${!dirty} @click=${() => this._saveSetting(s.key, draft)}>Save</button>
      </div>
    `;
  }

  private _renderAudit() {
    return html`
      <table class="audit-table">
        <thead>
          <tr><th>Event</th><th>Actor</th><th>Target</th><th>Time</th></tr>
        </thead>
        <tbody>
          ${this._auditEvents.map(e => html`
            <tr>
              <td><span class="badge">${e.eventType}</span></td>
              <td>${e.actorUsername ?? '-'}</td>
              <td>${e.targetType}${e.targetId ? ` (${e.targetId.slice(0, 8)})` : ''}</td>
              <td><sg-time-ago datetime=${e.createdAt}></sg-time-ago></td>
            </tr>
          `)}
        </tbody>
      </table>
    `;
  }
}
