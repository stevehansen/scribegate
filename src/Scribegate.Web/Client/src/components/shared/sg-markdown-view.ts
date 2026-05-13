import { LitElement, html, css, unsafeCSS } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { unsafeHTML } from 'lit/directives/unsafe-html.js';
import { marked } from 'marked';
import DOMPurify from 'dompurify';
import katexStyles from 'katex/dist/katex.min.css?inline';
import { boxReset } from '../../styles/shared.js';
import { highlightAllUnder } from '../../lib/highlight.js';
import { renderMermaidBlocks } from '../../lib/mermaid.js';
import '../../lib/markdown-extensions.js';

// marked v15 has GFM enabled by default (tables, strikethrough, task lists, autolinks).
// Configure sanitization for safe rendering.
marked.use({
  breaks: true, // GFM line breaks
});

// DOMPurify config: allow standard HTML from markdown, strip dangerous tags/attrs
const PURIFY_CONFIG = {
  ALLOWED_TAGS: [
    'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
    'p', 'br', 'hr',
    'ul', 'ol', 'li',
    'blockquote', 'pre', 'code',
    'table', 'thead', 'tbody', 'tr', 'th', 'td',
    'a', 'strong', 'em', 'del', 's', 'sub', 'sup',
    'img',
    'input', // for task list checkboxes
    'div', 'span',
    'dl', 'dt', 'dd', // definition lists
    'section', // footnotes wrapper
    // KaTeX MathML accessibility tree (rendered alongside the visual HTML spans)
    'math', 'semantics', 'mrow', 'mi', 'mo', 'mn', 'ms', 'mtext', 'mspace',
    'mfrac', 'msqrt', 'mroot', 'msub', 'msup', 'msubsup', 'mover', 'munder',
    'munderover', 'mtable', 'mtr', 'mtd', 'mstyle', 'mpadded', 'mphantom',
    'annotation',
  ],
  ALLOWED_ATTR: [
    'href', 'title', 'alt', 'src',
    'class', 'id',
    'type', 'checked', 'disabled', // task list checkboxes
    'align', 'colspan', 'rowspan', // tables
    'style', // required by KaTeX for positioning glyphs (DOMPurify sanitizes CSS values)
    'aria-hidden', 'aria-label', 'role', // accessibility
    'data-footnote-ref', 'data-footnote-backref', // marked-footnote hooks
    // MathML
    'xmlns', 'encoding', 'display', 'mathvariant', 'stretchy', 'fence', 'separator', 'accent',
  ],
  ALLOW_DATA_ATTR: false,
  // Force all links to open safely
  ADD_ATTR: ['target', 'rel'],
};

function renderMarkdown(content: string): string {
  const raw = marked.parse(content, { async: false }) as string;
  const clean = DOMPurify.sanitize(raw, PURIFY_CONFIG);
  return clean;
}

// Post-process: make external links safe
DOMPurify.addHook('afterSanitizeAttributes', (node) => {
  if (node.tagName === 'A') {
    const href = node.getAttribute('href') ?? '';
    if (href.startsWith('http://') || href.startsWith('https://')) {
      node.setAttribute('target', '_blank');
      node.setAttribute('rel', 'noopener noreferrer');
    }
  }
  // Ensure task list checkboxes are disabled
  if (node.tagName === 'INPUT' && node.getAttribute('type') === 'checkbox') {
    node.setAttribute('disabled', '');
  }
});

