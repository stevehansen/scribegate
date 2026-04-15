import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import { unsafeHTML } from 'lit/directives/unsafe-html.js';
import { marked } from 'marked';

@customElement('sg-markdown-view')
export class SgMarkdownView extends LitElement {
  static styles = css`
    :host { display: block; line-height: 1.7; color: #212529; }
    h1, h2, h3, h4, h5, h6 { margin-top: 1.5em; margin-bottom: 0.5em; line-height: 1.3; }
    h1 { font-size: 1.875rem; border-bottom: 1px solid #dee2e6; padding-bottom: 0.375rem; }
    h2 { font-size: 1.5rem; border-bottom: 1px solid #e9ecef; padding-bottom: 0.25rem; }
    h3 { font-size: 1.25rem; }
    p { margin: 0.75em 0; }
    a { color: #2563eb; text-decoration: none; }
    a:hover { text-decoration: underline; }
    code {
      font-family: 'SF Mono', 'Fira Code', Menlo, Consolas, monospace;
      background: #f1f3f5;
      padding: 0.125em 0.375em;
      border-radius: 4px;
      font-size: 0.875em;
    }
    pre {
      background: #f8f9fa;
      border: 1px solid #e9ecef;
      border-radius: 6px;
      padding: 1rem;
      overflow-x: auto;
      margin: 1em 0;
    }
    pre code { background: none; padding: 0; border-radius: 0; }
    blockquote {
      border-left: 4px solid #dee2e6;
      padding-left: 1rem;
      color: #6c757d;
      margin: 1em 0;
    }
    ul, ol { padding-left: 1.5em; margin: 0.75em 0; }
    li { margin: 0.25em 0; }
    table { border-collapse: collapse; width: 100%; margin: 1em 0; }
    th, td { border: 1px solid #dee2e6; padding: 0.5rem 0.75rem; text-align: left; }
    th { background: #f8f9fa; font-weight: 600; }
    hr { border: none; border-top: 1px solid #dee2e6; margin: 1.5em 0; }
    img { max-width: 100%; border-radius: 6px; }
  `;

  @property() content = '';

  render() {
    const rendered = marked.parse(this.content, { async: false }) as string;
    return html`${unsafeHTML(rendered)}`;
  }
}
