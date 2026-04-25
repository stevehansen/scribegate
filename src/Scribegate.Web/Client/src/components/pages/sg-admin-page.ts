import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { SettingResponse, AuditEventResponse } from '../../api/types.js';
import * as adminApi from '../../api/admin.js';
import { ApiException } from '../../api/client.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-time-ago.js';

@customElement('sg-admin-page')
export class SgAdminPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-2xl); margin-bottom: 1rem; color: var(--sg-text); }
    h2 {
      font-size: var(--sg-font-size-lg);
      margin-top: 2rem; margin-bottom: 0.75rem;
      color: var(--sg-text);
      padding-bottom: 0.375rem;
      border-bottom: 1px solid var(--sg-border);
    }
    h2:first-of-type { margin-top: 1rem; }
    .settings { display: flex; flex-direction: column; gap: 0.5rem; }
    .setting {
      display: flex; justify-content: space-between; align-items: center;
      gap: 1rem;
      padding: 0.75rem 1rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius-lg);
      background: var(--sg-bg-elevated);
    }
    .setting-left { min-width: 0; flex: 1 1 20rem; }
    .setting-label { font-weight: 500; font-size: var(--sg-font-size-sm); color: var(--sg-text); }
    .setting-key {
      font-family: var(--sg-font-mono, ui-monospace, SFMono-Regular, monospace);
      font-size: 0.75rem; color: var(--sg-text-secondary); margin-left: 0.375rem;
    }
    .setting-desc { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); margin-top: 0.125rem; }
    .setting-status { font-size: 0.6875rem; color: var(--sg-text-secondary); margin-top: 0.25rem; }
    .setting-status.default { color: var(--sg-text-secondary); opacity: 0.75; }
    .toggle {
      cursor: pointer; padding: 0.25rem 0.75rem; border-radius: var(--sg-radius);
      font-size: 0.8125rem; border: 1px solid var(--sg-border); background: var(--sg-bg-elevated);
      transition: background var(--sg-transition-fast), color var(--sg-transition-fast);
      white-space: nowrap;
    }
    .toggle.on { background: var(--sg-success-light); color: var(--sg-success); border-color: var(--sg-success-border); }
    .toggle.off { background: var(--sg-danger-light); color: var(--sg-danger); border-color: var(--sg-danger-border); }
    .value-edit { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; justify-content: flex-end; }
    .value-edit input, .value-edit select {
      background: var(--sg-bg); color: var(--sg-text);
      border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      padding: 0.25rem 0.5rem; font-size: 0.8125rem; min-width: 12rem;
    }
    .value-edit input:focus, .value-edit select:focus { outline: none; border-color: var(--sg-primary); }
    .value-edit button {
      cursor: pointer; padding: 0.25rem 0.75rem; border-radius: var(--sg-radius);
      font-size: 0.8125rem; border: 1px solid var(--sg-border); background: var(--sg-bg-elevated);
      color: var(--sg-text-secondary); white-space: nowrap;
    }
    .value-edit button.primary { background: var(--sg-primary); color: var(--sg-on-primary, #fff); border-color: var(--sg-primary); }
    .value-edit button:hover { background: var(--sg-bg-secondary); }
    .value-edit button.primary:hover { opacity: 0.9; }
    .value-edit button[disabled] { opacity: 0.4; cursor: not-allowed; }
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

  @state() private _error = '';
  @state() private _tab = 'settings';
  @state() private _drafts: Record<string, string> = {};
  @state() private _savedKey: string | null = null;
  @state() private _smtpTestState: 'idle' | 'sending' | 'sent' | 'error' = 'idle';
  @state() private _smtpTestMessage = '';
  @state() private _smtpTestTo = '';

  private static readonly GROUP_ORDER = [
    'Instance',
    'Registration',
    'Accounts',
    'SSO / OIDC',
    'Email (SMTP)',
    'Free tier limits',
    'Paid tier limits',
    'Other',
  ];

  private _settingsCtl = new LoadController<SettingResponse[]>(this, () =>
    adminApi.listSettings());
  private _auditCtl = new LoadController<AuditEventResponse[]>(this, () =>
    adminApi.listAuditEvents({ take: 50 }).then(r => r.items));

  private async _toggleSetting(key: string, current: string) {
    const newValue = current === 'true' ? 'false' : 'true';
    await this._saveSetting(key, newValue);
  }

  private async _saveSetting(key: string, newValue: string) {
    try {
      this._error = '';
      await adminApi.updateSetting(key, newValue);
      await this._settingsCtl.reload();
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

  private async _sendSmtpTest() {
    this._smtpTestState = 'sending';
    this._smtpTestMessage = '';
    try {
      const res = await adminApi.sendSmtpTest(this._smtpTestTo || undefined);
      this._smtpTestState = 'sent';
      this._smtpTestMessage = `Sent to ${res.toEmail}.`;
    } catch (e) {
      this._smtpTestState = 'error';
      this._smtpTestMessage = e instanceof ApiException ? e.error.message : 'Test failed.';
    }
  }

  render() {
    const settings = this._settingsCtl.data;
    const stillLoading = this._settingsCtl.status === 'loading' && !settings;
    if (stillLoading) return html`<p>Loading...</p>`;
    if (this._settingsCtl.status === 'error' && !settings)
      return html`<p class="error">${this._settingsCtl.error}</p>`;
    if (this._error && !settings?.length) return html`<p class="error">${this._error}</p>`;

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

  private _renderSmtpTest() {
    const enabled = (this._settingsCtl.data ?? []).find(s => s.key === 'smtp.enabled')?.value === 'true';
    if (!enabled) return '';
    return html`
      <div class="setting">
        <div class="setting-left">
          <div class="setting-label">Send test email</div>
          <div class="setting-desc">Dispatches a one-off message using the current SMTP settings. Leave blank to send to the admin account.</div>
          ${this._smtpTestMessage ? html`
            <div class="setting-status ${this._smtpTestState === 'error' ? 'error' : 'setting-saved'}">
              ${this._smtpTestMessage}
            </div>` : ''}
        </div>
        <div class="value-edit">
          <input type="email" placeholder="recipient@example.com"
            .value=${this._smtpTestTo}
            @input=${(e: Event) => this._smtpTestTo = (e.target as HTMLInputElement).value} />
          <button class="primary" ?disabled=${this._smtpTestState === 'sending'} @click=${() => this._sendSmtpTest()}>
            ${this._smtpTestState === 'sending' ? 'Sending…' : 'Send test email'}
          </button>
        </div>
      </div>
    `;
  }

  private _renderSettings() {
    const groups = new Map<string, SettingResponse[]>();
    for (const s of this._settingsCtl.data ?? []) {
      const g = s.group ?? 'Other';
      if (!groups.has(g)) groups.set(g, []);
      groups.get(g)!.push(s);
    }
    const ordered = [
      ...SgAdminPage.GROUP_ORDER.filter(g => groups.has(g)),
      ...Array.from(groups.keys()).filter(g => !SgAdminPage.GROUP_ORDER.includes(g)),
    ];

    return html`
      ${this._error ? html`<p class="error">${this._error}</p>` : ''}
      ${ordered.map(g => html`
        <h2>${g}</h2>
        ${g === 'Email (SMTP)' ? this._renderSmtpTest() : ''}
        <div class="settings">
          ${(groups.get(g) ?? []).map(s => this._renderRow(s))}
        </div>
      `)}
    `;
  }

  private _renderRow(s: SettingResponse) {
    const type = s.type ?? (this._isBooleanLike(s.value) ? 'bool' : 'string');
    return html`
      <div class="setting">
        <div class="setting-left">
          <div class="setting-label">
            ${s.label ?? s.key}
            <span class="setting-key">${s.key}</span>
          </div>
          ${s.description ? html`<div class="setting-desc">${s.description}</div>` : ''}
          ${!s.defined ? html`<div class="setting-status default">Using default value</div>` : ''}
        </div>
        ${type === 'bool' ? this._renderToggle(s) : this._renderEditor(s, type)}
      </div>
    `;
  }

  private _isBooleanLike(v: string) { return v === 'true' || v === 'false'; }

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

  private _renderEditor(s: SettingResponse, type: string) {
    const draft = this._draftValue(s);
    const dirty = draft !== s.value;
    const choices = s.choices;

    const input = choices && choices.length
      ? html`<select @change=${(e: Event) => this._onDraftInput(s.key, (e.target as HTMLSelectElement).value)}>
          ${choices.map(c => html`<option value=${c} ?selected=${draft === c}>${c}</option>`)}
        </select>`
      : html`<input
          type=${type === 'secret' ? 'password' : type === 'number' ? 'number' : 'text'}
          placeholder=${s.defined ? '' : (s.value || '')}
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
          ${(this._auditCtl.data ?? []).map(e => html`
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
