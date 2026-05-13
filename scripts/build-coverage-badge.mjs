#!/usr/bin/env node
// Aggregate per-layer Cobertura reports into a single coverage.json in the
// Shields.io endpoint format. The percentage is the weighted average across
// layers (weight = number of lines), which avoids letting a small high-coverage
// layer mask a large low-coverage layer.
//
// usage: node scripts/build-coverage-badge.mjs <cobertura-glob> <output-path>
//   <cobertura-glob>  whitespace-separated list of XML paths
//   <output-path>     where to write coverage.json
//
// Output schema (Shields endpoint):
//   { schemaVersion: 1, label: "coverage", message: "73%", color: "yellowgreen" }

import { readFileSync, writeFileSync } from 'node:fs';

const [, , globArg, outPath] = process.argv;
if (!globArg || !outPath) {
  console.error('usage: build-coverage-badge.mjs "<paths>" <output-path>');
  process.exit(2);
}

const files = globArg.split(/\s+/).filter(Boolean);
if (files.length === 0) {
  console.error('No cobertura files supplied');
  process.exit(2);
}

let totalCovered = 0;
let totalValid = 0;
for (const file of files) {
  const xml = readFileSync(file, 'utf8');
  const linesValid = Number((xml.match(/<coverage[^>]*\blines-valid="([0-9]+)"/) ?? [, '0'])[1]);
  const linesCovered = Number((xml.match(/<coverage[^>]*\blines-covered="([0-9]+)"/) ?? [, '0'])[1]);
  if (linesValid === 0) {
    console.warn(`[badge] ${file} has lines-valid=0, skipping`);
    continue;
  }
  totalValid += linesValid;
  totalCovered += linesCovered;
  console.log(`[badge] ${file}: ${linesCovered}/${linesValid} lines (${((linesCovered / linesValid) * 100).toFixed(2)}%)`);
}

if (totalValid === 0) {
  console.error('All cobertura files had zero valid lines');
  process.exit(1);
}

const rate = totalCovered / totalValid;
const pct = Math.round(rate * 100);
const color =
  pct >= 90 ? 'brightgreen'
  : pct >= 80 ? 'green'
  : pct >= 70 ? 'yellowgreen'
  : pct >= 60 ? 'yellow'
  : pct >= 50 ? 'orange'
  : 'red';

const badge = {
  schemaVersion: 1,
  label: 'coverage',
  message: `${pct}%`,
  color,
};

writeFileSync(outPath, JSON.stringify(badge, null, 2) + '\n');
console.log(`[badge] aggregate ${totalCovered}/${totalValid} lines → ${pct}% (${color})`);
console.log(`[badge] wrote ${outPath}`);
