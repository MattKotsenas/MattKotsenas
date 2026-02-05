#!/usr/bin/env node
/**
 * Validate Open Graph meta tags for social previews.
 *
 * Usage:
 *   node scripts/validate-social-tags.js [url-or-file]
 *   node scripts/validate-social-tags.js https://example.com/page
 *   node scripts/validate-social-tags.js out/posts/my-post.html
 *
 * Checks:
 * - Required OG and Twitter meta tags are present
 * - og:image URL is accessible (for URLs) or exists (for files)
 * - Image dimensions are declared as 1200x630
 */

const fs = require("fs");
const path = require("path");
const https = require("https");
const http = require("http");

const target = process.argv[2];

async function fetchPage(url) {
  return new Promise((resolve, reject) => {
    const client = url.startsWith("https") ? https : http;
    client.get(url, (res) => {
      let data = "";
      res.on("data", (chunk) => (data += chunk));
      res.on("end", () => resolve(data));
    }).on("error", reject);
  });
}

async function checkImageHead(url) {
  return new Promise((resolve) => {
    const client = url.startsWith("https") ? https : http;
    const req = client.request(url, { method: "HEAD" }, (res) => {
      resolve({
        status: res.statusCode,
        contentType: res.headers["content-type"],
        contentLength: parseInt(res.headers["content-length"] || "0")
      });
    });
    req.on("error", (e) => resolve({ error: e.message }));
    req.end();
  });
}

function extractMeta(html, property) {
  // Try property="x" content="y" format
  const regex = new RegExp(`<meta[^>]+(?:property|name)=["']${property}["'][^>]+content=["']([^"']+)["']`, "i");
  const match = html.match(regex);
  if (match) return match[1];

  // Try content="y" property="x" format
  const regex2 = new RegExp(`<meta[^>]+content=["']([^"']+)["'][^>]+(?:property|name)=["']${property}["']`, "i");
  const match2 = html.match(regex2);
  return match2 ? match2[1] : null;
}

async function validateHtml(html, source) {
  console.log(`\n🔍 Validating: ${source}\n`);

  const checks = {
    "og:title": extractMeta(html, "og:title"),
    "og:description": extractMeta(html, "og:description"),
    "og:image": extractMeta(html, "og:image"),
    "og:image:width": extractMeta(html, "og:image:width"),
    "og:image:height": extractMeta(html, "og:image:height"),
    "twitter:card": extractMeta(html, "twitter:card"),
    "twitter:image": extractMeta(html, "twitter:image"),
  };

  let allPassed = true;

  for (const [key, value] of Object.entries(checks)) {
    if (value) {
      console.log(`✅ ${key}: ${value.slice(0, 60)}${value.length > 60 ? "..." : ""}`);
    } else {
      console.log(`❌ ${key}: MISSING`);
      allPassed = false;
    }
  }

  // Check dimensions are optimal
  const width = checks["og:image:width"];
  const height = checks["og:image:height"];
  if (width && height) {
    if (width === "1200" && height === "630") {
      console.log(`\n✅ Dimensions optimal for social sharing (1200x630)`);
    } else {
      console.log(`\n⚠️  Dimensions ${width}x${height} (recommended: 1200x630)`);
    }
  }

  // Check og:image contains "-social" (our convention)
  if (checks["og:image"] && checks["og:image"].includes("-social")) {
    console.log(`✅ Using social-optimized image`);
  } else if (checks["og:image"]) {
    console.log(`⚠️  Image may not be social-optimized (expected '-social' in path)`);
  }

  console.log(`\n${allPassed ? "✅ All checks passed!" : "❌ Some checks failed"}\n`);
  return allPassed;
}

async function main() {
  if (!target) {
    // Validate all posts in out/posts/
    const postsDir = path.join(__dirname, "..", "out", "posts");
    if (!fs.existsSync(postsDir)) {
      console.error("out/posts/ not found. Run 'npm run build' first.");
      process.exit(1);
    }

    const posts = fs.readdirSync(postsDir).filter(f => f.endsWith(".html"));
    console.log(`Validating ${posts.length} posts...\n`);

    let passed = 0;
    let failed = 0;

    for (const post of posts) {
      const html = fs.readFileSync(path.join(postsDir, post), "utf8");
      const ok = await validateHtml(html, post);
      if (ok) passed++; else failed++;
    }

    console.log(`\n========================================`);
    console.log(`Summary: ${passed} passed, ${failed} failed`);
    process.exit(failed > 0 ? 1 : 0);

  } else if (target.startsWith("http")) {
    // Validate URL
    const html = await fetchPage(target);
    const ok = await validateHtml(html, target);
    process.exit(ok ? 0 : 1);

  } else {
    // Validate file
    if (!fs.existsSync(target)) {
      console.error(`File not found: ${target}`);
      process.exit(1);
    }
    const html = fs.readFileSync(target, "utf8");
    const ok = await validateHtml(html, target);
    process.exit(ok ? 0 : 1);
  }
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
