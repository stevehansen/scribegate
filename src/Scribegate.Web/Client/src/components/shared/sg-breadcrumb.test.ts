// Colocated component test for sg-breadcrumb.
//
// The breadcrumb is pure render-from-properties: no API calls, no
// timers, no shared state. The contract worth pinning at this layer
// is the path-segment walk:
//
//   - "Repositories" + repo link always render, even when path is empty.
//   - Non-empty path becomes a chain of segment links to the cumulative
//     subpath, with the final segment rendered as plain text (no link)
//     and tagged `.current`.
//   - When `repoName` is empty the repo link falls back to `owner/slug`.
//   - Leading / trailing / duplicated separators are filtered out so a
//     `path` like "guides//setup/" doesn't produce empty links.

import { describe, it, expect, afterEach } from 'vitest';
import './sg-breadcrumb.js';
import type { SgBreadcrumb } from './sg-breadcrumb.js';

async function mount(props: Partial<SgBreadcrumb> = {}): Promise<SgBreadcrumb> {
  const el = document.createElement('sg-breadcrumb') as SgBreadcrumb;
  Object.assign(el, props);
  document.body.appendChild(el);
  await el.updateComplete;
  return el;
}

afterEach(() => {
  document.body.querySelectorAll('sg-breadcrumb').forEach((n) => n.remove());
});

describe('sg-breadcrumb', () => {
  it('renders root and repo links when the path is empty', async () => {
    const el = await mount({
      repoOwner: 'alice',
      repoSlug: 'handbook',
      repoName: 'Handbook',
      path: '',
    });

    const root = el.shadowRoot!;
    const links = Array.from(root.querySelectorAll('a'));

    expect(links).toHaveLength(2);
    expect(links[0].getAttribute('href')).toBe('/');
    expect(links[0].textContent).toBe('Repositories');
    expect(links[1].getAttribute('href')).toBe('/alice/handbook');
    expect(links[1].textContent).toBe('alice/Handbook');

    expect(root.querySelector('.current')).toBeNull();
  });

  it('renders intermediate segments as cumulative links and the last as .current', async () => {
    const el = await mount({
      repoOwner: 'alice',
      repoSlug: 'handbook',
      repoName: 'Handbook',
      path: 'guides/setup/intro.md',
    });

    const root = el.shadowRoot!;
    const links = Array.from(root.querySelectorAll('a'));

    // root + repo + 2 intermediate segments = 4 links (the last one is .current, not a link)
    expect(links).toHaveLength(4);
    expect(links[2].getAttribute('href')).toBe('/alice/handbook/guides');
    expect(links[2].textContent).toBe('guides');
    expect(links[3].getAttribute('href')).toBe('/alice/handbook/guides/setup');
    expect(links[3].textContent).toBe('setup');

    const current = root.querySelector('.current');
    expect(current).not.toBeNull();
    expect(current!.textContent).toBe('intro.md');
  });

  it('falls back to owner/slug when repoName is empty', async () => {
    const el = await mount({
      repoOwner: 'alice',
      repoSlug: 'handbook',
      repoName: '',
      path: '',
    });

    const repoLink = el.shadowRoot!.querySelectorAll('a')[1];
    expect(repoLink.textContent).toBe('alice/handbook');
  });

  it('filters empty segments from leading, trailing, and duplicated separators', async () => {
    const el = await mount({
      repoOwner: 'alice',
      repoSlug: 'handbook',
      repoName: 'Handbook',
      path: '/guides//setup/',
    });

    const root = el.shadowRoot!;
    const links = Array.from(root.querySelectorAll('a'));

    expect(links.map((a) => a.textContent)).toEqual([
      'Repositories',
      'alice/Handbook',
      'guides',
    ]);

    const current = root.querySelector('.current');
    expect(current!.textContent).toBe('setup');
  });
});
