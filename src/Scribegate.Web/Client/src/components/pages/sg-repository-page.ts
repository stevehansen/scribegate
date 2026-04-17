import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, DocumentSummary } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import * as exportsApi from '../../api/exports.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-file-tree.js';
import '../shared/sg-breadcrumb.js';

@customElement('sg-repository-page')
export class SgRepositoryPage extends LitElement {
  static styles = [boxReset, css`
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
      box-shadow: var(--sg-shadow-sm);
    }
    h2 { font-size: var(--sg-font-size-lg); margin-bottom: 1rem; color: var(--sg-text); }
    .error { color: var(--sg-danger); }

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
    .dialog-error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border);
      color: var(--sg-danger); padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
    }

    .clone-box {
      margin-bottom: 1rem;
      padding: 0.75rem 1rem;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      background: var(--sg-bg-elevated);
      display: flex;
      gap: 0.75rem;
      align-items: center;
      flex-wrap: wrap;
    }
    .clone-box label {
      font-size: var(--sg-font-size-sm);
      font-weight: 500;
      color: var(--sg-text-secondary);
    }
    .clone-box code {
      flex: 1;
      min-width: 12rem;
      padding: 0.35rem 0.6rem;
      background: var(--sg-bg-tertiary);
      border-radius: var(--sg-radius);
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: var(--sg-font-size-sm);
      color: var(--sg-text);
      overflow: auto;
      white-space: nowrap;
    }
    .clone-copied {
      color: var(--sg-text-secondary);
      font-size: var(--sg-font-size-xs);
    }
  `];

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _docs: DocumentSummary[] = [];
  @state() private _loading = true;
  @state() private _error = '';
  @state() private _dialogError = '';
  @state() private _exporting = false;
  @state() private _generatingSite = false;
  @state() private _cloneCopied = false;

  private get _owner(): string {
    return this.location?.params?.owner ?? '';
  }

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
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
      const [repo, docs] = await Promise.all([
        repoApi.get(this._owner, this._slug),
        docApi.list(this._owner, this._slug),
      ]);
      this._repo = repo;
      this._docs = docs.items;
    } catch {
      this._error = 'Repository not found.';
    } finally {
      this._loading = false;
    }
  }

  private async _onExport() {
    if (this._exporting) return;
    this._exporting = true;
    try {
      await exportsApi.downloadRepoZip(this._owner, this._slug);
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Export failed.';
    } finally {
      this._exporting = false;
    }
  }

  private async _onGenerateSite() {
    if (this._generatingSite) return;
    this._generatingSite = true;
    try {
      await exportsApi.buildSite(this._owner, this._slug);
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Site generation failed.';
    } finally {
      this._generatingSite = false;
    }
  }

  private get _cloneUrl(): string {
    // Build from the current window origin so self-hosted deployments behind
    // custom domains get the right URL without any server round-trip.
    return `${window.location.origin}/${this._slug}.git`;
  }

  private async _onCopyCloneUrl() {
    try {
      await navigator.clipboard.writeText(this._cloneUrl);
      this._cloneCopied = true;
      setTimeout(() => (this._cloneCopied = false), 2000);
    } catch {
      // Clipboard permission denied or unavailable — ignore, the URL is still
      // selectable in the <code> block.
    }
  }

  private _openSettings() {
    this._dialogError = '';
    const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement;
    const form = dialog?.querySelector('form') as HTMLFormElement;
    if (form && this._repo) {
      (form.querySelector('[name="name"]') as HTMLInputElement).value = this._repo.name;
      (form.querySelector('[name="description"]') as HTMLTextAreaElement).value = this._repo.description ?? '';
      (form.querySelector('[name="visibility"]') as HTMLSelectElement).value = this._repo.visibility;
    }
    dialog?.showModal();
  }

  private async _onSaveSettings(e: Event) {
    e.preventDefault();
    const form = e.target as HTMLFormElement;
    const data = new FormData(form);
    this._dialogError = '';

    try {
      const updated = await repoApi.update(this._owner, this._slug, {
        name: data.get('name') as string,
        description: data.get('description') as string || undefined,
        visibility: data.get('visibility') as string,
      });
      this._repo = updated;
      const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement;
      dialog?.close();
      // If slug changed, navigate to new URL
      if (updated.slug !== this._slug) {
        window.history.replaceState(null, '', `/${updated.owner}/${updated.slug}`);
      }
    } catch (err) {
      this._dialogError = err instanceof ApiException ? err.error.message : 'Failed to update repository.';
    }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (this._error) return html`<p class="error">${this._error}</p>`;
    if (!this._repo) return html``;

    const repoBase = `/${this._owner}/${this._slug}`;

    return html`
      <sg-breadcrumb
        repoOwner=${this._repo.owner}
        repoSlug=${this._repo.slug}
        repoName=${this._repo.name}
      ></sg-breadcrumb>

      <div class="header">
        <div class="info">
          <h1>${this._repo.owner}/${this._repo.name} <span class="badge">${this._repo.visibility}</span></h1>
          ${this._repo.description ? html`<p>${this._repo.description}</p>` : ''}
        </div>
        <div class="actions">
          <a class="btn btn-secondary" href="${repoBase}/proposals">Proposals</a>
          <a class="btn btn-secondary" href="${repoBase}/members">Members</a>
          <a class="btn btn-secondary" href="${repoBase}/webhooks">Webhooks</a>
          <a class="btn btn-secondary" href="${repoBase}/templates">Templates</a>
          <button class="btn btn-secondary" @click=${this._onExport} ?disabled=${this._exporting}>
            ${this._exporting ? 'Exporting…' : 'Export'}
          </button>
          <button class="btn btn-secondary" @click=${this._onGenerateSite} ?disabled=${this._generatingSite}>
            ${this._generatingSite ? 'Generating…' : 'Generate site'}
          </button>
          ${authState.isAuthenticated
            ? html`
                <button class="btn btn-secondary" @click=${this._openSettings}>Settings</button>
                <a class="btn btn-primary" href="${repoBase}/edit/new">New document</a>`
            : ''}
        </div>
      </div>

      ${this._repo.visibility === 'Public' || authState.isAuthenticated
        ? html`
            <div class="clone-box">
              <label>Git clone</label>
              <code>git clone ${this._cloneUrl}</code>
              <button class="btn btn-secondary" @click=${this._onCopyCloneUrl}>
                ${this._cloneCopied ? 'Copied' : 'Copy'}
              </button>
              ${this._repo.visibility === 'Private'
                ? html`<span class="clone-copied">Private repo — use an API token as the password.</span>`
                : html`<span class="clone-copied">Public repo — no credentials required.</span>`}
            </div>`
        : ''}

      <section>
        <h2>Documents</h2>
        <sg-file-tree
          .documents=${this._docs}
          repoOwner=${this._owner}
          repoSlug=${this._slug}
        ></sg-file-tree>
      </section>

      <dialog>
        <h2>Repository settings</h2>
        ${this._dialogError ? html`<div class="dialog-error">${this._dialogError}</div>` : ''}
        <form @submit=${this._onSaveSettings}>
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
            <button type="submit" class="btn btn-primary">Save changes</button>
          </div>
        </form>
      </dialog>
    `;
  }
}
