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

## Credits

Cover photo credit [Taylor Bennett](https://www.flickr.com/photos/taylor90/14141304296).
