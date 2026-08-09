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

1. Generate fresh output with the console app so `src/config-tooling/root` contains the latest files.
2. Start the browser app from `src/config-browser` with `dotnet run`.

The GitHub Pages workflow copies `src/config-tooling/root` into the published browser site's `wwwroot/data` folder, then deploys that artifact. The browser app lets you filter by tenant and environment, browse the generated files, and open a detail page for each config.

For local Windows testing, run:

```bat
refresh-browser-data.bat
```

For local bash testing, run:

```bash
./refresh-browser-data.sh
```

Both scripts run the tooling project, clear `src/config-browser/wwwroot/data`, and copy the latest generated output into it.
