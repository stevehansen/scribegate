import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse, TemplateSummaryResponse } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import * as templateApi from '../../api/templates.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-markdown-view.js';
import '../shared/sg-breadcrumb.js';

@customElement('sg-editor-page')
export class SgEditorPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    .editor-layout {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1px;
      background: var(--sg-border);
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      overflow: hidden;
      min-height: 28rem;
    }
    .pane { background: var(--sg-bg-elevated); }
    textarea {
      width: 100%; height: 100%; min-height: 28rem;
      border: none; padding: 1rem;
      font-family: var(--sg-font-mono);
      font-size: var(--sg-font-size-sm); line-height: 1.6; resize: none; outline: none;
      background: var(--sg-bg-elevated);
      color: var(--sg-text);
    }
    .preview { padding: 1rem; overflow-y: auto; max-height: 32rem; }
    .fields { display: flex; flex-direction: column; gap: 0.75rem; margin-bottom: 1rem; }
    label { font-size: var(--sg-font-size-sm); font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; color: var(--sg-text); }
    input, select {
      padding: 0.5rem 0.75rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm); background: var(--sg-bg-elevated); color: var(--sg-text);
    }
    input:focus, select:focus, textarea:focus { outline: 2px solid var(--sg-primary); outline-offset: -1px; }
    .actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem; }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; transition: background var(--sg-transition-fast);
    }
    .btn-primary { background: var(--sg-primary); color: var(--sg-primary-text); }
    .btn-primary:hover { background: var(--sg-primary-hover); }
    .btn-secondary {
      background: var(--sg-bg-elevated); color: var(--sg-text-secondary); border: 1px solid var(--sg-border);
      text-decoration: none; display: inline-flex; align-items: center;
    }
    .btn-secondary:hover { background: var(--sg-bg-secondary); }
    .error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border); color: var(--sg-danger);
      padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm); margin-bottom: 1rem;
    }
    h1 { font-size: var(--sg-font-size-xl); margin-bottom: 1rem; color: var(--sg-text); }
    @media (max-width: 768px) {
      .editor-layout { grid-template-columns: 1fr; }
    }
  `];

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _content = '';
  @state() private _path = '';
  @state() private _message = '';
  @state() private _loading = true;
  @state() private _saving = false;
  @state() private _error = '';
  @state() private _isNew = false;
  @state() private _templates: TemplateSummaryResponse[] = [];
  @state() private _selectedTemplateId = '';

  private get _owner(): string {
    return this.location?.params?.owner ?? '';
  }

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
  }

  private get _editPath(): string {
    return this.location?.params?.[0] ?? '';
  }

  async connectedCallback() {
    super.connectedCallback();
    this._isNew = this.location?.route?.path?.includes('/edit/new');

    if (!this._owner || !this._slug) {
      this._error = 'Missing repository owner or slug.';
      this._loading = false;
      return;
    }

    try {
      this._repo = await repoApi.get(this._owner, this._slug);

      if (!this._isNew && this._editPath) {
        const path = this._editPath.endsWith('.md') ? this._editPath : this._editPath + '.md';
        const doc = await docApi.get(this._owner, this._slug, path);
        this._content = doc.content ?? '';
        this._path = doc.path;
      }

      if (this._isNew) {
        // Best-effort: templates listing is optional. If a user can't see them
        // (private repo, non-member) we silently hide the selector rather than
        // blocking the whole editor.
        try {
          const res = await templateApi.list(this._owner, this._slug);
          this._templates = res.items;
        } catch {
          this._templates = [];
        }
      }
    } catch {
      this._error = 'Failed to load.';
    } finally {
      this._loading = false;
    }
  }

  private async _onTemplateChange(e: Event) {
    const id = (e.target as HTMLSelectElement).value;
    this._selectedTemplateId = id;
    if (!id) return;

    if (this._content.trim().length > 0) {
      const ok = confirm('Replace current editor content with the selected template?');
      if (!ok) {
        this._selectedTemplateId = '';
        return;
      }
    }

    try {
      const tpl = await templateApi.get(this._owner, this._slug, id);
      this._content = tpl.content;
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Failed to load template.';
      this._selectedTemplateId = '';
    }
  }

  private async _save() {
    this._error = '';
    this._saving = true;

    const repoBase = `/${this._owner}/${this._slug}`;

    try {
      if (this._isNew) {
        const doc = await docApi.create(
          this._owner,
          this._slug,
          this._path,
          this._content,
          this._message || 'Initial content',
        );
        window.location.href = `${repoBase}/${doc.path.replace(/\.md$/, '')}`;
      } else {
        await docApi.update(
          this._owner,
          this._slug,
          this._path,
          this._content,
          this._message || 'Update content',
        );
        window.location.href = `${repoBase}/${this._path.replace(/\.md$/, '')}`;
      }
    } catch (err) {
      this._error = err instanceof ApiException
        ? (err.error.errors?.map((e) => e.message).join(' ') || err.error.message)
        : 'Failed to save.';
    } finally {
      this._saving = false;
    }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;

    const repoBase = `/${this._owner}/${this._slug}`;

    return html`
      ${this._repo ? html`
        <sg-breadcrumb
          repoOwner=${this._repo.owner}
          repoSlug=${this._repo.slug}
          repoName=${this._repo.name}
          path=${this._path}
        ></sg-breadcrumb>
      ` : ''}

      <h1>${this._isNew ? 'New document' : `Editing ${this._path}`}</h1>

      ${this._error ? html`<div class="error">${this._error}</div>` : ''}

      <div class="fields">
        ${this._isNew ? html`
          <label>Path <input type="text" .value=${this._path} @input=${(e: Event) => this._path = (e.target as HTMLInputElement).value} placeholder="folder/document-name" /></label>
          ${this._templates.length > 0 ? html`
            <label>Start from template
              <select .value=${this._selectedTemplateId} @change=${this._onTemplateChange}>
                <option value="">— Blank —</option>
                ${this._templates.map((t) => html`
                  <option value=${t.id}>${t.name}${t.description ? ` — ${t.description}` : ''}</option>
                `)}
              </select>
            </label>
          ` : ''}
        ` : ''}
        <label>Commit message <input type="text" .value=${this._message} @input=${(e: Event) => this._message = (e.target as HTMLInputElement).value} placeholder="${this._isNew ? 'Initial content' : 'Describe your changes'}" /></label>
      </div>

      <div class="editor-layout">
        <div class="pane">
          <textarea
            .value=${this._content}
            @input=${(e: Event) => this._content = (e.target as HTMLTextAreaElement).value}
            placeholder="Write your markdown here..."
          ></textarea>
        </div>
        <div class="pane preview">
          <sg-markdown-view .content=${this._content}></sg-markdown-view>
        </div>
      </div>

      <div class="actions">
        <a class="btn btn-secondary" href=${this._isNew ? repoBase : `${repoBase}/${this._path}`}>Cancel</a>
        <button class="btn btn-primary" @click=${this._save} ?disabled=${this._saving}>
          ${this._saving ? 'Saving...' : 'Save'}
        </button>
      </div>
    `;
  }
}
