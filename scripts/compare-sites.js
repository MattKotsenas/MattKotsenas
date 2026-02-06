#!/usr/bin/env node
/**
 * Comprehensive site comparison: crawl, snapshot, and diff two sites.
 * 
 * Usage: node scripts/compare-sites.js <site1-url> <site2-url>
 * 
 * Example: node scripts/compare-sites.js http://localhost:1313 https://matt.kotsenas.com
 */

const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');
const { execSync, spawn } = require('child_process');

const BEYOND_COMPARE = 'C:\\Program Files\\Beyond Compare 5\\BCompare.exe';

// Simple HTML pretty printer - adds newlines after tags for easier diffing
function prettyPrintHtml(html) {
  return html
    .replace(/></g, '>\n<')
    .replace(/(<\/(div|p|section|article|header|footer|nav|aside|main|head|body|html|ul|ol|li|h[1-6]|meta|link|script|style)>)/gi, '$1\n')
    .replace(/(<(meta|link)[^>]*>)/gi, '$1\n');
}

async function crawlSite(page, baseUrl) {
  const visited = new Set();
  const toVisit = [baseUrl];
  const results = [];

  while (toVisit.length > 0) {
    const url = toVisit.shift();
    const normalizedUrl = url.replace(/\/$/, '');
    
    if (visited.has(normalizedUrl)) continue;
    visited.add(normalizedUrl);

    try {
      const response = await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
      const status = response?.status() || 0;
      
      // Get path for this URL
      const urlObj = new URL(url);
      const urlPath = urlObj.pathname || '/';
      
      results.push({ url, path: urlPath, status });

      if (status === 200) {
        // Extract links
        const links = await page.evaluate(() => {
          return Array.from(document.querySelectorAll('a[href]'))
            .map(a => a.href)
            .filter(href => href && !href.startsWith('javascript:') && !href.startsWith('mailto:'));
        });

        for (const link of links) {
          try {
            const linkUrl = new URL(link);
            const baseUrlObj = new URL(baseUrl);
            if (linkUrl.origin === baseUrlObj.origin) {
              const normalized = link.replace(/\/$/, '');
              if (!visited.has(normalized) && !toVisit.includes(link)) {
                toVisit.push(link);
              }
            }
          } catch {}
        }
      }
    } catch (err) {
      results.push({ url, path: new URL(url).pathname, status: 0, error: err.message });
    }
  }

  return results;
}

async function snapshotPage(page, url, outputDir, filename) {
  const htmlPath = path.join(outputDir, `${filename}.html`);
  const imgPath = path.join(outputDir, `${filename}.png`);

  try {
    await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
    
    // Save HTML (pretty printed)
    let html = await page.content();
    html = prettyPrintHtml(html);
    fs.writeFileSync(htmlPath, html);
    
    // Save screenshot
    await page.screenshot({ path: imgPath, fullPage: true });
    
    return { html: htmlPath, img: imgPath };
  } catch (err) {
    console.error(`  Error snapshotting ${url}: ${err.message}`);
    return null;
  }
}

function pathToFilename(urlPath) {
  if (urlPath === '/' || urlPath === '') return 'index';
  return urlPath.replace(/^\//, '').replace(/\/$/, '').replace(/\//g, '_');
}

async function main() {
  const args = process.argv.slice(2);
  if (args.length < 2) {
    console.log('Usage: node compare-sites.js <site1-url> <site2-url>');
    console.log('Example: node compare-sites.js http://localhost:1313 https://matt.kotsenas.com');
    process.exit(1);
  }

  const [site1Url, site2Url] = args;
  const site1Dir = path.join(__dirname, '..', 'comparison-site1');
  const site2Dir = path.join(__dirname, '..', 'comparison-site2');

  // Clean up old comparison dirs
  if (fs.existsSync(site1Dir)) fs.rmSync(site1Dir, { recursive: true });
  if (fs.existsSync(site2Dir)) fs.rmSync(site2Dir, { recursive: true });
  fs.mkdirSync(site1Dir, { recursive: true });
  fs.mkdirSync(site2Dir, { recursive: true });

  console.log('Launching browser...');
  const browser = await chromium.launch({ channel: 'msedge' });
  const context = await browser.newContext({ viewport: { width: 1280, height: 720 } });

  // Crawl site 1
  console.log(`\nCrawling ${site1Url}...`);
  const page1 = await context.newPage();
  const site1Pages = await crawlSite(page1, site1Url);
  console.log(`  Found ${site1Pages.length} pages`);

  // Crawl site 2
  console.log(`\nCrawling ${site2Url}...`);
  const page2 = await context.newPage();
  const site2Pages = await crawlSite(page2, site2Url);
  console.log(`  Found ${site2Pages.length} pages`);

  // Get unique paths from both sites
  const allPaths = new Set([
    ...site1Pages.filter(p => p.status === 200).map(p => p.path),
    ...site2Pages.filter(p => p.status === 200).map(p => p.path)
  ]);
  console.log(`\nTotal unique paths: ${allPaths.size}`);

  // Snapshot each path from both sites
  console.log('\nSnapshotting pages...');
  const comparisons = [];

  for (const urlPath of allPaths) {
    const filename = pathToFilename(urlPath);
    console.log(`  ${urlPath}`);

    const url1 = new URL(urlPath, site1Url).href;
    const url2 = new URL(urlPath, site2Url).href;

    const snap1 = await snapshotPage(page1, url1, site1Dir, filename);
    const snap2 = await snapshotPage(page2, url2, site2Dir, filename);

    if (snap1 && snap2) {
      comparisons.push({
        path: urlPath,
        filename,
        site1Html: snap1.html,
        site2Html: snap2.html,
        site1Img: snap1.img,
        site2Img: snap2.img
      });
    }
  }

  await browser.close();

  console.log(`\nCaptured ${comparisons.length} page pairs`);
  console.log('\nLaunching Beyond Compare for each pair...');
  console.log('Close each comparison window to proceed to the next.\n');

  // Compare HTML files
  console.log('=== HTML Comparisons ===\n');
  for (const comp of comparisons) {
    console.log(`Comparing HTML: ${comp.path}`);
    try {
      execSync(`"${BEYOND_COMPARE}" "${comp.site1Html}" "${comp.site2Html}"`, { stdio: 'inherit' });
    } catch {}
  }

  // Compare screenshots
  console.log('\n=== Screenshot Comparisons ===\n');
  for (const comp of comparisons) {
    console.log(`Comparing screenshot: ${comp.path}`);
    try {
      execSync(`"${BEYOND_COMPARE}" "${comp.site1Img}" "${comp.site2Img}"`, { stdio: 'inherit' });
    } catch {}
  }

  console.log('\nDone!');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
