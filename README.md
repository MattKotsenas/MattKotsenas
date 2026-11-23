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

An authorized operator runs the [native Aspire bootstrap](docs/configure-azure-deployment.md) to create a
main-only GitHub OIDC identity. Aspire owns the blog application and its role assignments inside the `blog`
resource group. The existing AppHost defines the operator-only `bootstrap` target and routine native deployment
targets.

[The migration checklist](docs/container-app-migration.md) tracks temporary external permissions, identity
handoff, registry compatibility, and the later DNS/certificate/traffic stages.

Run the **Build and Deploy** workflow manually to deploy the preview Container App to the existing `blog` resource
group in West US 3 with `dotnet aspire deploy --environment preview --non-interactive`. Routine deployment
selects application resources; bootstrap modules and GitHub settings actions remain unselected. Publish exports
the full model to `generated/azure`; CI checks that output with `publish-and-diff`.

## Azure DNS

The publish model owns website records within the existing Azure DNS zones in resource group `dns`. These
records point to the legacy Web App throughout preview. Traffic cutover requires separate approval.

## Public HTTPS health

The **Public HTTPS Health** workflow defines daily TLS, certificate-lifetime, and HTTP checks for the public
website domains. Its schedule is currently disabled in GitHub. Run the same checks locally on demand:

```bash
dotnet aspire do check-public-https --environment preview --non-interactive
```

## Creating a new post

```bash
# Create a new post (replace 'my-post-slug' with your post's URL slug)
docker run --rm -v "${PWD}:/src" "$(docker build --quiet --target dev -f build/Dockerfile .)" new posts/my-post-slug/index.md
```

This creates a new post with the correct frontmatter. To add a hero image:

1. Add an image named `feature.jpg` or `feature.png` to the post folder
2. The `showHero: true` frontmatter is already set by the archetype

## Credits

Cover photo credit [Taylor Bennett](https://www.flickr.com/photos/taylor90/14141304296).
