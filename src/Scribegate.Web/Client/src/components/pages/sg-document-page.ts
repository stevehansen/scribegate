import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { DocumentResponse, RepositoryResponse } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import { authState } from '../../state/auth-state.js';
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
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _doc: DocumentResponse | null = null;
  @state() private _loading = true;
  @state() private _error = '';
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
      const [repo, doc] = await Promise.all([
        repoApi.get(this._owner, this._slug),
        docApi.get(this._owner, this._slug, this._path),
      ]);
      this._repo = repo;
      this._doc = doc;
    } catch {
      this._error = 'Document not found.';
    } finally {
      this._loading = false;
    }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    const repoBase = `/${this._owner}/${this._slug}`;
    if (this._error) return html`<div class="not-found"><p>${this._error}</p><p><a href="${repoBase}">Back to repository</a></p></div>`;
    if (!this._doc || !this._repo) return html``;

    return html`
      <sg-breadcrumb
        repoOwner=${this._repo.owner}
        repoSlug=${this._repo.slug}
        repoName=${this._repo.name}
        path=${this._doc.path}
      ></sg-breadcrumb>

      <div class="meta">
        ${this._doc.updatedAt ? html`Updated <sg-time-ago datetime=${this._doc.updatedAt}></sg-time-ago>` : ''}
        ${authState.isAuthenticated
          ? html`<a href="${repoBase}/edit/${this._doc.path.replace(/\.md$/, '')}">Edit</a>`
          : ''}
        <a href="${repoBase}/history/${this._doc.path.replace(/\.md$/, '')}">History</a>
        <a href="${repoBase}/proposals">Proposals</a>
        ${authState.isAuthenticated
          ? html`<a href="#" @click=${(e: Event) => { e.preventDefault(); this._shareOpen = true; }}>Share</a>`
          : ''}
      </div>

      <div class="content">
        <sg-markdown-view
          content=${this._doc.content ?? ''}
          owner=${this._owner}
          slug=${this._slug}
        ></sg-markdown-view>
      </div>

      <sg-share-dialog
        ?open=${this._shareOpen}
        repoOwner=${this._repo.owner}
        repoSlug=${this._repo.slug}
        docPath=${this._doc.path}
        @close=${() => { this._shareOpen = false; }}
      ></sg-share-dialog>
    `;
  }
}
