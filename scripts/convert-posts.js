#!/usr/bin/env node
/**
 * Convert DocPad posts to Hugo format.
 * - Renames postDate to date
 * - Removes isPost and active fields
 * - Extracts slug from filename
 */

const fs = require('fs');
const path = require('path');

const srcDir = path.join(__dirname, '..', 'src', 'render', 'posts');
const destDir = path.join(__dirname, '..', 'content', 'posts');

// Ensure dest directory exists
fs.mkdirSync(destDir, { recursive: true });

const files = fs.readdirSync(srcDir).filter(f => f.endsWith('.html.md'));

for (const file of files) {
  const srcPath = path.join(srcDir, file);
  let content = fs.readFileSync(srcPath, 'utf8');

  // Extract slug from filename (remove .html.md)
  const slug = file.replace('.html.md', '');

  // Normalize line endings
  content = content.replace(/\r\n/g, '\n').replace(/\r/g, '\n');

  // Parse frontmatter
  const match = content.match(/^---\n([\s\S]*?)\n---\n([\s\S]*)$/);
  if (!match) {
    console.error(`Skipping ${file}: no frontmatter found`);
    continue;
  }

  let frontmatter = match[1];
  const body = match[2];

  // Convert postDate to date
  frontmatter = frontmatter.replace(/^postDate:\s*['"]?(.+?)['"]?\s*$/m, (_, dateVal) => {
    // Parse the date and convert to ISO format
    const d = new Date(dateVal);
    return `date: ${d.toISOString()}`;
  });

  // Remove isPost and active fields (Hugo determines this from section)
  frontmatter = frontmatter.replace(/^isPost:\s*.*$/m, '');
  frontmatter = frontmatter.replace(/^active:\s*.*$/m, '');

  // Add slug
  frontmatter = frontmatter.trim() + `\nslug: "${slug}"`;

  // Clean up empty lines in frontmatter
  frontmatter = frontmatter.split('\n').filter(line => line.trim()).join('\n');

  // Write to destination
  const destPath = path.join(destDir, `${slug}.md`);
  fs.writeFileSync(destPath, `---\n${frontmatter}\n---\n${body}`);
  console.log(`Converted: ${file} -> ${slug}.md`);
}

console.log(`\nDone! Converted ${files.length} posts.`);
