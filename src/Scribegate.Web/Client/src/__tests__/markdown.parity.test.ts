// Markdown parity — SPA side.
//
// Mirrors tests/Scribegate.Web.Tests/Markdown/ParityTheoryTests.cs against
// the client's marked + DOMPurify pipeline. Snapshots the rendered HTML
// to tests/fixtures/markdown/marked-golden/{id}.html.
//
// First run: golden is created from the actual output.
// Subsequent runs: byte-for-byte equality is asserted.
//
// Cross-side divergence (Markdig vs marked) is tracked as a TODO and
// documented in docs/markdown.md.

import { describe, it, expect } from 'vitest';
import { readFileSync, existsSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

// Align with the client's sg-markdown-view pipeline.
marked.use({ breaks: true });

const PURIFY_CONFIG = {
  ALLOWED_TAGS: [
    'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
    'p', 'br', 'hr',
    'ul', 'ol', 'li',
    'blockquote', 'pre', 'code',
    'table', 'thead', 'tbody', 'tr', 'th', 'td',
    'a', 'strong', 'em', 'del', 's', 'sub', 'sup',
    'img',
    'input',
    'div', 'span',
  ],
  ALLOWED_ATTR: [
    'href', 'title', 'alt', 'src',
    'class', 'id',
    'type', 'checked', 'disabled',
    'align', 'colspan', 'rowspan',
  ],
  ALLOW_DATA_ATTR: false,
  ADD_ATTR: ['target', 'rel'],
};

interface Corpus {
  id: string;
  description: string;
  markdown: string;
}

// The corpus ships at the repo root, shared with the .NET Markdig parity test.
// Resolve relative to this test file so `vitest` works from any cwd.
function repoRoot(): string {
  // Walk up from this file until we find the package.json -> repo root is
  // three levels above src/Scribegate.Web/Client.
  return resolve(__dirname, '..', '..', '..', '..', '..');
}

const FIXTURES = resolve(repoRoot(), 'tests', 'fixtures', 'markdown');

const corpus: Corpus[] = JSON.parse(
  readFileSync(resolve(FIXTURES, 'corpus.json'), 'utf8')
);

describe('markdown parity — marked pipeline', () => {
  for (const testCase of corpus) {
    it(`${testCase.id}: ${testCase.description}`, () => {
      const raw = marked.parse(testCase.markdown, { async: false }) as string;
      const clean = DOMPurify.sanitize(raw, PURIFY_CONFIG);

      const goldenPath = resolve(FIXTURES, 'marked-golden', `${testCase.id}.html`);
      mkdirSync(dirname(goldenPath), { recursive: true });

      if (!existsSync(goldenPath)) {
        writeFileSync(goldenPath, clean, 'utf8');
        // First-run seeding; local only. CI runs with goldens committed.
        return;
      }

      const expected = readFileSync(goldenPath, 'utf8');
      expect(clean).toBe(expected);
    });
  }

  // TODO: cross-compare Markdig and marked outputs once the corpus grows.
  // The current Markdig pipeline adds GFM footnotes + figures; marked does
  // not. See docs/markdown.md for the known divergence table.
  it.skip('Markdig and marked agree across corpus (TODO)', () => {
    // intentionally empty — tracked in docs/markdown.md.
  });
});
