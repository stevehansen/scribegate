// Marked extensions that bring the SPA renderer to parity with the server-side
// Markdig pipeline: footnotes, definition lists, and GitHub-style emoji
// shortcodes. Registered once as a side effect of importing this module.
//
// KaTeX (~270 KB gzip) lives in lib/katex-lazy.ts and is registered on
// demand the first time sg-markdown-view detects math in a document.
import { marked, type Tokens } from 'marked';
import markedFootnote from 'marked-footnote';
import { get as getEmoji } from 'node-emoji';

marked.use(markedFootnote());

marked.use({
  extensions: [emojiExtension(), deflistExtension()],
});

function emojiExtension() {
  return {
    name: 'emoji',
    level: 'inline' as const,
    start(src: string): number | undefined {
      const i = src.indexOf(':');
      return i === -1 ? undefined : i;
    },
    tokenizer(src: string) {
      const match = /^:([a-z0-9_+\-]+):/.exec(src);
      if (!match) return;
      const char = getEmoji(match[1]);
      if (!char) return;
      return {
        type: 'emoji',
        raw: match[0],
        char,
      };
    },
    renderer(token: Tokens.Generic) {
      return String(token.char);
    },
  };
}

interface DeflistItem {
  termTokens: Tokens.Generic[];
  defTokens: Tokens.Generic[][];
}

function deflistExtension() {
  return {
    name: 'deflist',
    level: 'block' as const,
    start(src: string): number | undefined {
      const m = /\n[ \t]*[:~][ \t]+/.exec(src);
      return m ? m.index : undefined;
    },
    tokenizer(this: { lexer: { inlineTokens(src: string): Tokens.Generic[] } }, src: string) {
      // Match one or more (term\n(:\s+def\n)+) groups. Each term line must not
      // itself start with ':' or '~'. Definitions may span multiple lines
      // (each introduced by a fresh ':' or '~').
      const match = /^((?:(?!\s*$)(?![:~][ \t])[^\n]+\n(?:[:~][ \t]+[^\n]*(?:\n|$))+)+)/.exec(src);
      if (!match) return;

      const items: DeflistItem[] = [];
      const lines = match[1].split('\n');
      let current: DeflistItem | null = null;

      for (const line of lines) {
        if (!line) continue;
        const defMatch = /^[:~][ \t]+(.*)$/.exec(line);
        if (defMatch) {
          current?.defTokens.push(this.lexer.inlineTokens(defMatch[1]));
        } else {
          current = {
            termTokens: this.lexer.inlineTokens(line),
            defTokens: [],
          };
          items.push(current);
        }
      }

      if (items.length === 0 || items.every((i) => i.defTokens.length === 0)) return;

      return {
        type: 'deflist',
        raw: match[0],
        items,
      };
    },
    renderer(this: { parser: { parseInline(tokens: Tokens.Generic[]): string } }, token: Tokens.Generic) {
      const items = token.items as DeflistItem[];
      const body = items
        .map((item) => {
          const term = this.parser.parseInline(item.termTokens);
          const defs = item.defTokens
            .map((t) => `<dd>${this.parser.parseInline(t)}</dd>`)
            .join('');
          return `<dt>${term}</dt>${defs}`;
        })
        .join('');
      return `<dl>${body}</dl>`;
    },
  };
}
