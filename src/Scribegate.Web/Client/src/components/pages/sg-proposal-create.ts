import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import * as proposalApi from '../../api/proposals.js';
import { ApiException } from '../../api/client.js';
import '../shared/sg-markdown-view.js';

@customElement('sg-proposal-create')
export class SgProposalCreate extends LitElement {
  static styles = css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-xl); margin-bottom: 1rem; color: var(--sg-text); }
    .fields { display: flex; flex-direction: column; gap: 0.75rem; margin-bottom: 1rem; }
    label { font-size: var(--sg-font-size-sm); font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; color: var(--sg-text); }
    input, textarea {
      padding: 0.5rem 0.75rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm); background: var(--sg-bg-elevated); color: var(--sg-text);
    }
    textarea { min-height: 8rem; font-family: var(--sg-font-mono); resize: vertical; }
    .editor-layout {
      display: grid; grid-template-columns: 1fr 1fr; gap: 1px;
      background: var(--sg-border); border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg); overflow: hidden; min-height: 20rem;
    }
    .pane { background: var(--sg-bg-elevated); }
    .pane textarea { width: 100%; height: 100%; border: none; min-height: 20rem; padding: 1rem; outline: none; background: var(--sg-bg-elevated); color: var(--sg-text); }
    .preview { padding: 1rem; overflow-y: auto; }
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
    .error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border); color: var(--sg-danger);
      padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm); margin-bottom: 1rem;
    }
    @media (max-width: 768px) { .editor-layout { grid-template-columns: 1fr; } }
  `;

  @property() location: any;
  @state() private _title = '';
  @state() private _description = '';
  @state() private _documentPath = '';
  @state() private _content = '';
  @state() private _saving = false;
  @state() private _error = '';

  private get _slug(): string { return this.location?.params?.slug ?? ''; }

  private async _save() {
    this._error = '';
    this._saving = true;
    try {
      const result = await proposalApi.create(this._slug, {
        title: this._title,
        content: this._content,
        documentPath: this._documentPath || undefined,
        description: this._description || undefined,
      });
      window.location.href = `/${this._slug}/proposals/${result.id}`;
    } catch (err) {
      this._error = err instanceof ApiException
        ? (err.error.errors?.map(e => e.message).join(' ') || err.error.message)
        : 'Failed to create proposal.';
    } finally { this._saving = false; }
  }

  render() {
    return html`
      <h1>New Proposal</h1>

      ${this._error ? html`<div class="error">${this._error}</div>` : ''}

      <div class="fields">
        <label>Title <input type="text" .value=${this._title} @input=${(e: Event) => this._title = (e.target as HTMLInputElement).value} placeholder="What does this proposal do?" /></label>
        <label>Document path <input type="text" .value=${this._documentPath} @input=${(e: Event) => this._documentPath = (e.target as HTMLInputElement).value} placeholder="folder/document (leave empty for new doc)" /></label>
        <label>Description <input type="text" .value=${this._description} @input=${(e: Event) => this._description = (e.target as HTMLInputElement).value} placeholder="Brief description (optional)" /></label>
      </div>

      <div class="editor-layout">
        <div class="pane">
          <textarea .value=${this._content} @input=${(e: Event) => this._content = (e.target as HTMLTextAreaElement).value} placeholder="Write your proposed markdown content..."></textarea>
        </div>
        <div class="pane preview">
          <sg-markdown-view .content=${this._content}></sg-markdown-view>
        </div>
      </div>

      <div class="actions">
        <a class="btn btn-secondary" href="/${this._slug}/proposals">Cancel</a>
        <button class="btn btn-primary" @click=${this._save} ?disabled=${this._saving}>
          ${this._saving ? 'Creating...' : 'Create Proposal'}
        </button>
      </div>
    `;
  }
}
