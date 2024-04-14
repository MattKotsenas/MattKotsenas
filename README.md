# MattKotsenas

Personal website for [matt.kotsenas.com](http://matt.kotsenas.com). The site is generated using DocPad](https://docpad.bevry.me/) and uses the [Casper](https://github.com/TryGhost/Casper) theme.

## Building locally / testing

```powershell
docker build -f .\build\Dockerfile -t mattkotsenas/site:latest .
docker run --rm -it -p 3000:80 mattkotsenas/site:latest
```

## Credits

Cover photo credit [Taylor Bennett](https://www.flickr.com/photos/taylor90/14141304296).
