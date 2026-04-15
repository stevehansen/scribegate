import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { RepositoryResponse } from '../../api/types.js';
import * as repoApi from '../../api/repositories.js';
import * as docApi from '../../api/documents.js';
import { ApiException } from '../../api/client.js';
import '../shared/sg-markdown-view.js';
import '../shared/sg-breadcrumb.js';

@customElement('sg-editor-page')
export class SgEditorPage extends LitElement {
  static styles = css`
    :host { display: block; }
    .editor-layout {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 1px;
      background: #dee2e6;
      border: 1px solid #dee2e6;
      border-radius: 8px;
      overflow: hidden;
      min-height: 28rem;
    }
    .pane { background: #fff; }
    textarea {
      width: 100%; height: 100%; min-height: 28rem;
      border: none; padding: 1rem; font-family: 'SF Mono', 'Fira Code', Menlo, Consolas, monospace;
      font-size: 0.875rem; line-height: 1.6; resize: none; outline: none;
    }
    .preview { padding: 1rem; overflow-y: auto; max-height: 32rem; }
    .fields { display: flex; flex-direction: column; gap: 0.75rem; margin-bottom: 1rem; }
    label { font-size: 0.875rem; font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; }
    input {
      padding: 0.5rem 0.75rem; border: 1px solid #dee2e6; border-radius: 6px; font-size: 0.875rem;
    }
    input:focus, textarea:focus { outline: 2px solid #2563eb; outline-offset: -1px; }
    .actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem; }
    .btn {
      padding: 0.5rem 1rem; border-radius: 6px; font-size: 0.875rem;
      font-weight: 500; cursor: pointer; border: none;
    }
    .btn-primary { background: #2563eb; color: #fff; }
    .btn-primary:hover { background: #1d4ed8; }
    .btn-secondary { background: #fff; color: #6c757d; border: 1px solid #dee2e6; text-decoration: none; display: inline-flex; align-items: center; }
    .btn-secondary:hover { background: #f8f9fa; }
    .error { background: #fef2f2; border: 1px solid #fecaca; color: #dc2626; padding: 0.75rem; border-radius: 6px; font-size: 0.875rem; margin-bottom: 1rem; }
    h1 { font-size: 1.25rem; margin-bottom: 1rem; }
    @media (max-width: 768px) {
      .editor-layout { grid-template-columns: 1fr; }
    }
  `;

  @property() location: any;
  @state() private _repo: RepositoryResponse | null = null;
  @state() private _content = '';
  @state() private _path = '';
  @state() private _message = '';
  @state() private _loading = true;
  @state() private _saving = false;
  @state() private _error = '';
  @state() private _isNew = false;

  private get _slug(): string {
    return this.location?.params?.slug ?? '';
  }

  private get _editPath(): string {
    return this.location?.params?.[0] ?? '';
  }

  async connectedCallback() {
    super.connectedCallback();
    this._isNew = this.location?.route?.path?.includes('/edit/new');

    try {
      this._repo = await repoApi.get(this._slug);

      if (!this._isNew && this._editPath) {
        const path = this._editPath.endsWith('.md') ? this._editPath : this._editPath + '.md';
        const doc = await docApi.get(this._slug, path);
        this._content = doc.content ?? '';
        this._path = doc.path;
      }
    } catch {
      this._error = 'Failed to load.';
    } finally {
      this._loading = false;
    }
  }

  private async _save() {
    this._error = '';
    this._saving = true;

    try {
      if (this._isNew) {
        const doc = await docApi.create(
          this._slug,
          this._path,
          this._content,
          this._message || 'Initial content',
        );
        window.location.href = `/${this._slug}/${doc.path}`;
      } else {
        await docApi.update(
          this._slug,
          this._path,
          this._content,
          this._message || 'Update content',
        );
        window.location.href = `/${this._slug}/${this._path}`;
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

    return html`
      ${this._repo ? html`
        <sg-breadcrumb
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
        <a class="btn btn-secondary" href=${this._isNew ? `/${this._slug}` : `/${this._slug}/${this._path}`}>Cancel</a>
        <button class="btn btn-primary" @click=${this._save} ?disabled=${this._saving}>
          ${this._saving ? 'Saving...' : 'Save'}
        </button>
      </div>
    `;
  }
}
