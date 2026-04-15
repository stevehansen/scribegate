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
    try {
      await adminApi.updateSetting(key, newValue);
      const settings = await adminApi.listSettings();
      this._settings = settings;
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

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
      <div class="settings">
        ${this._settings.map(s => html`
          <div class="setting">
            <div>
              <div class="setting-key">${s.key}</div>
              <div class="setting-meta">Value: ${s.value}</div>
            </div>
            ${s.value === 'true' || s.value === 'false' ? html`
              <button class="toggle ${s.value === 'true' ? 'on' : 'off'}" @click=${() => this._toggleSetting(s.key, s.value)}>
                ${s.value === 'true' ? 'Enabled' : 'Disabled'}
              </button>
            ` : html`<span class="badge">${s.value}</span>`}
          </div>
        `)}
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
