import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, DocumentSummary } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import { authState } from '../../state/auth-state.js';
import '../shared/sg-file-tree.js';
import '../shared/sg-breadcrumb.js';

@customElement('sg-repository-page')
export class SgRepositoryPage extends LitElement {
  static styles = css`
    :host { display: block; }
    .header { display: flex; justify-content: space-between; align-items: flex-start; margin-bottom: 1.5rem; }
    .info h1 { font-size: var(--sg-font-size-2xl); margin-bottom: 0.25rem; color: var(--sg-text); }
    .info p { font-size: var(--sg-font-size-sm); color: var(--sg-text-secondary); }
    .badge {
      font-size: var(--sg-font-size-xs); padding: 0.125rem 0.5rem; border-radius: 999px;
      background: var(--sg-bg-tertiary); color: var(--sg-text-secondary);
    }
    .actions { display: flex; gap: 0.5rem; align-items: center; }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; text-decoration: none;
      transition: background var(--sg-transition-fast);
    }
    .btn-primary { background: var(--sg-primary); color: var(--sg-primary-text); display: inline-block; }
    .btn-primary:hover { background: var(--sg-primary-hover); }
    .btn-secondary { background: var(--sg-bg-tertiary); color: var(--sg-text-secondary); display: inline-block; }
    .btn-secondary:hover { background: var(--sg-border); }
    section {
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      padding: 1.25rem;
      background: var(--sg-bg-elevated);
    }
    h2 { font-size: var(--sg-font-size-lg); margin-bottom: 1rem; color: var(--sg-text); }
    .error { color: var(--sg-danger); }
  `;

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _docs: DocumentSummary[] = [];
  @state() private _loading = true;
  @state() private _error = '';

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
  }

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    try {
      const [repo, docs] = await Promise.all([
        repoApi.get(this._slug),
        docApi.list(this._slug),
      ]);
      this._repo = repo;
      this._docs = docs.items;
    } catch {
      this._error = 'Repository not found.';
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
      ></sg-breadcrumb>

      <div class="header">
        <div class="info">
          <h1>${this._repo.name} <span class="badge">${this._repo.visibility}</span></h1>
          ${this._repo.description ? html`<p>${this._repo.description}</p>` : ''}
        </div>
        <div class="actions">
          <a class="btn btn-secondary" href="/${this._slug}/proposals">Proposals</a>
          <a class="btn btn-secondary" href="/${this._slug}/members">Members</a>
          ${authState.isAuthenticated
            ? html`<a class="btn btn-primary" href="/${this._slug}/edit/new">New document</a>`
            : ''}
        </div>
      </div>

      <section>
        <h2>Documents</h2>
        <sg-file-tree
          .documents=${this._docs}
          repoSlug=${this._slug}
        ></sg-file-tree>
      </section>
    `;
  }
}
