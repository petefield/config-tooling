# config-tooling

This repository contains two related .NET 10 apps:

| Project | Path | Purpose |
| --- | --- | --- |
| Browser app | `src/config-browser` | Blazor WebAssembly UI for browsing configs and promoting them between environments. |
| Azure Functions API | `src/config-promote-api` | GitHub sign-in, live GitHub reads, token refresh, and promotion backend. |

## Repository flow

1. Config source files live in `configs/<tenant>/<environment>/*.json`.
2. The browser app reads live config data through the Azure Functions API.
3. Users must sign in with GitHub before any config data is shown.
4. The Functions API reads GitHub as the signed-in user, returns catalog/file/history data, and performs promotion actions.
5. Promotion creates a branch, copies the selected config into the next environment, and opens a pull request.

## Prerequisites

- .NET SDK 10
- For local Function App work:
  - Azure Functions Core Tools
  - Azurite or another local Azure Storage emulator
- A GitHub App configured for user-to-server authentication

## Browser app

The browser app lives in `src/config-browser`.

### What it does

- Shows a config catalog with tenant, environment, and contact type filters
- Loads live config JSON and git history from GitHub through the Function App
- Requires GitHub sign-in before browsing
- Supports promotion from:
  - `dev` to `uat`
  - `uat` to `prd`
- Uses an in-app confirmation modal for promotion, including a waiting state and inline error handling

### Local run

```bash
dotnet run --project src/config-browser/config-browser.csproj
```

The current local browser URL is:

```text
http://localhost:5172
```

Browser runtime settings are in `src/config-browser/wwwroot/appsettings.json`:

- `PromoteApiBaseUrl`
- `Repository.Owner`
- `Repository.Name`
- `Repository.BaseBranch`

## Azure Functions API

The Functions app lives in `src/config-promote-api`.

### What it does

- Starts and completes GitHub App sign-in
- Stores protected auth session payloads for the browser
- Refreshes GitHub user tokens when needed
- Returns live config catalog, config file, and git history data
- Creates promotion branches and pull requests

### Routes

| Route | Purpose |
| --- | --- |
| `/api/auth/github/start` | Starts GitHub sign-in |
| `/api/auth/github/callback` | Completes GitHub sign-in |
| `/api/configs/catalog` | Returns the live config catalog |
| `/api/configs/file` | Returns a config file body |
| `/api/configs/history` | Returns per-file git history |
| `/api/promote` | Creates the promotion branch and PR |

The browser uses signed-in requests for reads and promotion. Catalog/file/history requests are sent as simple `POST` requests to avoid browser preflight issues on GitHub Pages.

### Local setup

Copy the sample settings file:

```bash
cp src/config-promote-api/local.settings.sample.json src/config-promote-api/local.settings.json
```

Required settings:

- `AzureWebJobsStorage`
- `FUNCTIONS_WORKER_RUNTIME`
- `GitHubAppClientId`
- `GitHubAppClientSecret`
- `GitHubAppCallbackUrl`
- `GitHubAppSessionKey`
- `GitHubAppAllowedOrigins`

Current sample values:

- local browser origin: `http://localhost:5172`
- local callback URL: `http://localhost:7071/api/auth/github/callback`

### Run locally

From `src/config-promote-api`:

```bash
func start
```

To run the Function App and Azurite together in Docker Compose:

```bash
cp docker-compose.env.sample .env
docker compose up --build
```

That starts:

- the promote API on `http://localhost:7071`
- Azurite blob/queue/table endpoints on `10000`, `10001`, and `10002`

The compose file maps the GitHub App settings from `.env` into the environment variables expected by `src/config-promote-api`.

## GitHub App setup

Create a GitHub App with:

- **User authorization callback URL** set to your Function App callback route
- **User-to-server token expiration** enabled
- Repository permissions:
  - **Contents**: Read and write
  - **Pull requests**: Read and write
  - **Metadata**: Read-only

Your callback URL must exactly match both:

1. the GitHub App **User authorization callback URL**
2. the Function App setting `GitHubAppCallbackUrl`

Example deployed callback URL:

```text
https://config-promote-api-fpb3d0a2b8bqcudp.uksouth-01.azurewebsites.net/api/auth/github/callback
```

## Deployment

### Browser app

GitHub Pages deployment is handled by:

```text
.github/workflows/deploy-config-browser.yml
```

It publishes the Blazor app from `src/config-browser` and deploys it to GitHub Pages.

### Function App

Azure deployment is handled by:

```text
.github/workflows/deploy-function-app.yml
```

It restores and publishes:

```text
src/config-promote-api/config-promote-api.csproj
```

and deploys the publish output to the Azure Function App named:

```text
config-promote-api
```

## Current production settings

The checked-in browser app currently points at:

```text
PromoteApiBaseUrl = https://config-promote-api-fpb3d0a2b8bqcudp.uksouth-01.azurewebsites.net
Repository.Owner = petefield
Repository.Name = config-tooling
Repository.BaseBranch = main
```

## Troubleshooting

### GitHub sign-in says `The redirect_uri is not associated with this application`

Your GitHub App callback URL does not exactly match `GitHubAppCallbackUrl`.

### Browser reports a CORS error on config reads

Check:

- `GitHubAppAllowedOrigins` includes the browser origin
- the browser and Function App are both redeployed
- the browser is calling the Function App, not `api.github.com` directly

### The browser loads slowly

The current catalog flow already avoids many redundant reads by returning environment-match flags in the catalog response. If the deployed site still feels slow, confirm the latest browser app and Function App builds are deployed together.
