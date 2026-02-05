#!/usr/bin/env node
/**
 * Generate social preview images (1200x630) from cover images.
 *
 * Usage: node scripts/generate-social-images.js
 *
 * For each post with a cover image, creates a corresponding *-social.jpg
 * optimized for Open Graph / Twitter Card previews.
 */

const fs = require('fs');
const path = require('path');
const sharp = require('sharp');

const SOCIAL_WIDTH = 1200;
const SOCIAL_HEIGHT = 630;
const QUALITY = 85;

const POSTS_DIR = path.join(__dirname, '..', 'src', 'render', 'posts');
const STATIC_DIR = path.join(__dirname, '..', 'src', 'static');

async function findCoversInPosts() {
  const posts = fs.readdirSync(POSTS_DIR).filter(f => f.endsWith('.md'));
  const covers = [];

  for (const postFile of posts) {
    const content = fs.readFileSync(path.join(POSTS_DIR, postFile), 'utf8');
    const frontmatterMatch = content.match(/^---\r?\n([\s\S]*?)\r?\n---/);
    if (!frontmatterMatch) continue;

    const frontmatter = frontmatterMatch[1];
    const coverMatch = frontmatter.match(/^cover:\s*(.+)$/m);
    if (!coverMatch) continue;

    const coverPath = coverMatch[1].trim();
    covers.push({
      postFile,
      coverPath,
      absolutePath: path.join(STATIC_DIR, coverPath.replace(/^\//, ''))
    });
  }

  return covers;
}

function getSocialImagePath(coverPath) {
  const ext = path.extname(coverPath);
  const base = coverPath.slice(0, -ext.length);
  return `${base}-social.jpg`;
}

async function generateSocialImage(inputPath, outputPath) {
  // Ensure output directory exists
  const outputDir = path.dirname(outputPath);
  if (!fs.existsSync(outputDir)) {
    fs.mkdirSync(outputDir, { recursive: true });
  }

  await sharp(inputPath)
    .resize(SOCIAL_WIDTH, SOCIAL_HEIGHT, {
      fit: 'cover',
      position: 'center'
    })
    .jpeg({ quality: QUALITY })
    .toFile(outputPath);

  const stats = fs.statSync(outputPath);
  return Math.round(stats.size / 1024);
}

async function main() {
  console.log('Scanning posts for cover images...\n');

  const covers = await findCoversInPosts();
  console.log(`Found ${covers.length} posts with cover images.\n`);

  let generated = 0;
  let skipped = 0;
  let errors = 0;

  for (const { postFile, coverPath, absolutePath } of covers) {
    const socialPath = getSocialImagePath(coverPath);
    const absoluteSocialPath = path.join(STATIC_DIR, socialPath.replace(/^\//, ''));

    // Check if source exists
    if (!fs.existsSync(absolutePath)) {
      console.log(`⚠ ${postFile}: Source not found: ${coverPath}`);
      errors++;
      continue;
    }

    // Check if social image already exists and is newer than source
    if (fs.existsSync(absoluteSocialPath)) {
      const srcMtime = fs.statSync(absolutePath).mtime;
      const dstMtime = fs.statSync(absoluteSocialPath).mtime;
      if (dstMtime > srcMtime) {
        console.log(`⏭ ${postFile}: Up to date`);
        skipped++;
        continue;
      }
    }

    try {
      const sizeKB = await generateSocialImage(absolutePath, absoluteSocialPath);
      console.log(`✓ ${postFile}: Created ${socialPath} (${sizeKB} KB)`);
      generated++;
    } catch (err) {
      console.log(`✗ ${postFile}: ${err.message}`);
      errors++;
    }
  }

  console.log(`\nDone: ${generated} generated, ${skipped} skipped, ${errors} errors`);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
