// Prism-based syntax highlighter used by sg-markdown-view.
//
// Language set is deliberately curated — adding a language costs roughly 1-3 KB
// gzipped each. New languages go here so the cost is visible in review.
// `clike` and `markup` must stay — several other grammars depend on them.
import Prism from 'prismjs';
import 'prismjs/components/prism-markup.js';
import 'prismjs/components/prism-css.js';
import 'prismjs/components/prism-clike.js';
import 'prismjs/components/prism-javascript.js';
import 'prismjs/components/prism-typescript.js';
import 'prismjs/components/prism-jsx.js';
import 'prismjs/components/prism-tsx.js';
import 'prismjs/components/prism-json.js';
import 'prismjs/components/prism-yaml.js';
import 'prismjs/components/prism-bash.js';
import 'prismjs/components/prism-python.js';
import 'prismjs/components/prism-csharp.js';
import 'prismjs/components/prism-rust.js';
import 'prismjs/components/prism-go.js';
import 'prismjs/components/prism-java.js';
import 'prismjs/components/prism-sql.js';
import 'prismjs/components/prism-markdown.js';
import 'prismjs/components/prism-diff.js';
import 'prismjs/components/prism-toml.js';
import 'prismjs/components/prism-ini.js';
import 'prismjs/components/prism-docker.js';

export function highlightAllUnder(root: ParentNode): void {
  Prism.highlightAllUnder(root as unknown as Element);
}
