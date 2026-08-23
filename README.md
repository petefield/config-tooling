# config-tooling

Copies each JSON file from `./configs` into `./root/{tenant}/{environment}` based on its `featureFlags`, then writes a `history-index.json` file with one entry for each generated file and the last five git modifications of its source config file. Before each run, the destination root folder is deleted and recreated.

## Usage

```bash
dotnet run
```

Optional arguments:

```bash
dotnet run -- <source-config-directory> <destination-root-directory>
```

An environment value of `all` is expanded into `dev`, `uat`, and `prd`.

When no source directory is supplied, the app searches upward from the current directory for a `configs` folder.

## Browser app

A standalone Blazor WebAssembly browser app lives in `src/config-browser`.

1. Start the browser app from `src/config-browser` with `dotnet run`.

The browser app reads the config list, config bodies, and per-file history directly from GitHub, so merged config changes show up in the UI without waiting for generated browser data to be refreshed. The GitHub Pages workflow now only republishes the browser app itself when its files change.

The details page can also promote a config to the next environment. The published browser app signs the user in with a GitHub App via the Azure Functions backend in `src/config-promote-api`, then that backend creates a branch, copies the config into the target `configs/<tenant>/<environment>/...` path, and opens a pull request as the signed-in user.

### GitHub App + Azure Functions promote setup

1. Create a GitHub App with:
   - **User authorization callback URL** pointing to `/api/auth/github/callback` on your Azure Functions app
   - repository permissions for **Contents: Read and write**, **Pull requests: Read and write**, and **Metadata: Read-only**
   - user-to-server tokens enabled
2. Deploy `src/config-promote-api` as an Azure Functions app.
3. Set these function app settings:
   - `GitHubAppClientId`
   - `GitHubAppClientSecret`
   - `GitHubAppCallbackUrl`
   - `GitHubAppSessionKey` as a base64-encoded 32-byte secret used to protect the browser session payload
   - `GitHubAppAllowedOrigins` as a comma-separated list of allowed browser origins such as your local dev URL and GitHub Pages URL
4. Update `src/config-browser/wwwroot/appsettings.json` so `PromoteApiBaseUrl` points at the deployed Azure Functions base URL.

For local backend development, copy `src/config-promote-api/local.settings.sample.json` to `local.settings.json` and fill in the real GitHub App values.

For local Windows testing of the console tooling output, run:

```bat
refresh-browser-data.bat
```

For local bash testing of the console tooling output, run:

```bash
./refresh-browser-data.sh
```

Both scripts run the tooling project, clear `src/config-browser/wwwroot/data`, and copy the latest generated output into it. They are only needed if you still want to inspect the generated output folder locally; the browser app itself no longer depends on those copied files.
