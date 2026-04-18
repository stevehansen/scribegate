// Scans a rendered markdown tree for ```mermaid fenced blocks and replaces
// them with inline SVG. Mermaid is ~400 KB gzipped, so the import is dynamic —
// it only loads when a page actually has a diagram.
import DOMPurify from 'dompurify';

type MermaidModule = typeof import('mermaid');
let loader: Promise<MermaidModule['default']> | null = null;

async function load(): Promise<MermaidModule['default']> {
  if (!loader) {
    loader = import('mermaid').then((mod) => {
      mod.default.initialize({
        startOnLoad: false,
        theme: detectTheme(),
        securityLevel: 'strict',
        fontFamily: 'inherit',
        // Flowchart labels default to HTML inside <foreignObject>, which
        // DOMPurify's SVG profile strips as belt-and-braces XSS protection,
        // leaving the nodes without any visible text. Force native <text>
        // labels — sequence, git, and journey diagrams already render text
        // natively, which is why they survived the same pipeline.
        flowchart: { htmlLabels: false },
      });
      return mod.default;
    });
  }
  return loader;
}

function detectTheme(): 'default' | 'dark' {
  const explicit = document.documentElement.getAttribute('data-theme');
  if (explicit === 'dark') return 'dark';
  if (explicit) return 'default';
  return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'default';
}

export async function renderMermaidBlocks(root: ParentNode): Promise<void> {
  const blocks = Array.from(
    root.querySelectorAll<HTMLElement>('pre > code.language-mermaid'),
  );
  if (blocks.length === 0) return;

  const mermaid = await load();

  let counter = 0;
  for (const code of blocks) {
    const pre = code.parentElement;
    if (!pre) continue;
    const source = code.textContent ?? '';
    const id = `sg-mmd-${Date.now().toString(36)}-${counter++}`;
    try {
      const { svg } = await mermaid.render(id, source);
      // Mermaid's SVG output is trusted but the diagram source is
      // user-authored markdown. DOMPurify's SVG profile strips foreign XML,
      // event handlers, and javascript: hrefs as belt-and-braces.
      const clean = DOMPurify.sanitize(svg, { USE_PROFILES: { svg: true, svgFilters: true } });
      const wrap = document.createElement('div');
      wrap.className = 'sg-mermaid';
      wrap.innerHTML = String(clean);
      pre.replaceWith(wrap);
    } catch (err) {
      // Leave the original code block in place and surface the error inline
      // so authors can fix their diagram without digging into the console.
      const message = err instanceof Error ? err.message : String(err);
      const error = document.createElement('div');
      error.className = 'sg-mermaid-error';
      error.textContent = `Mermaid error: ${message}`;
      pre.after(error);
    }
  }
}
