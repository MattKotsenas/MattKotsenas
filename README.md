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

An authorized operator configures GitHub-to-Azure OIDC and scoped deployment roles for the existing preview
environment. The setup command requires its registry resource ID and, when reusing an application, its verified
application ID. See [Configure Azure deployment](docs/configure-azure-deployment.md) for prerequisites, commands,
permission scopes, and the rollout procedure for replacing subscription-wide grants.

Run the **Build and Deploy** workflow manually to deploy the preview Container App to the existing `blog` resource
group in West US 3. The workflow cannot create that resource group.

## Azure DNS

The publish model owns website records within the existing Azure DNS zones in resource group `dns`. These
records point to the legacy Web App; preview deployment does not cut public traffic over to Container Apps.

## Public HTTPS health

The **Public HTTPS Health** workflow runs daily to verify TLS trust, certificate lifetime, and HTTP availability for
the public website domains. Run the same checks on demand after setting `DeploymentPrincipalId` to the deployment
service principal's object ID:

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
