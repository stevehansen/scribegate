#!/usr/bin/env node
// Coverage soft-gate: read a Cobertura XML file, compare its top-level
// line-rate against the floor in coverage-thresholds.json, fail if below.
//
// usage: node scripts/check-coverage.mjs <layer> <cobertura-xml-path>
//   <layer>           key in coverage-thresholds.json (core|data|web|frontend)
//   <cobertura-xml>   path to a Cobertura XML report
//
// Emits a GitHub Actions ::notice:: line with the measured percentage on
// success and ::error:: + exit 1 on regression. The _description key in the
// thresholds file is ignored.

import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const repoRoot = resolve(__dirname, '..');

const [, , layer, reportPath] = process.argv;
if (!layer || !reportPath) {
  console.error('usage: check-coverage.mjs <layer> <cobertura-xml-path>');
  process.exit(2);
}

const thresholdsPath = resolve(repoRoot, 'coverage-thresholds.json');
const thresholds = JSON.parse(readFileSync(thresholdsPath, 'utf8'));

const floor = thresholds[layer];
if (typeof floor !== 'number') {
  console.error(`No threshold defined for layer "${layer}" in ${thresholdsPath}`);
  process.exit(2);
}

const xml = readFileSync(reportPath, 'utf8');
// Cobertura's top element looks like:
//   <coverage line-rate="0.84" branch-rate="0.7" ...>
// We only need the first match — that's the project-level summary.
const match = xml.match(/<coverage[^>]*\bline-rate="([0-9.]+)"/);
if (!match) {
  console.error(`Could not find line-rate attribute in ${reportPath}`);
  process.exit(2);
}

const rate = Number(match[1]);
const ratePct = (rate * 100).toFixed(2);
const floorPct = (floor * 100).toFixed(2);

if (rate + 1e-9 < floor) {
  console.log(`::error::${layer} coverage ${ratePct}% is below the floor ${floorPct}% (see coverage-thresholds.json)`);
  process.exit(1);
}

console.log(`::notice::${layer} coverage ${ratePct}% (floor ${floorPct}%)`);
