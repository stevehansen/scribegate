import type { UserInfo } from '../api/types.js';
import * as authApi from '../api/auth.js';
import { themeManager } from './theme.js';

type AuthListener = () => void;

class AuthState {
  private _user: UserInfo | null = null;
  private _listeners: Set<AuthListener> = new Set();

  get isAuthenticated(): boolean {
    return !!this.token;
  }

  get token(): string | null {
    return localStorage.getItem('sg_token');
  }

  get user(): UserInfo | null {
    return this._user;
  }

  subscribe(listener: AuthListener): () => void {
    this._listeners.add(listener);
    return () => this._listeners.delete(listener);
  }

  private notify() {
    this._listeners.forEach((fn) => fn());
  }

  async login(email: string, password: string) {
    const response = await authApi.login(email, password);
    localStorage.setItem('sg_token', response.token);
    this._user = response.user;
    this.notify();
    return response;
  }

  async register(username: string, email: string, password: string) {
    const response = await authApi.register(username, email, password);
    localStorage.setItem('sg_token', response.token);
    this._user = response.user;
    this.notify();
    return response;
  }

  logout() {
    localStorage.removeItem('sg_token');
    this._user = null;
    this.notify();
    window.location.href = '/';
  }

  async loadUser() {
    if (!this.token) return;
    try {
      this._user = await authApi.getMe();
      // Sync theme preference from server
      if (this._user.themePreference) {
        themeManager.setFromServer(this._user.themePreference);
      }
      this.notify();
    } catch {
      localStorage.removeItem('sg_token');
      this._user = null;
      this.notify();
    }
  }
}

export const authState = new AuthState();
