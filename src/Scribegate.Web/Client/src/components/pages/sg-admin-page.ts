import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { SettingResponse, AuditEventResponse } from '../../api/types.js';
import * as adminApi from '../../api/admin.js';
import { ApiException } from '../../api/client.js';
import '../shared/sg-time-ago.js';

@customElement('sg-admin-page')
export class SgAdminPage extends LitElement {
  static styles = css`
    :host { display: block; }
    h1 { font-size: 1.5rem; margin-bottom: 1rem; }
    h2 { font-size: 1.125rem; margin-top: 1.5rem; margin-bottom: 0.75rem; }
    .settings { display: flex; flex-direction: column; gap: 0.5rem; }
    .setting {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; border: 1px solid #dee2e6; border-radius: 8px;
    }
    .setting-key { font-weight: 500; font-size: 0.875rem; }
    .setting-meta { font-size: 0.75rem; color: #6c757d; }
    .toggle { cursor: pointer; padding: 0.25rem 0.75rem; border-radius: 6px; font-size: 0.8125rem; border: 1px solid #dee2e6; background: #fff; }
    .toggle.on { background: #d1fae5; color: #059669; border-color: #a7f3d0; }
    .toggle.off { background: #fee2e2; color: #dc2626; border-color: #fecaca; }
    .audit-table { width: 100%; border-collapse: collapse; font-size: 0.8125rem; }
    .audit-table th { text-align: left; padding: 0.5rem; border-bottom: 2px solid #dee2e6; font-size: 0.75rem; color: #6c757d; }
    .audit-table td { padding: 0.5rem; border-bottom: 1px solid #f1f3f5; }
    .badge { font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px; background: #e9ecef; color: #6c757d; }
    .error { color: #dc2626; }
    .tabs { display: flex; gap: 0; border-bottom: 1px solid #dee2e6; margin-bottom: 1rem; }
    .tab { padding: 0.5rem 1rem; cursor: pointer; font-size: 0.875rem; border-bottom: 2px solid transparent; color: #6c757d; }
    .tab.active { color: #2563eb; border-bottom-color: #2563eb; font-weight: 500; }
  `;

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
