# MattKotsenas

Personal website for [matt.kotsenas.com](https://matt.kotsenas.com). The site is generated using [Hugo](https://gohugo.io/) with the [Blowfish](https://blowfish.page/) theme.

## Building locally

```bash
# Generate the site with the production Docker build
docker build --target export --output type=local,dest=public -f build/Dockerfile .
```

## Development server

```bash
dotnet tool restore
dotnet aspire start

# Open http://localhost:1313
```

## Container App preview deployment

The `container-app-preview` GitHub environment requires `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, and
`AZURE_SUBSCRIPTION_ID` secrets. The Azure identity needs a federated credential with issuer
`https://token.actions.githubusercontent.com`, audience `api://AzureADTokenExchange`, and subject
`repo:MattKotsenas/MattKotsenas:environment:container-app-preview`. It also needs the **Contributor** and **Role Based
Access Control Administrator** roles on the target subscription for initial provisioning.

Run the **Build and Deploy** workflow manually to deploy the preview Container App to the `blog` resource group in
West US 3.

## Creating a new post

```bash
# Create a new post (replace 'my-post-slug' with your post's URL slug)
docker build --target dev --tag mattkotsenas/blog-dev -f build/Dockerfile .
docker run --rm -v ${PWD}:/src mattkotsenas/blog-dev new posts/my-post-slug/index.md
```

This creates a new post with the correct frontmatter. To add a hero image:

1. Add an image named `feature.jpg` or `feature.png` to the post folder
2. The `showHero: true` frontmatter is already set by the archetype

## Credits

Cover photo credit [Taylor Bennett](https://www.flickr.com/photos/taylor90/14141304296).
