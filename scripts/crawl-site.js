#!/usr/bin/env node
/**
 * Site Crawler - Recursively crawls a website and outputs all URLs with status codes.
 * Usage: node scripts/crawl-site.js https://matt.kotsenas.com
 * Output: Sorted list of URLs with HTTP status codes
 */

const https = require('https');
const http = require('http');
const { URL } = require('url');

const visited = new Set();
const results = [];
const queue = [];

function fetch(url) {
  return new Promise((resolve, reject) => {
    const parsedUrl = new URL(url);
    const client = parsedUrl.protocol === 'https:' ? https : http;

    const req = client.get(url, {
      headers: { 'User-Agent': 'SiteCrawler/1.0' },
      timeout: 10000
    }, (res) => {
      let body = '';

      // Follow redirects
      if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
        const redirectUrl = new URL(res.headers.location, url).href;
        resolve({ statusCode: res.statusCode, body: '', redirectUrl });
        return;
      }

      res.on('data', chunk => body += chunk);
      res.on('end', () => resolve({ statusCode: res.statusCode, body }));
    });

    req.on('error', (err) => reject(err));
    req.on('timeout', () => {
      req.destroy();
      reject(new Error('Request timeout'));
    });
  });
}

function extractLinks(html, baseUrl) {
  const links = [];
  // Match href with or without quotes
  const hrefRegex = /href=(?:["']([^"']+)["']|([^\s>]+))/gi;
  const srcRegex = /src=(?:["']([^"']+)["']|([^\s>]+))/gi;
  let match;

  const processMatch = (href) => {
    try {
      // Skip external protocols
      if (href.startsWith('mailto:') || href.startsWith('tel:') ||
          href.startsWith('javascript:') || href.startsWith('data:')) {
        return;
      }

      // Skip fragment-only links
      if (href.startsWith('#')) {
        return;
      }

      const absoluteUrl = new URL(href, baseUrl).href;
      const parsedBase = new URL(baseUrl);
      const parsedLink = new URL(absoluteUrl);

      // Only follow same-origin links
      if (parsedLink.origin === parsedBase.origin) {
        // Normalize: remove fragment and query string
        let normalized = absoluteUrl.split('#')[0].split('?')[0];
        // Remove trailing slash for consistency (except root)
        if (normalized.endsWith('/') && normalized !== parsedBase.origin + '/') {
          normalized = normalized.slice(0, -1);
        }
        links.push(normalized);
      }
    } catch (e) {
      // Invalid URL, skip
    }
  };

  while ((match = hrefRegex.exec(html)) !== null) {
    processMatch(match[1] || match[2]);
  }

  while ((match = srcRegex.exec(html)) !== null) {
    processMatch(match[1] || match[2]);
  }

  return links;
}

async function crawl(startUrl) {
  const baseOrigin = new URL(startUrl).origin;
  queue.push(startUrl);

  while (queue.length > 0) {
    const url = queue.shift();

    if (visited.has(url)) continue;
    visited.add(url);

    try {
      const { statusCode, body, redirectUrl } = await fetch(url);
      const pathname = new URL(url).pathname || '/';
      results.push({ pathname, statusCode, url });

      // If redirect, also crawl the target
      if (redirectUrl && new URL(redirectUrl).origin === baseOrigin) {
        if (!visited.has(redirectUrl)) {
          queue.push(redirectUrl);
        }
      }

      // Extract and queue new links from HTML pages
      if (statusCode === 200 && body) {
        const links = extractLinks(body, url);
        for (const link of links) {
          if (!visited.has(link)) {
            queue.push(link);
          }
        }
      }

      // Progress indicator to stderr
      process.stderr.write(`\rCrawled: ${results.length} pages, Queue: ${queue.length}   `);

    } catch (err) {
      const pathname = new URL(url).pathname || '/';
      results.push({ pathname, statusCode: 'ERROR', url, error: err.message });
    }
  }

  process.stderr.write('\n');
}

async function main() {
  const startUrl = process.argv[2];

  if (!startUrl) {
    console.error('Usage: node crawl-site.js <url>');
    console.error('Example: node crawl-site.js https://matt.kotsenas.com');
    process.exit(1);
  }

  console.error(`Crawling ${startUrl}...`);
  await crawl(startUrl);

  // Sort by pathname and output
  results.sort((a, b) => a.pathname.localeCompare(b.pathname));

  console.log('# Site Crawl Results');
  console.log(`# Crawled: ${new Date().toISOString()}`);
  console.log(`# Base URL: ${startUrl}`);
  console.log(`# Total URLs: ${results.length}`);
  console.log('#');
  console.log('# Format: STATUS_CODE PATH');
  console.log('#');

  for (const { pathname, statusCode } of results) {
    console.log(`${statusCode} ${pathname}`);
  }

  // Summary
  const statuses = {};
  for (const { statusCode } of results) {
    statuses[statusCode] = (statuses[statusCode] || 0) + 1;
  }

  console.log('#');
  console.log('# Summary:');
  for (const [code, count] of Object.entries(statuses).sort()) {
    console.log(`#   ${code}: ${count}`);
  }
}

main().catch(err => {
  console.error('Crawl failed:', err);
  process.exit(1);
});
