import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, MemberResponse } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as memberApi from '../../api/members.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-breadcrumb.js';

@customElement('sg-members-page')
export class SgMembersPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-xl); margin-bottom: 1rem; color: var(--sg-text); }
    .members { display: flex; flex-direction: column; gap: 0; }
    .member {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; border: 1px solid var(--sg-border); border-bottom: none;
      background: var(--sg-bg-elevated);
      transition: background var(--sg-transition-fast);
    }
    .member:hover { background: var(--sg-bg-secondary); }
    .member:first-child { border-radius: var(--sg-radius-lg) var(--sg-radius-lg) 0 0; }
    .member:last-child { border-bottom: 1px solid var(--sg-border); border-radius: 0 0 var(--sg-radius-lg) var(--sg-radius-lg); }
    .member:only-child { border-radius: var(--sg-radius-lg); border-bottom: 1px solid var(--sg-border); }
    .member-name { font-weight: 500; font-size: var(--sg-font-size-sm); color: var(--sg-text); }
    .member-email { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); }
    .role-badge {
      font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px;
      background: var(--sg-bg-tertiary); color: var(--sg-text-secondary); font-weight: 600;
    }
    .add-form { display: flex; gap: 0.5rem; margin-bottom: 1rem; flex-wrap: wrap; }
    .add-form input, .add-form select {
      padding: 0.5rem 0.75rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm); background: var(--sg-bg-elevated); color: var(--sg-text);
    }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; transition: background var(--sg-transition-fast);
    }
    .btn-primary { background: var(--sg-primary); color: var(--sg-primary-text); }
    .btn-sm { padding: 0.25rem 0.5rem; font-size: var(--sg-font-size-xs); }
    .btn-danger { background: var(--sg-danger); color: #fff; }
    .error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border); color: var(--sg-danger);
      padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm); margin-bottom: 1rem;
    }
    .empty { text-align: center; padding: 2rem; color: var(--sg-text-secondary); }
    .right { display: flex; gap: 0.5rem; align-items: center; }
  `];

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _members: MemberResponse[] = [];
  @state() private _loading = true;
  @state() private _error = '';
  @state() private _newUsername = '';
  @state() private _newRole = 'Reader';

  private get _owner(): string { return this.location?.params?.owner ?? ''; }
  private get _slug(): string { return this.location?.params?.slug ?? ''; }

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    if (!this._owner || !this._slug) {
      this._error = 'Missing repository owner or slug.';
      this._loading = false;
      return;
    }
    try {
      const [repo, members] = await Promise.all([
        repoApi.get(this._owner, this._slug),
        memberApi.list(this._owner, this._slug),
      ]);
      this._repo = repo;
      this._members = members.items;
    } catch { this._error = 'Failed to load members.'; }
    finally { this._loading = false; }
  }

  private async _addMember() {
    this._error = '';
    try {
      await memberApi.add(this._owner, this._slug, { username: this._newUsername, role: this._newRole });
      this._newUsername = '';
      const members = await memberApi.list(this._owner, this._slug);
      this._members = members.items;
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed to add member.'; }
  }

  private async _removeMember(userId: string) {
    try {
      await memberApi.remove(this._owner, this._slug, userId);
      const members = await memberApi.list(this._owner, this._slug);
      this._members = members.items;
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (!this._repo) return html``;

    return html`
      <sg-breadcrumb repoOwner=${this._repo.owner} repoSlug=${this._repo.slug} repoName=${this._repo.name}></sg-breadcrumb>
      <h1>Members</h1>

      ${this._error ? html`<div class="error">${this._error}</div>` : ''}

      ${authState.isAuthenticated ? html`
        <div class="add-form">
          <input type="text" .value=${this._newUsername} @input=${(e: Event) => this._newUsername = (e.target as HTMLInputElement).value} placeholder="Username" />
          <select .value=${this._newRole} @change=${(e: Event) => this._newRole = (e.target as HTMLSelectElement).value}>
            <option value="Reader">Reader</option>
            <option value="Contributor">Contributor</option>
            <option value="Reviewer">Reviewer</option>
            <option value="Admin">Admin</option>
          </select>
          <button class="btn btn-primary" @click=${this._addMember}>Add Member</button>
        </div>
      ` : ''}

      ${this._members.length === 0
        ? html`<div class="empty">No members yet.</div>`
        : html`
          <div class="members">
            ${this._members.map(m => html`
                <div class="member">
                  <div>
                    <div class="member-name">${m.username}</div>
                    ${m.email ? html`<div class="member-email">${m.email}</div>` : ''}
                  </div>
                  <div class="right">
                  <span class="role-badge">${m.role}</span>
                  ${authState.isAuthenticated ? html`
                    <button class="btn btn-sm btn-danger" @click=${() => this._removeMember(m.userId)}>Remove</button>
                  ` : ''}
                </div>
              </div>
            `)}
          </div>
        `}
    `;
  }
}
