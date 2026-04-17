import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { boxReset } from '../../styles/shared.js';
import { getInstanceInfo, type InstanceInfo } from '../../api/info.js';

@customElement('sg-footer')
export class SgFooter extends LitElement {
  static styles = [boxReset, css`
    :host {
      display: block;
      border-top: 1px solid var(--sg-border);
      background: color-mix(in srgb, var(--sg-bg-elevated) 50%, transparent);
      color: var(--sg-text-secondary);
      font-size: var(--sg-font-size-xs);
    }
    .bar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem 1rem;
      align-items: center;
      justify-content: space-between;
      max-width: var(--sg-content-width-wide);
      margin: 0 auto;
      padding: 0.75rem 1.5rem;
    }
    .left, .right { display: flex; flex-wrap: wrap; gap: 0.75rem; align-items: center; }
    a { color: var(--sg-text-secondary); text-decoration: none; transition: color var(--sg-transition-fast); }
    a:hover { color: var(--sg-text); }
    .sep { opacity: 0.4; }
    .tagline { opacity: 0.8; }
  `];

  @state() private _info: InstanceInfo | null = null;

  async connectedCallback() {
    super.connectedCallback();
    try { this._info = await getInstanceInfo(); } catch { /* offline — stay silent */ }
  }

  render() {
    const info = this._info;
    return html`
      <div class="bar">
        <div class="left">
          <strong>${info?.product ?? 'Scribegate'}</strong>
          ${info?.version ? html`<span class="sep">·</span><span>v${info.version}</span>` : ''}
          ${info?.tagline ? html`<span class="sep">·</span><span class="tagline">${info.tagline}</span>` : ''}
        </div>
        <div class="right">
          ${info?.sourceUrl
            ? html`<a href=${info.sourceUrl} target="_blank" rel="noopener">Source code</a><span class="sep">·</span>`
            : ''}
          <a href="https://docs.scribegate.dev/legal/imprint/" target="_blank" rel="noopener">Imprint</a>
          <span class="sep">·</span>
          <a href="https://docs.scribegate.dev/legal/privacy/" target="_blank" rel="noopener">Privacy</a>
          <span class="sep">·</span>
          <a href="https://docs.scribegate.dev/legal/terms/" target="_blank" rel="noopener">Terms</a>
        </div>
      </div>
    `;
  }
}
