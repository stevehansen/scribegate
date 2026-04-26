// Colocated component test for sg-time-ago.
//
// Two contracts that aren't visible from the helper exports (the
// helpers are private — this is the only way to pin them):
//
//   1. Bucket boundaries: < 60s → "just now", < 1h → Nm, < 24h → Nh,
//      < 30d → Nd, otherwise the locale date.
//   2. UTC parsing: backend timestamps land here without a 'Z' suffix.
//      The component must treat them as UTC, not local time, or
//      relative ages drift by the local timezone offset (up to ±14h).
//
// vi.setSystemTime pins "now" so the relative-time math is
// deterministic across timezones / DST / leap seconds.

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import './sg-time-ago.js';
import type { SgTimeAgo } from './sg-time-ago.js';

const NOW = new Date('2026-04-26T12:00:00Z');

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW);
});

afterEach(() => {
  vi.useRealTimers();
  document.body.querySelectorAll('sg-time-ago').forEach((n) => n.remove());
});

async function mount(datetime: string): Promise<SgTimeAgo> {
  const el = document.createElement('sg-time-ago') as SgTimeAgo;
  el.datetime = datetime;
  document.body.appendChild(el);
  await el.updateComplete;
  return el;
}

function relativeText(el: SgTimeAgo): string {
  return el.shadowRoot!.querySelector('time')!.textContent ?? '';
}

describe('sg-time-ago — relative bucket boundaries', () => {
  it('renders "just now" for the sub-minute bucket', async () => {
    const el = await mount('2026-04-26T11:59:30Z');
    expect(relativeText(el)).toBe('just now');
  });

  it('rounds down to whole minutes inside the < 1h bucket', async () => {
    const el = await mount('2026-04-26T11:34:45Z');
    expect(relativeText(el)).toBe('25m ago');
  });

  it('rounds down to whole hours inside the < 24h bucket', async () => {
    const el = await mount('2026-04-25T18:30:00Z');
    expect(relativeText(el)).toBe('17h ago');
  });

  it('rounds down to whole days inside the < 30d bucket', async () => {
    const el = await mount('2026-04-19T12:00:00Z');
    expect(relativeText(el)).toBe('7d ago');
  });

  it('falls back to a locale date once it crosses the 30-day bucket', async () => {
    const el = await mount('2026-01-01T12:00:00Z');
    const text = relativeText(el);

    // The exact format is locale-specific; just assert it's NOT a
    // relative-bucket string and contains a 4-digit year so the
    // regression has teeth without coupling to the runtime locale.
    expect(text).not.toMatch(/(just now|m ago|h ago|d ago)/);
    expect(text).toMatch(/2026/);
  });
});

describe('sg-time-ago — UTC parsing', () => {
  it('parses a Z-less backend timestamp as UTC', async () => {
    // A naive `new Date(s)` would treat this as local time — for
    // anyone east of UTC that pushes the timestamp into the future
    // ("in 1h") and west pushes it well past the minute window.
    // The component normalizes by appending 'Z' before parsing.
    const el = await mount('2026-04-26T11:30:00');
    expect(relativeText(el)).toBe('30m ago');
  });

  it('preserves an explicit Z suffix without double-appending', async () => {
    const el = await mount('2026-04-26T11:30:00Z');
    expect(relativeText(el)).toBe('30m ago');
  });

  it('preserves a +HH:MM offset without forcing UTC', async () => {
    // 11:30 in +02:00 is 09:30Z, i.e. 2h30m before NOW.
    const el = await mount('2026-04-26T11:30:00+02:00');
    expect(relativeText(el)).toBe('2h ago');
  });
});

describe('sg-time-ago — DOM contract', () => {
  it('renders a <time> element echoing the original datetime attribute', async () => {
    const el = await mount('2026-04-26T11:30:00Z');
    const time = el.shadowRoot!.querySelector('time')!;

    expect(time.getAttribute('datetime')).toBe('2026-04-26T11:30:00Z');
    expect(time.getAttribute('title')).toBeTruthy();
  });

  it('renders nothing when datetime is empty', async () => {
    const el = await mount('');
    expect(el.shadowRoot!.querySelector('time')).toBeNull();
  });
});
