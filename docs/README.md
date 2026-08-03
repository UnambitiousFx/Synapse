# Website

This website is built using [Docusaurus](https://docusaurus.io/), a modern static website generator.

## Installation

```bash
pnpm install
```

## Local Development

```bash
pnpm start
```

This command starts a local development server and opens up a browser window. Most changes are reflected live without having to restart the server.

## Build

```bash
pnpm build
```

This command generates static content into the `build` directory and can be served using any static contents hosting service.

## Docker

Serve the built site (nginx on port 8080):

```bash
docker compose up --build docs
# http://localhost:8080
```

Hot-reload dev server (source bind-mounted, port 3000):

```bash
docker compose --profile dev up --build docs-dev
# http://localhost:3000
```

Without Compose:

```bash
docker build --target serve -t synapse-docs .
docker run --rm -p 8080:8080 synapse-docs
```

Build stages: `deps` (pnpm install) → `dev` (`docusaurus start`) / `build` (static output) → `serve` (nginx). Node 24 matches the version used by `.github/workflows/docs.yml`.

## Deployment

Docs deploy to GitHub Pages via `.github/workflows/docs.yml` on `v*` tags (or manual dispatch). The `docusaurus deploy` command is not used by CI.
