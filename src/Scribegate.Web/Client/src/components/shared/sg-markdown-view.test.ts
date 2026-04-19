// Colocated component test for sg-markdown-view.
//
// Exercises the pure helper that rewrites relative <img src> values so
// `![diagram](diagram.png)` resolves through the media-by-name endpoint.
// The Lit component itself is harder to render under jsdom (it relies on
// Shadow DOM + dynamic imports of Prism/Mermaid) so the unit here targets
// the exported helper that carries the security-relevant logic.

import { describe, it, expect } from 'vitest';
import { resolveRelativeDocumentHref, resolveRelativeMediaSrc } from './sg-markdown-view.js';

describe('resolveRelativeDocumentHref', () => {
  it('rewrites top-level README links against the repository root', () => {
    const out = resolveRelativeDocumentHref('diagrams.md', 'alice', 'handbook', 'README.md');
    expect(out).toBe('/alice/handbook/diagrams.md');
  });

  it('rewrites nested sibling links against the document directory', () => {
    const out = resolveRelativeDocumentHref('./install.md', 'alice', 'handbook', 'guides/setup.md');
    expect(out).toBe('/alice/handbook/guides/install.md');
  });

  it('normalizes parent-directory traversal and preserves search/hash', () => {
    const out = resolveRelativeDocumentHref('../README.md?view=full#top', 'alice', 'handbook', 'guides/setup/intro.md');
    expect(out).toBe('/alice/handbook/guides/README.md?view=full#top');
  });

  it('leaves anchors, absolute paths, and scheme-qualified links alone', () => {
    expect(resolveRelativeDocumentHref('#section', 'a', 'b', 'README.md')).toBeNull();
    expect(resolveRelativeDocumentHref('/docs/intro.md', 'a', 'b', 'README.md')).toBeNull();
    expect(resolveRelativeDocumentHref('https://example.com/docs', 'a', 'b', 'README.md')).toBeNull();
    expect(resolveRelativeDocumentHref('mailto:test@example.com', 'a', 'b', 'README.md')).toBeNull();
  });
});

describe('resolveRelativeMediaSrc', () => {
  it('rewrites a bare filename to the media-by-name endpoint', () => {
    const out = resolveRelativeMediaSrc('diagram.png', 'alice', 'docs');
    expect(out).toBe('/api/v1/repositories/alice/docs/media/by-name/diagram.png');
  });

  it('rewrites a ./-prefixed filename', () => {
    const out = resolveRelativeMediaSrc('./diagram.png', 'alice', 'docs');
    expect(out).toBe('/api/v1/repositories/alice/docs/media/by-name/diagram.png');
  });

  it('leaves absolute http(s) URLs alone', () => {
    expect(resolveRelativeMediaSrc('https://example.com/x.png', 'a', 'b')).toBeNull();
    expect(resolveRelativeMediaSrc('http://example.com/x.png', 'a', 'b')).toBeNull();
  });

  it('leaves absolute-path references alone', () => {
    expect(resolveRelativeMediaSrc('/x.png', 'a', 'b')).toBeNull();
    expect(resolveRelativeMediaSrc('//cdn/x.png', 'a', 'b')).toBeNull();
  });

  it('ignores data: and blob: URIs', () => {
    expect(resolveRelativeMediaSrc('data:image/png;base64,xyz', 'a', 'b')).toBeNull();
    expect(resolveRelativeMediaSrc('blob:http://x/y', 'a', 'b')).toBeNull();
  });

  it('ignores filenames with path separators (avoid directory traversal)', () => {
    expect(resolveRelativeMediaSrc('foo/bar.png', 'a', 'b')).toBeNull();
    expect(resolveRelativeMediaSrc('..', 'a', 'b')).toBeNull();
  });

  it('encodes weird characters in the filename', () => {
    const out = resolveRelativeMediaSrc('a b.png', 'alice', 'docs');
    expect(out).toBe('/api/v1/repositories/alice/docs/media/by-name/a%20b.png');
  });
});
