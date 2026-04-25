import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import * as repoApi from '../../api/repositories.js';
import * as proposalApi from '../../api/proposals.js';
import { authState } from '../../state/auth-state.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-breadcrumb.js';
import '../shared/sg-time-ago.js';

@customElement('sg-proposal-list')
export class SgProposalList extends LitElement {
  static styles = [boxReset, css`
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
  `];

  @property() location: any;
  @state() private _statusFilter = 'Open';

  private get _owner(): string { return this.location?.params?.owner ?? ''; }
  private get _slug(): string { return this.location?.params?.slug ?? ''; }

  private _repoCtl = new LoadController(this, () =>
    repoApi.get(this._owner, this._slug));
  private _proposalsCtl = new LoadController(this, () =>
    proposalApi.list(this._owner, this._slug, this._statusFilter === 'All' ? undefined : this._statusFilter)
      .then(r => r.items));

  private _setFilter(status: string) {
    this._statusFilter = status;
    void this._proposalsCtl.reload();
  }

  private _statusClass(status: string): string {
    return `status status-${status.toLowerCase()}`;
  }

  render() {
    const repo = this._repoCtl.data;
    const proposals = this._proposalsCtl.data ?? [];

    if (this._repoCtl.status === 'loading' && !repo) return html`<p>Loading...</p>`;
    if (this._repoCtl.status === 'error' || this._proposalsCtl.status === 'error')
      return html`<p class="error">Failed to load proposals.</p>`;
    if (!repo) return html``;

    const tabs = ['Open', 'Approved', 'Rejected', 'Withdrawn', 'All'];
    const repoBase = `/${this._owner}/${this._slug}`;

    return html`
      <sg-breadcrumb repoOwner=${repo.owner} repoSlug=${repo.slug} repoName=${repo.name}></sg-breadcrumb>

      <div class="header">
        <h1>Proposals</h1>
        ${authState.isAuthenticated
          ? html`<a class="btn btn-primary" href="${repoBase}/proposals/new">New proposal</a>`
          : ''}
      </div>

      <div class="tabs">
        ${tabs.map(t => html`
          <div class="tab ${this._statusFilter === t ? 'active' : ''}" @click=${() => this._setFilter(t)}>${t}</div>
        `)}
      </div>

      ${proposals.length === 0
        ? html`<div class="empty">No ${this._statusFilter.toLowerCase()} proposals.</div>`
        : html`
          <div class="proposals">
            ${proposals.map(p => html`
              <div class="proposal">
                <div>
                  <a class="proposal-title" href="${repoBase}/proposals/${p.id}">${p.title}</a>
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
