// Vitest setup — runs once before every test file.
//
// Node 22+ ships an experimental file-backed localStorage on the global
// scope that prints a warning and has no working setItem/clear on
// vitest's worker processes. Force a simple in-memory shim so the SPA
// can round-trip tokens deterministically under jsdom.

import { beforeEach } from 'vitest';

class MemoryStorage implements Storage {
  private store = new Map<string, string>();
  get length(): number { return this.store.size; }
  key(i: number): string | null {
    return Array.from(this.store.keys())[i] ?? null;
  }
  getItem(k: string): string | null { return this.store.get(k) ?? null; }
  setItem(k: string, v: string): void { this.store.set(k, String(v)); }
  removeItem(k: string): void { this.store.delete(k); }
  clear(): void { this.store.clear(); }
}

// Override the global regardless of what Node or jsdom already put there.
Object.defineProperty(globalThis, 'localStorage', {
  value: new MemoryStorage(),
  configurable: true,
  writable: true,
});

beforeEach(() => {
  globalThis.localStorage.clear();
});
