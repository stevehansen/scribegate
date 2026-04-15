import { LitElement, html, css } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { authState } from '../../state/auth-state.js';
import { ApiException } from '../../api/client.js';
import { boxReset } from '../../styles/shared.js';

@customElement('sg-register-page')
export class SgRegisterPage extends LitElement {
  static styles = [boxReset, css`
    :host {
      display: block;
      max-width: 24rem;
      margin: 4rem auto;
      padding: 2rem;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius-lg);
      background: var(--sg-bg-elevated);
      box-shadow: var(--sg-shadow-md);
      position: relative;
      overflow: hidden;
    }
    :host::before {
      content: '';
      position: absolute;
      top: 0;
      left: 0;
      right: 0;
      height: 3px;
      background: var(--sg-accent-gradient);
    }
    h1 { font-size: var(--sg-font-size-2xl); margin-bottom: 1.5rem; color: var(--sg-text); }
    form { display: flex; flex-direction: column; gap: 1rem; }
    label { font-size: var(--sg-font-size-sm); font-weight: 500; display: flex; flex-direction: column; gap: 0.25rem; color: var(--sg-text); }
    input {
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius);
      font-size: var(--sg-font-size-sm);
      background: var(--sg-bg);
      color: var(--sg-text);
      transition: border-color var(--sg-transition-fast);
    }
    input:focus { outline: 2px solid var(--sg-primary); outline-offset: -1px; border-color: var(--sg-primary); }
    .hint { font-size: var(--sg-font-size-xs); color: var(--sg-text-secondary); font-weight: 400; }
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
    .tos-label {
      flex-direction: row;
      align-items: center;
      gap: 0.5rem;
      font-weight: 400;
    }
    .tos-label input[type="checkbox"] { width: auto; margin: 0; }
    .link { text-align: center; font-size: var(--sg-font-size-sm); color: var(--sg-text-secondary); }
    .link a { color: var(--sg-primary); text-decoration: none; }
    .link a:hover { text-decoration: underline; }
  `];

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
        !!data.get('acceptTos'),
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
        <label class="tos-label">
          <input type="checkbox" name="acceptTos" value="true" required />
          I accept the Terms of Service
        </label>
        <button type="submit" ?disabled=${this._loading}>
          ${this._loading ? 'Creating account...' : 'Create account'}
        </button>
      </form>
      <p class="link">Already have an account? <a href="/login">Sign in</a></p>
    `;
  }
}
