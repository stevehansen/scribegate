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
    h1 { font-size: var(--sg-font-size-2xl); color: var(--sg-text); }
    .repos { display: flex; flex-direction: column; gap: 0.75rem; }
    .repo {
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      padding: 1rem 1.25rem;
      display: flex;
      justify-content: space-between;
      align-items: center;
      background: var(--sg-bg-elevated);
      transition: border-color var(--sg-transition-fast), box-shadow var(--sg-transition-fast);
    }
    .repo:hover { border-color: var(--sg-border-hover); box-shadow: var(--sg-shadow-sm); }
    .repo a { text-decoration: none; color: inherit; display: block; flex: 1; }
    .repo-name { font-weight: 600; color: var(--sg-primary); }
    .repo-desc { font-size: var(--sg-font-size-sm); color: var(--sg-text-secondary); margin-top: 0.25rem; }
    .repo-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-muted); margin-top: 0.375rem; }
    .badge {
      font-size: var(--sg-font-size-xs);
      padding: 0.125rem 0.5rem;
      border-radius: 999px;
      background: var(--sg-bg-tertiary);
      color: var(--sg-text-secondary);
    }
    .empty { text-align: center; padding: 3rem; color: var(--sg-text-secondary); }
    .empty h2 { font-size: var(--sg-font-size-xl); margin-bottom: 0.5rem; color: var(--sg-text); }

    dialog {
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      padding: 1.5rem;
      max-width: 28rem;
      width: 100%;
      background: var(--sg-bg-elevated);
      color: var(--sg-text);
      box-shadow: var(--sg-shadow-lg);
    }
    dialog::backdrop { background: var(--sg-overlay); }
    dialog h2 { font-size: var(--sg-font-size-xl); margin-bottom: 1rem; }
    dialog form { display: flex; flex-direction: column; gap: 0.75rem; }
    dialog label { font-size: var(--sg-font-size-sm); font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; }
    dialog input, dialog select, dialog textarea {
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm);
      background: var(--sg-bg);
      color: var(--sg-text);
    }
    dialog textarea { resize: vertical; min-height: 4rem; }
    .dialog-actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 0.5rem; }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; transition: background var(--sg-transition-fast);
    }
    .btn-primary { background: var(--sg-primary); color: var(--sg-primary-text); }
    .btn-primary:hover { background: var(--sg-primary-hover); }
    .btn-secondary { background: var(--sg-bg-elevated); color: var(--sg-text-secondary); border: 1px solid var(--sg-border); }
    .btn-secondary:hover { background: var(--sg-bg-secondary); }
    .error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border);
      color: var(--sg-danger); padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
    }
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
