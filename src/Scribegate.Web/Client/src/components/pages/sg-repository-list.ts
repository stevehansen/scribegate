import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { RepositoryResponse } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';

@customElement('sg-repository-list')
export class SgRepositoryList extends LitElement {
  static styles = css`
    :host { display: block; }
    .header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
    h1 { font-size: 1.5rem; }
    .repos { display: flex; flex-direction: column; gap: 0.75rem; }
    .repo {
      border: 1px solid #dee2e6;
      border-radius: 8px;
      padding: 1rem 1.25rem;
      display: flex;
      justify-content: space-between;
      align-items: center;
      transition: border-color 0.15s;
    }
    .repo:hover { border-color: #adb5bd; }
    .repo a { text-decoration: none; color: inherit; display: block; flex: 1; }
    .repo-name { font-weight: 600; color: #2563eb; }
    .repo-desc { font-size: 0.875rem; color: #6c757d; margin-top: 0.25rem; }
    .repo-meta { font-size: 0.75rem; color: #adb5bd; margin-top: 0.375rem; }
    .badge {
      font-size: 0.75rem;
      padding: 0.125rem 0.5rem;
      border-radius: 999px;
      background: #e9ecef;
      color: #6c757d;
    }
    .empty {
      text-align: center;
      padding: 3rem;
      color: #6c757d;
    }
    .empty h2 { font-size: 1.25rem; margin-bottom: 0.5rem; color: #212529; }

    /* Create form */
    dialog { border: 1px solid #dee2e6; border-radius: 8px; padding: 1.5rem; max-width: 28rem; width: 100%; }
    dialog::backdrop { background: rgba(0,0,0,0.3); }
    dialog h2 { font-size: 1.25rem; margin-bottom: 1rem; }
    dialog form { display: flex; flex-direction: column; gap: 0.75rem; }
    dialog label { font-size: 0.875rem; font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; }
    dialog input, dialog select, dialog textarea {
      padding: 0.5rem 0.75rem; border: 1px solid #dee2e6; border-radius: 6px; font-size: 0.875rem;
    }
    dialog textarea { resize: vertical; min-height: 4rem; }
    .dialog-actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 0.5rem; }
    .btn {
      padding: 0.5rem 1rem; border-radius: 6px; font-size: 0.875rem; font-weight: 500; cursor: pointer; border: none;
    }
    .btn-primary { background: #2563eb; color: #fff; }
    .btn-primary:hover { background: #1d4ed8; }
    .btn-secondary { background: #fff; color: #6c757d; border: 1px solid #dee2e6; }
    .btn-secondary:hover { background: #f8f9fa; }
    .error { background: #fef2f2; border: 1px solid #fecaca; color: #dc2626; padding: 0.75rem; border-radius: 6px; font-size: 0.875rem; }
  `;

  @state() private _repos: RepositoryResponse[] = [];
  @state() private _loading = true;
  @state() private _error = '';

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    try {
      const res = await repoApi.list();
      this._repos = res.items;
    } catch {
      this._error = 'Failed to load repositories.';
    } finally {
      this._loading = false;
    }
  }

  private _openCreate() {
    const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement;
    dialog?.showModal();
  }

  private async _onCreate(e: Event) {
    e.preventDefault();
    const form = e.target as HTMLFormElement;
    const data = new FormData(form);
    this._error = '';

    try {
      await repoApi.create(
        data.get('name') as string,
        data.get('description') as string || undefined,
        data.get('visibility') as string,
      );
      const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement;
      dialog?.close();
      form.reset();
      await this._load();
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Failed to create repository.';
    }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;

    return html`
      <div class="header">
        <h1>Repositories</h1>
        ${authState.isAuthenticated
          ? html`<button class="btn btn-primary" @click=${this._openCreate}>New repository</button>`
          : ''}
      </div>

      ${this._error ? html`<div class="error">${this._error}</div>` : ''}

      ${this._repos.length === 0
        ? html`
          <div class="empty">
            <h2>No repositories yet</h2>
            <p>${authState.isAuthenticated ? 'Create your first repository to get started.' : 'Sign in to create a repository.'}</p>
          </div>`
        : html`
          <div class="repos">
            ${this._repos.map((r) => html`
              <div class="repo">
                <a href="/${r.slug}">
                  <div class="repo-name">${r.name}</div>
                  ${r.description ? html`<div class="repo-desc">${r.description}</div>` : ''}
                  <div class="repo-meta">${r.documentCount} document${r.documentCount !== 1 ? 's' : ''}</div>
                </a>
                <span class="badge">${r.visibility}</span>
              </div>
            `)}
          </div>`}

      <dialog>
        <h2>New repository</h2>
        <form @submit=${this._onCreate}>
          <label>Name <input type="text" name="name" required maxlength="200" /></label>
          <label>Description <textarea name="description" maxlength="1000"></textarea></label>
          <label>Visibility
            <select name="visibility">
              <option value="Private">Private</option>
              <option value="Public">Public</option>
            </select>
          </label>
          <div class="dialog-actions">
            <button type="button" class="btn btn-secondary"
              @click=${() => (this.renderRoot.querySelector('dialog') as HTMLDialogElement)?.close()}>Cancel</button>
            <button type="submit" class="btn btn-primary">Create</button>
          </div>
        </form>
      </dialog>
    `;
  }
}
