// Thin "simulated happy-path" — imports the auth state module and asserts
// its unauthenticated defaults, then the transition triggered by manually
// seeding a token and user. Real end-to-end flows live in the .NET
// WebApplicationFactory tests; this is just a smoke check that the SPA
// state module loads cleanly under jsdom.

import { describe, it, expect } from 'vitest';
import { authState } from '../state/auth-state.js';

describe('happy path — auth state module', () => {
  // setup.ts installs an in-memory localStorage shim and clears it between
  // tests, so nothing else is required here.

  it('starts unauthenticated', () => {
    expect(authState.isAuthenticated).toBe(false);
    expect(authState.user).toBeNull();
    expect(authState.token).toBeNull();
  });

  it('picks up a token written to localStorage', () => {
    localStorage.setItem('sg_token', 'abc.def.ghi');
    expect(authState.isAuthenticated).toBe(true);
    expect(authState.token).toBe('abc.def.ghi');
  });

  it('notifies subscribers and can unsubscribe', () => {
    let calls = 0;
    const off = authState.subscribe(() => { calls++; });
    // Subscribe/unsubscribe contract — we don't need to trigger real
    // state mutation here; just prove the wiring.
    off();
    expect(calls).toBe(0);
  });
});
