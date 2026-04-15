import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, ProposalSummary } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as proposalApi from '../../api/proposals.js';
import { authState } from '../../state/auth-state.js';
import '../shared/sg-breadcrumb.js';
import '../shared/sg-time-ago.js';

@customElement('sg-proposal-list')
export class SgProposalList extends LitElement {
  static styles = css`
    :host { display: block; }
    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
    h1 { font-size: var(--sg-font-size-xl); color: var(--sg-text); }
    .tabs { display: flex; gap: 0; border-bottom: 1px solid var(--sg-border); margin-bottom: 1rem; }
    .tab {
      padding: 0.5rem 1rem; cursor: pointer; font-size: var(--sg-font-size-sm);
      border-bottom: 2px solid transparent; color: var(--sg-text-secondary);
      transition: color var(--sg-transition-fast);
    }
    .tab:hover { color: var(--sg-text); }
    .tab.active { color: var(--sg-primary); border-bottom-color: var(--sg-primary); font-weight: 500; }
    .proposals { display: flex; flex-direction: column; gap: 0; }
    .proposal {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; border: 1px solid var(--sg-border); border-bottom: none;
      background: var(--sg-bg-elevated);
      transition: background var(--sg-transition-fast);
    }
    .proposal:hover { background: var(--sg-bg-secondary); }
    .proposal:first-child { border-radius: var(--sg-radius-lg) var(--sg-radius-lg) 0 0; }
    .proposal:last-child { border-bottom: 1px solid var(--sg-border); border-radius: 0 0 var(--sg-radius-lg) var(--sg-radius-lg); }
    .proposal:only-child { border-radius: var(--sg-radius-lg); border-bottom: 1px solid var(--sg-border); }
    .proposal a { text-decoration: none; color: inherit; }
    .proposal-title { font-weight: 500; font-size: var(--sg-font-size-sm); color: var(--sg-primary); }
    .proposal-title:hover { text-decoration: underline; }
    .proposal-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); margin-top: 0.125rem; }
    .status {
      font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px;
      font-weight: 600; text-transform: uppercase;
    }
    .status-open { background: var(--sg-status-open-bg); color: var(--sg-status-open-text); }
    .status-approved { background: var(--sg-status-approved-bg); color: var(--sg-status-approved-text); }
    .status-rejected { background: var(--sg-status-rejected-bg); color: var(--sg-status-rejected-text); }
    .status-withdrawn { background: var(--sg-status-withdrawn-bg); color: var(--sg-status-withdrawn-text); }
    .status-draft { background: var(--sg-status-draft-bg); color: var(--sg-status-draft-text); }
    .empty { text-align: center; padding: 2rem; color: var(--sg-text-secondary); }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; text-decoration: none;
      transition: background var(--sg-transition-fast);
    }
    .btn-primary { background: var(--sg-primary); color: var(--sg-primary-text); display: inline-block; }
    .btn-primary:hover { background: var(--sg-primary-hover); }
    .error { color: var(--sg-danger); }
    .right { display: flex; align-items: center; gap: 0.75rem; }
  `;

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _proposals: ProposalSummary[] = [];
  @state() private _loading = true;
  @state() private _error = '';
  @state() private _statusFilter = 'Open';

  private get _slug(): string { return this.location?.params?.slug ?? ''; }

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    this._loading = true;
    try {
      const [repo, proposals] = await Promise.all([
        repoApi.get(this._slug),
        proposalApi.list(this._slug, this._statusFilter === 'All' ? undefined : this._statusFilter),
      ]);
      this._repo = repo;
      this._proposals = proposals.items;
    } catch { this._error = 'Failed to load proposals.'; }
    finally { this._loading = false; }
  }

  private _setFilter(status: string) {
    this._statusFilter = status;
    this._load();
  }

  private _statusClass(status: string): string {
    return `status status-${status.toLowerCase()}`;
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (this._error) return html`<p class="error">${this._error}</p>`;
    if (!this._repo) return html``;

    const tabs = ['Open', 'Approved', 'Rejected', 'Withdrawn', 'All'];

    return html`
      <sg-breadcrumb repoSlug=${this._repo.slug} repoName=${this._repo.name}></sg-breadcrumb>

      <div class="header">
        <h1>Proposals</h1>
        ${authState.isAuthenticated
          ? html`<a class="btn btn-primary" href="/${this._slug}/proposals/new">New proposal</a>`
          : ''}
      </div>

      <div class="tabs">
        ${tabs.map(t => html`
          <div class="tab ${this._statusFilter === t ? 'active' : ''}" @click=${() => this._setFilter(t)}>${t}</div>
        `)}
      </div>

      ${this._proposals.length === 0
        ? html`<div class="empty">No ${this._statusFilter.toLowerCase()} proposals.</div>`
        : html`
          <div class="proposals">
            ${this._proposals.map(p => html`
              <div class="proposal">
                <div>
                  <a class="proposal-title" href="/${this._slug}/proposals/${p.id}">${p.title}</a>
                  <div class="proposal-meta">
                    ${p.documentPath ?? 'new document'} &middot; by ${p.createdBy} &middot;
                    ${p.reviewCount} review${p.reviewCount !== 1 ? 's' : ''}, ${p.commentCount} comment${p.commentCount !== 1 ? 's' : ''}
                  </div>
                </div>
                <div class="right">
                  <sg-time-ago datetime=${p.createdAt}></sg-time-ago>
                  <span class=${this._statusClass(p.status)}>${p.status}</span>
                </div>
              </div>
            `)}
          </div>
        `}
    `;
  }
}
