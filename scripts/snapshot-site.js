#!/usr/bin/env node
/**
 * Site Snapshot - Uses Playwright to capture HTML and screenshots of every page.
 * Usage: node scripts/snapshot-site.js https://matt.kotsenas.com ./snapshots
 * Output: ./snapshots/html/ and ./snapshots/screenshots/
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const { URL } = require('url');

const visited = new Set();
const queue = [];

function sanitizePath(pathname) {
  // Convert URL path to safe filesystem path
  let safePath = pathname.replace(/^\//, '').replace(/\//g, '_');
  if (!safePath) safePath = 'index';
  return safePath;
}

async function extractLinks(page, baseOrigin) {
  return await page.evaluate((origin) => {
    const links = [];
    document.querySelectorAll('a[href]').forEach(a => {
      try {
        const url = new URL(a.href, origin);
        if (url.origin === origin) {
          let normalized = url.href.split('#')[0].split('?')[0];
          if (normalized.endsWith('/') && normalized !== origin + '/') {
            normalized = normalized.slice(0, -1);
          }
          links.push(normalized);
        }
      } catch (e) {}
    });
    return [...new Set(links)];
  }, baseOrigin);
}

async function snapshotSite(startUrl, outputDir) {
  const baseOrigin = new URL(startUrl).origin;
  const htmlDir = path.join(outputDir, 'html');
  const screenshotDir = path.join(outputDir, 'screenshots');

  fs.mkdirSync(htmlDir, { recursive: true });
  fs.mkdirSync(screenshotDir, { recursive: true });

  const browser = await chromium.launch({ channel: 'msedge' });
  const context = await browser.newContext({
    viewport: { width: 1280, height: 720 }
  });
  const page = await context.newPage();

  queue.push(startUrl);
  const results = [];

  while (queue.length > 0) {
    const url = queue.shift();

    if (visited.has(url)) continue;
    visited.add(url);

    const pathname = new URL(url).pathname || '/';
    const safeName = sanitizePath(pathname);

    try {
      const response = await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
      const status = response?.status() || 0;

      // Only snapshot HTML pages (not assets)
      const contentType = response?.headers()['content-type'] || '';
      if (!contentType.includes('text/html')) {
        results.push({ pathname, status, skipped: 'not HTML' });
        continue;
      }

      // Save HTML
      const html = await page.content();
      fs.writeFileSync(path.join(htmlDir, `${safeName}.html`), html);

      // Save screenshot
      await page.screenshot({
        path: path.join(screenshotDir, `${safeName}.png`),
        fullPage: true
      });

      results.push({ pathname, status, saved: true });

      // Extract links and add to queue
      const links = await extractLinks(page, baseOrigin);
      for (const link of links) {
        if (!visited.has(link)) {
          queue.push(link);
        }
      }

      process.stderr.write(`\rSnapshotted: ${results.length} pages, Queue: ${queue.length}   `);

    } catch (err) {
      results.push({ pathname, error: err.message });
      process.stderr.write(`\nError on ${pathname}: ${err.message}\n`);
    }
  }

  await browser.close();
  process.stderr.write('\n');

  // Write manifest
  const manifest = {
    crawledAt: new Date().toISOString(),
    baseUrl: startUrl,
    totalPages: results.length,
    pages: results
  };
  fs.writeFileSync(path.join(outputDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

  return results;
}

async function main() {
  const startUrl = process.argv[2];
  const outputDir = process.argv[3] || './snapshots-baseline';

  if (!startUrl) {
    console.error('Usage: node snapshot-site.js <url> [output-dir]');
    console.error('Example: node snapshot-site.js https://matt.kotsenas.com ./snapshots-baseline');
    process.exit(1);
  }

  console.error(`Snapshotting ${startUrl} to ${outputDir}...`);
  const results = await snapshotSite(startUrl, outputDir);

  console.log(`\nDone! Captured ${results.length} pages.`);
  console.log(`  HTML: ${outputDir}/html/`);
  console.log(`  Screenshots: ${outputDir}/screenshots/`);
  console.log(`  Manifest: ${outputDir}/manifest.json`);
}

main().catch(err => {
  console.error('Snapshot failed:', err);
  process.exit(1);
});
