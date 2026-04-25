import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import * as repoApi from '../../api/repositories.js';
import * as revisionApi from '../../api/revisions.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-breadcrumb.js';
import '../shared/sg-time-ago.js';

@customElement('sg-history-page')
export class SgHistoryPage extends LitElement {
  static styles = [boxReset, css`
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
  `];

  @property() location: any;

  private get _owner(): string {
    return this.location?.params?.owner ?? '';
  }

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
  }

  private get _path(): string {
    const raw = this.location?.params?.[0] ?? '';
    return raw.endsWith('.md') ? raw : raw + '.md';
  }

  private _repoCtl = new LoadController(this, () =>
    repoApi.get(this._owner, this._slug));
  private _revisionsCtl = new LoadController(this, () =>
    revisionApi.list(this._owner, this._slug, this._path).then(r => r.items));

  render() {
    const repo = this._repoCtl.data;
    const revisions = this._revisionsCtl.data ?? [];

    if (this._repoCtl.status === 'loading' && !repo) return html`<p>Loading...</p>`;
    if (this._repoCtl.status === 'error' || this._revisionsCtl.status === 'error')
      return html`<p class="error">Failed to load history.</p>`;
    if (!repo) return html``;

    const repoBase = `/${this._owner}/${this._slug}`;

    return html`
      <sg-breadcrumb
        repoOwner=${repo.owner}
        repoSlug=${repo.slug}
        repoName=${repo.name}
        path=${this._path}
      ></sg-breadcrumb>

      <a class="back" href="${repoBase}/${this._path.replace(/\.md$/, '')}">Back to document</a>
      <h1>History: ${this._path}</h1>

      ${revisions.length === 0
        ? html`<div class="empty">No revisions yet.</div>`
        : html`
          <div class="revisions">
            ${revisions.map((rev, i) => html`
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
