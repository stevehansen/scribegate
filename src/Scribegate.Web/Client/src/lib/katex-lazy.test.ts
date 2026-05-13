// Colocated tests for the lazy KaTeX loader.
//
// The point of this module is keeping ~270 KB gzip out of the main SPA
// chunk on math-free pages. These tests pin the `hasMath` predicate that
// decides whether a render is allowed to pull KaTeX in, plus the
// idempotency of `ensureKatexRegistered` (callers must be able to fire it
// once per content change without re-triggering the dynamic import).

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { hasMath, ensureKatexRegistered, _resetKatexRegistrationForTests } from './katex-lazy.js';

describe('hasMath', () => {
  it('detects inline `$…$` math', () => {
    expect(hasMath('The value is $x = 1$ here.')).toBe(true);
  });

  it('detects display `$$…$$` math, including across lines', () => {
    expect(hasMath('Block:\n$$\n\\sqrt{2}\n$$\nDone.')).toBe(true);
  });

  it('returns false for prose without math', () => {
    expect(hasMath('Plain paragraph.\n\nA second one.')).toBe(false);
    expect(hasMath('# Heading\n\n- list item')).toBe(false);
  });

  it('does not mistake bare dollar signs in prose for inline math', () => {
    // A single `$5` should not look like a math expression.
    expect(hasMath('Costs about $5 on average.')).toBe(false);
  });

  it('returns false for empty input', () => {
    expect(hasMath('')).toBe(false);
  });
});

describe('ensureKatexRegistered', () => {
  beforeEach(() => {
    _resetKatexRegistrationForTests();
    document.getElementById('sg-katex-styles')?.remove();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('resolves once and is idempotent across repeated calls', async () => {
    const first = ensureKatexRegistered();
    const second = ensureKatexRegistered();
    // The in-flight import is shared, so both calls observe the same promise
    // identity. After it settles, subsequent calls return a fresh resolved
    // promise without touching the module graph again.
    expect(first).toBe(second);

    await first;
    const third = ensureKatexRegistered();
    expect(third).not.toBe(first);
    await third;
  });

  it('injects a single KaTeX <style> element into <head> on first load', async () => {
    await ensureKatexRegistered();
    const styles = document.head.querySelectorAll('style#sg-katex-styles');
    expect(styles).toHaveLength(1);

    await ensureKatexRegistered();
    expect(document.head.querySelectorAll('style#sg-katex-styles')).toHaveLength(1);
  });
});
