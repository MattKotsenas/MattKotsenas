# MattKotsenas

Personal website for [matt.kotsenas.com](https://matt.kotsenas.com). The site is generated using [Hugo](https://gohugo.io/) with the [Blowfish](https://blowfish.page/) theme.

## Building locally

```bash
# Build the Hugo Docker image
docker build -t mattkotsenas/blog -f build/Dockerfile .

# Generate the site
docker run --rm -v ${PWD}:/src mattkotsenas/blog hugo --minify

# Output is in the public/ directory
```

## Development server

```bash
# Start dev server with live reload
docker run --rm -v ${PWD}:/src -p 1313:1313 mattkotsenas/blog hugo server --bind 0.0.0.0

# Open http://localhost:1313
```

## Creating a new post

```bash
# Create a new post (replace 'my-post-slug' with your post's URL slug)
docker run --rm -v ${PWD}:/src mattkotsenas/blog hugo new posts/my-post-slug/index.md
```

This creates a new post with the correct frontmatter. To add a hero image:

1. Add an image named `feature.jpg` or `feature.png` to the post folder
2. The `showHero: true` frontmatter is already set by the archetype

## Credits

Cover photo credit [Taylor Bennett](https://www.flickr.com/photos/taylor90/14141304296).
