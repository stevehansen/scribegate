import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { unsafeHTML } from 'lit/directives/unsafe-html.js';
import { marked } from 'marked';
import DOMPurify from 'dompurify';

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
  ],
  ALLOWED_ATTR: [
    'href', 'title', 'alt', 'src',
    'class', 'id',
    'type', 'checked', 'disabled', // task list checkboxes
    'align', 'colspan', 'rowspan', // tables
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
  static styles = css`
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
  `;

  @property() content = '';

  render() {
    const rendered = renderMarkdown(this.content);
    return html`${unsafeHTML(rendered)}`;
  }
}
