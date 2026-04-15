import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';

@customElement('sg-breadcrumb')
export class SgBreadcrumb extends LitElement {
  static styles = css`
    :host { display: block; font-size: var(--sg-font-size-sm); color: var(--sg-text-secondary); margin-bottom: 1rem; }
    a { color: var(--sg-primary); text-decoration: none; transition: color var(--sg-transition-fast); }
    a:hover { text-decoration: underline; }
    .sep { margin: 0 0.375rem; }
    .current { color: var(--sg-text); font-weight: 500; }
  `;

  @property() repoSlug = '';
  @property() repoName = '';
  @property() path = '';

  render() {
    const parts = this.path ? this.path.split('/').filter(Boolean) : [];

    return html`
      <a href="/">Repositories</a>
      <span class="sep">/</span>
      <a href="/${this.repoSlug}">${this.repoName || this.repoSlug}</a>
      ${parts.map((part, i) => {
        const isLast = i === parts.length - 1;
        const partPath = parts.slice(0, i + 1).join('/');
        return html`
          <span class="sep">/</span>
          ${isLast
            ? html`<span class="current">${part}</span>`
            : html`<a href="/${this.repoSlug}/${partPath}">${part}</a>`}
        `;
      })}
    `;
  }
}
