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

After signing in with the Azure and GitHub CLIs, start the AppHost and run **Configure Container App deployment** on
the `blog` resource. The command configures GitHub-to-Azure OIDC deployment. It can also be run from the terminal:

```bash
dotnet aspire start
dotnet aspire resource blog configure-container-app-deployment
dotnet aspire stop
```

Run `dotnet aspire stop` even if configuration fails. If the application already exists, verify its application ID and
pass it with `--applicationId`. If the deployment service principal is replaced, rerun this command and commit the
regenerated deployment artifacts.

Run the **Build and Deploy** workflow manually to deploy the preview Container App to the `blog` resource group in
West US 3.

## Azure DNS

The publish model owns website records within the Azure DNS zones in resource group `dns`. Until the registrar
nameservers change, those records are non-authoritative and point to the legacy Web App.

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
