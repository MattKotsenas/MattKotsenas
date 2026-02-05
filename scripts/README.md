# Build Scripts

Scripts for generating and validating social media preview assets.

## Social Image Generation

**Script:** `generate-social-images.js`

Generates 1200×630 social preview images from post cover images. These optimized
images are used for Open Graph (`og:image`) and Twitter Card (`twitter:image`)
meta tags.

### When it runs

- **Automatically** during `npm run generate` or `npm run build` (if `sharp` is
  installed)
- **Manually** with `npm run generate:social-images`
- **Skipped** during production deployment (`npm install --production` doesn't
  install `sharp`)

### Important: Commit generated images

Social images are generated locally and **must be committed to the repo**. The
production deployment environment (Azure) uses Node 8.9.0 and doesn't support
the `sharp` library. The deployment just copies the pre-generated images.

### What it does

1. Scans all posts in `src/render/posts/` for `cover:` frontmatter
2. For each cover image, creates a `*-social.jpg` variant at 1200×630
3. Skips images that are already up-to-date (based on file modification time)
4. Outputs to the same directory as the source image

### Adding a new post

1. Add `cover: /img/your-post/cover.jpg` to your post's frontmatter
2. Add `socialImage: /img/your-post/cover-social.jpg` to your post's frontmatter
3. Run `npm run build` — the social image is generated automatically
4. **Commit the generated `-social.jpg` file** to the repo

If you forget step 2, the template falls back to using the cover image directly
(but it may not display correctly on social platforms).

## Validation

**Script:** `validate-social-tags.js`

Validates that generated HTML has correct Open Graph and Twitter Card meta tags.

### Usage

```bash
# Validate all posts in out/posts/
npm run validate

# Validate a specific file
node scripts/validate-social-tags.js out/posts/my-post.html

# Validate a live URL
node scripts/validate-social-tags.js https://example.com/posts/my-post
```

### What it checks

- `og:title`, `og:description`, `og:image` present
- `og:image:width` = 1200, `og:image:height` = 630
- `twitter:card`, `twitter:image` present
- Image path contains `-social` (our naming convention)

### When to run

- Runs automatically as part of `npm run build`
- Run manually after deploying to verify live site: `node scripts/validate-social-tags.js https://matt.kotsenas.com/posts/your-post`

## Why separate social images?

Social platforms (Teams, Twitter, Facebook, LinkedIn) have specific requirements:

| Requirement | Value |
|-------------|-------|
| Recommended size | 1200×630 pixels |
| Max file size | < 1MB ideal |
| Format | JPEG or PNG |

Large images (like 5472×3072 camera photos) may:
- Fail to load on some platforms
- Be cropped unpredictably
- Slow down preview generation

The `og:image:width` and `og:image:height` meta tags are hints only — they don't
resize the actual image. Platforms still fetch and process the full file.
