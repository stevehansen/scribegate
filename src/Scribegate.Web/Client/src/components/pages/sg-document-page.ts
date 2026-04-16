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
    try {
      const [repo, doc] = await Promise.all([
        repoApi.get(this._slug),
        docApi.get(this._slug, this._path),
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
    if (this._error) return html`<div class="not-found"><p>${this._error}</p><p><a href="/${this._slug}">Back to repository</a></p></div>`;
    if (!this._doc || !this._repo) return html``;

    return html`
      <sg-breadcrumb
        repoSlug=${this._repo.slug}
        repoName=${this._repo.name}
        path=${this._doc.path}
      ></sg-breadcrumb>

      <div class="meta">
        ${this._doc.updatedAt ? html`Updated <sg-time-ago datetime=${this._doc.updatedAt}></sg-time-ago>` : ''}
        ${authState.isAuthenticated
          ? html`<a href="/${this._slug}/edit/${this._doc.path.replace(/\.md$/, '')}">Edit</a>`
          : ''}
        <a href="/${this._slug}/history/${this._doc.path.replace(/\.md$/, '')}">History</a>
        <a href="/${this._slug}/proposals">Proposals</a>
        ${authState.isAuthenticated
          ? html`<a href="#" @click=${(e: Event) => { e.preventDefault(); this._shareOpen = true; }}>Share</a>`
          : ''}
      </div>

      <div class="content">
        <sg-markdown-view content=${this._doc.content ?? ''}></sg-markdown-view>
      </div>

      <sg-share-dialog
        ?open=${this._shareOpen}
        repoSlug=${this._repo.slug}
        docPath=${this._doc.path}
        @close=${() => { this._shareOpen = false; }}
      ></sg-share-dialog>
    `;
  }
}
