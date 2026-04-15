import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';
import { themeManager } from '../../state/theme.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-header')
export class SgHeader extends LitElement {
  static styles = [boxReset, css`
    :host {
      display: block;
      border-bottom: 1px solid var(--sg-border);
      background: color-mix(in srgb, var(--sg-bg-elevated) 85%, transparent);
      transition: background var(--sg-transition-base), border-color var(--sg-transition-base);
      position: sticky;
      top: 3px; /* below accent stripe */
      z-index: 100;
      backdrop-filter: blur(8px);
      -webkit-backdrop-filter: blur(8px);
    }
    nav {
      display: flex;
      align-items: center;
      justify-content: space-between;
      max-width: var(--sg-content-width-wide);
      margin: 0 auto;
      padding: 0 1.5rem;
      height: var(--sg-header-height);
    }
    .logo {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-size: var(--sg-font-size-lg);
      font-weight: 700;
      color: var(--sg-text);
      transition: opacity var(--sg-transition-fast);
    }
    .logo:hover { opacity: 0.8; }
    .logo svg {
      width: 24px;
      height: 24px;
      color: var(--sg-primary);
      flex-shrink: 0;
    }
    .actions {
      display: flex;
      align-items: center;
      gap: 1rem;
    }
    a {
      color: var(--sg-text-secondary);
      text-decoration: none;
      font-size: var(--sg-font-size-sm);
      transition: color var(--sg-transition-fast);
    }
    a:hover { color: var(--sg-text); }
    .user-info {
      font-size: var(--sg-font-size-sm);
      color: var(--sg-text);
      font-weight: 500;
    }
    button {
      background: none;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius);
      padding: 0.375rem 0.75rem;
      font-size: var(--sg-font-size-sm);
      cursor: pointer;
      color: var(--sg-text-secondary);
      transition: background var(--sg-transition-fast), color var(--sg-transition-fast), border-color var(--sg-transition-fast);
    }
    button:hover {
      background: var(--sg-bg-secondary);
      color: var(--sg-text);
    }
    .theme-toggle {
      display: flex;
      align-items: center;
      justify-content: center;
      width: 2rem;
      height: 2rem;
      padding: 0;
      border-radius: 50%;
      font-size: 1rem;
      line-height: 1;
    }
  `];

  @state() private _isAuth = false;
  @state() private _username = '';
  @state() private _isAdmin = false;
  @state() private _theme: 'light' | 'dark' | 'system' = 'system';

  private _unsub?: () => void;
  private _themeUnsub?: () => void;

  async connectedCallback() {
    super.connectedCallback();
    this._unsub = authState.subscribe(() => this._sync());
    this._themeUnsub = themeManager.subscribe(() => {
      this._theme = themeManager.current;
    });
    this._theme = themeManager.current;

    if (authState.isAuthenticated) {
      if (!authState.user) {
        await authState.loadUser();
      }
      this._sync();
    }
  }

  disconnectedCallback() {
    super.disconnectedCallback();
    this._unsub?.();
    this._themeUnsub?.();
  }

  private _sync() {
    this._isAuth = authState.isAuthenticated;
    const user = authState.user;
    this._username = user?.username ?? '';
    this._isAdmin = user?.isAdmin === true;
    this.requestUpdate();
  }

  render() {
    return html`
      <nav>
        <a class="logo" href="/">
          <svg viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">
            <path d="M7 28V12a9 9 0 0 1 18 0v16" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"/>
            <line x1="20" y1="8" x2="13" y2="24" stroke="currentColor" stroke-width="2" stroke-linecap="round"/>
            <path d="M13 24l-1.5 3.5 3.5-1.5z" fill="currentColor"/>
          </svg>
          Scribegate
        </a>
        <div class="actions">
          ${this._isAuth
            ? html`
                ${this._isAdmin ? html`<a href="/admin">Admin</a>` : ''}
                <span class="user-info">${this._username}</span>
                <button @click=${() => authState.logout()}>Sign out</button>
              `
            : html`
                <a href="/login">Sign in</a>
                <a href="/register">Register</a>
              `}
          <button class="theme-toggle" @click=${() => themeManager.toggle()} title=${`Theme: ${this._theme}`}>
            ${this._theme === 'light' ? '\u2600\uFE0F' : this._theme === 'dark' ? '\uD83C\uDF19' : '\uD83D\uDCBB'}
          </button>
        </div>
      </nav>
    `;
  }
}