@customElement('sg-markdown-view')
export class SgMarkdownView extends LitElement {
  static styles = [boxReset, unsafeCSS(katexStyles), css`
    :host { display: block; line-height: 1.7; color: var(--sg-text); }
    h1, h2, h3, h4, h5, h6 { margin-top: 1.5em; margin-bottom: 0.5em; line-height: 1.3; color: var(--sg-text); }
    h1 { font-size: 1.875rem; border-bottom: 1px solid var(--sg-border); padding-bottom: 0.375rem; }
    h2 { font-size: 1.5rem; border-bottom: 1px solid var(--sg-border); padding-bottom: 0.25rem; }
    h3 { font-size: 1.25rem; }
    p { margin: 0.75em 0; }
    a { color: var(--sg-primary); text-decoration: none; }
    a:hover { text-decoration: underline; }
    code {
      font-family: var(--sg-font-mono);
      background: var(--sg-code-bg);
      padding: 0.125em 0.375em;
      border-radius: 4px;
      font-size: 0.875em;
    }
    pre {
      background: var(--sg-pre-bg);
      border: 1px solid var(--sg-border);
      border-radius: var(--sg-radius);
      padding: 1rem;
      overflow-x: auto;
      margin: 1em 0;
    }
    pre code { background: none; padding: 0; border-radius: 0; }
    /* Prism token colours — consume CSS vars so the palette tracks the app theme */
    .token.comment, .token.prolog, .token.cdata { color: var(--sg-syn-comment); font-style: italic; }
    .token.punctuation { color: var(--sg-syn-punct); }
    .token.tag, .token.selector, .token.attr-name, .token.builtin { color: var(--sg-syn-tag); }
    .token.string, .token.char, .token.attr-value, .token.regex { color: var(--sg-syn-string); }
    .token.keyword, .token.atrule, .token.important, .token.rule, .token.boolean, .token.operator { color: var(--sg-syn-keyword); }
    .token.number, .token.hexcode, .token.unit { color: var(--sg-syn-number); }
    .token.function, .token.class-name { color: var(--sg-syn-function); }
    .token.variable, .token.symbol, .token.property, .token.constant, .token.entity { color: var(--sg-syn-variable); }
    .token.deleted { color: var(--sg-danger); }
    .token.inserted { color: var(--sg-success); }
    .token.italic { font-style: italic; }
    .token.bold { font-weight: 600; }
    .sg-mermaid {
      margin: 1em 0;
      display: flex;
      justify-content: center;
      overflow-x: auto;
    }
    .sg-mermaid svg { max-width: 100%; height: auto; }
    .sg-mermaid-error {
      margin: 0.5em 0 1em;
      padding: 0.5em 0.75em;
      border: 1px solid var(--sg-danger-border);
      background: var(--sg-danger-light);
      color: var(--sg-danger);
      border-radius: var(--sg-radius);
      font-family: var(--sg-font-mono);
      font-size: 0.875em;
    }
    blockquote {
      border-left: 4px solid var(--sg-border);
      padding-left: 1rem;
      color: var(--sg-text-secondary);
      margin: 1em 0;
    }
    ul, ol { padding-left: 1.5em; margin: 0.75em 0; }
    li { margin: 0.25em 0; }
    /* Task list items (GFM) */
    li:has(> input[type="checkbox"]) { list-style: none; margin-left: -1.25em; }
    input[type="checkbox"] { margin-right: 0.375em; vertical-align: middle; }
    table { border-collapse: collapse; width: 100%; margin: 1em 0; }
    th, td { border: 1px solid var(--sg-border); padding: 0.5rem 0.75rem; text-align: left; }
    th { background: var(--sg-bg-secondary); font-weight: 600; }
    hr { border: none; border-top: 1px solid var(--sg-border); margin: 1.5em 0; }
    img { max-width: 100%; border-radius: var(--sg-radius); }
    del { text-decoration: line-through; color: var(--sg-text-secondary); }
    dl { margin: 0.75em 0; }
    dt { font-weight: 600; margin-top: 0.75em; }
    dd { margin: 0.25em 0 0.5em 1.5em; }
    .footnotes {
      margin-top: 2em;
      padding-top: 0.75em;
      border-top: 1px solid var(--sg-border);
      font-size: 0.875em;
      color: var(--sg-text-secondary);
    }
    .footnotes ol { padding-left: 1.25em; }
    sup.footnote-ref { margin: 0 0.1em; }
    a[data-footnote-backref] { margin-left: 0.25em; text-decoration: none; }
  `];

  @property() content = '';
  // When both are set, relative <img src> values in the rendered markdown
  // are resolved against the repository's media-by-name endpoint.
  @property() owner = '';
  @property() slug = '';
  // When set alongside owner/slug, relative <a href> values are rewritten as
  // if the markdown were opened at its canonical document route.
  @property() documentPath = '';

  render() {
    const rendered = renderMarkdown(this.content);
    return html`${unsafeHTML(rendered)}`;
  }

