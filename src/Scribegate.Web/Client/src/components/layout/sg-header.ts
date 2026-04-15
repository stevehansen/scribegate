import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';

@customElement('sg-header')
export class SgHeader extends LitElement {
  static styles = css`
    :host {
      display: block;
      border-bottom: 1px solid #dee2e6;
      background: #fff;
    }
    nav {
      display: flex;
      align-items: center;
      justify-content: space-between;
      max-width: 72rem;
      margin: 0 auto;
      padding: 0 1.5rem;
      height: 3.5rem;
    }
    .logo {
      font-size: 1.125rem;
      font-weight: 700;
      color: #212529;
    }
    .logo:hover { opacity: 0.8; }
    .actions {
      display: flex;
      align-items: center;
      gap: 1rem;
    }
    a {
      color: #6c757d;
      text-decoration: none;
      font-size: 0.875rem;
    }
    a:hover { color: #212529; }
    .user-info {
      font-size: 0.875rem;
      color: #212529;
      font-weight: 500;
    }
    button {
      background: none;
      border: 1px solid #dee2e6;
      border-radius: 6px;
      padding: 0.375rem 0.75rem;
      font-size: 0.875rem;
      cursor: pointer;
      color: #6c757d;
    }
    button:hover {
      background: #f8f9fa;
      color: #212529;
    }
  `;

  @state() private _isAuth = false;
  @state() private _username = '';
  @state() private _isAdmin = false;

  private _unsub?: () => void;

  async connectedCallback() {
    super.connectedCallback();
    this._unsub = authState.subscribe(() => this._sync());

    // If we have a token but no user data yet, load it directly
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
        <a class="logo" href="/">Scribegate</a>
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
        </div>
      </nav>
    `;
  }
}
