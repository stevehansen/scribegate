import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';

@customElement('sg-login-page')
export class SgLoginPage extends LitElement {
  static styles = css`
    :host { display: block; max-width: 24rem; margin: 3rem auto; }
    h1 { font-size: 1.5rem; margin-bottom: 1.5rem; }
    form { display: flex; flex-direction: column; gap: 1rem; }
    label { font-size: 0.875rem; font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; }
    input {
      padding: 0.5rem 0.75rem;
      border: 1px solid #dee2e6;
      border-radius: 6px;
      font-size: 0.875rem;
    }
    input:focus { outline: 2px solid #2563eb; outline-offset: -1px; border-color: #2563eb; }
    button[type="submit"] {
      background: #2563eb;
      color: #fff;
      border: none;
      border-radius: 6px;
      padding: 0.625rem 1rem;
      font-size: 0.875rem;
      font-weight: 500;
      cursor: pointer;
    }
    button[type="submit"]:hover { background: #1d4ed8; }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .error {
      background: #fef2f2;
      border: 1px solid #fecaca;
      color: #dc2626;
      padding: 0.75rem;
      border-radius: 6px;
      font-size: 0.875rem;
    }
    .link { text-align: center; font-size: 0.875rem; color: #6c757d; }
    .link a { color: #2563eb; text-decoration: none; }
    .link a:hover { text-decoration: underline; }
  `;

  @state() private _error = '';
  @state() private _loading = false;

  private async _onSubmit(e: Event) {
    e.preventDefault();
    const form = e.target as HTMLFormElement;
    const data = new FormData(form);
    const email = data.get('email') as string;
    const password = data.get('password') as string;

    this._error = '';
    this._loading = true;

    try {
      await authState.login(email, password);
      window.location.href = '/';
    } catch (err) {
      this._error = err instanceof ApiException ? err.error.message : 'Login failed.';
    } finally {
      this._loading = false;
    }
  }

  render() {
    return html`
      <h1>Sign in</h1>
      ${this._error ? html`<div class="error">${this._error}</div>` : ''}
      <form @submit=${this._onSubmit}>
        <label>Email <input type="email" name="email" required autocomplete="email" /></label>
        <label>Password <input type="password" name="password" required autocomplete="current-password" /></label>
        <button type="submit" ?disabled=${this._loading}>
          ${this._loading ? 'Signing in...' : 'Sign in'}
        </button>
      </form>
      <p class="link">Don't have an account? <a href="/register">Register</a></p>
    `;
  }
}