  async updated() {
    // 1) Rewrite relative <a href> values against the document's canonical
    //    repo path so inline README renders behave like full document views.
    // 2) Rewrite relative <img> src values to the media endpoint so
    //    `![diagram](foo.png)` resolves against the repo's MediaAssets.
    // 3) Upgrade <img src="*.{mp4,webm,ogg,mov}"> to <video controls> so the
    //    SPA matches Markdig's server-side UseMediaLinks behaviour.
    // 4) Render Mermaid diagrams (replaces <pre> blocks).
    // 5) Highlight remaining fenced code blocks via Prism.
    // All passes are safe no-ops when there's nothing to do.
    this._resolveDocumentReferences();
    this._resolveMediaReferences();
    this._upgradeVideoImages();
    await renderMermaidBlocks(this.renderRoot);
    highlightAllUnder(this.renderRoot);
  }

  private _resolveDocumentReferences() {
    if (!this.owner || !this.slug || !this.documentPath) return;
    const links = this.renderRoot.querySelectorAll<HTMLAnchorElement>('a[href]');
    for (const link of Array.from(links)) {
      const href = link.getAttribute('href') ?? '';
      const resolved = resolveRelativeDocumentHref(href, this.owner, this.slug, this.documentPath);
      if (resolved && resolved !== href) {
        link.setAttribute('href', resolved);
        link.removeAttribute('target');
        link.removeAttribute('rel');
      }
    }
  }

  private _resolveMediaReferences() {
    if (!this.owner || !this.slug) return;
    const imgs = this.renderRoot.querySelectorAll<HTMLImageElement>('img');
    for (const img of Array.from(imgs)) {
      const src = img.getAttribute('src') ?? '';
      const resolved = resolveRelativeMediaSrc(src, this.owner, this.slug);
      if (resolved && resolved !== src) img.setAttribute('src', resolved);
    }
  }

  private _upgradeVideoImages() {
    const imgs = this.renderRoot.querySelectorAll<HTMLImageElement>('img');
    for (const img of Array.from(imgs)) {
      const src = img.getAttribute('src') ?? '';
      if (!isVideoSrc(src)) continue;
      const video = document.createElement('video');
      video.setAttribute('src', src);
      video.setAttribute('controls', '');
      video.setAttribute('preload', 'metadata');
      const alt = img.getAttribute('alt');
      if (alt) video.setAttribute('aria-label', alt);
      img.replaceWith(video);
    }
  }
}

// Matches the file extensions Markdig's UseMediaLinks treats as <video> sources.
// Query string and fragment are ignored so cache-busting parameters don't break
// the upgrade.
const VIDEO_EXT_RE = /\.(mp4|webm|ogg|mov)(?:[?#]|$)/i;

export function isVideoSrc(src: string): boolean {
  if (!src) return false;
  if (src.startsWith('data:') || src.startsWith('blob:')) return false;
  return VIDEO_EXT_RE.test(src);
}

// Returns a rewritten absolute repo route when `href` is a relative link that
// should behave as if the markdown were opened at `documentPath`. Returns null
// for hash-only, absolute-path, or scheme-qualified links that must pass
// through unchanged.
export function resolveRelativeDocumentHref(
  href: string,
  owner: string,
  slug: string,
  documentPath: string,
): string | null {
  if (!href || !owner || !slug || !documentPath) return null;
  if (href.startsWith('#') || href.startsWith('?')) return null;
  if (href.startsWith('//') || href.startsWith('/')) return null;
  if (/^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(href)) return null;

  try {
    const basePath = documentPath.startsWith('/') ? documentPath.slice(1) : documentPath;
    const baseUrl = new URL(basePath, 'https://scribegate.invalid/');
    const resolved = new URL(href, baseUrl);
    return `/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}${resolved.pathname}${resolved.search}${resolved.hash}`;
  } catch {
    return null;
  }
}

// Returns a rewritten absolute URL when `src` looks like a bare or `./`-prefixed
// filename that should resolve to a MediaAsset in the repository. Returns null
// for anything that must pass through unchanged (absolute URL paths, scheme-
// qualified URLs, data/blob URIs, or anything containing a path separator).
export function resolveRelativeMediaSrc(src: string, owner: string, slug: string): string | null {
  if (!src) return null;
  if (src.startsWith('http://') || src.startsWith('https://')) return null;
  if (src.startsWith('//') || src.startsWith('/')) return null;
  if (src.startsWith('data:') || src.startsWith('blob:') || src.startsWith('mailto:')) return null;

  const trimmed = src.startsWith('./') ? src.slice(2) : src;
  if (trimmed.includes('/') || trimmed.includes('\\') || trimmed === '..' || trimmed === '.') return null;
  if (!trimmed) return null;

  return `/api/v1/repositories/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}/media/by-name/${encodeURIComponent(trimmed)}`;
}
