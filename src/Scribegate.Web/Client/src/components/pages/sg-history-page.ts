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
    h1 { font-size: 1.25rem; margin-bottom: 1rem; }
    .revisions { display: flex; flex-direction: column; gap: 0; }
    .revision {
      display: flex; justify-content: space-between; align-items: center;
      padding: 0.75rem 1rem;
      border: 1px solid #dee2e6; border-bottom: none;
    }
    .revision:first-child { border-radius: 8px 8px 0 0; }
    .revision:last-child { border-bottom: 1px solid #dee2e6; border-radius: 0 0 8px 8px; }
    .revision:only-child { border-radius: 8px; border-bottom: 1px solid #dee2e6; }
    .revision-msg { font-weight: 500; font-size: 0.875rem; }
    .revision-meta { font-size: 0.75rem; color: #6c757d; margin-top: 0.125rem; }
    .current-badge {
      font-size: 0.625rem; background: #dbeafe; color: #2563eb;
      padding: 0.125rem 0.375rem; border-radius: 999px; font-weight: 600;
      margin-left: 0.5rem;
    }
    .empty { text-align: center; padding: 2rem; color: #6c757d; }
    .error { color: #dc2626; }
    a.back { font-size: 0.875rem; color: #2563eb; text-decoration: none; display: inline-block; margin-bottom: 1rem; }
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
