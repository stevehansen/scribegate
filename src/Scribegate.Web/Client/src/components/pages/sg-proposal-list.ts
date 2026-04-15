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
    h1 { font-size: 1.25rem; }
    .tabs { display: flex; gap: 0; border-bottom: 1px solid #dee2e6; margin-bottom: 1rem; }
    .tab {
      padding: 0.5rem 1rem; cursor: pointer; font-size: 0.875rem;
      border-bottom: 2px solid transparent; color: #6c757d;
    }
    .tab:hover { color: #212529; }
    .tab.active { color: #2563eb; border-bottom-color: #2563eb; font-weight: 500; }
    .proposals { display: flex; flex-direction: column; gap: 0; }
    .proposal {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem; border: 1px solid #dee2e6; border-bottom: none;
    }
    .proposal:first-child { border-radius: 8px 8px 0 0; }
    .proposal:last-child { border-bottom: 1px solid #dee2e6; border-radius: 0 0 8px 8px; }
    .proposal:only-child { border-radius: 8px; border-bottom: 1px solid #dee2e6; }
    .proposal a { text-decoration: none; color: inherit; }
    .proposal-title { font-weight: 500; font-size: 0.875rem; color: #2563eb; }
    .proposal-title:hover { text-decoration: underline; }
    .proposal-meta { font-size: 0.75rem; color: #6c757d; margin-top: 0.125rem; }
    .status {
      font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px;
      font-weight: 600; text-transform: uppercase;
    }
    .status-open { background: #dbeafe; color: #2563eb; }
    .status-approved { background: #d1fae5; color: #059669; }
    .status-rejected { background: #fee2e2; color: #dc2626; }
    .status-withdrawn { background: #e5e7eb; color: #6b7280; }
    .status-draft { background: #fef3c7; color: #d97706; }
    .empty { text-align: center; padding: 2rem; color: #6c757d; }
    .btn { padding: 0.5rem 1rem; border-radius: 6px; font-size: 0.875rem; font-weight: 500; cursor: pointer; border: none; text-decoration: none; }
    .btn-primary { background: #2563eb; color: #fff; display: inline-block; }
    .btn-primary:hover { background: #1d4ed8; }
    .error { color: #dc2626; }
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
