import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';

@customElement('sg-login-page')
export class SgLoginPage extends LitElement {
  static styles = css`
    :host { display: block; max-width: 24rem; margin: 3rem auto; }
    h1 { font-size: var(--sg-font-size-2xl); margin-bottom: 1.5rem; color: var(--sg-text); }
    form { display: flex; flex-direction: column; gap: 1rem; }
    label { font-size: var(--sg-font-size-sm); font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; color: var(--sg-text); }
    input {
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm);
      background: var(--sg-bg-elevated);
      color: var(--sg-text);
      transition: border-color var(--sg-transition-fast);
    }
    input:focus { outline: 2px solid var(--sg-primary); outline-offset: -1px; border-color: var(--sg-primary); }
    button[type="submit"] {
      background: var(--sg-primary);
      color: var(--sg-primary-text);
      border: none;
      border-radius: var(--sg-radius);
      padding: 0.625rem 1rem;
      font-size: var(--sg-font-size-sm);
      font-weight: 500;
      cursor: pointer;
      transition: background var(--sg-transition-fast);
    }
    button[type="submit"]:hover { background: var(--sg-primary-hover); }
    button:disabled { opacity: 0.6; cursor: not-allowed; }
    .error {
      background: var(--sg-danger-light);
      border: 1px solid var(--sg-danger-border);
      color: var(--sg-danger);
      padding: 0.75rem;
      border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm);
    }
    .link { text-align: center; font-size: var(--sg-font-size-sm); color: var(--sg-text-secondary); }
    .link a { color: var(--sg-primary); text-decoration: none; }
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
