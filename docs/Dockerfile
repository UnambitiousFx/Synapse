# syntax=docker/dockerfile:1

# Node 24 matches .github/workflows/docs.yml so a local build reproduces CI.
FROM node:24-bookworm-slim AS deps
WORKDIR /app
# Corepack resolves the exact pnpm pinned by package.json#packageManager.
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN --mount=type=cache,id=pnpm-store,target=/pnpm-store \
    pnpm config set store-dir /pnpm-store && \
    pnpm install --frozen-lockfile

# ---- dev: `docusaurus start` with hot reload -------------------------------
FROM deps AS dev
ENV NODE_ENV=development
EXPOSE 3000
CMD ["pnpm", "start", "--host", "0.0.0.0", "--port", "3000"]

# ---- build: static site into /app/build ------------------------------------
FROM deps AS build
COPY . .
RUN pnpm build

# ---- serve: nginx over the static output -----------------------------------
FROM nginx:1.31-alpine AS serve
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/build /usr/share/nginx/html
EXPOSE 8080
