// Lazy KaTeX loader.
//
// Loading KaTeX and `marked-katex-extension` eagerly adds ~270 KB gzip to
// the main SPA chunk for a feature most documents don't use. This module
// defers the import until a document is detected to contain `$…$` or
// `$$…$$` math, registers the extension on the marked singleton once, and
// injects the KaTeX stylesheet into <head> on the same first hit.
//
// `ensureKatexRegistered()` is idempotent — the second and subsequent
// calls return the same in-flight (or resolved) promise without touching
// the network or the module graph.

let registered = false;
let pending: Promise<void> | null = null;

// Match both `$inline$` and `$$display$$` math markers without misfiring on
// bare dollar signs in prose ("$5"). Inline matches must stay on one line
// (no embedded newlines) and contain at least one non-`$` character. Block
// matches may span multiple lines.
const MATH_RE = /\$\$[\s\S]+?\$\$|\$[^\s$][^$\n]*\$/;

export function hasMath(source: string): boolean {
  if (!source) return false;
  return MATH_RE.test(source);
}

export function ensureKatexRegistered(): Promise<void> {
  if (registered) return Promise.resolve();
  if (pending) return pending;

  pending = (async () => {
    const [{ marked }, markedKatexModule, katexCssModule] = await Promise.all([
      import('marked'),
      import('marked-katex-extension'),
      import('katex/dist/katex.min.css?inline'),
    ]);
    marked.use(
      markedKatexModule.default({
        output: 'html',
        throwOnError: false,
      }),
    );
    injectKatexStyles(katexCssModule.default);
    registered = true;
  })();

  return pending;
}

function injectKatexStyles(cssText: string): void {
  if (typeof document === 'undefined') return;
  if (document.getElementById('sg-katex-styles')) return;
  const style = document.createElement('style');
  style.id = 'sg-katex-styles';
  style.textContent = cssText;
  document.head.appendChild(style);
}

// Test-only reset hook. Vitest needs to assert idempotency across test
// boundaries, which requires clearing module-level state between cases.
export function _resetKatexRegistrationForTests(): void {
  registered = false;
  pending = null;
}
