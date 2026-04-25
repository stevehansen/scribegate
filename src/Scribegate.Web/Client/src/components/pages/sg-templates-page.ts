import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import type { RouterLocation } from '@vaadin/router';
import * as templateApi from '../../api/templates.js';
import { ApiException } from '../../api/client.js';
import { authState } from '../../state/auth-state.js';
import type { TemplateSummaryResponse, TemplateResponse } from '../../api/types.js';
import { LoadController } from '../../state/load-controller.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-templates-page')
export class SgTemplatesPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; max-width: var(--sg-content-width, 900px); margin: 0 auto; padding: 2rem 1rem; }
    h1 { margin: 0 0 1rem; font-size: 1.5rem; }
    h2 { margin: 2rem 0 0.5rem; font-size: 1.1rem; border-bottom: 1px solid var(--sg-border, #ddd); padding-bottom: 0.4rem; }
    p.help { color: var(--sg-text-secondary, #666); font-size: 0.9rem; margin: 0.25rem 0 1rem; }

    form { display: flex; flex-direction: column; gap: 0.75rem; max-width: 720px; }
    label { display: flex; flex-direction: column; gap: 0.25rem; font-size: 0.875rem; }
    input[type=text], textarea {
      padding: 0.5rem; border: 1px solid var(--sg-border, #ccc);
      border-radius: 4px; font: inherit; background: var(--sg-bg, #fff); color: inherit;
    }
    textarea.content {
      font-family: var(--sg-font-mono, monospace);
      font-size: 0.85rem;
      line-height: 1.5;
      min-height: 14rem;
      resize: vertical;
    }

    button {
      padding: 0.5rem 1rem; border-radius: 4px; border: 1px solid var(--sg-border, #ccc);
      background: var(--sg-surface, #fff); color: inherit; cursor: pointer; font: inherit;
    }
    button.primary { background: var(--sg-primary, #2563eb); color: #fff; border-color: var(--sg-primary, #2563eb); }
    button.danger { color: var(--sg-danger, #c00); border-color: var(--sg-danger, #c00); background: none; }
    button:disabled { opacity: 0.5; cursor: not-allowed; }

    .error { color: var(--sg-danger, #c00); font-size: 0.875rem; white-space: pre-wrap; }

    table { width: 100%; border-collapse: collapse; margin-top: 0.75rem; font-size: 0.9rem; }
    th, td { text-align: left; padding: 0.5rem 0.75rem; border-bottom: 1px solid var(--sg-border, #eee); vertical-align: top; }
    th { font-weight: 600; color: var(--sg-text-secondary, #666); }
    td.muted { color: var(--sg-text-secondary, #888); }
    td.actions { text-align: right; white-space: nowrap; }
    td.name { font-weight: 500; }
    .empty { color: var(--sg-text-secondary, #888); font-style: italic; padding: 1rem 0; }

    dialog {
      border: 1px solid var(--sg-border, #ccc);
      border-radius: var(--sg-radius-lg, 8px);
      padding: 1.25rem;
      background: var(--sg-bg-elevated, var(--sg-bg, #fff));
      color: inherit;
      max-width: 48rem;
      width: 100%;
    }
    dialog::backdrop { background: rgba(0,0,0,0.35); }
    dialog h2 { margin-top: 0; border-bottom: none; padding-bottom: 0; }
    .dialog-actions { display: flex; gap: 0.5rem; justify-content: flex-end; margin-top: 1rem; }
  `];

  @state() private _repoOwner = '';
  @state() private _repoSlug = '';
  @state() private _error = '';
  @state() private _submitting = false;

  // Create form state
  @state() private _name = '';
  @state() private _description = '';
  @state() private _content = '';

  // Edit dialog state
  @state() private _editing: TemplateResponse | null = null;
  @state() private _editName = '';
  @state() private _editDescription = '';
  @state() private _editContent = '';
  @state() private _editError = '';

  private _templatesCtl = new LoadController<TemplateSummaryResponse[]>(this, () =>
    templateApi.list(this._repoOwner, this._repoSlug).then(r => r.items));

  onBeforeEnter(location: RouterLocation) {
    this._repoOwner = (location.params.owner as string) ?? '';
    this._repoSlug = (location.params.slug as string) ?? '';
  }

  private _messageFor(err: unknown, fallback: string): string {
    if (err instanceof ApiException) {
      const details = err.error.errors?.map((e) => `${e.field}: ${e.message}`).join('\n');
      return details ? `${err.error.message}\n${details}` : err.error.message;
    }
    return fallback;
  }

  private async _onCreate(e: Event) {
    e.preventDefault();
    this._error = '';
    if (!this._name.trim()) { this._error = 'Name is required.'; return; }
    if (!this._content) { this._error = 'Content is required.'; return; }
    this._submitting = true;
    try {
      await templateApi.create(this._repoOwner, this._repoSlug, {
        name: this._name.trim(),
        description: this._description.trim() || null,
        content: this._content,
      });
      this._name = '';
      this._description = '';
      this._content = '';
      await this._templatesCtl.reload();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to create template.');
    } finally {
      this._submitting = false;
    }
  }

  private async _onEdit(summary: TemplateSummaryResponse) {
    this._editError = '';
    try {
      const full = await templateApi.get(this._repoOwner, this._repoSlug, summary.id);
      this._editing = full;
      this._editName = full.name;
      this._editDescription = full.description ?? '';
      this._editContent = full.content;
      await this.updateComplete;
      const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement | null;
      dialog?.showModal();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to load template.');
    }
  }

  private async _onSaveEdit(e: Event) {
    e.preventDefault();
    if (!this._editing) return;
    this._editError = '';
    this._submitting = true;
    try {
      await templateApi.update(this._repoOwner, this._repoSlug, this._editing.id, {
        name: this._editName.trim(),
        description: this._editDescription.trim() || null,
        content: this._editContent,
      });
      const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement | null;
      dialog?.close();
      this._editing = null;
      await this._templatesCtl.reload();
    } catch (err) {
      this._editError = this._messageFor(err, 'Failed to update template.');
    } finally {
      this._submitting = false;
    }
  }

  private _closeEdit() {
    const dialog = this.renderRoot.querySelector('dialog') as HTMLDialogElement | null;
    dialog?.close();
    this._editing = null;
    this._editError = '';
  }

  private async _onDelete(summary: TemplateSummaryResponse) {
    if (!confirm(`Delete template "${summary.name}"? This cannot be undone.`)) return;
    try {
      await templateApi.remove(this._repoOwner, this._repoSlug, summary.id);
      await this._templatesCtl.reload();
    } catch (err) {
      this._error = this._messageFor(err, 'Failed to delete template.');
    }
  }

  render() {
    const canEdit = authState.isAuthenticated;

    return html`
      <h1>Templates — ${this._repoOwner}/${this._repoSlug}</h1>
      <p class="help">
        Templates prefill the editor when creating a new document. Authors can
        pick a template on the "New document" page, or start from a blank canvas.
        <a href="/${this._repoOwner}/${this._repoSlug}">← back to repository</a>
      </p>

      ${canEdit ? html`
        <h2>Add template</h2>
        <form @submit=${this._onCreate}>
          <label>
            Name
            <input type="text" required maxlength="100" placeholder="e.g., Meeting notes"
              .value=${this._name}
              @input=${(e: Event) => { this._name = (e.target as HTMLInputElement).value; }} />
          </label>
          <label>
            Description (optional)
            <input type="text" maxlength="500"
              .value=${this._description}
              @input=${(e: Event) => { this._description = (e.target as HTMLInputElement).value; }} />
          </label>
          <label>
            Content (markdown)
            <textarea class="content" required
              .value=${this._content}
              @input=${(e: Event) => { this._content = (e.target as HTMLTextAreaElement).value; }}
              placeholder="# Title&#10;&#10;Write the markdown that should prefill the editor..."
            ></textarea>
          </label>
          ${this._error ? html`<div class="error">${this._error}</div>` : ''}
          <div>
            <button class="primary" type="submit" ?disabled=${this._submitting}>
              ${this._submitting ? 'Saving…' : 'Create template'}
            </button>
          </div>
        </form>
      ` : html`<p class="help">Log in as a repository admin to add templates.</p>`}

      <h2>Existing templates</h2>
      ${this._templatesCtl.status === 'loading' && !this._templatesCtl.data
        ? html`<p>Loading…</p>`
        : this._templatesCtl.status === 'error'
          ? html`<div class="error">${this._templatesCtl.error}</div>`
          : (this._templatesCtl.data ?? []).length === 0
            ? html`<p class="empty">No templates yet.</p>`
            : html`
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Description</th>
                  <th>Created by</th>
                  <th>Updated</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                ${(this._templatesCtl.data ?? []).map((t) => html`
                  <tr>
                    <td class="name">${t.name}</td>
                    <td class="muted">${t.description ?? ''}</td>
                    <td class="muted">${t.createdBy}</td>
                    <td class="muted">${new Date(t.updatedAt ?? t.createdAt).toLocaleString()}</td>
                    <td class="actions">
                      ${canEdit ? html`
                        <button @click=${() => this._onEdit(t)}>Edit</button>
                        <button class="danger" @click=${() => this._onDelete(t)}>Delete</button>
                      ` : ''}
                    </td>
                  </tr>
                `)}
              </tbody>
            </table>
          `}

      <dialog>
        ${this._editing ? html`
          <h2>Edit template</h2>
          <form @submit=${this._onSaveEdit}>
            <label>
              Name
              <input type="text" required maxlength="100"
                .value=${this._editName}
                @input=${(e: Event) => { this._editName = (e.target as HTMLInputElement).value; }} />
            </label>
            <label>
              Description (optional)
              <input type="text" maxlength="500"
                .value=${this._editDescription}
                @input=${(e: Event) => { this._editDescription = (e.target as HTMLInputElement).value; }} />
            </label>
            <label>
              Content (markdown)
              <textarea class="content" required
                .value=${this._editContent}
                @input=${(e: Event) => { this._editContent = (e.target as HTMLTextAreaElement).value; }}
              ></textarea>
            </label>
            ${this._editError ? html`<div class="error">${this._editError}</div>` : ''}
            <div class="dialog-actions">
              <button type="button" @click=${this._closeEdit}>Cancel</button>
              <button class="primary" type="submit" ?disabled=${this._submitting}>
                ${this._submitting ? 'Saving…' : 'Save changes'}
              </button>
            </div>
          </form>
        ` : ''}
      </dialog>
    `;
  }
}
