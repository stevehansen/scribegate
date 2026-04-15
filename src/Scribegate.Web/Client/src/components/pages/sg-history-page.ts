import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, RevisionSummary } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as revisionApi from '../../api/revisions.js';
import '../shared/sg-breadcrumb.js';
import '../shared/sg-time-ago.js';

@customElement('sg-history-page')
export class SgHistoryPage extends LitElement {
  static styles = css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-xl); margin-bottom: 1rem; color: var(--sg-text); }
    .revisions { display: flex; flex-direction: column; gap: 0; }
    .revision {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem;
      border: 1px solid var(--sg-border); border-bottom: none;
      background: var(--sg-bg-elevated);
      transition: background var(--sg-transition-fast);
    }
    .revision:hover { background: var(--sg-bg-secondary); }
    .revision:first-child { border-radius: var(--sg-radius-lg) var(--sg-radius-lg) 0 0; }
    .revision:last-child { border-bottom: 1px solid var(--sg-border); border-radius: 0 0 var(--sg-radius-lg) var(--sg-radius-lg); }
    .revision:only-child { border-radius: var(--sg-radius-lg); border-bottom: 1px solid var(--sg-border); }
    .revision-msg { font-weight: 500; font-size: var(--sg-font-size-sm); color: var(--sg-text); }
    .revision-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); margin-top: 0.125rem; }
    .current-badge {
      font-size: 0.625rem; background: var(--sg-primary-light); color: var(--sg-primary);
      padding: 0.125rem 0.375rem; border-radius: 999px; font-weight: 600;
      margin-left: 0.5rem;
    }
    .empty { text-align: center; padding: 2rem; color: var(--sg-text-secondary); }
    .error { color: var(--sg-danger); }
    a.back { font-size: var(--sg-font-size-sm); color: var(--sg-primary); text-decoration: none; display: inline-block; margin-bottom: 1rem; }
    a.back:hover { text-decoration: underline; }
  `;

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _revisions: RevisionSummary[] = [];
  @state() private _loading = true;
  @state() private _error = '';

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
  }

  private get _path(): string {
    const raw = this.location?.params?.[0] ?? '';
    return raw.endsWith('.md') ? raw : raw + '.md';
  }

  async connectedCallback() {
    super.connectedCallback();
    try {
      const [repo, revs] = await Promise.all([
        repoApi.get(this._slug),
        revisionApi.list(this._slug, this._path),
      ]);
      this._repo = repo;
      this._revisions = revs.items;
    } catch {
      this._error = 'Failed to load history.';
    } finally {
      this._loading = false;
    }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (this._error) return html`<p class="error">${this._error}</p>`;
    if (!this._repo) return html``;

    return html`
      <sg-breadcrumb
        repoSlug=${this._repo.slug}
        repoName=${this._repo.name}
        path=${this._path}
      ></sg-breadcrumb>

      <a class="back" href="/${this._slug}/${this._path.replace(/\.md$/, '')}">Back to document</a>
      <h1>History: ${this._path}</h1>

      ${this._revisions.length === 0
        ? html`<div class="empty">No revisions yet.</div>`
        : html`
          <div class="revisions">
            ${this._revisions.map((rev, i) => html`
              <div class="revision">
                <div>
                  <div class="revision-msg">
                    ${rev.message}
                    ${i === 0 ? html`<span class="current-badge">current</span>` : ''}
                  </div>
                  <div class="revision-meta">
                    by ${rev.createdBy}
                  </div>
                </div>
                <sg-time-ago datetime=${rev.createdAt}></sg-time-ago>
              </div>
            `)}
          </div>
        `}
    `;
  }
}
