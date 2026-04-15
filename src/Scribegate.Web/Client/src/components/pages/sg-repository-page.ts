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
    .info h1 { font-size: 1.5rem; margin-bottom: 0.25rem; }
    .info p { font-size: 0.875rem; color: #6c757d; }
    .badge {
      font-size: 0.75rem; padding: 0.125rem 0.5rem; border-radius: 999px;
      background: #e9ecef; color: #6c757d;
    }
    .actions { display: flex; gap: 0.5rem; align-items: center; }
    .btn {
      padding: 0.5rem 1rem; border-radius: 6px; font-size: 0.875rem;
      font-weight: 500; cursor: pointer; border: none; text-decoration: none;
    }
    .btn-primary { background: #2563eb; color: #fff; display: inline-block; }
    .btn-primary:hover { background: #1d4ed8; }
    section { border: 1px solid #dee2e6; border-radius: 8px; padding: 1.25rem; }
    h2 { font-size: 1.125rem; margin-bottom: 1rem; }
    .error { color: #dc2626; }
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
          <a class="btn btn-primary" style="background:#6c757d" href="/${this._slug}/proposals">Proposals</a>
          <a class="btn btn-primary" style="background:#6c757d" href="/${this._slug}/members">Members</a>
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
