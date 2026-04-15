import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';

@customElement('sg-register-page')
export class SgRegisterPage extends LitElement {
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
    .hint { font-size: 0.75rem; color: #6c757d; font-weight: 400; }
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

    this._error = '';
    this._loading = true;

    try {
      await authState.register(
        data.get('username') as string,
        data.get('email') as string,
        data.get('password') as string,
      );
      window.location.href = '/';
    } catch (err) {
      if (err instanceof ApiException) {
        const fieldErrors = err.error.errors;
        this._error = fieldErrors
          ? fieldErrors.map((e) => e.message).join(' ')
          : err.error.message;
      } else {
        this._error = 'Registration failed.';
      }
    } finally {
      this._loading = false;
    }
  }

  render() {
    return html`
      <h1>Create account</h1>
      ${this._error ? html`<div class="error">${this._error}</div>` : ''}
      <form @submit=${this._onSubmit}>
        <label>Username <input type="text" name="username" required minlength="3" autocomplete="username" /></label>
        <label>Email <input type="email" name="email" required autocomplete="email" /></label>
        <label>
          Password
          <input type="password" name="password" required minlength="10" autocomplete="new-password" />
          <span class="hint">At least 10 characters. No complexity rules.</span>
        </label>
        <button type="submit" ?disabled=${this._loading}>
          ${this._loading ? 'Creating account...' : 'Create account'}
        </button>
      </form>
      <p class="link">Already have an account? <a href="/login">Sign in</a></p>
    `;
  }
}
