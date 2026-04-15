import { LitElement, html, css } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import type { ProposalResponse, ReviewResponse, CommentResponse } from '../../api/types.js';
import * as proposalApi from '../../api/proposals.js';
import * as reviewApi from '../../api/reviews.js';
import * as commentApi from '../../api/comments.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';
import '../shared/sg-markdown-view.js';
import '../shared/sg-time-ago.js';

@customElement('sg-proposal-page')
export class SgProposalPage extends LitElement {
  static styles = [boxReset, css`
    :host { display: block; }
    h1 { font-size: var(--sg-font-size-xl); margin-bottom: 0.5rem; color: var(--sg-text); }
    .meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); margin-bottom: 1rem; }
    .status { font-size: 0.625rem; padding: 0.125rem 0.375rem; border-radius: 999px; font-weight: 600; }
    .status-open { background: var(--sg-status-open-bg); color: var(--sg-status-open-text); }
    .status-approved { background: var(--sg-status-approved-bg); color: var(--sg-status-approved-text); }
    .status-rejected { background: var(--sg-status-rejected-bg); color: var(--sg-status-rejected-text); }
    .status-withdrawn { background: var(--sg-status-withdrawn-bg); color: var(--sg-status-withdrawn-text); }
    .tabs { display: flex; gap: 0; border-bottom: 1px solid var(--sg-border); margin-bottom: 1rem; }
    .tab {
      padding: 0.5rem 1rem; cursor: pointer; font-size: var(--sg-font-size-sm);
      border-bottom: 2px solid transparent; color: var(--sg-text-secondary);
      transition: color var(--sg-transition-fast);
    }
    .tab:hover { color: var(--sg-text); }
    .tab.active { color: var(--sg-primary); border-bottom-color: var(--sg-primary); font-weight: 500; }
    .diff {
      font-family: var(--sg-font-mono); font-size: 0.8125rem;
      border: 1px solid var(--sg-border); border-radius: var(--sg-radius-lg); overflow: hidden;
    }
    .diff-line { padding: 0.125rem 0.75rem; white-space: pre-wrap; }
    .diff-added { background: var(--sg-diff-added); }
    .diff-removed { background: var(--sg-diff-removed); }
    .actions { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
    .btn {
      padding: 0.5rem 1rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm);
      font-weight: 500; cursor: pointer; border: none; transition: background var(--sg-transition-fast);
    }
    .btn-approve { background: var(--sg-success); color: #fff; }
    .btn-approve:hover { background: var(--sg-success-hover); }
    .btn-reject { background: var(--sg-danger); color: #fff; }
    .btn-reject:hover { background: var(--sg-danger-hover); }
    .btn-withdraw { background: var(--sg-status-withdrawn-text); color: #fff; }
    .btn-withdraw:hover { opacity: 0.85; }
    .btn-secondary { background: var(--sg-bg-elevated); color: var(--sg-text-secondary); border: 1px solid var(--sg-border); }
    .section { margin-top: 1.5rem; }
    .section h2 { font-size: var(--sg-font-size-base); margin-bottom: 0.75rem; color: var(--sg-text); }
    .review {
      border: 1px solid var(--sg-border); border-radius: var(--sg-radius-lg);
      padding: 0.75rem 1rem; margin-bottom: 0.5rem; background: var(--sg-bg-elevated);
    }
    .review-verdict { font-weight: 600; font-size: 0.8125rem; color: var(--sg-text); }
    .review-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); }
    .comment {
      border-left: 3px solid var(--sg-border); padding: 0.5rem 0.75rem; margin-bottom: 0.5rem;
      color: var(--sg-text);
    }
    .comment-meta { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); }
    textarea {
      width: 100%; min-height: 4rem; padding: 0.5rem;
      border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm); resize: vertical;
      background: var(--sg-bg-elevated); color: var(--sg-text);
    }
    select {
      padding: 0.375rem 0.75rem; border: 1px solid var(--sg-border); border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm); background: var(--sg-bg-elevated); color: var(--sg-text);
    }
    .form-row { display: flex; gap: 0.5rem; align-items: flex-start; margin-top: 0.5rem; }
    .error {
      background: var(--sg-danger-light); border: 1px solid var(--sg-danger-border); color: var(--sg-danger);
      padding: 0.75rem; border-radius: var(--sg-radius); font-size: var(--sg-font-size-sm); margin-bottom: 1rem;
    }
    a.back { font-size: var(--sg-font-size-sm); color: var(--sg-primary); text-decoration: none; display: inline-block; margin-bottom: 1rem; }
  `];

  @property() location: any;
  @state() private _proposal: ProposalResponse | null = null;
  @state() private _reviews: ReviewResponse[] = [];
  @state() private _comments: CommentResponse[] = [];
  @state() private _loading = true;
  @state() private _error = '';
  @state() private _tab = 'changes';
  @state() private _reviewVerdict = 'Comment';
  @state() private _reviewBody = '';
  @state() private _commentBody = '';

  private get _slug(): string { return this.location?.params?.slug ?? ''; }
  private get _id(): string { return this.location?.params?.id ?? ''; }

  async connectedCallback() {
    super.connectedCallback();
    await this._load();
  }

  private async _load() {
    try {
      const [proposal, reviews, comments] = await Promise.all([
        proposalApi.get(this._slug, this._id),
        reviewApi.list(this._slug, this._id),
        commentApi.list(this._slug, this._id),
      ]);
      this._proposal = proposal;
      this._reviews = reviews.items;
      this._comments = comments.items;
    } catch { this._error = 'Failed to load proposal.'; }
    finally { this._loading = false; }
  }

