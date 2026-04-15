import * as authApi from '../api/auth.js';

const STORAGE_KEY = 'sg-theme';

export type Theme = 'light' | 'dark' | 'system';

class ThemeManager {
  private _listeners = new Set<() => void>();

  get current(): Theme {
    return (localStorage.getItem(STORAGE_KEY) as Theme) ?? 'system';
  }

  get resolved(): 'light' | 'dark' {
    const pref = this.current;
    if (pref === 'system') {
      return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }
    return pref;
  }

  init() {
    this._apply();
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => {
      if (this.current === 'system') {
        this._apply();
        this._notify();
      }
    });
  }

  /** Apply server-side preference (called after loading user) */
  setFromServer(pref: Theme) {
    const local = this.current;
    if (local !== pref) {
      localStorage.setItem(STORAGE_KEY, pref);
      this._apply();
      this._notify();
    }
  }

  /** Cycle: light -> dark -> system -> light */
  toggle() {
    const cur = this.current;
    let next: Theme;
    if (cur === 'light') next = 'dark';
    else if (cur === 'dark') next = 'system';
    else next = 'light';

    localStorage.setItem(STORAGE_KEY, next);
    this._apply();
    this._notify();

    // Persist to server if logged in (fire-and-forget)
    if (localStorage.getItem('sg_token')) {
      authApi.updatePreferences({ themePreference: next }).catch(() => {});
    }
  }

  subscribe(fn: () => void): () => void {
    this._listeners.add(fn);
    return () => this._listeners.delete(fn);
  }

  private _apply() {
    const resolved = this.resolved;
    if (resolved === 'dark') {
      document.documentElement.setAttribute('data-theme', 'dark');
    } else {
      document.documentElement.removeAttribute('data-theme');
    }
  }

  private _notify() {
    this._listeners.forEach(fn => fn());
  }
}

export const themeManager = new ThemeManager();
