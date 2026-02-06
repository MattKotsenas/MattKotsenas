# Build Scripts

Scripts for validating the site during migration and deployment.

## Site Comparison Scripts

These scripts help validate that site migrations don't break existing URLs or content.

### crawl-site.js

Crawls a site and extracts all URLs.

```bash
node scripts/crawl-site.js https://matt.kotsenas.com > urls.txt
```

### snapshot-site.js

Takes screenshots of all pages for visual comparison.

### compare-sites.js

Compares URLs between two site versions.

### compare-screenshots.js

Compares screenshots for visual regression testing.