  private async _approve() {
    try {
      await proposalApi.approve(this._slug, this._id);
      await this._load();
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  private async _reject() {
    try {
      await proposalApi.reject(this._slug, this._id);
      await this._load();
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  private async _withdraw() {
    try {
      await proposalApi.withdraw(this._slug, this._id);
      await this._load();
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  private async _submitReview() {
    try {
      await reviewApi.create(this._slug, this._id, { verdict: this._reviewVerdict, body: this._reviewBody || undefined });
      this._reviewBody = '';
      const reviews = await reviewApi.list(this._slug, this._id);
      this._reviews = reviews.items;
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  private async _submitComment() {
    if (!this._commentBody.trim()) return;
    try {
      await commentApi.create(this._slug, this._id, { body: this._commentBody });
      this._commentBody = '';
      const comments = await commentApi.list(this._slug, this._id);
      this._comments = comments.items;
    } catch (e) { this._error = e instanceof ApiException ? e.error.message : 'Failed.'; }
  }

  render() {
    if (this._loading) return html`<p>Loading...</p>`;
    if (this._error && !this._proposal) return html`<p class="error">${this._error}</p>`;
    if (!this._proposal) return html``;

    const p = this._proposal;
    const isOpen = p.status === 'Open';

    return html`
      <a class="back" href="/${this._slug}/proposals">Back to proposals</a>
      <h1>${p.title} <span class="status status-${p.status.toLowerCase()}">${p.status}</span></h1>
      <div class="meta">
        ${p.documentPath ?? p.proposedPath ?? 'new document'} &middot; by ${p.createdBy}
        <sg-time-ago datetime=${p.createdAt}></sg-time-ago>
        ${p.resolvedBy ? html` &middot; resolved by ${p.resolvedBy}` : ''}
      </div>
      ${p.description ? html`<p>${p.description}</p>` : ''}

      ${this._error ? html`<div class="error">${this._error}</div>` : ''}

      ${isOpen && authState.isAuthenticated ? html`
        <div class="actions">
          <button class="btn btn-approve" @click=${this._approve}>Approve</button>
          <button class="btn btn-reject" @click=${this._reject}>Reject</button>
          ${p.createdBy === authState.user?.username
            ? html`<button class="btn btn-withdraw" @click=${this._withdraw}>Withdraw</button>`
            : ''}
        </div>
      ` : ''}

      <div class="tabs">
        <div class="tab ${this._tab === 'changes' ? 'active' : ''}" @click=${() => this._tab = 'changes'}>Changes</div>
        <div class="tab ${this._tab === 'reviews' ? 'active' : ''}" @click=${() => this._tab = 'reviews'}>Reviews (${this._reviews.length})</div>
        <div class="tab ${this._tab === 'comments' ? 'active' : ''}" @click=${() => this._tab = 'comments'}>Discussion (${this._comments.length})</div>
        <div class="tab ${this._tab === 'preview' ? 'active' : ''}" @click=${() => this._tab = 'preview'}>Preview</div>
      </div>

      ${this._tab === 'changes' ? this._renderDiff() : ''}
      ${this._tab === 'reviews' ? this._renderReviews() : ''}
      ${this._tab === 'comments' ? this._renderComments() : ''}
      ${this._tab === 'preview' ? html`<sg-markdown-view .content=${p.proposedContent}></sg-markdown-view>` : ''}
    `;
  }

  private _renderDiff() {
    const diff = this._proposal?.diff;
    if (!diff || !diff.hasChanges) return html`<p>No changes.</p>`;
    return html`
      <div class="diff">
        ${diff.lines.map(l => html`
          <div class="diff-line ${l.type === 'added' ? 'diff-added' : l.type === 'removed' ? 'diff-removed' : ''}">${l.type === 'added' ? '+' : l.type === 'removed' ? '-' : ' '} ${l.text}</div>
        `)}
      </div>
    `;
  }

  private _renderReviews() {
    return html`
      <div class="section">
        ${this._reviews.map(r => html`
          <div class="review">
            <div class="review-verdict">${r.verdict}</div>
            ${r.body ? html`<p>${r.body}</p>` : ''}
            <div class="review-meta">by ${r.createdBy} <sg-time-ago datetime=${r.createdAt}></sg-time-ago></div>
          </div>
        `)}
        ${this._proposal?.status === 'Open' && authState.isAuthenticated ? html`
          <h2>Submit Review</h2>
          <select .value=${this._reviewVerdict} @change=${(e: Event) => this._reviewVerdict = (e.target as HTMLSelectElement).value}>
            <option value="Approved">Approve</option>
            <option value="ChangesRequested">Request Changes</option>
            <option value="Comment" selected>Comment</option>
          </select>
          <textarea .value=${this._reviewBody} @input=${(e: Event) => this._reviewBody = (e.target as HTMLTextAreaElement).value} placeholder="Optional review comment..."></textarea>
          <button class="btn btn-approve" @click=${this._submitReview} style="margin-top:0.5rem">Submit Review</button>
        ` : ''}
      </div>
    `;
  }

  private _renderComments() {
    return html`
      <div class="section">
        ${this._comments.map(c => html`
          <div class="comment">
            <p>${c.body}</p>
            <div class="comment-meta">by ${c.createdBy} <sg-time-ago datetime=${c.createdAt}></sg-time-ago></div>
          </div>
        `)}
        ${authState.isAuthenticated ? html`
          <textarea .value=${this._commentBody} @input=${(e: Event) => this._commentBody = (e.target as HTMLTextAreaElement).value} placeholder="Write a comment..."></textarea>
          <button class="btn btn-secondary" @click=${this._submitComment} style="margin-top:0.5rem">Post Comment</button>
        ` : ''}
      </div>
    `;
  }
}
