import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import { authState } from '../../state/auth-state.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-markdown-view.js';
import '../shared/sg-breadcrumb.js';
import '../shared/sg-time-ago.js';
import '../shared/sg-share-dialog.js';

@customElement('sg-document-page')
export class SgDocumentPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    .meta {
      display: flex; gap: 1rem; align-items: center; flex-wrap: wrap;
      font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary);
      padding: 0.75rem 0; border-bottom: 1px solid var(--sg-border); margin-bottom: 1.5rem;
    }
    .meta a {
      color: var(--sg-primary); text-decoration: none; font-weight: 500;
      transition: color var(--sg-transition-fast);
    }
    .meta a:hover { text-decoration: underline; }
    .content { max-width: var(--sg-content-width); }
    .error { color: var(--sg-danger); }
    .not-found { text-align: center; padding: 3rem; color: var(--sg-text-secondary); }
  `];

  @property() location: any;
  @state() private _shareOpen = false;

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
  private _docCtl = new LoadController(this, () =>
    docApi.get(this._owner, this._slug, this._path));

  render() {
    const repoBase = `/${this._owner}/${this._slug}`;
    const repo = this._repoCtl.data;
    const doc = this._docCtl.data;

    const isLoading = this._repoCtl.status === 'loading' || this._docCtl.status === 'loading';
    if (isLoading && (!repo || !doc)) return html`<p>Loading...</p>`;

    if (this._docCtl.status === 'error' || this._repoCtl.status === 'error') {
      return html`<div class="not-found"><p>Document not found.</p><p><a href="${repoBase}">Back to repository</a></p></div>`;
    }
    if (!doc || !repo) return html``;

    return html`
      <sg-breadcrumb
        repoOwner=${repo.owner}
        repoSlug=${repo.slug}
        repoName=${repo.name}
        path=${doc.path}
      ></sg-breadcrumb>

      <div class="meta">
        ${doc.updatedAt ? html`Updated <sg-time-ago datetime=${doc.updatedAt}></sg-time-ago>` : ''}
        ${authState.isAuthenticated
          ? html`<a href="${repoBase}/edit/${doc.path.replace(/\.md$/, '')}">Edit</a>`
          : ''}
        <a href="${repoBase}/history/${doc.path.replace(/\.md$/, '')}">History</a>
        <a href="${repoBase}/proposals">Proposals</a>
        ${authState.isAuthenticated
          ? html`<a href="#" @click=${(e: Event) => { e.preventDefault(); this._shareOpen = true; }}>Share</a>`
          : ''}
      </div>

      <div class="content">
        <sg-markdown-view
          content=${doc.content ?? ''}
          owner=${this._owner}
          slug=${this._slug}
          documentPath=${doc.path}
        ></sg-markdown-view>
      </div>

      <sg-share-dialog
        ?open=${this._shareOpen}
        repoOwner=${repo.owner}
        repoSlug=${repo.slug}
        docPath=${doc.path}
        @close=${() => { this._shareOpen = false; }}
      ></sg-share-dialog>
    `;
  }
}
