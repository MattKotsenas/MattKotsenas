#!/usr/bin/env node
/**
 * Compare screenshots between baseline and Hugo using Beyond Compare.
 * Opens each pair in a separate Beyond Compare window.
 * Usage: node scripts/compare-screenshots.js
 */

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const baselineDir = path.join(__dirname, '..', 'snapshots-baseline', 'screenshots');
const hugoDir = path.join(__dirname, '..', 'snapshots-hugo', 'screenshots');

// Beyond Compare executable path
const bcPath = 'C:\\Program Files\\Beyond Compare 5\\BCompare.exe';

// Get all screenshots from both directories
const baselineFiles = fs.readdirSync(baselineDir).filter(f => f.endsWith('.png'));
const hugoFiles = fs.readdirSync(hugoDir).filter(f => f.endsWith('.png'));

console.log(`Baseline: ${baselineFiles.length} screenshots`);
console.log(`Hugo: ${hugoFiles.length} screenshots\n`);

// Normalize filename for matching
function normalize(filename) {
  let n = filename
    .replace('.html.png', '.png')  // Remove .html suffix
    .replace(/_index\.png$/, '.png') // 2_index.png -> 2.png
    .toLowerCase();
  return n;
}

// Build Hugo file map
const hugoMap = new Map();
for (const f of hugoFiles) {
  hugoMap.set(normalize(f), f);
}

// Match and track results
const pairs = [];
const noMatch = [];

for (const baseFile of baselineFiles) {
  const normalized = normalize(baseFile);
  const hugoFile = hugoMap.get(normalized);

  if (hugoFile) {
    pairs.push({
      baseline: path.join(baselineDir, baseFile),
      hugo: path.join(hugoDir, hugoFile),
      baseName: baseFile,
      hugoName: hugoFile
    });
    // Remove from map so we can track unmatched Hugo files
    hugoMap.delete(normalized);
  } else {
    noMatch.push(baseFile);
  }
}

// Report unmatched
if (noMatch.length > 0) {
  console.log(`\n❌ Baseline files with NO Hugo match (${noMatch.length}):`);
  noMatch.forEach(f => console.log(`   ${f}`));
}

const unmatchedHugo = Array.from(hugoMap.values());
if (unmatchedHugo.length > 0) {
  console.log(`\n⚠️  Hugo files with NO baseline match (${unmatchedHugo.length}):`);
  unmatchedHugo.forEach(f => console.log(`   ${f}`));
}

console.log(`\n✅ Matched ${pairs.length} pairs\n`);
console.log('Opening Beyond Compare for each pair...\n');

// Open Beyond Compare for each pair
let count = 0;
for (const pair of pairs) {
  count++;
  console.log(`[${count}/${pairs.length}] ${pair.baseName} <-> ${pair.hugoName}`);

  const bc = spawn(bcPath, [pair.baseline, pair.hugo], {
    detached: true,
    stdio: 'ignore'
  });
  bc.unref();
}

console.log('\nDone! All comparison windows opened.');

