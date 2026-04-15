import { LitElement, html, css } from 'lit';
import { customElement, property } from 'lit/decorators.js';
import type { DocumentSummary } from '../../api/types.js';

interface TreeNode {
  name: string;
  path?: string;
  children: TreeNode[];
}

function buildTree(docs: DocumentSummary[]): TreeNode[] {
  const root: TreeNode[] = [];

  for (const doc of docs) {
    const parts = doc.path.split('/');
    let current = root;

    for (let i = 0; i < parts.length; i++) {
      const name = parts[i];
      const isFile = i === parts.length - 1;

      let node = current.find((n) => n.name === name);
      if (!node) {
        node = {
          name,
          path: isFile ? doc.path : undefined,
          children: [],
        };
        current.push(node);
      }
      current = node.children;
    }
  }

  return root;
}

@customElement('sg-file-tree')
export class SgFileTree extends LitElement {
  static styles = css`
    :host { display: block; font-size: var(--sg-font-size-sm); }
    ul { list-style: none; padding-left: 1rem; margin: 0; }
    :host > ul { padding-left: 0; }
    li { padding: 0.125rem 0; }
    .folder { font-weight: 500; color: var(--sg-text); cursor: default; }
    .file a {
      color: var(--sg-primary);
      text-decoration: none;
      display: inline-block;
      padding: 0.125rem 0.25rem;
      border-radius: 4px;
      transition: background var(--sg-transition-fast);
    }
    .file a:hover { background: var(--sg-primary-light); text-decoration: underline; }
  `;

  @property({ attribute: false }) documents: DocumentSummary[] = [];
  @property() repoSlug = '';

  private _renderNode(node: TreeNode): unknown {
    if (node.path) {
      const urlPath = node.path.replace(/\.md$/, '');
      return html`<li class="file"><a href="/${this.repoSlug}/${urlPath}">${node.name}</a></li>`;
    }
    return html`
      <li class="folder">
        ${node.name}/
        <ul>${node.children.map((c) => this._renderNode(c))}</ul>
      </li>
    `;
  }

  render() {
    const tree = buildTree(this.documents);
    if (tree.length === 0) {
      return html`<p style="color:var(--sg-text-secondary);font-size:var(--sg-font-size-sm);">No documents yet.</p>`;
    }
    return html`<ul>${tree.map((n) => this._renderNode(n))}</ul>`;
  }
}
