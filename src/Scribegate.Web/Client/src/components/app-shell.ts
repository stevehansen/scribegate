import { LitElement, html, css } from 'lit';
import { customElement } from 'lit/decorators.js';
import { initRouter } from '../router.js';
import { boxReset } from '../styles/shared.js';
import './layout/sg-header.js';
import './layout/sg-footer.js';

@customElement('sg-app')
export class SgApp extends LitElement {
  static styles = [boxReset, css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 100dvh;
    }
    main {
      flex: 1;
      padding: 1.5rem;
      max-width: var(--sg-content-width-wide);
      margin: 0 auto;
      width: 100%;
    }
  `];

  firstUpdated() {
    const outlet = this.renderRoot.querySelector('#outlet') as HTMLElement;
    initRouter(outlet);
  }

  render() {
    return html`
      <sg-header></sg-header>
      <main id="outlet"></main>
      <sg-footer></sg-footer>
    `;
  }
}
