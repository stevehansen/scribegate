import { describe, expect, it } from 'vitest';
import { buildMermaidConfig } from './mermaid.js';

describe('buildMermaidConfig', () => {
  it('disables html labels globally so sanitized SVG keeps node text', () => {
    const config = buildMermaidConfig('dark');

    expect(config.theme).toBe('dark');
    expect(config.htmlLabels).toBe(false);
    expect(config.flowchart.htmlLabels).toBe(false);
  });
});
